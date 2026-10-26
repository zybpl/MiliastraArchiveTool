using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed partial class Stage1GameUploadExecutor
{
    private enum DraftImageState { Unknown, Existing, Empty, Failed }

    private static bool ImageActionLabelMatches(string text, params string[] expected)
    {
        var label = NormalizeWorkflowOcrText(text);
        return expected.Any(value => label.EndsWith(value, StringComparison.Ordinal) &&
            label.Length - value.Length <= 2 &&
            label[..^value.Length].All(c => c > 127 || !char.IsLetterOrDigit(c)));
    }

    private static (DraftImageState State, string Evidence) ClassifyDraftImageActions(
        StablePageResult page, IReadOnlyList<WorkflowOcrTextResult> texts, CancellationToken ct)
    {
        var scores = new Dictionary<string, double>();
        var labels = new Dictionary<string, WorkflowOcrTextResult>();
        foreach (var action in new[] { "view", "delete", "upload" })
        {
            var tokens = action switch {
                "view" => new[] { "VIEWIMAGE", "查看图片" },
                "delete" => new[] { "DELETEIMAGE", "删除图片" },
                _ => new[] { "UPLOADIMAGE", "上传图片" }
            };
            scores[action] = 0;
            foreach (var label in texts.Where(t => t.Confidence >= .65 && ImageActionLabelMatches(t.Text, tokens)))
            {
                var score = ImageActionIconTemplate.Match(page.Capture.Bitmap, label, action, ct);
                if (score > scores[action]) { scores[action] = score; labels[action] = label; }
            }
        }
        const double threshold = .80;
        bool existing = scores["view"] >= threshold && scores["delete"] >= threshold &&
            labels["view"].NormalizedBox.CenterY < labels["delete"].NormalizedBox.CenterY &&
            Math.Abs(labels["view"].NormalizedBox.CenterX - labels["delete"].NormalizedBox.CenterX) < .08;
        bool empty = scores["upload"] >= threshold;
        var warning = TextEditIconTemplate.LocateUploadFailure(page.Capture.Bitmap, ct);
        var state = warning != null ? DraftImageState.Failed : existing && !empty ? DraftImageState.Existing :
            empty && !existing ? DraftImageState.Empty : DraftImageState.Unknown;
        return (state, $"viewScore={scores["view"]:F3} deleteScore={scores["delete"]:F3} uploadScore={scores["upload"]:F3} failureScore={warning?.Confidence:F3}");
    }

    private async Task<(StablePageResult Page, DraftImageState State)> InspectDraftImageBeforeUploadAsync(
        StablePageResult draft, ImageAssetResourceSpec resource, List<string> log, CancellationToken ct)
    {
        var area = FindImageAssetDraftUploadArea(draft);
        ReportOperation($"Checking image resource: {HudField(resource.FieldName)}");
        await _input.MoveAsync(draft.Capture.Window, area?.CenterX ?? .5, area?.CenterY ?? .46, ct);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(400), ct);
        var previous = DraftImageState.Unknown;
        StablePageResult current = draft;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            current = await RecognizeOnceAsync(ct);
            var context = await DetectImageAssetDraftBoxContextAsync(current, resource, ct);
            if (!context.Evidence.Accepted) { previous = DraftImageState.Unknown; }
            else
            {
                var texts = await _workflowOcr.RecognizeBoxAsync(current.Capture,
                    new NormalizedBox(.5, .46, .38, .28), "ImageAssetDraftBox/image_actions", ct);
                var result = ClassifyDraftImageActions(current, texts, ct);
                LogWorkflow(log, $"step=EditImageAssets field={resource.FieldName} action=InspectHoveredImage " +
                    $"attempt={attempt} state={result.State} {result.Evidence} ocr=\"{FormatOcrTexts(texts)}\"");
                if (result.State != DraftImageState.Unknown && result.State == previous)
                    return (WithContextualPage(current, GamePage.ImageAssetDraftBox), result.State);
                previous = result.State;
            }
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), ct);
        }
        SaveDiagnostic(current, "stage1-image-action-unconfirmed", isFinalFailure: false,
            recoverable: true, phase: resource.FieldName);
        throw new InvalidOperationException($"无法确认 {HudField(resource.FieldName)} 的图片状态：查看/删除或上传图片的文字与图标未稳定匹配。请保持当前草稿页后继续。");
    }
}
