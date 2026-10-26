using System.IO;
using System.Text.Json;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public static class YoloOnnxInferenceServiceSelfCheck
{
    public static async Task<YoloInferenceResult> RunAsync(
        string imagePath,
        string? outputJsonPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Self-check image does not exist: {imagePath}", imagePath);

        var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        using var service = new YoloOnnxInferenceService();
        var result = await service.PredictAsync(imageBytes, imagePath, cancellationToken);

        if (!string.IsNullOrWhiteSpace(outputJsonPath))
        {
            var directory = Path.GetDirectoryName(outputJsonPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(
                outputJsonPath,
                JsonSerializer.Serialize(result, options),
                cancellationToken);
        }

        return result;
    }
}
