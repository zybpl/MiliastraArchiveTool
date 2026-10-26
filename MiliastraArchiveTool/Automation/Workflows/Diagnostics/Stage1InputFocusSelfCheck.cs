using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Automation.Recognition.Yolo;
using MiliastraArchiveTool.Automation.Workflows;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Automation.Workflows.Diagnostics;

public static class Stage1InputFocusSelfCheck
{
    private const int FirstFrameDelayMs = 800;
    private const int FrameIntervalMs = 500;
    private const int TotalDurationMs = 8000;
    private const double MinimumPaimonConfidence = 0.3;
    private const string WinningStrategyEsc = "Esc";
    private const string WinningStrategyPaimonClick = "PaimonClick";
    private const string WinningStrategyNone = "None";

    public static async Task<Stage1InputFocusProbeReport> RunOpenMainMenuProbeAsync(
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        outputDirectory ??= ApplicationPaths.DiagnosticsDirectory;
        Directory.CreateDirectory(outputDirectory);

        var reportPath = Path.Combine(outputDirectory, "stage1-input-focus-probe-report.json");
        var summaryPath = Path.Combine(outputDirectory, "stage1-input-focus-probe-summary.md");
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var beforeScreenshotPath = Path.Combine(outputDirectory, $"stage1-input-focus-before-{timestamp}.png");
        var afterScreenshotPath = Path.Combine(outputDirectory, $"stage1-input-focus-after-{timestamp}.png");

        var snapshotProvider = new GameSnapshotProvider(GameCaptureStrategy.LiveGameplay);
        var activation = new GameWindowActivationService();
        var input = new GameInputAutomationService(activation);

        Stage1InputFocusProbeReport report;
        try
        {
            var beforeCapture = await snapshotProvider.CaptureAsync(cancellationToken);
            GameWindowCaptureService.SavePng(beforeCapture.Bitmap, beforeScreenshotPath);

            using var recognizer = new Stage1GatedPageRecognizer();
            using var rawRecognizer = new YoloPageRecognizer();
            var beforeRecognition = await recognizer.RecognizeAsync(beforeCapture.Bitmap, cancellationToken);

            var activated = activation.TryRestoreAndActivate(beforeCapture.Window, out var activationDiagnostic);
            if (!activated)
            {
                var failed = new Stage1InputFocusProbeReport(
                    DateTimeOffset.Now,
                    false,
                    "Game window activation failed before ESC probe.",
                    beforeScreenshotPath,
                    null,
                    reportPath,
                    summaryPath,
                    activationDiagnostic,
                    null,
                    null,
                    false,
                    null,
                    null,
                    WinningStrategyNone,
                    null,
                    false,
                    null,
                    null,
                    null,
                    null,
                    FirstFrameDelayMs,
                    FrameIntervalMs,
                    TotalDurationMs,
                    false,
                    false,
                    GetMainMenuRecognitionLimitation(),
                    [],
                    beforeRecognition.Page,
                    GamePage.Unknown,
                    beforeRecognition.Confidence,
                    0,
                    activationDiagnostic);
                await WriteReportsAsync(failed, reportPath, summaryPath, cancellationToken);
                return failed;
            }

            var foregroundBeforeKey = activation.GetForegroundWindowInfo(beforeCapture.Window).ToDiagnosticString();
            var escPressedAt = DateTimeOffset.Now;
            await input.PressKeyAsync(beforeCapture.Window, GameVirtualKey.Escape, cancellationToken);
            var foregroundAfterKey = activation.GetForegroundWindowInfo(beforeCapture.Window).ToDiagnosticString();

            string? waitError = null;
            var usedPostMessageFallback = false;
            string? foregroundAfterFallbackKey = null;
            var usedPaimonFallback = false;
            double? paimonTargetConfidence = null;
            double? paimonClickCenterX = null;
            double? paimonClickCenterY = null;
            var winningStrategy = WinningStrategyNone;

            var frames = new List<Stage1InputFocusProbeFrame>();
            frames.AddRange(await CaptureAndRecognizePhaseAsync(
                "Esc",
                snapshotProvider,
                rawRecognizer,
                recognizer,
                outputDirectory,
                timestamp,
                firstFrameDelayMs: FirstFrameDelayMs,
                frameIntervalMs: FrameIntervalMs,
                totalDurationMs: TotalDurationMs,
                cancellationToken));

            var winningFrameIndex = FindWinningFrameIndex(frames);
            if (winningFrameIndex >= 0)
            {
                winningStrategy = WinningStrategyEsc;
            }
            else
            {
                var paimonTarget = FindLatestPaimonTarget(frames);
                if (paimonTarget != null)
                {
                    usedPaimonFallback = true;
                    paimonTargetConfidence = paimonTarget.Confidence;
                    paimonClickCenterX = paimonTarget.CenterX;
                    paimonClickCenterY = paimonTarget.CenterY;

                    await input.ClickAsync(
                        beforeCapture.Window,
                        paimonTarget.CenterX,
                        paimonTarget.CenterY,
                        cancellationToken);

                    frames.AddRange(await CaptureAndRecognizePhaseAsync(
                        "PaimonClick",
                        snapshotProvider,
                        rawRecognizer,
                        recognizer,
                        outputDirectory,
                        timestamp,
                        firstFrameDelayMs: FirstFrameDelayMs,
                        frameIntervalMs: FrameIntervalMs,
                        totalDurationMs: TotalDurationMs,
                        cancellationToken));

                    winningFrameIndex = FindWinningFrameIndex(frames);
                    if (winningFrameIndex >= 0)
                        winningStrategy = WinningStrategyPaimonClick;
                }
            }

            var afterCapture = await snapshotProvider.CaptureAsync(cancellationToken);
            GameWindowCaptureService.SavePng(afterCapture.Bitmap, afterScreenshotPath);
            var afterRecognition = await recognizer.RecognizeAsync(afterCapture.Bitmap, cancellationToken);
            var foregroundAfterWait = activation.GetForegroundWindowInfo(beforeCapture.Window).ToDiagnosticString();
            var anyMainMenu = frames.Any(frame => frame.GatedPage == GamePage.MainMenu || frame.RawPage == GamePage.MainMenu);
            var anyQianxing = frames.Any(frame => frame.HasQianxingRealmEntry);
            var anyPaimon = frames.Any(frame => frame.HasWorldPaimonIcon);
            if (!anyMainMenu)
            {
                waitError =
                    $"MainMenu was not recognized. ESC phase sampled every {FrameIntervalMs}ms for {TotalDurationMs}ms after an initial {FirstFrameDelayMs}ms delay. " +
                    $"Paimon fallback used={usedPaimonFallback}. {GetMainMenuRecognitionLimitation()}";
            }

            var success = anyMainMenu;
            var message = success
                ? winningStrategy == WinningStrategyPaimonClick
                    ? "OpenMainMenu probe recognized MainMenu after clicking world_paimon_icon."
                    : anyQianxing
                        ? "OpenMainMenu probe recognized MainMenu after ESC and qianxing_realm_entry was detected."
                        : "OpenMainMenu probe recognized MainMenu after ESC, but qianxing_realm_entry was not detected."
                : "OpenMainMenu probe did not recognize MainMenu after ESC or Paimon fallback.";

            report = new Stage1InputFocusProbeReport(
                DateTimeOffset.Now,
                success,
                message,
                beforeScreenshotPath,
                afterScreenshotPath,
                reportPath,
                summaryPath,
                activationDiagnostic,
                foregroundBeforeKey,
                foregroundAfterKey,
                usedPostMessageFallback,
                foregroundAfterFallbackKey,
                foregroundAfterWait,
                winningStrategy,
                winningFrameIndex >= 0 ? winningFrameIndex : null,
                usedPaimonFallback,
                paimonTargetConfidence,
                paimonClickCenterX,
                paimonClickCenterY,
                escPressedAt,
                FirstFrameDelayMs,
                FrameIntervalMs,
                TotalDurationMs,
                anyQianxing,
                anyPaimon,
                GetMainMenuRecognitionLimitation(),
                frames,
                beforeRecognition.Page,
                afterRecognition.Page,
                beforeRecognition.Confidence,
                afterRecognition.Confidence,
                waitError);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report = new Stage1InputFocusProbeReport(
                DateTimeOffset.Now,
                false,
                "ESC probe failed before a complete report could be collected.",
                File.Exists(beforeScreenshotPath) ? beforeScreenshotPath : null,
                File.Exists(afterScreenshotPath) ? afterScreenshotPath : null,
                reportPath,
                summaryPath,
                null,
                null,
                null,
                false,
                null,
                null,
                WinningStrategyNone,
                null,
                false,
                null,
                null,
                null,
                null,
                FirstFrameDelayMs,
                FrameIntervalMs,
                TotalDurationMs,
                false,
                false,
                GetMainMenuRecognitionLimitation(),
                [],
                GamePage.Unknown,
                GamePage.Unknown,
                0,
                0,
                ex.Message);
        }

        await WriteReportsAsync(report, reportPath, summaryPath, cancellationToken);
        return report;
    }

