using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

/// <summary>Keep OCR inside the intended control and join only neighbouring words on one line.</summary>
public static class ArchiveOcrTextLayout
{
    public static IReadOnlyList<WorkflowOcrTextResult> Lines(
        IEnumerable<WorkflowOcrTextResult> texts, NormalizedBox region, int width, int height)
    {
        var kept = texts.Where(t => ContainsCenter(region, t.NormalizedBox))
            .OrderBy(t => t.NormalizedBox.CenterY).ThenBy(t => Left(t.NormalizedBox)).ToList();
        var groups = new List<List<WorkflowOcrTextResult>>();
        foreach (var text in kept)
        {
            var line = groups.FirstOrDefault(g => Math.Abs(g[0].NormalizedBox.CenterY - text.NormalizedBox.CenterY)
                <= Math.Min(g[0].NormalizedBox.Height, text.NormalizedBox.Height) * .35);
            if (line == null) groups.Add([text]); else line.Add(text);
        }
        var result = new List<WorkflowOcrTextResult>();
        foreach (var line in groups)
        {
            WorkflowOcrTextResult? pending = null;
            foreach (var text in line.OrderBy(t => Left(t.NormalizedBox)))
            {
                if (pending != null)
                {
                    var a = pending.NormalizedBox; var b = text.NormalizedBox;
                    var gap = (Left(b) - (a.CenterX + a.Width / 2)) * width;
                    if (gap >= 0 && gap <= Math.Min(a.Height, b.Height) * height * .55)
                    {
                        var left = Left(a); var right = b.CenterX + b.Width / 2;
                        var top = Math.Min(a.CenterY-a.Height/2,b.CenterY-b.Height/2);
                        var bottom = Math.Max(a.CenterY+a.Height/2,b.CenterY+b.Height/2);
                        var combined = pending.Text + " " + text.Text;
                        pending = new(combined, RapidOcrWorkflowOcrService.NormalizePreviewText(combined),
                            new((left+right)/2,(top+bottom)/2,right-left,bottom-top),Math.Min(pending.Confidence,text.Confidence));
                        continue;
                    }
                    result.Add(pending);
                }
                pending = text;
            }
            if (pending != null) result.Add(pending);
        }
        return result;
    }

    public static NormalizedBox Box(AutomationTarget target) => new(target.CenterX,target.CenterY,target.Width,target.Height);
    public static double Left(NormalizedBox box) => box.CenterX-box.Width/2;
    private static bool ContainsCenter(NormalizedBox region, NormalizedBox text) =>
        text.CenterX >= Left(region) && text.CenterX <= region.CenterX+region.Width/2 &&
        text.CenterY >= region.CenterY-region.Height/2 && text.CenterY <= region.CenterY+region.Height/2;

    public static IReadOnlyList<LocalArchiveCardTextResult> CardLines(IEnumerable<LocalArchiveCardTextResult> texts,
        NormalizedBox region, int width, int height) => Lines(texts.Select(t =>
            new WorkflowOcrTextResult(t.Text, RapidOcrWorkflowOcrService.NormalizePreviewText(t.Text),t.NormalizedBox,t.Confidence)),region,width,height)
            .Select(t=>new LocalArchiveCardTextResult(t.Text,t.NormalizedBox,t.Confidence)).ToList();
}
