using System.Text;
using System.Text.RegularExpressions;
using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

internal static class PreviewMediaUploadCounter
{
    public static NormalizedBox? FindTitleRetryBox(IReadOnlyList<WorkflowOcrTextResult> texts, bool video)
    {
        var pattern = video ? @"(?:SHOWCASEVIDEOS?|展示视频)" : @"(?:MORESHOWCASEIMAGES|更多展示图片)";
        var titles = texts.Where(t => t.Confidence >= 0.6 && Regex.IsMatch(
            Regex.Replace(t.Text.Normalize(NormalizationForm.FormKC), @"\s+", "").ToUpperInvariant(), pattern)).ToArray();
        if (titles.Length != 1) return null;
        var box = titles[0].NormalizedBox;
        // Retain native pixels and vertical context; a tight text line can lose
        // the thin denominator digit during detection/recognition.
        var width = Math.Clamp(box.Width + 0.06, 0.12, 0.50);
        var height = Math.Clamp(box.Height * 3, 0.06, 0.14);
        return new NormalizedBox(Math.Clamp(box.CenterX, width / 2, 1 - width / 2),
            Math.Clamp(box.CenterY, height / 2, 1 - height / 2), width, height);
    }

    // Counts must belong to the field's title, not to nearby tags or upload hints.
    public static int? Read(IReadOnlyList<WorkflowOcrTextResult> texts, bool video)
    {
        var values = new List<int>();
        foreach (var title in texts.Where(t => t.Confidence >= 0.6))
        {
            var line = string.Concat(texts.Where(t => t.Confidence >= 0.6 &&
                    Math.Abs(t.NormalizedBox.CenterY - title.NormalizedBox.CenterY) <=
                    Math.Min(t.NormalizedBox.Height, title.NormalizedBox.Height) * 0.5)
                .OrderBy(t => t.NormalizedBox.CenterX).Select(t => t.Text));
            line = Regex.Replace(line.Normalize(NormalizationForm.FormKC), @"\s+", "").ToUpperInvariant();
            // At small native resolutions OCR can read '/' as '1': (2/4) -> (214).
            // Accept only this ambiguity inside a known media heading, with one
            // numerator digit and the exact field capacity. Never replace '1' globally
            // or infer a missing separator. The caller still requires two fresh frames.
            var pattern = video
                ? @"(?:SHOWCASEVIDEOS?|展示视频)\(([01])[/1]1\)"
                : @"(?:MORESHOWCASEIMAGES|更多展示图片)\(([0-4])[/1]4\)";
            foreach (Match match in Regex.Matches(line, pattern))
                if (int.TryParse(match.Groups[1].Value, out var count) && count <= (video ? 1 : 4))
                    values.Add(count);
        }
        var distinct = values.Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }
}
