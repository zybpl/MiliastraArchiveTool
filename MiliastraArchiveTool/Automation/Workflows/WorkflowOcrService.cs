using MiliastraArchiveTool.Infrastructure;
using System.Windows;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

public interface IWorkflowOcrService
{
    string EngineInfo { get; }

    Task<IReadOnlyList<WorkflowOcrTextResult>> RecognizeTargetAsync(
        GameCaptureResult capture,
        AutomationTarget target,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowOcrTextResult>> RecognizeBoxAsync(
        GameCaptureResult capture,
        NormalizedBox box,
        string sourceDescription,
        CancellationToken cancellationToken);
}

public sealed record WorkflowOcrTextResult(
    string Text,
    string NormalizedText,
    NormalizedBox NormalizedBox,
    double Confidence);

public sealed class RapidOcrWorkflowOcrService : IWorkflowOcrService, IDisposable
{
    private readonly RapidOcrLocalArchiveCardTextRecognizer _recognizer;
    private bool _disposed;
    private readonly OcrRegionDiagnostics _diagnostics = new();
    private readonly ConditionalWeakTable<BitmapSource, Dictionary<string, IReadOnlyList<WorkflowOcrTextResult>>> _frameResults = new();
    public string? DiagnosticRunId { get; set; }

    public RapidOcrWorkflowOcrService(RapidOcrLocalArchiveCardTextRecognizer? recognizer = null)
    {
        _recognizer = recognizer ?? new RapidOcrLocalArchiveCardTextRecognizer();
    }

    public string EngineInfo => _recognizer.EngineInfo;

