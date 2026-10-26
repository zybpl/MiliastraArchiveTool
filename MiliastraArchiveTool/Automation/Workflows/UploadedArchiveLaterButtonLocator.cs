using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;
using OpenCvSharp;

namespace MiliastraArchiveTool.Automation.Workflows;

public interface IUploadedArchiveLaterButtonLocator
{
    UploadedArchiveLaterButtonLocationResult Locate(
        GameCaptureResult capture,
        CancellationToken cancellationToken);
}

public sealed record UploadedArchiveLaterButtonLocationResult(
    bool Matched,
    AutomationTarget? Target,
    double Confidence,
    NormalizedBox SearchRoi,
    string? RejectedReason);

public sealed class OpenCvUploadedArchiveLaterButtonLocator : IUploadedArchiveLaterButtonLocator
{
    private static readonly NormalizedBox SearchRoi = new(0.435, 0.575, 0.245, 0.155);

    public UploadedArchiveLaterButtonLocationResult Locate(
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
            return new UploadedArchiveLaterButtonLocationResult(
                false,
                null,
                0,
                SearchRoi,
                "OpenCV uploaded-archive later search ROI is too small.");
        }

        using var roi = new Mat(bgr, search);
        using var gray = new Mat();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

        using var edges = new Mat();
        Cv2.Canny(gray, edges, 48, 132);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(13, 5));
        Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel);

        Cv2.FindContours(
            edges,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var candidates = new List<(Rect Rect, double Score, double EdgeFill, double CenterBias)>();
        foreach (var contour in contours)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rect = Cv2.BoundingRect(contour);
            var normalizedWidth = rect.Width / (double)sourceWidth;
            var normalizedHeight = rect.Height / (double)sourceHeight;
            if (normalizedWidth < 0.080 || normalizedWidth > 0.220)
                continue;
            if (normalizedHeight < 0.025 || normalizedHeight > 0.085)
                continue;

            var aspect = rect.Width / (double)Math.Max(1, rect.Height);
            if (aspect < 2.4 || aspect > 8.5)
                continue;

            var globalCenterX = (search.X + rect.X + rect.Width / 2.0) / sourceWidth;
            var globalCenterY = (search.Y + rect.Y + rect.Height / 2.0) / sourceHeight;
            if (globalCenterX < 0.30 || globalCenterX > 0.55 || globalCenterY < 0.48 || globalCenterY > 0.68)
                continue;

            using var contourMask = Mat.Zeros(edges.Size(), MatType.CV_8UC1).ToMat();
            Cv2.DrawContours(contourMask, [contour], -1, Scalar.White, 1);
            var edgeFill = Cv2.CountNonZero(contourMask) / (double)Math.Max(1, rect.Width * rect.Height);
            if (edgeFill < 0.025)
                continue;

            var expectedDistance = Math.Sqrt(
                Math.Pow((globalCenterX - 0.435) / 0.18, 2) +
                Math.Pow((globalCenterY - 0.575) / 0.12, 2));
            var centerBias = 1.0 - Math.Clamp(expectedDistance, 0, 1);
            var sizeScore = 1.0 - Math.Clamp(Math.Abs(normalizedWidth - 0.13) / 0.13, 0, 1);
            var score = centerBias * 0.48 + sizeScore * 0.32 + Math.Clamp(edgeFill / 0.10, 0, 1) * 0.20;
            candidates.Add((rect, score, edgeFill, centerBias));
        }

        if (candidates.Count == 0)
        {
            return new UploadedArchiveLaterButtonLocationResult(
                false,
                null,
                0,
                SearchRoi,
                "OpenCV did not find an outlined secondary button in the uploaded-archive prompt left-button ROI.");
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Rect.Width * candidate.Rect.Height)
            .ToList();
        var best = ordered[0];
        if (ordered.Count > 1 && ordered[1].Score > best.Score * 0.92)
        {
            return new UploadedArchiveLaterButtonLocationResult(
                false,
                null,
                best.Score,
                SearchRoi,
                $"OpenCV found ambiguous uploaded-archive later candidates. best={best.Score:F2}, second={ordered[1].Score:F2}.");
        }

        var centerX = search.X + best.Rect.X + best.Rect.Width / 2.0;
        var centerY = search.Y + best.Rect.Y + best.Rect.Height / 2.0;
        var target = new AutomationTarget(
            WorkflowTargetNames.SecondaryActionButton,
            Math.Clamp(centerX / sourceWidth, 0, 1),
            Math.Clamp(centerY / sourceHeight, 0, 1),
            Math.Clamp(best.Rect.Width / (double)sourceWidth, 0.07, 0.24),
            Math.Clamp(best.Rect.Height / (double)sourceHeight, 0.020, 0.10),
            Math.Clamp(best.Score, 0, 1));

        return new UploadedArchiveLaterButtonLocationResult(
            true,
            target,
            target.Confidence,
            SearchRoi,
            $"opencv_uploaded_archive_later edgeFill={best.EdgeFill:F3} centerBias={best.CenterBias:F2}");
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
