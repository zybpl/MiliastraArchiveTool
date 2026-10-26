namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public interface IYoloInferenceService
{
    Task<YoloInferenceResult> PredictAsync(
        byte[] imageBytes,
        string sourceDescription,
        CancellationToken cancellationToken);
}
