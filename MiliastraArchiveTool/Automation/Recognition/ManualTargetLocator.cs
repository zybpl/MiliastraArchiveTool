namespace MiliastraArchiveTool.Automation.Recognition;

public sealed class ManualTargetLocator : ITargetLocator
{
    public AutomationTarget? FindTarget(
        PageRecognitionResult recognition,
        string targetName,
        double minimumConfidence = 0.3)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        if (string.IsNullOrWhiteSpace(targetName))
            throw new ArgumentException("Target name cannot be empty.", nameof(targetName));

        return recognition.Targets
            .Where(target =>
                target.Confidence >= minimumConfidence &&
                string.Equals(target.Name, targetName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(target => target.Confidence)
            .FirstOrDefault();
    }
}
