namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed class YoloModelAssets
{
    public YoloModelAssets(
        string assetDirectory,
        string modelPath,
        string onnxModelPath,
        string classesPath,
        string thresholdsPath,
        IReadOnlyList<string> classes,
        IReadOnlyDictionary<string, double> thresholds,
        double defaultThreshold)
    {
        AssetDirectory = assetDirectory;
        ModelPath = modelPath;
        OnnxModelPath = onnxModelPath;
        ClassesPath = classesPath;
        ThresholdsPath = thresholdsPath;
        Classes = classes;
        Thresholds = thresholds;
        DefaultThreshold = defaultThreshold;
    }

    public string AssetDirectory { get; }
    public string ModelPath { get; }
    public string OnnxModelPath { get; }
    public string ClassesPath { get; }
    public string ThresholdsPath { get; }
    public IReadOnlyList<string> Classes { get; }
    public IReadOnlyDictionary<string, double> Thresholds { get; }
    public double DefaultThreshold { get; }

    public double GetThreshold(string className) =>
        Thresholds.TryGetValue(className, out var threshold)
            ? threshold
            : DefaultThreshold;
}