    private static async Task<IReadOnlyList<Stage1InputFocusProbeFrame>> CaptureAndRecognizePhaseAsync(
        string phase,
        IGameSnapshotProvider snapshotProvider,
        IPageRecognizer rawRecognizer,
        IPageRecognizer gatedRecognizer,
        string outputDirectory,
        string runTimestamp,
        int firstFrameDelayMs,
        int frameIntervalMs,
        int totalDurationMs,
        CancellationToken cancellationToken)
    {
        var frameCount = Math.Max(1, totalDurationMs / frameIntervalMs);
        var started = DateTimeOffset.UtcNow;
        var capturedFrames = new List<CapturedProbeFrame>();
        for (var index = 0; index < frameCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scheduledAt = started + TimeSpan.FromMilliseconds(firstFrameDelayMs + frameIntervalMs * index);
            var delay = scheduledAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            capturedFrames.Add(await CaptureProbeFrameAsync(
                phase,
                snapshotProvider,
                outputDirectory,
                runTimestamp,
                index,
                started,
                cancellationToken));
        }

        var frames = new List<Stage1InputFocusProbeFrame>();
        foreach (var capturedFrame in capturedFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            frames.Add(await RecognizeProbeFrameAsync(
                capturedFrame,
                rawRecognizer,
                gatedRecognizer,
                cancellationToken));
        }

        return frames;
    }

