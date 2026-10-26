using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

internal sealed class ArchiveEditorExitEvidence
{
    private AutomationTarget? _previous;
    private DateTimeOffset _observedAt;
    public bool Observe(PageRecognitionResult page, DateTimeOffset now)
    {
        if (page.Page is not GamePage.Unknown and not GamePage.ArchiveEditorMenu)
        {
            _previous = null;
            return false;
        }
        var targets = page.Targets.Where(t => t.Name == WorkflowTargetNames.ArchiveEditorExitButton &&
            t.Confidence >= .45 && t.CenterX is > 0 and < .06 && t.CenterY is > .88 and < 1 &&
            t.Width is > .004 and < .06 && t.Height is > .006 and < .08).ToList();
        if (targets.Count != 1) return false;
        var target = targets[0];
        var confirmed = _previous != null && now - _observedAt <= TimeSpan.FromSeconds(4) &&
            Math.Abs(target.CenterX - _previous.CenterX) < .015 && Math.Abs(target.CenterY - _previous.CenterY) < .02;
        _previous = target;
        _observedAt = now;
        return confirmed;
    }
}
