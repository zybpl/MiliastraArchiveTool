using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;
using OpenCvSharp;

namespace MiliastraArchiveTool.Automation.Workflows;

public interface IUpdateArchiveButtonLocator
{
    UpdateArchiveButtonLocationResult Locate(
        GameCaptureResult capture,
        CancellationToken cancellationToken);
}

public sealed record UpdateArchiveButtonLocationResult(
    bool Matched,
    AutomationTarget? Target,
    double Confidence,
    NormalizedBox SearchRoi,
    string? RejectedReason);

public sealed class OpenCvUpdateArchiveButtonLocator : IUpdateArchiveButtonLocator
{
    private static readonly NormalizedBox SearchRoi = new(0.855, 0.915, 0.285, 0.165);

    public UpdateArchiveButtonLocationResult Locate(
        GameCaptureResult capture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();

        using var bgr = BitmapSourceToBgrMat(capture.Bitmap);
        var sourceWidth = bgr.Width;
        var sourceHeight = bgr.Height;
        var search = RectFromNormalized(sourceWidth, sourceHeight, SearchRoi);
        if (search.Width <= 8 || search.Height <= 8)
        {
            return new UpdateArchiveButtonLocationResult(
                false,
                null,
                0,
                SearchRoi,
                "OpenCV update button search ROI is too small.");
        }

        using var roi = new Mat(bgr, search);
        using var hsv = new Mat();
        Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);

        using var brightLowSaturation = new Mat();
        Cv2.InRange(
            hsv,
            new Scalar(0, 0, 135),
            new Scalar(180, 125, 255),
            brightLowSaturation);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(11, 7));
        Cv2.MorphologyEx(brightLowSaturation, brightLowSaturation, MorphTypes.Close, kernel);
        Cv2.MorphologyEx(brightLowSaturation, brightLowSaturation, MorphTypes.Open, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 3)));

        Cv2.FindContours(
            brightLowSaturation,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var candidates = new List<(Rect Rect, double Score, double Fill, double CenterBias)>();
        foreach (var contour in contours)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rect = Cv2.BoundingRect(contour);
            var normalizedWidth = rect.Width / (double)sourceWidth;
            var normalizedHeight = rect.Height / (double)sourceHeight;
            if (normalizedWidth < 0.095 || normalizedWidth > 0.300)
                continue;
            if (normalizedHeight < 0.030 || normalizedHeight > 0.095)
                continue;

            var aspect = rect.Width / (double)Math.Max(1, rect.Height);
            if (aspect < 2.2 || aspect > 8.2)
                continue;

            var globalCenterX = (search.X + rect.X + rect.Width / 2.0) / sourceWidth;
            var globalCenterY = (search.Y + rect.Y + rect.Height / 2.0) / sourceHeight;
            if (globalCenterX < 0.72 || globalCenterX > 0.99 || globalCenterY < 0.82 || globalCenterY > 0.99)
                continue;

            var area = Cv2.ContourArea(contour);
            var fill = area / Math.Max(1, rect.Width * rect.Height);
            if (fill < 0.25)
                continue;

            var expectedDistance = Math.Sqrt(
                Math.Pow((globalCenterX - 0.88) / 0.18, 2) +
                Math.Pow((globalCenterY - 0.94) / 0.12, 2));
            var centerBias = 1.0 - Math.Clamp(expectedDistance, 0, 1);
            var sizeScore = 1.0 - Math.Clamp(Math.Abs(normalizedWidth - 0.17) / 0.17, 0, 1);
            var score = fill * 0.45 + centerBias * 0.35 + sizeScore * 0.20;
            candidates.Add((rect, score, fill, centerBias));
        }

        if (candidates.Count == 0)
        {
            return new UpdateArchiveButtonLocationResult(
                false,
                null,
                0,
                SearchRoi,
                "OpenCV did not find a bright button-like contour in the UpdateArchiveSelect lower-right ROI.");
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Rect.Width * candidate.Rect.Height)
            .ToList();
        var best = ordered[0];
        if (ordered.Count > 1 && ordered[1].Score > best.Score * 0.92)
        {
            return new UpdateArchiveButtonLocationResult(
                false,
                null,
                best.Score,
                SearchRoi,
                $"OpenCV found ambiguous update button candidates. best={best.Score:F2}, second={ordered[1].Score:F2}.");
        }

        var centerX = search.X + best.Rect.X + best.Rect.Width / 2.0;
        var centerY = search.Y + best.Rect.Y + best.Rect.Height / 2.0;
        var target = new AutomationTarget(
            WorkflowTargetNames.PrimaryActionButton,
            Math.Clamp(centerX / sourceWidth, 0, 1),
            Math.Clamp(centerY / sourceHeight, 0, 1),
            Math.Clamp(best.Rect.Width / (double)sourceWidth, 0.08, 0.32),
            Math.Clamp(best.Rect.Height / (double)sourceHeight, 0.025, 0.11),
            Math.Clamp(best.Score, 0, 1));

        return new UpdateArchiveButtonLocationResult(
            true,
            target,
            target.Confidence,
            SearchRoi,
            $"opencv_bright_button fill={best.Fill:F2} centerBias={best.CenterBias:F2}");
    }

    private static Rect RectFromNormalized(int sourceWidth, int sourceHeight, NormalizedBox box)
    {
        var left = Math.Clamp(box.CenterX - box.Width / 2, 0, 1);
        var top = Math.Clamp(box.CenterY - box.Height / 2, 0, 1);
        var right = Math.Clamp(box.CenterX + box.Width / 2, 0, 1);
        var bottom = Math.Clamp(box.CenterY + box.Height / 2, 0, 1);

        var x = Math.Clamp((int)Math.Floor(left * sourceWidth), 0, sourceWidth - 1);
        var y = Math.Clamp((int)Math.Floor(top * sourceHeight), 0, sourceHeight - 1);
        var width = Math.Clamp((int)Math.Ceiling((right - left) * sourceWidth), 1, sourceWidth - x);
        var height = Math.Clamp((int)Math.Ceiling((bottom - top) * sourceHeight), 1, sourceHeight - y);
        return new Rect(x, y, width, height);
    }

    private static Mat BitmapSourceToBgrMat(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        if (converted.CanFreeze)
            converted.Freeze();

        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var bgra = new Mat(converted.PixelHeight, converted.PixelWidth, MatType.CV_8UC4);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bgra.Data, pixels.Length);
        var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        bgra.Dispose();
        return bgr;
    }
}
