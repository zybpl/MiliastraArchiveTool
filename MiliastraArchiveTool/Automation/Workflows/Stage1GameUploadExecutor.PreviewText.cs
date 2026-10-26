using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed partial class Stage1GameUploadExecutor
{
    private async Task<StablePageResult> FillPreviewInlineFieldConfirmedAsync(
        StablePageResult current, AutomationTarget target, string fieldName, string value,
        List<string> workflowLog, CancellationToken cancellationToken)
    {
        // Retries stay in the same viewport and replace the entire current field.
        // Exclude the character counter and pencil from the OCR evidence.
        var body = new NormalizedBox(target.CenterX, target.CenterY - target.Height * .1,
            target.Width * .94, target.Height * .72);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ClickTextInputAreaForFocusAsync(current, target, fieldName, workflowLog, cancellationToken);
            current = await ReplaceTextInPreviewInlineAsync(current, fieldName, value,
                workflowLog, commitWithEnter: false, cancellationToken);
            var verifiedFrames = 0;
            for (var observation = 0; observation < 2; observation++)
            {
                var texts = await _workflowOcr.RecognizeBoxAsync(current.Capture, body,
                    $"preview_text/{fieldName}/readback", cancellationToken);
                var matched = PreviewTextReadbackHasContent(texts);
                LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} action=VerifyTextReadback " +
                    $"attempt={attempt} observation={observation + 1} validation=NonEmptyBody matched={matched} ocr=\"{FormatOcrTexts(texts)}\"");
                if (!matched) break;
                verifiedFrames++;
                if (verifiedFrames < 2)
                {
                    await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
                    current = await RecognizeOnceAsync(cancellationToken);
                    if (current.Recognition.Page != GamePage.PreviewEditor)
                        throw new InvalidOperationException($"Preview field {fieldName}: page changed during text verification.");
                }
            }
            if (verifiedFrames == 2)
            {
                LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} result=TextWriteConfirmed attempt={attempt}");
                return current;
            }
        }
        throw new InvalidOperationException($"预览字段 {fieldName} 填写后未能确认内容，已重试 3 次。请保持当前页面，检查文本框后继续。");
    }

    private static bool PreviewTextReadbackHasContent(IReadOnlyList<WorkflowOcrTextResult> texts)
    {
        var observed = NormalizeWorkflowOcrText(string.Concat(texts.Where(t => t.Confidence >= .6).Select(t => t.Text)));
        if (string.IsNullOrWhiteSpace(observed)) return false;
        if (new[] { "Please enter stage description", "Please enter level description",
                "Please enter gameplay details", "Please enter the early access version description",
                "Please enter early access version description", "请输入关卡描述", "请输入玩法说明",
                "请输入先行版本说明", "请输入先行体验版本说明", "请输入「先行体验」版本说明" }
            .Any(placeholder => observed == NormalizeWorkflowOcrText(placeholder))) return false;
        // Verify presence only: OCR substitutions or missing characters do not
        // imply that text entry failed. The crop excludes counters and controls.
        return true;
    }
}
