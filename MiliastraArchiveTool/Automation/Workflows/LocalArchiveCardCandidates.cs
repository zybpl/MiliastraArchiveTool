using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed record LocalArchiveCardCandidate(
    AutomationTarget CardBox,
    string Source,
    int Index,
    NormalizedBox? NameRegion,
    bool IsSynthetic);

public interface ILocalArchiveCardCandidateProvider
{
    IReadOnlyList<LocalArchiveCardCandidate> GetCandidates(
        PageRecognitionResult recognition,
        GameCaptureResult capture);
}

public sealed class LocalArchiveCardCandidateProvider : ILocalArchiveCardCandidateProvider
{
    public IReadOnlyList<LocalArchiveCardCandidate> GetCandidates(
        PageRecognitionResult recognition,
        GameCaptureResult capture)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(capture);

        return recognition.Targets
            .Where(target => string.Equals(target.Name, WorkflowTargetNames.LocalArchiveCard, StringComparison.Ordinal))
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .Select((target, index) => new LocalArchiveCardCandidate(
                target,
                "yolo",
                index,
                CreateNameRegion(target),
                IsSynthetic: false))
            .ToList();
    }

    private static NormalizedBox CreateNameRegion(AutomationTarget card) =>
        new(
            Math.Clamp(card.CenterX + card.Width * 0.18, 0, 1),
            Math.Clamp(card.CenterY - card.Height * 0.22, 0, 1),
            Math.Clamp(card.Width * 0.58, 0, 1),
            Math.Clamp(card.Height * 0.34, 0, 1));

}
