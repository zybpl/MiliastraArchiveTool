using System.IO;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed class YoloModelAssetOptions
{
    public string AssetDirectory { get; init; } = Path.Combine(
        AppContext.BaseDirectory,
        "Automation",
        "Recognition",
        "Yolo",
        "Assets",
        "stage1");

    public string ModelFileName { get; init; } = "best.pt";
    public string OnnxFileName { get; init; } = "best.onnx";
    public string ClassesFileName { get; init; } = "classes.txt";
    public string ThresholdsFileName { get; init; } = "thresholds.json";
}
