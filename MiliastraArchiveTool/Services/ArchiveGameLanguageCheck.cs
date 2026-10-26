using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MiliastraArchiveTool.Automation;
using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Automation.Recognition.Yolo;
using MiliastraArchiveTool.Automation.Workflows;
using MiliastraArchiveTool.Infrastructure;
using MiliastraArchiveTool.Models;

namespace MiliastraArchiveTool.Services;

/// <summary>Checks actual client UI language before any server or archive upload actions.</summary>
public sealed class ArchiveGameLanguageCheck
{
    public async Task<string> RunAsync(IReadOnlyList<LevelArchive> archives, GamePage initialPage,
        CancellationToken cancellationToken, bool recoveryEscapeUsed = false)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("StartupLanguageCheck", new { InitialPage = initialPage.ToString(), Archives = archives.Select(a => new { a.LevelName, a.DefaultLanguage }).ToArray() });
        using var ocr = new RapidOcrWorkflowOcrService();
        using var recognizer = new YoloPageRecognizer();
        var snapshots = new GameSnapshotProvider(GameCaptureStrategy.LiveGameplay);
        var input = new GameInputAutomationService();
        var activation = new GameWindowActivationService();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(60));
        var workToken = budget.Token;
        var timer = Stopwatch.StartNew();
        var streak = new LanguageObservationStreak();
        var recovery = new StartupPageRecovery(recoveryEscapeUsed);
        var menuFallback = new StartupMenuLanguageFallback();
        GamePage? previousPage = null;
        var observations = new List<object>();
        try
        {
            while (timer.Elapsed < TimeSpan.FromSeconds(60))
            {
                workToken.ThrowIfCancellationRequested();
                var capture = await snapshots.CaptureAsync(workToken);
                if (!activation.IsForeground(capture.Window))
                    throw new UploadStartupException("游戏语言检查失败", "游戏窗口失去焦点，本次任务已中止。请将游戏保持在前台后重试。");
                var recognition = await recognizer.RecognizeAsync(capture.Bitmap, workToken);
                var page = recognition.Page;
                var action = menuFallback.ExitMenuRequested ? null : recovery.Observe(page, page, capture.IsLikelyBlackFrame, true);
                if (previousPage != page) streak = new LanguageObservationStreak();
                previousPage = page;
                if (action != null)
                {
                    observations.Add(new { Page = page.ToString(), Action = action });
                    WorkflowPerformanceTrace.Event("StartupLanguageCheck", action);
                    await input.PressKeyAsync(capture.Window, GameVirtualKey.Escape, workToken);
                    streak = new LanguageObservationStreak();
                    await Task.Delay(500, workToken);
                    continue;
                }
                var targets = capture.IsLikelyBlackFrame ? Array.Empty<AutomationTarget>() : StartupLanguageTargets.Select(recognition);
                var texts = await RecognizeTargetsAsync(recognition, capture, ocr, workToken);
                var language = DetectLanguage(page, texts);
                WorkflowPerformanceTrace.Event("StartupLanguageCheck", "Observed", new { Page = page.ToString(), Language = language, TargetCount = targets.Count });
                observations.Add(new { Page = page.ToString(), Language = language, Targets = targets, RawTargets = recognition.Targets, Texts = texts });
                if (menuFallback.Observe(page, language, capture.IsLikelyBlackFrame))
                {
                    var exit = recognition.Targets.Where(t => t.Name == WorkflowTargetNames.MainMenuExitButton &&
                            t.Confidence >= .35 && t.CenterX is > 0 and < .06 && t.CenterY is > .88 and < 1 &&
                            t.Width is > 0 and < .08 && t.Height is > 0 and < .15)
                        .OrderByDescending(t => t.Confidence).FirstOrDefault();
                    var source = exit == null ? "Template" : "YOLO";
                    exit ??= MainMenuExitTemplate.LocateExit(capture.Bitmap, workToken);
                    var stable = menuFallback.ConfirmExitTarget(exit);
                    observations.Add(new { Page = page.ToString(), Action = "LocateExitIconForLanguage", Source = source, Target = exit, Stable = stable });
                    WorkflowPerformanceTrace.Event("StartupLanguageCheck", "LocateExitIconForLanguage", new { Source = source, Target = exit, Stable = stable });
                    if (stable)
                    {
                        if (!activation.IsForeground(capture.Window))
                            throw new UploadStartupException("游戏语言检查失败", "游戏窗口失去焦点，本次任务已中止。请将游戏保持在前台后重试。");
                        WorkflowPerformanceTrace.Event("StartupLanguageCheck", "ClickExitIconForLanguage");
                        await input.ClickAsync(capture.Window, exit!.CenterX, exit.CenterY, workToken);
                        streak = new LanguageObservationStreak();
                        await Task.Delay(500, workToken);
                        continue;
                    }
                }
                if (streak.Observe(language))
                {
                    ValidateCompatibility(archives, language!);
                    if (page == GamePage.ExitMenu)
                    {
                        // Close only the fallback menu; never invoke logout/desktop actions.
                        WorkflowPerformanceTrace.Event("StartupLanguageCheck", "CloseExitMenuAfterLanguage");
                        observations.Add(new { Page = page.ToString(), Action = "CloseExitMenuAfterLanguage" });
                        await input.PressKeyAsync(capture.Window, GameVirtualKey.Escape, workToken);
                        var restoredFrames = 0;
                        var restoreDeadline = timer.Elapsed + TimeSpan.FromSeconds(10);
                        while (timer.Elapsed < restoreDeadline)
                        {
                            await Task.Delay(500, workToken);
                            var restored = await snapshots.CaptureAsync(workToken);
                            if (!activation.IsForeground(restored.Window))
                                throw new UploadStartupException("游戏语言检查失败", "退出菜单返回期间游戏失去焦点，本次任务已中止。");
                            var result = await recognizer.RecognizeAsync(restored.Bitmap, workToken);
                            observations.Add(new { Page = result.Page.ToString(), Action = "VerifyExitMenuClosed" });
                            restoredFrames = !restored.IsLikelyBlackFrame && result.Page is GamePage.MainMenu or GamePage.WorldMain
                                ? restoredFrames + 1 : 0;
                            if (restoredFrames >= 2) return language!;
                        }
                        throw new UploadStartupException("游戏语言检查失败", "已确认游戏语言，但退出菜单未正常关闭。本次任务已中止。");
                    }
                    return language!;
                }
                using (WorkflowPerformanceTrace.Measure("Delay", new { Caller = "ArchiveGameLanguageCheck.Poll", RequestedMs = 500 }))
                    await Task.Delay(500, workToken);
            }
            var reason = previousPage is GamePage.Unknown or GamePage.WorldMain or GamePage.LoginServerSelect
                ? "页面恢复后仍未确认可检测语言的起点。"
                : "YOLO 文字目标的 OCR 未能连续两次确认 English 或简体中文。";
            throw new UploadStartupException("无法确认游戏语言", reason + "本次任务已中止，请返回开始游戏界面后重试。\n诊断目录：" + ApplicationPaths.DiagnosticsDirectory);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UploadStartupException("无法确认游戏语言", "语言检查达到 60 秒时限，页面恢复或文字目标识别尚未稳定。本次任务已中止。\n诊断目录：" + ApplicationPaths.DiagnosticsDirectory);
        }
        finally
        {
            // Keep the evidence even for conflicts and failed OCR, without letting logging
            // mask the actual startup error or cancellation.
            try
            {
                Directory.CreateDirectory(ApplicationPaths.DiagnosticsDirectory);
                File.WriteAllText(Path.Combine(ApplicationPaths.DiagnosticsDirectory,
                    $"stage1-startup-game-language-{DateTime.Now:yyyyMMdd-HHmmssfff}.json"),
                    JsonSerializer.Serialize(new { InitialPage = initialPage.ToString(), recovery.RecoveryEscapeUsed, recovery.ServerReturnUsed, menuFallback.ExitMenuRequested, Observations = observations }));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static async Task<IReadOnlyList<WorkflowOcrTextResult>> RecognizeTargetsAsync(
        PageRecognitionResult recognition, GameCaptureResult capture, IWorkflowOcrService ocr, CancellationToken ct)
    {
        var texts = new List<WorkflowOcrTextResult>();
        if (capture.IsLikelyBlackFrame) return texts;
        foreach (var target in StartupLanguageTargets.Select(recognition))
        {
            ct.ThrowIfCancellationRequested();
            var crop = StartupLanguageTargets.Crop(target);
            var found = await ocr.RecognizeBoxAsync(capture, crop, "StartupGameLanguage/" + target.Name, ct);
            texts.AddRange(found);
            if (found.Count > 1)
                texts.Add(new WorkflowOcrTextResult(string.Concat(found.OrderBy(t => t.NormalizedBox.CenterY).ThenBy(t => t.NormalizedBox.CenterX).Select(t => t.Text)),
                    "", new NormalizedBox(target.CenterX, target.CenterY, target.Width, target.Height), found.Min(t => t.Confidence)));
            WorkflowPerformanceTrace.Event("StartupLanguageCheck", "TargetOcr", new { Page = recognition.Page.ToString(), target.Name, Crop = crop, Texts = found });
        }
        return texts;
    }
    public static void ValidateCompatibility(IReadOnlyList<LevelArchive> archives, string gameLanguage)
    {
        if (gameLanguage is not "en" and not "zh-CN")
            throw new UploadStartupException("无法确认游戏语言", "游戏语言未确认，本次任务已中止。");
        var conflicts = archives.Where(a => a.DefaultLanguage != gameLanguage).ToList();
        if (conflicts.Count == 0) return;
        throw new UploadStartupException("存档默认语言与游戏语言冲突",
            $"当前游戏语言：{DisplayName(gameLanguage)}\n以下存档的默认语言不一致：\n" +
            string.Join("\n", conflicts.Select(a => $"• {a.LevelName}：{DisplayName(a.DefaultLanguage)}")) +
            "\n\n游戏会强制保留当前游戏语言作为默认语言，无法删除。本次任务已中止，尚未开始上传。请切换游戏语言，或确认内容语言后修改存档默认语言；修改选项不会自动翻译内容。");
    }

    private static string DisplayName(string code) => code switch { "en" => "English", "zh-CN" => "简体中文", _ => code };

    public static string? DetectLanguage(GamePage page, IReadOnlyList<WorkflowOcrTextResult> texts)
    {
        string[] chinese;
        string[] english;
        Func<NormalizedBox, bool> inUiRegion;
        switch (page)
        {
            case GamePage.LoginStart:
            case GamePage.LoginEnter:
                chinese = ["开始游戏", "点击进入", "选择服务器", "请选择服务器"];
                english = ["STARTGAME", "CLICKTOBEGIN", "CLICKTOENTER", "SELECTSERVER", "SELECTASERVER"];
                inUiRegion = b => b.CenterX is > .2 and < .8;
                break;
            case GamePage.LoginServerConfirm:
                chinese = ["确认", "取消", "确定"];
                english = ["CONFIRM", "CANCEL", "OK"];
                inUiRegion = b => b.CenterX is > .2 and < .8;
                break;
            case GamePage.MainMenu:
                chinese = ["千星奇域", "我的奇域"];
                english = ["MILIASTRAWONDERLAND", "MYMILIASTRAWONDERLAND"];
                inUiRegion = _ => true; // Text already comes only from the YOLO entry box.
                break;
            case GamePage.ExitMenu:
                chinese = ["继续游戏", "退出至登录界面", "退出至桌面"];
                english = ["CONTINUE", "CONTINUEGAME", "RESUMEGAME", "RETURNTOGAME", "LOGOUT", "EXITTOLOGININTERFACE", "EXITGAME", "EXITTODESKTOP", "QUITTODESKTOP"];
                inUiRegion = b => b.CenterX is > .2 and < .8;
                break;
            case GamePage.LocalArchive:
            case GamePage.UploadedArchive:
                chinese = ["本地存档", "导入存档", "新建存档"];
                english = ["LOCALSAVE", "LOCALSAVES", "IMPORTSAVE", "NEWSAVE", "CREATESAVE"];
                inUiRegion = b => b.CenterY < .15 || b.CenterY > .88;
                break;
            default:
                return null;
        }
        var labels = texts.Where(t => t.Confidence >= .8 && inUiRegion(t.NormalizedBox))
            .Select(t => new string(t.Text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray())).ToHashSet();
        var zh = chinese.Any(labels.Contains);
        var en = english.Any(labels.Contains);
        return zh == en ? null : zh ? "zh-CN" : "en";
    }
}

public sealed class LanguageObservationStreak
{
    private string? _last;
    private int _count;
    public bool Observe(string? language)
    {
        _count = language == null ? 0 : language == _last ? _count + 1 : 1;
        _last = language;
        return _count >= 2;
    }
}



