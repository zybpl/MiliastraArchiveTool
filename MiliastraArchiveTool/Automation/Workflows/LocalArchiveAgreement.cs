using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

internal static class LocalArchiveAgreement
{
    private static string Normalize(string text) => string.Concat(text.Normalize().Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static (bool NoticeVisible, AutomationTarget? Agree) Read(IReadOnlyList<WorkflowOcrTextResult> texts)
    {
        var reliable = texts.Where(t => t.Confidence >= .6).ToList();
        var center = reliable.Where(t => t.NormalizedBox.CenterX is > .25 and < .75 &&
            t.NormalizedBox.CenterY is > .25 and < .75).ToList();
        var title = center.Any(t => Normalize(t.Text) is "须知" or "須知" or "NOTICE" or "IMPORTANTNOTICE");
        var terms = center.Any(t => Normalize(t.Text).Contains("条款") || Normalize(t.Text).Contains("條款") || Normalize(t.Text).Contains("TERMS"));
        var archive = reliable.Any(t => t.NormalizedBox.CenterY < .18 && Normalize(t.Text) is "本地存档" or "本地存檔" or "LOCALARCHIVES" or "LOCALSAVES" or "LOCALARCHIVE" or "LOCALSAVE");
        var agree = center.Where(t => t.NormalizedBox.CenterX > .5 && t.NormalizedBox.CenterY > .5 && Normalize(t.Text) is "同意" or "AGREE").ToList();
        var reject = center.Where(t => t.NormalizedBox.CenterX < .5 && t.NormalizedBox.CenterY > .5 && Normalize(t.Text) is "拒绝" or "拒絕" or "DECLINE" or "REJECT" or "DISAGREE").ToList();
        var visible = title || terms || agree.Count > 0 || reject.Count > 0;
        if (!archive || !title || !terms || agree.Count != 1 || reject.Count != 1 ||
            Math.Abs(agree[0].NormalizedBox.CenterY - reject[0].NormalizedBox.CenterY) > .04)
            return (visible, null);
        var box = agree[0].NormalizedBox;
        return (true, new AutomationTarget("local_archive_agree", box.CenterX, box.CenterY, box.Width, box.Height, agree[0].Confidence));
    }
}
