using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Automation.Workflows;

internal sealed class OcrRegionDiagnostics
{
    private readonly ConditionalWeakTable<BitmapSource, FrameFile> _frames = new();
    private sealed record FrameFile(string Path);

    public string? SaveBefore(string runId, string region, BitmapSource frame, BitmapSource crop, object coordinates)
    {
        try
        {
            var directory = ApplicationPaths.DiagnosticsDirectory;
            Directory.CreateDirectory(directory);
            var original = _frames.GetValue(frame, source =>
            {
                var path = Path.Combine(directory, $"stage1-ocr-frame-{runId}-{Guid.NewGuid():N}.png");
                Save(source, path);
                return new FrameFile(path);
            });
            var stem = Path.Combine(directory, $"stage1-ocr-roi-{runId}-{Guid.NewGuid():N}");
            Save(crop, stem + ".png");
            File.WriteAllText(stem + ".json", JsonSerializer.Serialize(new
            {
                RunId = runId, Region = region, CreatedAt = DateTimeOffset.Now,
                OriginalPath = original.Path, CropPath = stem + ".png", Coordinates = coordinates,
                SourceWidth = frame.PixelWidth, SourceHeight = frame.PixelHeight,
                CropWidth = crop.PixelWidth, CropHeight = crop.PixelHeight, Scale = 1,
                Status = "RecognitionPending"
            }, new JsonSerializerOptions { WriteIndented = true }));
            return stem + ".json";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OCR diagnostic save failed: {ex.Message}");
            return null;
        }
    }

    public static void Complete(string? path, object? results, string? error)
    {
        if (path == null) return;
        try
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            json["Status"] = error == null ? "Completed" : "Failed";
            json["Results"] = JsonSerializer.SerializeToNode(results);
            json["Error"] = error;
            File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"OCR diagnostic update failed: {ex.Message}"); }
    }

    private static void Save(BitmapSource source, string path)
    {
        // Copy pixels to avoid BitmapFrame decoder metadata thread affinity.
        var bitmap = new WriteableBitmap(source);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