    private static async Task<CapturedProbeFrame> CaptureProbeFrameAsync(
        string phase,
        IGameSnapshotProvider snapshotProvider,
        string outputDirectory,
        string runTimestamp,
        int index,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        var capture = await snapshotProvider.CaptureAsync(cancellationToken);
        var screenshotPath = Path.Combine(outputDirectory, $"stage1-open-main-menu-{phase.ToLowerInvariant()}-frame-{runTimestamp}-{index:00}.png");
        GameWindowCaptureService.SavePng(capture.Bitmap, screenshotPath);

        return new CapturedProbeFrame(
            phase,
            DateTimeOffset.Now,
            (DateTimeOffset.UtcNow - started).TotalSeconds,
            screenshotPath,
            capture,
            new GameWindowActivationService().GetForegroundWindowInfo(capture.Window).ToDiagnosticString());
    }

    private static async Task<Stage1InputFocusProbeFrame> RecognizeProbeFrameAsync(
        CapturedProbeFrame capturedFrame,
        IPageRecognizer rawRecognizer,
        IPageRecognizer gatedRecognizer,
        CancellationToken cancellationToken)
    {
        var raw = await rawRecognizer.RecognizeAsync(capturedFrame.Capture.Bitmap, cancellationToken);
        var gated = await gatedRecognizer.RecognizeAsync(capturedFrame.Capture.Bitmap, cancellationToken);
        var targets = raw.Targets
            .Concat(gated.Targets)
            .GroupBy(target => target.Name, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(target => target.Confidence).First())
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

        return new Stage1InputFocusProbeFrame(
            capturedFrame.Phase,
            capturedFrame.CapturedAt,
            capturedFrame.ElapsedSeconds,
            capturedFrame.ScreenshotPath,
            capturedFrame.Capture.Strategy,
            capturedFrame.Capture.CaptureMethod,
            capturedFrame.Capture.Warning,
            capturedFrame.Capture.CandidateQualities ?? [],
            capturedFrame.Capture.IsLikelyBlackFrame,
            capturedFrame.Capture.FallbackToGdi,
            capturedFrame.ForegroundWindowDiagnostic,
            raw.Page,
            raw.Confidence,
            gated.Page,
            gated.Confidence,
            HasTarget(raw, WorkflowTargetNames.QianxingRealmEntry) || HasTarget(gated, WorkflowTargetNames.QianxingRealmEntry),
            HasTarget(raw, WorkflowTargetNames.WorldPaimonIcon) || HasTarget(gated, WorkflowTargetNames.WorldPaimonIcon),
            targets);
    }

