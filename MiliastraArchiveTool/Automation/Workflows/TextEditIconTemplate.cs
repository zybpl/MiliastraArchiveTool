using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;
using OpenCvSharp;

namespace MiliastraArchiveTool.Automation.Workflows;

// Supplies visual candidates only. Callers must still establish the field/row semantics.
internal static class TextEditIconTemplate
{
    private static readonly byte[] TemplateBytes = LoadTemplate("text-edit-icon.png");
    private static readonly byte[] PanelTemplateBytes = LoadTemplate("info-panel-edit-icon.png");
    private static readonly byte[] UploadFailureBytes = LoadTemplate("image-upload-failed.png");
    internal static AutomationTarget? LocateUploadFailure(BitmapSource bitmap, CancellationToken ct) =>
        LocateCore(bitmap, new NormalizedBox(.5, .46, .38, .48), UploadFailureBytes, ct, 1600, 900)
            .FirstOrDefault(t => t.Confidence >= .90);
    private static byte[] LoadTemplate(string file)
    {
        using var stream = typeof(TextEditIconTemplate).Assembly.GetManifestResourceStream(
            "MiliastraArchiveTool.Automation.Workflows.Assets." + file)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal static IReadOnlyList<AutomationTarget> Locate(BitmapSource bitmap, NormalizedBox box,
        CancellationToken cancellationToken) => LocateCore(bitmap, box, TemplateBytes, cancellationToken);

    internal static IReadOnlyList<AutomationTarget> LocateInfoPanel(BitmapSource bitmap, NormalizedBox box,
        CancellationToken cancellationToken) => LocateCore(bitmap, box, PanelTemplateBytes, cancellationToken);

    private static IReadOnlyList<AutomationTarget> LocateCore(BitmapSource bitmap, NormalizedBox box,
        byte[] templateBytes, CancellationToken cancellationToken, double referenceWidth = 1280, double referenceHeight = 720)
    {
        var x = Math.Clamp((int)((box.CenterX - box.Width / 2) * bitmap.PixelWidth), 0, bitmap.PixelWidth - 1);
        var y = Math.Clamp((int)((box.CenterY - box.Height / 2) * bitmap.PixelHeight), 0, bitmap.PixelHeight - 1);
        var width = Math.Min(bitmap.PixelWidth - x, (int)(box.Width * bitmap.PixelWidth));
        var height = Math.Min(bitmap.PixelHeight - y, (int)(box.Height * bitmap.PixelHeight));
        if (width < 12 || height < 12) return [];
        var gray = new FormatConvertedBitmap(new CroppedBitmap(bitmap,
            new System.Windows.Int32Rect(x, y, width, height)), PixelFormats.Gray8, null, 0);
        var pixels = new byte[width * height];
        gray.CopyPixels(pixels, width, 0);
        using var source = new Mat(height, width, MatType.CV_8UC1);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, source.Data, pixels.Length);
        // Preserve anti-aliasing; only the template is resized, never the screenshot.
        // Keep native grayscale anti-aliasing.
        using var template = Cv2.ImDecode(templateBytes, ImreadModes.Grayscale);
        var candidates = new List<AutomationTarget>();
        var scale = Math.Min(bitmap.PixelWidth / referenceWidth, bitmap.PixelHeight / referenceHeight);
        foreach (var adjustment in new[] { .85, .925, 1.0, 1.075, 1.15 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scaled = new Mat();
            var w = Math.Max(12, (int)Math.Round(template.Width * scale * adjustment));
            var h = Math.Max(12, (int)Math.Round(template.Height * scale * adjustment));
            if (w >= width || h >= height) continue;
            Cv2.Resize(template, scaled, new Size(w, h), 0, 0, InterpolationFlags.Linear);
            
            using var scores = new Mat();
            Cv2.MatchTemplate(source, scaled, scores, TemplateMatchModes.CCoeffNormed);
            for (var count = 0; count < 40; count++)
            {
                Cv2.MinMaxLoc(scores, out _, out var score, out _, out var point);
                if (score < .82) break;
                candidates.Add(new AutomationTarget(WorkflowTargetNames.TextEditIcon,
                    (x + point.X + w / 2.0) / bitmap.PixelWidth,
                    (y + point.Y + h / 2.0) / bitmap.PixelHeight,
                    w / (double)bitmap.PixelWidth, h / (double)bitmap.PixelHeight, score));
                var left = Math.Max(0, point.X - w / 2);
                var top = Math.Max(0, point.Y - h / 2);
                using var peak = new Mat(scores, new Rect(left, top,
                    Math.Min(scores.Width, point.X + w / 2 + 1) - left,
                    Math.Min(scores.Height, point.Y + h / 2 + 1) - top));
                peak.SetTo(0);
            }
        }
        var unique = new List<AutomationTarget>();
        foreach (var candidate in candidates.OrderByDescending(t => t.Confidence))
            if (!unique.Any(t => SameLocation(t, candidate))) unique.Add(candidate);
        return unique;
    }

    internal static bool SameLocation(AutomationTarget a, AutomationTarget b) =>
        Math.Abs(a.CenterX - b.CenterX) < Math.Max(a.Width, b.Width) * .6 &&
        Math.Abs(a.CenterY - b.CenterY) < Math.Max(a.Height, b.Height) * .6;
}
