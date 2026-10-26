namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed record YoloDetection(
    int ClassId,
    string ClassName,
    double Confidence,
    double X1,
    double Y1,
    double X2,
    double Y2,
    double CenterXNormalized,
    double CenterYNormalized,
    double WidthNormalized,
    double HeightNormalized);
