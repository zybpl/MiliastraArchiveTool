using System.IO;
using System.Globalization;
using System.Text.Json;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed class YoloModelAssetService
{
    private const string DefaultThresholdKey = "default";

    private readonly YoloModelAssetOptions _options;

    public YoloModelAssetService(YoloModelAssetOptions? options = null)
    {
        _options = options ?? new YoloModelAssetOptions();
    }

    public YoloModelAssets LoadStage1Assets()
    {
        var assetDirectory = _options.AssetDirectory;
        var modelPath = Path.Combine(assetDirectory, _options.ModelFileName);
        var onnxModelPath = Path.Combine(assetDirectory, _options.OnnxFileName);
        var classesPath = Path.Combine(assetDirectory, _options.ClassesFileName);
        var thresholdsPath = Path.Combine(assetDirectory, _options.ThresholdsFileName);

        EnsureFileExists(modelPath, "Model file");
        EnsureFileExists(onnxModelPath, "ONNX model file");
        EnsureFileExists(classesPath, "classes.txt");
        EnsureFileExists(thresholdsPath, "thresholds.json");

        var classes = LoadClasses(classesPath);
        var thresholds = LoadThresholds(thresholdsPath);
        var defaultThreshold = thresholds[DefaultThresholdKey];

        return new YoloModelAssets(
            assetDirectory,
            modelPath,
            onnxModelPath,
            classesPath,
            thresholdsPath,
            classes,
            thresholds,
            defaultThreshold);
    }

    private static IReadOnlyList<string> LoadClasses(string classesPath)
    {
        var lines = File.ReadAllLines(classesPath)
            .Select(line => line.Trim())
            .ToList();

        var emptyLineIndex = lines.FindIndex(string.IsNullOrWhiteSpace);
        if (emptyLineIndex >= 0)
        {
            throw new InvalidOperationException(
                $"classes.txt contains an empty line at 1-based line {emptyLineIndex + 1}: {classesPath}");
        }

        if (lines.Count == 0)
            throw new InvalidOperationException($"classes.txt contains no classes: {classesPath}");

        var duplicate = lines
            .GroupBy(line => line, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            throw new InvalidOperationException(
                $"classes.txt contains duplicate class '{duplicate.Key}': {classesPath}");
        }

        return lines;
    }

    private static IReadOnlyDictionary<string, double> LoadThresholds(string thresholdsPath)
    {
        Dictionary<string, double>? thresholds;
        try
        {
            thresholds = JsonSerializer.Deserialize<Dictionary<string, double>>(
                File.ReadAllText(thresholdsPath));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"thresholds.json could not be parsed: {thresholdsPath}. {ex.Message}",
                ex);
        }

        if (thresholds == null || thresholds.Count == 0)
            throw new InvalidOperationException($"thresholds.json contains no thresholds: {thresholdsPath}");

        if (!thresholds.ContainsKey(DefaultThresholdKey))
            throw new InvalidOperationException($"thresholds.json is missing required 'default' threshold: {thresholdsPath}");

        foreach (var (key, value) in thresholds)
        {
            if (double.IsNaN(value) || value < 0 || value > 1)
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"thresholds.json value for '{key}' is out of range 0..1: {value}. File: {thresholdsPath}"));
            }
        }

        return thresholds;
    }

    private static void EnsureFileExists(string path, string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{description} does not exist: {path}", path);
    }
}
