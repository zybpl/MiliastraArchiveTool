using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public static class Stage1GatedPageRecognizerSelfCheck
{
    public static async Task<Stage1GatedPageRecognizerSelfCheckResult> RunAsync(
        string imagePath,
        string? outputJsonPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Self-check image does not exist: {imagePath}", imagePath);

        var bitmap = LoadBitmap(imagePath);
        using var inferenceService = new YoloOnnxInferenceService();
        using var rawRecognizer = new YoloPageRecognizer(inferenceService);
        using var gatedRecognizer = new Stage1GatedPageRecognizer(rawRecognizer);

        var raw = await rawRecognizer.RecognizeAsync(bitmap, cancellationToken);
        var gated = await gatedRecognizer.RecognizeAsync(bitmap, cancellationToken);
        var reason = raw.Page == gated.Page
            ? Stage1RecognitionCapabilities.GetStage1CapabilityReason(raw.Page)
            : gated.Source;

        var result = new Stage1GatedPageRecognizerSelfCheckResult(
            imagePath,
            raw.Page,
            gated.Page,
            raw.Confidence,
            gated.Confidence,
            reason,
            gated.Targets);

        if (!string.IsNullOrWhiteSpace(outputJsonPath))
        {
            var directory = Path.GetDirectoryName(outputJsonPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };

            await File.WriteAllTextAsync(
                outputJsonPath,
                JsonSerializer.Serialize(result, options),
                cancellationToken);
        }

        return result;
    }

    private static BitmapSource LoadBitmap(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var bitmap = decoder.Frames.First();
        bitmap.Freeze();
        return bitmap;
    }
}

public sealed record Stage1GatedPageRecognizerSelfCheckResult(
    string ImagePath,
    GamePage RawPage,
    GamePage GatedPage,
    double RawConfidence,
    double GatedConfidence,
    string Reason,
    IReadOnlyList<AutomationTarget> Targets);
