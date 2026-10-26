namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed record YoloInferenceResult(
    string SourceDescription,
    int ImageWidth,
    int ImageHeight,
    IReadOnlyList<YoloDetection> Detections);
