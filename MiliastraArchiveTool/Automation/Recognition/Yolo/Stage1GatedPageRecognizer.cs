using System.Windows.Media.Imaging;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed class Stage1GatedPageRecognizer : IPageRecognizer, IDisposable
{
    private readonly IPageRecognizer _inner;
    private readonly bool _ownsInner;

    private bool _disposed;

    public Stage1GatedPageRecognizer()
    {
        _inner = new YoloPageRecognizer();
        _ownsInner = true;
    }

    public Stage1GatedPageRecognizer(IPageRecognizer inner, bool disposeInner = false)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ownsInner = disposeInner;
    }

    public async Task<PageRecognitionResult> RecognizeAsync(
        BitmapSource bitmap,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var recognition = await _inner.RecognizeAsync(bitmap, cancellationToken);
        if (Stage1RecognitionCapabilities.IsSafeForStage1(recognition.Page))
            return recognition;

        if (Stage1RecognitionCapabilities.IsUnsupportedOrUnsafe(recognition.Page))
        {
            var reason = Stage1RecognitionCapabilities.GetStage1CapabilityReason(recognition.Page);
            return new PageRecognitionResult(
                GamePage.Unknown,
                recognition.Confidence,
                $"{recognition.Source}; Stage1 gated: {recognition.Page}. {reason}",
                recognition.Targets);
        }

        return recognition.Page == GamePage.Unknown
            ? recognition
            : new PageRecognitionResult(
                GamePage.Unknown,
                recognition.Confidence,
                $"{recognition.Source}; Stage1 gated: {recognition.Page}. {Stage1RecognitionCapabilities.GetStage1CapabilityReason(recognition.Page)}",
                recognition.Targets);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_ownsInner && _inner is IDisposable disposable)
            disposable.Dispose();

        _disposed = true;
    }
}
