using System.IO;
using System.Runtime.InteropServices;
using MiliastraArchiveTool.Services;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Automation.Recognition.Yolo;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Automation.Workflows.Diagnostics;

public sealed class Stage1PreflightCheck
{
    private readonly YoloModelAssetService _assetService;
    private readonly GameWindowCaptureService _windowCapture;
    private readonly BestGameScreenCaptureService _bestCapture;
    private readonly GameWindowActivationService _activation = new();

    public Stage1PreflightCheck(
        YoloModelAssetService? assetService = null,
        GameWindowCaptureService? windowCapture = null,
        BestGameScreenCaptureService? bestCapture = null)
    {
        _assetService = assetService ?? new YoloModelAssetService();
        _windowCapture = windowCapture ?? new GameWindowCaptureService();
        _bestCapture = bestCapture ?? new BestGameScreenCaptureService();
    }

    public async Task<Stage1PreflightCheckReport> RunAsync(
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("StartupPreflight");
        outputDirectory ??= GetDefaultDiagnosticsDirectory();
        Directory.CreateDirectory(outputDirectory);

        Stage1PreflightModelInfo modelInfo;
        try
        {
            var assets = _assetService.LoadStage1Assets();
            modelInfo = CreateModelInfo(assets);
        }
        catch (Exception ex)
        {
            var failed = CreateFailureReport(
                CreateEmptyModelInfo(),
                null,
                [],
                null,
                null,
                $"Model asset check failed: {ex.Message}");
            await WriteReportsAsync(failed, outputDirectory, cancellationToken);
            return failed;
        }

        GameWindowInfo window;
        Stage1PreflightWindowInfo windowInfo;
        IReadOnlyList<GameWindowCandidateInfo> windowCandidates;
        IReadOnlyList<Stage1PreflightWindowCandidateInfo> windowCandidateInfos;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            windowCandidates = _windowCapture.FindGameWindowCandidates();
            window = _windowCapture.FindGameWindow();
            // Desktop capture includes covering windows. Activate before taking the preflight image,
            // not in BeginServer/Execute, which are reached only after preflight succeeds.
            _activation.RestoreAndActivate(window);
            await Task.Delay(250, cancellationToken);
            window = _windowCapture.FindGameWindow();
            if (!_activation.IsForeground(window))
                throw new InvalidOperationException("游戏窗口未保持在前台，无法进行启动截图预检。");
            windowInfo = CreateWindowInfo(window);
            windowCandidateInfos = CreateWindowCandidateInfos(windowCandidates, window.Handle, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            windowCandidates = SafeFindWindowCandidates();
            windowCandidateInfos = CreateWindowCandidateInfos(windowCandidates, null, ex.Message);
            var failed = CreateFailureReport(
                modelInfo,
                null,
                windowCandidateInfos,
                null,
                null,
                $"Game window detection/activation failed: {ex.Message}");
            await WriteReportsAsync(failed, outputDirectory, cancellationToken);
            return failed;
        }

        GameCaptureResult captureResult;
        Stage1PreflightCaptureInfo captureInfo;
        string? screenshotPath;
        try
        {
            captureResult = _bestCapture.Capture(GameCaptureStrategy.LiveGameplay);
            cancellationToken.ThrowIfCancellationRequested();
            captureInfo = CreateCaptureInfo(captureResult);
            screenshotPath = SaveScreenshot(captureResult, outputDirectory);
        }
        catch (Exception ex)
        {
            var failed = CreateFailureReport(
                modelInfo,
                windowInfo,
                windowCandidateInfos,
                null,
                null,
                $"Screenshot check failed: {ex.Message}");
            await WriteReportsAsync(failed, outputDirectory, cancellationToken);
            return failed;
        }

        try
        {
            using var inferenceService = new YoloOnnxInferenceService(_assetService);
            using var rawRecognizer = new YoloPageRecognizer(inferenceService);
            using var gatedRecognizer = new Stage1GatedPageRecognizer(rawRecognizer);

            var raw = await rawRecognizer.RecognizeAsync(captureResult.Bitmap, cancellationToken);
            var gated = await gatedRecognizer.RecognizeAsync(captureResult.Bitmap, cancellationToken);
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
            var streak = new PageObservationStreak();
            var recovery = new StartupPageRecovery();
            var stable = false;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_activation.IsForeground(captureResult.Window))
                    throw new InvalidOperationException("游戏窗口失去焦点，启动预检已中止。");
                var action = recovery.Observe(raw.Page, gated.Page, captureInfo.IsLikelyBlackFrame, false);
                if (action != null)
                {
                    WorkflowPerformanceTrace.Event("StartupPreflight", action, new { Page = raw.Page.ToString() });
                    await new GameInputAutomationService().PressKeyAsync(captureResult.Window, GameVirtualKey.Escape, cancellationToken);
                    streak = new PageObservationStreak();
                    // Give the resulting menu its own bounded observation window.
                    deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
                }
                stable = streak.Observe(gated.Page,
                    !captureInfo.IsLikelyBlackFrame && raw.Page == gated.Page &&
                    (IsSupportedCurrentServerStartPage(gated.Page) || gated.Page == GamePage.LoginServerSelect));
                if (stable || DateTimeOffset.UtcNow >= deadline)
                    break;
                using (WorkflowPerformanceTrace.Measure("Delay", new { Caller = "Stage1PreflightCheck.Poll", RequestedMs = 500 }))
                    await Task.Delay(500, cancellationToken);
                captureResult = _bestCapture.Capture(GameCaptureStrategy.LiveGameplay);
                captureInfo = CreateCaptureInfo(captureResult);
                raw = await rawRecognizer.RecognizeAsync(captureResult.Bitmap, cancellationToken);
                gated = await gatedRecognizer.RecognizeAsync(captureResult.Bitmap, cancellationToken);
            } while (true);
            screenshotPath = SaveScreenshot(captureResult, outputDirectory);
            var message = CreateRecommendationMessage(gated);
            var readyForStart = stable && !captureInfo.IsLikelyBlackFrame &&
                                IsSupportedCurrentServerStartPage(raw.Page) &&
                                IsSupportedCurrentServerStartPage(gated.Page);
            if (captureInfo.IsLikelyBlackFrame)
                message = "Not ready: screenshot appears to be a black frame.";
            if (!stable)
                message = "启动页面在 15 秒内未获得连续两次一致识别，请等待页面加载完成后重试。";

