using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed partial class Stage1GameUploadExecutor
{
    private static NormalizedBox? EditIconSearchBox(PageRecognitionResult recognition) => recognition.Page switch
    {
        GamePage.PreviewEditor => new(.50, .45, .76, .74),
        GamePage.MultiLanguageEditor => new(.55, .48, .62, .52),
        GamePage.UploadedArchive when recognition.Targets.Any(t => t.Name == WorkflowTargetNames.InfoPanelRow) &&
            !recognition.Targets.Any(t => t.Name == WorkflowTargetNames.ModalDialog) => new(.95, .50, .08, .72),
        // This page is deliberately gated to Unknown; table/OCR validation still runs later.
        GamePage.Unknown when recognition.Source.Contains("Stage1 gated: MultiLanguageEditor", StringComparison.Ordinal)
            => new(.55, .48, .62, .52),
        _ => null
    };

    private static IReadOnlyList<AutomationTarget> LocateEditIcons(StablePageResult page, NormalizedBox box, CancellationToken ct) =>
        page.Recognition.Page == GamePage.UploadedArchive
            ? TextEditIconTemplate.LocateInfoPanel(page.Capture.Bitmap, box, ct)
            : TextEditIconTemplate.Locate(page.Capture.Bitmap, box, ct);

    private async Task<StablePageResult> AddMissingEditIconsAsync(StablePageResult first, CancellationToken cancellationToken)
    {
        var box = EditIconSearchBox(first.Recognition);
        if (box == null) return first;
        var candidates = LocateEditIcons(first, box, cancellationToken)
            .Where(t => !first.Recognition.Targets.Any(existing =>
                existing.Name is WorkflowTargetNames.TextEditIcon or WorkflowTargetNames.PreviewLevelNameEditButton &&
                TextEditIconTemplate.SameLocation(existing, t))).ToList();
        if (candidates.Count == 0) return first;

        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(100), cancellationToken);
        var capture = await _snapshotProvider.CaptureAsync(cancellationToken);
        var recognition = await _recognizer.RecognizeAsync(capture.Bitmap, cancellationToken);
        var fresh = new StablePageResult(capture, recognition);
        if (recognition.Page != first.Recognition.Page || EditIconSearchBox(recognition) != box ||
            capture.Window != first.Capture.Window || capture.Bitmap.PixelWidth != first.Capture.Bitmap.PixelWidth ||
            capture.Bitmap.PixelHeight != first.Capture.Bitmap.PixelHeight) return fresh;

        var confirmed = LocateEditIcons(fresh, box, cancellationToken)
            .Where(t => candidates.Any(previous => TextEditIconTemplate.SameLocation(previous, t)))
            .Where(t => !recognition.Targets.Any(existing =>
                existing.Name is WorkflowTargetNames.TextEditIcon or WorkflowTargetNames.PreviewLevelNameEditButton &&
                TextEditIconTemplate.SameLocation(existing, t))).ToList();
        LogWorkflow(_activeDiagnostics?.WorkflowLog as List<string>,
            $"step=EditIconFallback page={recognition.Page} candidates={candidates.Count} confirmed={confirmed.Count} " +
            $"targets=[{string.Join("; ", confirmed.Select(FormatTarget))}]");
        return confirmed.Count == 0 ? fresh : new StablePageResult(capture, recognition with
        {
            Targets = recognition.Targets.Concat(confirmed).ToList(),
            Source = recognition.Source + "+EditIconTemplate(two_frames)"
        });
    }
}

