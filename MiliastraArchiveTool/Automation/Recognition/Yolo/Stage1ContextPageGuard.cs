namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed class Stage1ContextPageGuard
{
    public Stage1ContextPageDecision Decide(
        PageRecognitionResult recognition,
        IReadOnlySet<GamePage> expectedPages,
        string stepName,
        bool allowWaitingPages = true)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(expectedPages);
        if (expectedPages.Count == 0)
            throw new ArgumentException("Expected pages cannot be empty.", nameof(expectedPages));
        if (string.IsNullOrWhiteSpace(stepName))
            throw new ArgumentException("Step name cannot be empty.", nameof(stepName));

        var expectedText = FormatExpectedPages(expectedPages);
        if (expectedPages.Contains(recognition.Page))
        {
            return new Stage1ContextPageDecision(
                true,
                true,
                false,
                false,
                recognition.Page.IsBlocking(),
                recognition.Page,
                $"Step '{stepName}' accepted expected page {recognition.Page}.",
                recognition);
        }

        if (recognition.Page.IsBlocking())
        {
            return new Stage1ContextPageDecision(
                true,
                false,
                false,
                false,
                true,
                recognition.Page,
                $"Step '{stepName}' detected blocking page {recognition.Page}; upper workflow must handle it immediately. Expected [{expectedText}].",
                recognition);
        }

        if (recognition.Page == GamePage.Unknown)
        {
            return new Stage1ContextPageDecision(
                false,
                false,
                false,
                true,
                false,
                recognition.Page,
                $"Step '{stepName}' saw Unknown. Expected [{expectedText}].",
                recognition);
        }

        if (Stage1RecognitionCapabilities.IsSafeForStage1(recognition.Page))
        {
            return new Stage1ContextPageDecision(
                false,
                false,
                true,
                false,
                false,
                recognition.Page,
                $"Step '{stepName}' rejected unexpected safe page {recognition.Page}. Expected [{expectedText}], recognized {recognition.Page}.",
                recognition);
        }

        if (Stage1RecognitionCapabilities.IsUnsupportedOrUnsafe(recognition.Page))
        {
            var reason = Stage1RecognitionCapabilities.GetStage1CapabilityReason(recognition.Page);
            if (allowWaitingPages && recognition.Page.IsWaiting())
                reason = $"{reason} It may be treated only as a transient waiting signal for this step.";

            return new Stage1ContextPageDecision(
                false,
                false,
                false,
                false,
                false,
                recognition.Page,
                $"Step '{stepName}' rejected unsupported page {recognition.Page}. Expected [{expectedText}]. {reason}",
                recognition);
        }

        return new Stage1ContextPageDecision(
            false,
            false,
            false,
            false,
            false,
            recognition.Page,
            $"Step '{stepName}' rejected page {recognition.Page}; Stage1 capability is not declared. Expected [{expectedText}].",
            recognition);
    }

    private static string FormatExpectedPages(IEnumerable<GamePage> expectedPages) =>
        string.Join(", ", expectedPages.OrderBy(page => page.ToString(), StringComparer.Ordinal));
}
