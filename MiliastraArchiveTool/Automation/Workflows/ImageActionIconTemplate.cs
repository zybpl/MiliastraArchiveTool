using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace MiliastraArchiveTool.Automation.Workflows;

internal static class ImageActionIconTemplate
{
    private static readonly Dictionary<string, byte[]> Templates = new[] { "view", "delete", "upload", "upload-1600" }
        .ToDictionary(k => k, k =>
        {
            using var stream = typeof(ImageActionIconTemplate).Assembly.GetManifestResourceStream(
                $"MiliastraArchiveTool.Automation.Workflows.Assets.image-{k}-action.png")!;
            using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
        });

    internal static double Match(BitmapSource bitmap, WorkflowOcrTextResult label, string action, CancellationToken ct)
    {
        var box = label.NormalizedBox;
        var h = box.Height * bitmap.PixelHeight;
        var left = (box.CenterX - box.Width / 2) * bitmap.PixelWidth;
        var cy = box.CenterY * bitmap.PixelHeight;
        // Search only beside this OCR label. The icon may be included as an OCR prefix.
        int x = Math.Max(0, (int)(left - h * 2.5)), y = Math.Max(0, (int)(cy - h));
        int right = Math.Min(bitmap.PixelWidth, (int)(left + h * 1.4));
        int bottom = Math.Min(bitmap.PixelHeight, (int)(cy + h));
        if (right <= x || bottom <= y || h < 5) return 0;
        var gray = new FormatConvertedBitmap(new CroppedBitmap(bitmap,
            new System.Windows.Int32Rect(x, y, right - x, bottom - y)), PixelFormats.Gray8, null, 0);
        var pixels = new byte[gray.PixelWidth * gray.PixelHeight]; gray.CopyPixels(pixels, gray.PixelWidth, 0);
        using var source = new Mat(gray.PixelHeight, gray.PixelWidth, MatType.CV_8UC1);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, source.Data, pixels.Length);
        double best = 0;
        foreach (var key in action == "upload" ? new[] { "upload", "upload-1600" } : new[] { action })
        {
            using var template = Cv2.ImDecode(Templates[key], ImreadModes.Grayscale);
            // Scale only the template; OCR and screenshots retain native resolution.
            for (int height = Math.Max(8, (int)Math.Floor(h * .65)); height <= Math.Ceiling(h * 1.55); height++)
            {
                ct.ThrowIfCancellationRequested();
                int width = Math.Max(8, (int)Math.Round(template.Width * height / (double)template.Height));
                if (height > source.Height || width > source.Width) continue;
                using var scaled = new Mat(); using var scores = new Mat();
                Cv2.Resize(template, scaled, new Size(width, height));
                Cv2.MatchTemplate(source, scaled, scores, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(scores, out double _, out double score);
                if (double.IsFinite(score)) best = Math.Max(best, score);
            }
        }
        return best;
    }
}