    private sealed record CapturedProbeFrame(
        string Phase,
        DateTimeOffset CapturedAt,
        double ElapsedSeconds,
        string ScreenshotPath,
        GameCaptureResult Capture,
        string ForegroundWindowDiagnostic);

    private static bool HasTarget(PageRecognitionResult recognition, string targetName) =>
        recognition.Targets.Any(target => string.Equals(target.Name, targetName, StringComparison.Ordinal));

    private static int FindWinningFrameIndex(IReadOnlyList<Stage1InputFocusProbeFrame> frames)
    {
        for (var index = 0; index < frames.Count; index++)
        {
            if (frames[index].GatedPage == GamePage.MainMenu || frames[index].RawPage == GamePage.MainMenu)
                return index;
        }

        return -1;
    }

    private static Stage1PreflightTargetInfo? FindLatestPaimonTarget(IEnumerable<Stage1InputFocusProbeFrame> frames)
    {
        foreach (var frame in frames.Reverse())
        {
            if (frame.GatedPage != GamePage.WorldMain && frame.RawPage != GamePage.WorldMain)
                continue;

            var target = frame.TopTargets
                .Where(target => string.Equals(target.Name, WorkflowTargetNames.WorldPaimonIcon, StringComparison.Ordinal))
                .Where(target => target.Confidence >= MinimumPaimonConfidence)
                .OrderByDescending(target => target.Confidence)
                .FirstOrDefault();
            if (target != null)
                return target;
        }

        return null;
    }

    private static string GetMainMenuRecognitionLimitation() =>
        "Stage1 classes currently have no generic MainMenu targets. " +
        "MainMenu is recognized only when qianxing_realm_entry is detected; " +
        "if the screenshots show MainMenu but qianxing_realm_entry is false, the current model cannot confirm the menu.";

