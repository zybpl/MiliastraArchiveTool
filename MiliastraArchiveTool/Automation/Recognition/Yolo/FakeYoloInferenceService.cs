namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed class FakeYoloInferenceService : IYoloInferenceService
{
    private readonly IReadOnlyList<YoloDetection> _detections;
    private readonly int _imageWidth;
    private readonly int _imageHeight;

    public FakeYoloInferenceService(
        IReadOnlyList<YoloDetection>? detections = null,
        int imageWidth = 0,
        int imageHeight = 0)
    {
        _detections = detections ?? [];
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
    }

    public Task<YoloInferenceResult> PredictAsync(
        byte[] imageBytes,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new YoloInferenceResult(
            sourceDescription,
            _imageWidth,
            _imageHeight,
            _detections));
    }
}