    public Task<IReadOnlyList<WorkflowOcrTextResult>> RecognizeTargetAsync(
        GameCaptureResult capture,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        var box = new NormalizedBox(target.CenterX, target.CenterY, target.Width, target.Height);
        return RecognizeBoxAsync(capture, box, target.Name, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowOcrTextResult>> RecognizeBoxAsync(
        GameCaptureResult capture,
        NormalizedBox box,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("OcrRegion", new { Region = sourceDescription, Box = box });
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();

        var roi = CreateRoiCapture(capture, box, sourceDescription);
        var cacheKey = $"{roi.Roi.X},{roi.Roi.Y},{roi.Roi.Width},{roi.Roi.Height}";
        var cache = capture.Bitmap.IsFrozen ? _frameResults.GetOrCreateValue(capture.Bitmap) : null;
        if (cache != null)
        {
            lock (cache)
                if (cache.TryGetValue(cacheKey, out var cached))
                {
                    WorkflowPerformanceTrace.Event("OcrCacheHit", "Reused", new { Region = sourceDescription, PixelCrop = cacheKey });
                    return cached;
                }
        }
        var trace = DiagnosticRunId != null && (sourceDescription.StartsWith("text_input_area/", StringComparison.Ordinal) ||
            sourceDescription.StartsWith("PreviewMedia/", StringComparison.Ordinal) ||
            sourceDescription is WorkflowTargetNames.MenuActionItem or WorkflowTargetNames.PrimaryActionButton or WorkflowTargetNames.SecondaryActionButton)
            ? _diagnostics.SaveBefore(DiagnosticRunId, sourceDescription, capture.Bitmap, roi.Capture.Bitmap,
                new { RequestedBox = box, PixelCrop = new { roi.Roi.X, roi.Roi.Y, roi.Roi.Width, roi.Roi.Height } })
            : null;
        try
        {
            var results = await _recognizer.RecognizeCardsAsync(roi.Capture, cancellationToken);
            var mapped = results
                .Select(result => MapRoiResultToOriginal(result, roi.Roi))
                .Select(result => new WorkflowOcrTextResult(
                    result.Text,
                    NormalizePreviewText(result.Text),
                    result.NormalizedBox,
                    result.Confidence))
                .ToList();
            OcrRegionDiagnostics.Complete(trace, mapped, null);
            if (cache != null)
                lock (cache)
                    cache[cacheKey] = mapped;
            return mapped;
        }
        catch (Exception ex)
        {
            OcrRegionDiagnostics.Complete(trace, null, ex.ToString());
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _recognizer.Dispose();
        _disposed = true;
    }

    public static string NormalizePreviewText(string value) =>
        RapidOcrLocalArchiveCardTextRecognizer.NormalizeForMatch(value)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

    private CardRoiCapture CreateRoiCapture(
        GameCaptureResult source,
        NormalizedBox box,
        string captureMethodSuffix)
    {
        var bitmap = CreateFrozenBitmapCopy(source.Bitmap);
        var width = Math.Max(1, bitmap.PixelWidth);
        var height = Math.Max(1, bitmap.PixelHeight);
        // Menu detector boxes are tight around letters; retain their native-pixel border.
        // Other workflow regions already include context, so avoid adding neighbouring text.
        var isMenuItem = captureMethodSuffix == WorkflowTargetNames.MenuActionItem;
        var paddingX = isMenuItem ? Math.Max(4, (int)Math.Round(box.Width * width * 0.08)) : 0;
        var paddingY = isMenuItem ? Math.Max(4, (int)Math.Round(box.Height * height * 0.12)) : 0;
        var x = Math.Clamp((int)Math.Floor((box.CenterX - box.Width / 2) * width) - paddingX, 0, width - 1);
        var y = Math.Clamp((int)Math.Floor((box.CenterY - box.Height / 2) * height) - paddingY, 0, height - 1);
        var x2 = Math.Clamp((int)Math.Ceiling((box.CenterX + box.Width / 2) * width) + paddingX, x + 1, width);
        var y2 = Math.Clamp((int)Math.Ceiling((box.CenterY + box.Height / 2) * height) + paddingY, y + 1, height);
        var rect = new Int32Rect(x, y, x2 - x, y2 - y);
        var cropped = new CroppedBitmap(bitmap, rect);
        cropped.Freeze();

        BitmapSource roiBitmap = cropped;
        const int roiScale = 1; // Preserve captured pixels for all OCR regions.


        var roi = new NormalizedPixelRoi(x, y, rect.Width, rect.Height, width, height, roiScale);
        var capture = new GameCaptureResult(
            source.Window,
            roiBitmap,
            source.CaptureMethod + " / workflow OCR ROI / " + captureMethodSuffix,
            source.GdiQuality,
            source.PrintWindowQuality,
            source.DxgiQuality,
            source.Warning,
            source.Strategy,
            source.CandidateQualities,
            source.IsLikelyBlackFrame,
            source.FallbackToGdi);
        return new CardRoiCapture(capture, roi);
    }

    private static BitmapSource CreateFrozenBitmapCopy(BitmapSource source)
    {
        var copy = new WriteableBitmap(source);
        copy.Freeze();
        return copy;
    }

    private static LocalArchiveCardTextResult MapRoiResultToOriginal(
        LocalArchiveCardTextResult result,
        NormalizedPixelRoi roi)
    {
        var box = result.NormalizedBox;
        var centerX = (roi.X + box.CenterX * roi.Width) / roi.SourceWidth;
        var centerY = (roi.Y + box.CenterY * roi.Height) / roi.SourceHeight;
        var width = box.Width * roi.Width / roi.SourceWidth;
        var height = box.Height * roi.Height / roi.SourceHeight;
        return result with
        {
            NormalizedBox = new NormalizedBox(
                Math.Clamp(centerX, 0, 1),
                Math.Clamp(centerY, 0, 1),
                Math.Clamp(width, 0, 1),
                Math.Clamp(height, 0, 1))
        };
    }

    private sealed record CardRoiCapture(GameCaptureResult Capture, NormalizedPixelRoi Roi);

    private sealed record NormalizedPixelRoi(
        int X,
        int Y,
        int Width,
        int Height,
        int SourceWidth,
        int SourceHeight,
        int RoiScale);
}


