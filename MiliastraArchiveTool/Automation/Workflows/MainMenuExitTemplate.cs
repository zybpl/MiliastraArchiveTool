using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;
using OpenCvSharp;

namespace MiliastraArchiveTool.Automation.Workflows;

// Only called on a confirmed Paimon menu, when its YOLO exit target is missing.
internal static class MainMenuExitTemplate
{
    private static readonly byte[] Template = LoadTemplate();

    private static byte[] LoadTemplate()
    {
        using var stream = typeof(MainMenuExitTemplate).Assembly.GetManifestResourceStream(
            "MiliastraArchiveTool.Automation.Workflows.Assets.main-menu-exit.png")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal static AutomationTarget? LocateExit(BitmapSource bitmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var top = (int)(bitmap.PixelHeight * .88);
        var rect = new System.Windows.Int32Rect(0, top,
            Math.Max(1, (int)(bitmap.PixelWidth * .06)), bitmap.PixelHeight - top);
        var gray = new FormatConvertedBitmap(new CroppedBitmap(bitmap, rect), PixelFormats.Gray8, null, 0);
        var pixels = new byte[gray.PixelWidth * gray.PixelHeight];
        gray.CopyPixels(pixels, gray.PixelWidth, 0);
        using var source = new Mat(gray.PixelHeight, gray.PixelWidth, MatType.CV_8UC1);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, source.Data, pixels.Length);
        using var template = Cv2.ImDecode(Template, ImreadModes.Grayscale);
        double best = 0;
        Rect bestRect = default;
        var candidates = new List<(double Score, Rect Rect)>();
        // Match native screenshot pixels. Only the reference icon is resized.
        var baseHeight = template.Height * bitmap.PixelHeight / 1080.0;
        for (var height = Math.Max(8, (int)(baseHeight * .75)); height <= baseHeight * 1.25; height++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = (int)Math.Round(template.Width * height / (double)template.Height);
            if (width > source.Width || height > source.Height) continue;
            using var scaled = new Mat();
            using var result = new Mat();
            Cv2.Resize(template, scaled, new Size(width, height));
            Cv2.MatchTemplate(source, scaled, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var score, out _, out var point);
            var match = new Rect(point.X, point.Y, width, height);
            candidates.Add((score, match));
            if (double.IsFinite(score) && score > best) { best = score; bestRect = match; }
            var x = Math.Max(0, point.X - width / 2);
            var y = Math.Max(0, point.Y - height / 2);
            using (var peak = new Mat(result, new Rect(x, y,
                Math.Min(result.Width, point.X + width / 2 + 1) - x,
                Math.Min(result.Height, point.Y + height / 2 + 1) - y))) peak.SetTo(0);
            Cv2.MinMaxLoc(result, out _, out var second, out _, out var secondPoint);
            candidates.Add((second, new Rect(secondPoint.X, secondPoint.Y, width, height)));
        }
        if (best < .82) return null;
        double X(Rect r) => r.X + r.Width / 2.0;
        double Y(Rect r) => r.Y + r.Height / 2.0;
        if (candidates.Any(c => c.Score >= .82 &&
            (Math.Abs(X(c.Rect) - X(bestRect)) > bestRect.Width * .6 ||
             Math.Abs(Y(c.Rect) - Y(bestRect)) > bestRect.Height * .6))) return null;
        return new AutomationTarget(WorkflowTargetNames.MainMenuExitButton,
            X(bestRect) / bitmap.PixelWidth, (top + Y(bestRect)) / bitmap.PixelHeight,
            bestRect.Width / (double)bitmap.PixelWidth, bestRect.Height / (double)bitmap.PixelHeight, best);
    }
}
