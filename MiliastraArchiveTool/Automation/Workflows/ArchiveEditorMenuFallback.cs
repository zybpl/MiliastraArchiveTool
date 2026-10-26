using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;
using OpenCvSharp;

namespace MiliastraArchiveTool.Automation.Workflows;

// Only used while returning from the editor. A template alone never authorizes input.
internal static class ArchiveEditorMenuFallback
{
    internal static readonly NormalizedBox MenuTextBox = new(.125, .51, .18, .25);
    private static readonly string[][] MenuLabels =
    [
        ["Manage Character Preview", "角色预览管理", "管理角色预览"],
        ["Timer Management", "计时器管理", "管理计时器"],
        ["Manage Stage Variable", "关卡变量管理", "管理关卡变量"],
        ["Preset Point Management", "预设点管理"],
        ["Skill Resource Management", "技能资源管理"],
        ["Manage Background Music", "背景音乐管理", "管理背景音乐"]
    ];

    internal static bool ConfirmsMenu(IReadOnlyList<WorkflowOcrTextResult> texts) =>
        MenuLabels.Count(aliases => texts.Any(text => text.Confidence >= .65 &&
            aliases.Any(alias => RapidOcrWorkflowOcrService.NormalizePreviewText(alias) == text.NormalizedText))) >= 2;

    internal static AutomationTarget? LocateExit(BitmapSource bitmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Native-pixel search: scale the icon template, never the OCR input or screenshot.
        var rect = new System.Windows.Int32Rect(0, (int)(bitmap.PixelHeight * .93),
            Math.Max(1, (int)(bitmap.PixelWidth * .04)), Math.Max(1, (int)(bitmap.PixelHeight * .065)));
        var crop = new CroppedBitmap(bitmap, rect);
        var gray = new FormatConvertedBitmap(crop, PixelFormats.Gray8, null, 0);
        var pixels = new byte[gray.PixelWidth * gray.PixelHeight];
        gray.CopyPixels(pixels, gray.PixelWidth, 0);
        using var source = new Mat(gray.PixelHeight, gray.PixelWidth, MatType.CV_8UC1);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, source.Data, pixels.Length);
        Cv2.Threshold(source, source, 140, 255, ThresholdTypes.Binary);
        using var stream = typeof(ArchiveEditorMenuFallback).Assembly.GetManifestResourceStream(
            "MiliastraArchiveTool.Automation.Workflows.Assets.archive-editor-exit.png")!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        using var template = Cv2.ImDecode(bytes.ToArray(), ImreadModes.Grayscale);
        Cv2.Threshold(template, template, 140, 255, ThresholdTypes.Binary);
        double best = 0;
        Rect bestRect = default;
        var candidates = new List<(double Score, Rect Rect)>();
        foreach (var adjustment in new[] { .85, .925, 1.0, 1.075, 1.15 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = Math.Max(8, (int)Math.Round(19 * bitmap.PixelHeight / 720.0 * adjustment));
            if (size >= source.Width || size >= source.Height) continue;
            using var scaled = new Mat();
            Cv2.Resize(template, scaled, new Size(size, size), 0, 0, InterpolationFlags.Nearest);
            using var result = new Mat();
            Cv2.MatchTemplate(source, scaled, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var score, out _, out var point);
            var match = new Rect(point.X, point.Y, size, size);
            candidates.Add((score, match));
            if (score > best) { best = score; bestRect = match; }
            // Reject a second spatially separate icon with a similarly strong match.
            var excluded = new Rect(Math.Max(0, point.X - size / 2), Math.Max(0, point.Y - size / 2),
                Math.Min(result.Width, point.X + size / 2 + 1) - Math.Max(0, point.X - size / 2),
                Math.Min(result.Height, point.Y + size / 2 + 1) - Math.Max(0, point.Y - size / 2));
            using (var peak = new Mat(result, excluded)) peak.SetTo(0);
            Cv2.MinMaxLoc(result, out _, out var second, out _, out var secondPoint);
            candidates.Add((second, new Rect(secondPoint.X, secondPoint.Y, size, size)));
        }
        if (best < .82) return null;
        double X(Rect r) => r.X + r.Width / 2.0;
        double Y(Rect r) => r.Y + r.Height / 2.0;
        if (candidates.Any(c => c.Score >= .82 &&
            (Math.Abs(X(c.Rect) - X(bestRect)) > bestRect.Width * .6 ||
             Math.Abs(Y(c.Rect) - Y(bestRect)) > bestRect.Height * .6))) return null;
        return new AutomationTarget(WorkflowTargetNames.ArchiveEditorExitButton,
            (rect.X + X(bestRect)) / bitmap.PixelWidth, (rect.Y + Y(bestRect)) / bitmap.PixelHeight,
            bestRect.Width / (double)bitmap.PixelWidth, bestRect.Height / (double)bitmap.PixelHeight, best);
    }
}
