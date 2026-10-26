using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed partial class Stage1GameUploadExecutor
{
    private static int QuestionCount(string value) => value.Count(c => c is '?' or '？' or '\uFFFD');

    private static bool IsDamagedMultilanguageText(string text) => QuestionCount(text) >= 2 &&
        (!text.Any(char.IsLetter) || QuestionCount(text) * 2 >= text.Count(c => !char.IsWhiteSpace(c)));

    private static string MultilanguageReadbackStatus(string source, IReadOnlyList<WorkflowOcrTextResult> texts)
    {
        var body = string.Join(" ", texts.Where(t => t.Confidence >= .75).Select(t => t.Text)).Trim();
        if (QuestionCount(body) > QuestionCount(source) && IsDamagedMultilanguageText(body))
            return "QuestionMarkCorruption";
        var normalized = NormalizeWorkflowOcrText(body);
        if (normalized.Length == 0 || new[] { "Edit", "Click to edit", "编辑", "点击编辑", "Please enter", "请输入" }
            .Any(t => normalized == NormalizeWorkflowOcrText(t)) ||
            !PreviewTextReadbackHasContent(texts)) return "EmptyOrPlaceholder";
        return "ContentPresent";
    }

    private static int? ReadMultilanguageCharacterCount(IReadOnlyList<WorkflowOcrTextResult> texts)
    {
        var counts = new HashSet<int>();
        foreach (var text in texts.Where(t => t.Confidence >= .8))
        {
            var match = Regex.Match(text.Text.Trim(), @"^(\d{1,6})\s*[/／]\s*(\d{1,6})$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var count) &&
                int.TryParse(match.Groups[2].Value, out var limit) && limit > 0 && count <= limit)
                counts.Add(count);
        }
        return counts.Count == 1 ? counts.Single() : null;
    }

    private async Task<IReadOnlyList<WorkflowOcrTextResult>> ReadMultilanguageEvidenceAsync(
        StablePageResult page, NormalizedBox box, string scope, List<string> workflowLog, CancellationToken ct)
    {
        try { return await _workflowOcr.RecognizeBoxAsync(page.Capture, box, scope, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=OptionalOcrUnavailable scope={scope} error={ex.GetType().Name}");
            return Array.Empty<WorkflowOcrTextResult>();
        }
    }

    private void LogMultilanguageSource(string language, string column, string value, List<string> workflowLog)
    {
        LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=InputSource " +
            $"language={language} column=\"{column}\" method=UnicodeClipboard chars={value.Length} " +
            $"nonAscii={value.Count(c => c > 127)} questions={QuestionCount(value)} " +
            $"sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}");
        if (IsDamagedMultilanguageText(value))
            throw new InvalidOperationException($"多语言 {language} / {column} 的源配置包含异常连续问号或替换字符，请修正存档配置后重试。");
    }

    private async Task<StablePageResult> VerifyMultilanguageTextAsync(StablePageResult current,
        NormalizedBox body, string language, string column, string value,
        List<string> workflowLog, CancellationToken ct, NormalizedBox? counterBox = null, int? previousCount = null,
        bool reuseInitialFrame = false)
    {
        var questionFrames = 0;
        var zeroFrames = 0;
        var positiveFrames = 0;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (attempt == 1 && reuseInitialFrame)
                LogWorkflow(workflowLog, "step=Multilanguage action=ReadbackFrame source=settled_table captureSkipped=True yoloSkipped=True");
            if (attempt != 1 || !reuseInitialFrame)
            {
                await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), ct);
                // Readback checks content and page state; it does not need edit-icon coordinates.
                current = await RecognizeOnceAsync(ct, supplementEditIcons: false);
            }
            if (current.Recognition.Page.IsBlocking() || current.Recognition.Page is not
                (GamePage.Unknown or GamePage.TextEditDialog or GamePage.MultiLanguageEditor))
                throw new InvalidOperationException($"Multilingual readback left the editor: {current.Recognition.Page}.");
            var texts = await ReadMultilanguageEvidenceAsync(current, body,
                $"multilanguage/{language}/{column}/readback", workflowLog, ct);
            var status = MultilanguageReadbackStatus(value, texts);
            int? count = counterBox == null ? null : ReadMultilanguageCharacterCount(
                await ReadMultilanguageEvidenceAsync(current, counterBox,
                    $"multilanguage/{language}/{column}/counter", workflowLog, ct));
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=VerifyTextReadback " +
                $"language={language} column=\"{column}\" observation={attempt} result={status} " +
                $"counterBefore={previousCount?.ToString() ?? "unavailable"} counterAfter={count?.ToString() ?? "unavailable"} " +
                $"ocr=\"{FormatOcrTexts(texts)}\"");
            questionFrames = status == "QuestionMarkCorruption" ? questionFrames + 1 : 0;
            zeroFrames = count == 0 ? zeroFrames + 1 : 0;
            positiveFrames = count > 0 ? positiveFrames + 1 : 0;
        }
        if (questionFrames >= 2 || zeroFrames >= 2)
        {
            var path = SaveDiagnostic(current, "stage1-multilanguage-text-readback-failed");
            throw new InvalidOperationException($"多语言 {language} / {column} 连续两次检测到异常问号或字符计数为零，未继续提交。" +
                $"请保留当前编辑页面，检查内容后继续。diagnostic={path}");
        }
        var evidence = positiveFrames >= 2
            ? previousCount == 0 ? "CounterChangedFromEmptyToNonEmpty" : "CounterNonEmpty"
            : "ContentUnverified";
        LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=TextEvidence " +
            $"language={language} column=\"{column}\" result={evidence} exactContentVerified=False");
        return current;
    }
}
