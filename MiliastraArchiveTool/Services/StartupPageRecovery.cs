using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Automation.Workflows;

namespace MiliastraArchiveTool.Services;

// Shared by preflight and language checking; never spend the same recovery ESC twice.
public sealed class StartupPageRecovery(bool recoveryEscapeUsed = false)
{
    public bool RecoveryEscapeUsed { get; private set; } = recoveryEscapeUsed;
    public bool ServerReturnUsed { get; private set; }
    private GamePage? _last;
    private int _frames;
    public string? Observe(GamePage raw, GamePage gated, bool black, bool allowLanguageNavigation)
    {
        if (black || raw != gated) { _last = null; _frames = 0; return null; }
        _frames = _last == raw ? _frames + 1 : 1;
        _last = raw;
        if (_frames < 2) return null;
        if (!RecoveryEscapeUsed && (raw == GamePage.Unknown || (allowLanguageNavigation && raw == GamePage.WorldMain)))
        {
            RecoveryEscapeUsed = true; _frames = 0;
            return raw == GamePage.Unknown ? "RecoverUnknown" : "OpenPaimonMenu";
        }
        if (allowLanguageNavigation && raw == GamePage.LoginServerSelect && !ServerReturnUsed)
        {
            ServerReturnUsed = true; _frames = 0;
            return "ReturnFromServerList";
        }
        return null;
    }
}

public static class StartupLanguageTargets
{
    public static IReadOnlyList<AutomationTarget> Select(PageRecognitionResult page)
    {
        string[] names = page.Page switch
        {
            GamePage.LoginStart => [WorkflowTargetNames.LoginStartGameButton, WorkflowTargetNames.LoginStartGame, WorkflowTargetNames.LoginClickEnter],
            GamePage.LoginEnter => [WorkflowTargetNames.LoginClickEnter, WorkflowTargetNames.LoginStartGameButton],
            GamePage.LoginServerConfirm => [WorkflowTargetNames.LoginServerConfirmButton, WorkflowTargetNames.ServerCancelButton],
            GamePage.MainMenu => [WorkflowTargetNames.QianxingRealmEntry, WorkflowTargetNames.MainMenuQianxingSectionHeader],
            GamePage.ExitMenu => [WorkflowTargetNames.ExitMenuContinueButton, WorkflowTargetNames.ExitMenuLogoutButton, WorkflowTargetNames.ExitMenuDesktopButton],
            GamePage.LocalArchive => [WorkflowTargetNames.LocalArchiveTab, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.PrimaryActionButton],
            _ => []
        };
        return page.Targets.Where(t => names.Contains(t.Name) && t.Confidence >= .35 &&
                t.Width > 0 && t.Height > 0 && t.Width <= .55 && t.Height <= .30 &&
                (page.Page != GamePage.LocalArchive || t.CenterY < .15 || t.CenterY > .88))
            .OrderBy(t => Array.IndexOf(names, t.Name)).ThenByDescending(t => t.Confidence)
            .DistinctBy(t => (Math.Round(t.CenterX, 2), Math.Round(t.CenterY, 2)))
            .Take(4).ToArray();
    }

    public static NormalizedBox Crop(AutomationTarget target) => new(
        target.CenterX, target.CenterY, target.Width + .012, target.Height + .012);
}

// Open the exit dialog by clicking the menu's exit icon. ESC here returns to the world.
public sealed class StartupMenuLanguageFallback
{
    public bool ExitMenuRequested { get; private set; }
    private int _misses;
    private AutomationTarget? _previousExit;
    public bool Observe(GamePage page, string? language, bool black)
    {
        if (ExitMenuRequested) return false;
        if (black || page != GamePage.MainMenu || language != null) { _misses = 0; _previousExit = null; return false; }
        if (++_misses < 3) return false;
        return true;
    }

    public bool ConfirmExitTarget(AutomationTarget? target)
    {
        if (_misses < 3 || ExitMenuRequested) return false;
        var stable = !ExitMenuRequested && target != null && _previousExit != null &&
            Math.Abs(target.CenterX - _previousExit.CenterX) < .01 &&
            Math.Abs(target.CenterY - _previousExit.CenterY) < .01;
        _previousExit = target;
        if (stable) ExitMenuRequested = true;
        return stable;
    }
}