            var report = new Stage1PreflightCheckReport(
                DateTimeOffset.Now,
                modelInfo,
                windowInfo,
                windowCandidateInfos,
                captureInfo,
                screenshotPath,
                raw.Page,
                gated.Page,
                gated.Confidence,
                gated.Source,
                readyForStart,
                message,
                BuildTopTargets(gated.Targets),
                stable ? null : message) { RecoveryEscapeUsed = recovery.RecoveryEscapeUsed };

            await WriteReportsAsync(report, outputDirectory, cancellationToken);
            return report;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = CreateFailureReport(
                modelInfo,
                windowInfo,
                windowCandidateInfos,
                captureInfo,
                screenshotPath,
                $"Page recognition check failed: {ex.Message}");
            await WriteReportsAsync(failed, outputDirectory, cancellationToken);
            return failed;
        }
    }

    public static string GetDefaultDiagnosticsDirectory() =>
        ApplicationPaths.DiagnosticsDirectory;

    public static string? GetStartupBlockingReason(Stage1PreflightCheckReport report, bool explicitServerMode)
    {
        if (!string.IsNullOrWhiteSpace(report.ErrorMessage))
            return report.ErrorMessage;
        if (!report.Model.OnnxModelExists || report.Window == null || report.Capture == null)
            return "模型、游戏窗口或截图检查未完成。";
        if (report.Capture.IsLikelyBlackFrame)
            return "游戏截图疑似黑屏，请确认游戏窗口正常显示后重试。";

        if (!IsSupportedStartPage(report.RawPage, explicitServerMode) || !IsSupportedStartPage(report.GatedPage, explicitServerMode))
            return $"当前页面不支持所选服务器模式启动（原始识别：{report.RawPage}，门控识别：{report.GatedPage}）。" +
                   (explicitServerMode
                       ? "请返回开始游戏、服务器选择、大世界、主菜单或本地存档页面。"
                       : "请返回开始游戏、点击进入、服务器选择、服务器确认、大世界、主菜单或本地存档页面。");

        return null;
    }

    private static Stage1PreflightModelInfo CreateModelInfo(YoloModelAssets assets) =>
        new(
            assets.AssetDirectory,
            assets.OnnxModelPath,
            File.Exists(assets.OnnxModelPath),
            assets.ClassesPath,
            assets.Classes.Count,
            assets.DefaultThreshold);

    private static Stage1PreflightModelInfo CreateEmptyModelInfo() =>
        new(string.Empty, string.Empty, false, string.Empty, 0, 0);

    private static Stage1PreflightWindowInfo CreateWindowInfo(GameWindowInfo window) =>
        new(
            window.Title,
            $"0x{window.Handle.ToInt64():X}",
            window.ProcessId,
            window.ClientWidth,
            window.ClientHeight,
            window.ClientScreenLeft,
            window.ClientScreenTop,
            window.Dpi,
            window.IsFullscreenLike,
            IsIconic(window.Handle));

    private IReadOnlyList<GameWindowCandidateInfo> SafeFindWindowCandidates()
    {
        try
        {
            return _windowCapture.FindGameWindowCandidates();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<Stage1PreflightWindowCandidateInfo> CreateWindowCandidateInfos(
        IReadOnlyList<GameWindowCandidateInfo> candidates,
        nint? selectedHandle,
        string? windowLookupFailureReason) =>
        candidates
            .Select(candidate => new Stage1PreflightWindowCandidateInfo(
                $"0x{candidate.Handle.ToInt64():X}",
                candidate.ProcessId,
                candidate.Title,
                candidate.IsVisible,
                candidate.IsIconic,
                candidate.ClientWidth,
                candidate.ClientHeight,
                candidate.WindowWidth,
                candidate.WindowHeight,
                candidate.ClientArea,
                selectedHandle.HasValue && candidate.Handle == selectedHandle.Value,
                CreateCandidateFailureReason(candidate, windowLookupFailureReason)))
            .ToList();

    private static string? CreateCandidateFailureReason(
        GameWindowCandidateInfo candidate,
        string? windowLookupFailureReason)
    {
        var reasons = new List<string>();
        if (!string.IsNullOrWhiteSpace(windowLookupFailureReason))
            reasons.Add(windowLookupFailureReason);
        if (!candidate.IsVisible)
            reasons.Add("Window is not visible.");
        if (candidate.IsIconic)
            reasons.Add("Window is minimized.");
        if (candidate.ClientWidth <= 0 || candidate.ClientHeight <= 0 || candidate.ClientArea <= 0)
            reasons.Add("Client area is zero or invalid.");
        if (candidate.WindowWidth <= 0 || candidate.WindowHeight <= 0)
            reasons.Add("Window bounds are zero or invalid.");

        return reasons.Count == 0 ? null : string.Join(" ", reasons.Distinct(StringComparer.Ordinal));
    }

    private static Stage1PreflightCaptureInfo CreateCaptureInfo(GameCaptureResult capture)
    {
        var selectedQuality = GameWindowCaptureService.CalculateVisualQuality(capture.Bitmap);
        return new Stage1PreflightCaptureInfo(
            capture.Strategy,
            capture.CaptureMethod,
            capture.GdiQuality,
            capture.PrintWindowQuality,
            capture.DxgiQuality,
            capture.CandidateQualities ?? [],
            selectedQuality,
            GameWindowCaptureService.IsLikelyBlackFrame(capture.Bitmap),
            capture.FallbackToGdi,
            capture.Warning);
    }

    private static string SaveScreenshot(GameCaptureResult capture, string outputDirectory)
    {
        var path = Path.Combine(
            outputDirectory,
            $"stage1-preflight-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
        GameWindowCaptureService.SavePng(capture.Bitmap, path);
        return path;
    }

    private static string CreateRecommendationMessage(PageRecognitionResult gated)
    {
        if (IsSupportedCurrentServerStartPage(gated.Page))
            return $"Ready: {gated.Page} can be used as a Stage1 upload start page.";

        if (gated.Page == GamePage.Unknown)
            return "Not ready: current page cannot be recognized safely. Return to WorldMain/LoginStart/LoginServerSelect/LoginEnter/LoginServerConfirm or collect more samples.";

        if (Stage1RecognitionCapabilities.IsSafeForStage1(gated.Page))
            return $"Not ready: Stage1 current-server upload cannot start from {gated.Page}.";

        if (Stage1RecognitionCapabilities.IsUnsupportedOrUnsafe(gated.Page))
            return $"Not ready: {Stage1RecognitionCapabilities.GetStage1CapabilityReason(gated.Page)}";

        return $"Not ready: Stage1 capability is not declared for page {gated.Page}.";
    }

    public static bool IsSupportedStartPage(GamePage page, bool explicitServerMode) => explicitServerMode
        ? page is GamePage.LoginStart or GamePage.LoginServerSelect or GamePage.WorldMain or GamePage.MainMenu or GamePage.LocalArchive
        : IsSupportedCurrentServerStartPage(page);

    private static bool IsSupportedCurrentServerStartPage(GamePage page) =>
        page is GamePage.WorldMain
            or GamePage.MainMenu
            or GamePage.LocalArchive
            or GamePage.LoginStart
            or GamePage.LoginEnter
            or GamePage.LoginServerSelect
            or GamePage.LoginServerConfirm;

    private static IReadOnlyList<Stage1PreflightTargetInfo> BuildTopTargets(
        IReadOnlyList<AutomationTarget> targets) =>
        targets
            .OrderByDescending(target => target.Confidence)
            .Take(20)
            .Select(target => new Stage1PreflightTargetInfo(
                target.Name,
                target.Confidence,
                target.CenterX,
                target.CenterY,
                target.Width,
                target.Height))
            .ToList();

    private static Stage1PreflightCheckReport CreateFailureReport(
        Stage1PreflightModelInfo model,
        Stage1PreflightWindowInfo? window,
        IReadOnlyList<Stage1PreflightWindowCandidateInfo> windowCandidates,
        Stage1PreflightCaptureInfo? capture,
        string? screenshotPath,
        string errorMessage) =>
        new(
            DateTimeOffset.Now,
            model,
            window,
            windowCandidates,
            capture,
            screenshotPath,
            GamePage.Unknown,
            GamePage.Unknown,
            0,
            "Stage1PreflightCheck",
            false,
            "Not ready: " + errorMessage,
            [],
            errorMessage);

    private static async Task WriteReportsAsync(
        Stage1PreflightCheckReport report,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(outputDirectory, "stage1-preflight-report.json");
        var markdownPath = Path.Combine(outputDirectory, "stage1-preflight-summary.md");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            markdownPath,
            BuildMarkdownSummary(report),
            Encoding.UTF8,
            cancellationToken);
    }

    private static string BuildMarkdownSummary(Stage1PreflightCheckReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Stage1 Preflight Check");
        builder.AppendLine();
        builder.AppendLine($"- CreatedAt: `{report.CreatedAt:O}`");
        builder.AppendLine($"- ReadyForStage1CurrentServerStart: `{report.ReadyForStage1CurrentServerStart}`");
        builder.AppendLine($"- Message: {report.Message}");
        if (!string.IsNullOrWhiteSpace(report.ErrorMessage))
            builder.AppendLine($"- ErrorMessage: `{report.ErrorMessage}`");
        builder.AppendLine();

        builder.AppendLine("## Model");
        builder.AppendLine();
        builder.AppendLine($"- AssetDirectory: `{report.Model.AssetDirectory}`");
        builder.AppendLine($"- OnnxModelPath: `{report.Model.OnnxModelPath}`");
        builder.AppendLine($"- OnnxModelExists: `{report.Model.OnnxModelExists}`");
        builder.AppendLine($"- ClassesPath: `{report.Model.ClassesPath}`");
        builder.AppendLine($"- ClassCount: `{report.Model.ClassCount}`");
        builder.AppendLine($"- DefaultThreshold: `{report.Model.DefaultThreshold:0.###}`");
        builder.AppendLine();

        if (report.Window != null)
        {
            builder.AppendLine("## Window");
            builder.AppendLine();
            builder.AppendLine($"- Title: `{report.Window.Title}`");
            builder.AppendLine($"- Handle: `{report.Window.Handle}`");
            builder.AppendLine($"- ProcessId: `{report.Window.ProcessId}`");
            builder.AppendLine($"- ClientSize: `{report.Window.ClientWidth}x{report.Window.ClientHeight}`");
            builder.AppendLine($"- ClientScreenOrigin: `{report.Window.ClientScreenLeft},{report.Window.ClientScreenTop}`");
            builder.AppendLine($"- Dpi: `{report.Window.Dpi}`");
            builder.AppendLine($"- IsFullscreenLike: `{report.Window.IsFullscreenLike}`");
            builder.AppendLine($"- IsMinimized: `{report.Window.IsMinimized}`");
            builder.AppendLine();
        }

        builder.AppendLine("## Window Candidates");
        builder.AppendLine();
        if (report.WindowCandidates.Count == 0)
        {
            builder.AppendLine("No GenshinImpact.exe visible window candidates were found.");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("| Selected | Handle | PID | Visible | Minimized | Client | Window | ClientArea | Title | FailureReason |");
            builder.AppendLine("| --- | --- | ---: | --- | --- | --- | --- | ---: | --- | --- |");
            foreach (var candidate in report.WindowCandidates)
            {
                builder.AppendLine(
                    $"| {(candidate.IsSelected ? "yes" : "")} | `{candidate.Handle}` | {candidate.ProcessId} | {candidate.IsVisible} | {candidate.IsMinimized} | {candidate.ClientWidth}x{candidate.ClientHeight} | {candidate.WindowWidth}x{candidate.WindowHeight} | {candidate.ClientArea} | `{EscapeMarkdownCell(candidate.Title)}` | {EscapeMarkdownCell(candidate.FailureReason ?? "")} |");
            }
            builder.AppendLine();
        }

        if (report.Capture != null)
        {
            builder.AppendLine("## Capture");
            builder.AppendLine();
            builder.AppendLine($"- ScreenshotPath: `{report.ScreenshotPath}`");
            builder.AppendLine($"- CaptureStrategy: `{report.Capture.CaptureStrategy}`");
            builder.AppendLine($"- CaptureMethod: `{report.Capture.CaptureMethod}`");
            builder.AppendLine($"- SelectedQuality: `{report.Capture.SelectedQuality:0.##}`");
            builder.AppendLine($"- GdiQuality: `{report.Capture.GdiQuality:0.##}`");
            builder.AppendLine($"- PrintWindowQuality: `{report.Capture.PrintWindowQuality?.ToString("0.##") ?? "<none>"}`");
            builder.AppendLine($"- DxgiQuality: `{report.Capture.DxgiQuality?.ToString("0.##") ?? "<none>"}`");
            builder.AppendLine($"- IsLikelyBlackFrame: `{report.Capture.IsLikelyBlackFrame}`");
            builder.AppendLine($"- FallbackToGdi: `{report.Capture.FallbackToGdi}`");
            builder.AppendLine($"- Warning: `{report.Capture.Warning ?? "<none>"}`");
            builder.AppendLine();
        }

        builder.AppendLine("## Recognition");
        builder.AppendLine();
        builder.AppendLine($"- RawPage: `{report.RawPage}`");
        builder.AppendLine($"- GatedPage: `{report.GatedPage}`");
        builder.AppendLine($"- Confidence: `{report.Confidence:0.###}`");
        builder.AppendLine($"- Source: `{report.Source}`");
        builder.AppendLine();

        builder.AppendLine("## Top Targets");
        builder.AppendLine();
        if (report.TopTargets.Count == 0)
        {
            builder.AppendLine("No YOLO targets detected.");
        }
        else
        {
            builder.AppendLine("| Name | Confidence | CenterX | CenterY | Width | Height |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
            foreach (var target in report.TopTargets)
            {
                builder.AppendLine(
                    $"| `{target.Name}` | {target.Confidence:0.###} | {target.CenterX:0.###} | {target.CenterY:0.###} | {target.Width:0.###} | {target.Height:0.###} |");
            }
        }

        return builder.ToString();
    }

    private static string EscapeMarkdownCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint windowHandle);
}
