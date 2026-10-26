using System.Windows.Media.Imaging;

namespace MiliastraArchiveTool.Automation.Recognition;

public sealed class FakePageRecognizer : IPageRecognizer
{
    private readonly PageRecognitionResult _result;

    public FakePageRecognizer(
        GamePage page = GamePage.Unknown,
        double confidence = 0,
        string source = "Fake",
        IReadOnlyList<AutomationTarget>? targets = null)
    {
        _result = new PageRecognitionResult(
            page,
            confidence,
            source,
            targets ?? Array.Empty<AutomationTarget>());
    }

    public Task<PageRecognitionResult> RecognizeAsync(
        BitmapSource bitmap,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_result);
    }
}