    private static async Task WriteReportsAsync(
        Stage1InputFocusProbeReport report,
        string reportPath,
        string summaryPath,
        CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, options), cancellationToken);
        await File.WriteAllTextAsync(summaryPath, BuildSummary(report), cancellationToken);
    }

    private static string BuildSummary(Stage1InputFocusProbeReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Stage1 Input Focus Probe");
        builder.AppendLine();
        builder.AppendLine($"- CreatedAt: {report.CreatedAt:O}");
        builder.AppendLine($"- Success: {report.Success}");
        builder.AppendLine($"- Message: {report.Message}");
        builder.AppendLine($"- PageBeforeKey: {report.PageBeforeKey} ({report.ConfidenceBeforeKey:F2})");
        builder.AppendLine($"- PageAfterWait: {report.PageAfterWait} ({report.ConfidenceAfterWait:F2})");
        builder.AppendLine($"- BeforeScreenshotPath: {report.BeforeScreenshotPath ?? "<none>"}");
        builder.AppendLine($"- AfterScreenshotPath: {report.AfterScreenshotPath ?? "<none>"}");
        builder.AppendLine($"- ActivationDiagnostic: {report.ActivationDiagnostic ?? "<none>"}");
        builder.AppendLine($"- ForegroundBeforeKey: {report.ForegroundBeforeKey ?? "<none>"}");
        builder.AppendLine($"- ForegroundAfterKey: {report.ForegroundAfterKey ?? "<none>"}");
        builder.AppendLine($"- UsedPostMessageFallback: {report.UsedPostMessageFallback}");
        builder.AppendLine($"- ForegroundAfterFallbackKey: {report.ForegroundAfterFallbackKey ?? "<none>"}");
        builder.AppendLine($"- ForegroundAfterWait: {report.ForegroundAfterWait ?? "<none>"}");
        builder.AppendLine($"- WinningStrategy: {report.WinningStrategy}");
        builder.AppendLine($"- WinningFrameIndex: {report.WinningFrameIndex?.ToString() ?? "<none>"}");
        builder.AppendLine($"- UsedPaimonFallback: {report.UsedPaimonFallback}");
        builder.AppendLine($"- PaimonTargetConfidence: {report.PaimonTargetConfidence?.ToString("F2") ?? "<none>"}");
        builder.AppendLine($"- PaimonClickCenterX: {report.PaimonClickCenterX?.ToString("F3") ?? "<none>"}");
        builder.AppendLine($"- PaimonClickCenterY: {report.PaimonClickCenterY?.ToString("F3") ?? "<none>"}");
        builder.AppendLine($"- EscPressedAt: {report.EscPressedAt?.ToString("O") ?? "<none>"}");
        builder.AppendLine($"- FirstFrameDelayMs: {report.FirstFrameDelayMs}");
        builder.AppendLine($"- FrameIntervalMs: {report.FrameIntervalMs}");
        builder.AppendLine($"- TotalDurationMs: {report.TotalDurationMs}");
        builder.AppendLine($"- HasAnyQianxingRealmEntry: {report.HasAnyQianxingRealmEntry}");
        builder.AppendLine($"- HasAnyWorldPaimonIcon: {report.HasAnyWorldPaimonIcon}");
        builder.AppendLine($"- MainMenuRecognitionLimitation: {report.MainMenuRecognitionLimitation}");
        builder.AppendLine($"- ErrorMessage: {report.ErrorMessage ?? "<none>"}");
        builder.AppendLine();
        builder.AppendLine("## Frames");
        foreach (var frame in report.Frames)
        {
            builder.AppendLine();
            builder.AppendLine($"### {frame.Phase} Frame {frame.ElapsedSeconds:F1}s");
            builder.AppendLine($"- ScreenshotPath: {frame.ScreenshotPath}");
            builder.AppendLine($"- CaptureStrategy: {frame.CaptureStrategy}");
            builder.AppendLine($"- CaptureMethod: {frame.CaptureMethod}");
            builder.AppendLine($"- CaptureWarning: {frame.CaptureWarning ?? "<none>"}");
            builder.AppendLine($"- IsLikelyBlackFrame: {frame.IsLikelyBlackFrame}");
            builder.AppendLine($"- FallbackToGdi: {frame.FallbackToGdi}");
            builder.AppendLine($"- ForegroundWindowDiagnostic: {frame.ForegroundWindowDiagnostic}");
            builder.AppendLine($"- RawPage: {frame.RawPage} ({frame.RawConfidence:F2})");
            builder.AppendLine($"- GatedPage: {frame.GatedPage} ({frame.GatedConfidence:F2})");
            builder.AppendLine($"- HasQianxingRealmEntry: {frame.HasQianxingRealmEntry}");
            builder.AppendLine($"- HasWorldPaimonIcon: {frame.HasWorldPaimonIcon}");
            builder.AppendLine("- TopTargets:");
            foreach (var target in frame.TopTargets)
            {
                builder.AppendLine(
                    $"  - {target.Name}: conf={target.Confidence:F2}, " +
                    $"box=({target.CenterX:F3},{target.CenterY:F3},{target.Width:F3},{target.Height:F3})");
            }

            if (frame.TopTargets.Count == 0)
                builder.AppendLine("  - <none>");
        }
        return builder.ToString();
    }
}
