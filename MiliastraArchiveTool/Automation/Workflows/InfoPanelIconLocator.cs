using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;
using OpenCvSharp;

namespace MiliastraArchiveTool.Automation.Workflows;

public interface IInfoPanelIconLocator
{
    InfoPanelIconLocationResult Locate(
        GameCaptureResult capture,
        AutomationTarget row,
        string targetName,
        CancellationToken cancellationToken);
}

public sealed record InfoPanelIconLocationResult(
    bool Matched,
    AutomationTarget? Target,
    double Confidence,
    NormalizedBox SearchRoi,
    string? RejectedReason);

public sealed class OpenCvInfoPanelIconLocator : IInfoPanelIconLocator
{
    public InfoPanelIconLocationResult Locate(
        GameCaptureResult capture,
        AutomationTarget row,
        string targetName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(row);
        cancellationToken.ThrowIfCancellationRequested();

        var searchRoi = GetInfoPanelIconSearchRoi(row);
        using var bgr = BitmapSourceToBgrMat(capture.Bitmap);
        var sourceWidth = bgr.Width;
        var sourceHeight = bgr.Height;
        var search = RectFromNormalized(sourceWidth, sourceHeight, searchRoi);
        if (search.Width <= 4 || search.Height <= 4)
        {
            return new InfoPanelIconLocationResult(
                false,
                null,
                0,
                searchRoi,
                "OpenCV info-panel icon search ROI is too small.");
        }

        using var roi = new Mat(bgr, search);
        using var gray = new Mat();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);
        using var mask = new Mat();
        Cv2.Canny(blurred, mask, 20, 80);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);

        Cv2.FindContours(
            mask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var candidates = new List<(Rect Rect, double Score, double Fill, double CenterBias)>();
        foreach (var contour in contours)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rect = Cv2.BoundingRect(contour);
            if (rect.X <= 2 || rect.Y <= 2 || rect.Right >= roi.Width - 2 || rect.Bottom >= roi.Height - 2)
                continue;

            var normalizedWidth = rect.Width / (double)sourceWidth;
            var normalizedHeight = rect.Height / (double)sourceHeight;
            if (normalizedWidth < 0.006 || normalizedWidth > 0.045)
                continue;
            if (normalizedHeight < 0.010 || normalizedHeight > 0.060)
                continue;

            var aspect = rect.Width / (double)Math.Max(1, rect.Height);
            if (aspect < 0.30 || aspect > 2.8)
                continue;

            var globalCenterX = (search.X + rect.X + rect.Width / 2.0) / sourceWidth;
            var globalCenterY = (search.Y + rect.Y + rect.Height / 2.0) / sourceHeight;
            if (!IsTargetInside(new AutomationTarget(targetName, globalCenterX, globalCenterY, normalizedWidth, normalizedHeight, 1), row, 0.025, 0.055))
                continue;

            using var candidateMask = new Mat(mask, rect);
            var fill = Cv2.CountNonZero(candidateMask) / (double)Math.Max(1, rect.Width * rect.Height);
            if (fill < 0.02 || fill > 0.92)
                continue;

            var expectedX = searchRoi.CenterX + searchRoi.Width * 0.22;
            var expectedDistance = Math.Sqrt(
                Math.Pow((globalCenterX - expectedX) / Math.Max(0.01, searchRoi.Width), 2) +
                Math.Pow((globalCenterY - row.CenterY) / Math.Max(0.01, row.Height), 2));
            var centerBias = 1.0 - Math.Clamp(expectedDistance, 0, 1);
            var sizeScore = 1.0 - Math.Clamp(Math.Abs(normalizedHeight - 0.025) / 0.025, 0, 1);
            var score = fill * 0.34 + centerBias * 0.36 + sizeScore * 0.30;
            candidates.Add((rect, score, fill, centerBias));
        }

        if (candidates.Count == 0)
        {
            return new InfoPanelIconLocationResult(
                false,
                null,
                0,
                searchRoi,
                "OpenCV did not find a small icon edge inside the OCR-confirmed info-panel row right ROI.");
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Rect.Width * candidate.Rect.Height)
            .ToList();
        var best = ordered[0];
        if (ordered.Count > 1 && ordered[1].Score > best.Score * 0.90)
        {
            return new InfoPanelIconLocationResult(
                false,
                null,
                best.Score,
                searchRoi,
                $"OpenCV found ambiguous info-panel icon candidates. best={best.Score:F2}, second={ordered[1].Score:F2}.");
        }

        var centerX = search.X + best.Rect.X + best.Rect.Width / 2.0;
        var centerY = search.Y + best.Rect.Y + best.Rect.Height / 2.0;
        var target = new AutomationTarget(
            targetName,
            Math.Clamp(centerX / sourceWidth, 0, 1),
            Math.Clamp(centerY / sourceHeight, 0, 1),
            Math.Clamp(best.Rect.Width / (double)sourceWidth, 0.006, 0.040),
            Math.Clamp(best.Rect.Height / (double)sourceHeight, 0.010, 0.060),
            Math.Clamp(best.Score, 0, 1));

        return new InfoPanelIconLocationResult(
            true,
            target,
            target.Confidence,
            searchRoi,
            $"opencv_info_panel_icon fill={best.Fill:F2} centerBias={best.CenterBias:F2}");
    }

    public static NormalizedBox GetInfoPanelIconSearchRoi(AutomationTarget row)
    {
        var left = Math.Clamp(row.CenterX + row.Width * 0.30, 0, 1);
        var right = Math.Clamp(row.CenterX + row.Width * 0.53, 0, 1);
        var top = Math.Clamp(row.CenterY - row.Height * 0.70, 0, 1);
        var bottom = Math.Clamp(row.CenterY + row.Height * 0.70, 0, 1);
        if (right <= left)
            right = Math.Min(1, left + 0.04);
        if (bottom <= top)
            bottom = Math.Min(1, top + 0.05);

        return new NormalizedBox(
            (left + right) / 2,
            (top + bottom) / 2,
            right - left,
            bottom - top);
    }

    private static bool IsTargetInside(AutomationTarget target, AutomationTarget container, double horizontalPadding, double verticalPadding)
    {
        var left = container.CenterX - container.Width / 2 - horizontalPadding;
        var right = container.CenterX + container.Width / 2 + horizontalPadding;
        var top = container.CenterY - container.Height / 2 - verticalPadding;
        var bottom = container.CenterY + container.Height / 2 + verticalPadding;
        return target.CenterX >= left && target.CenterX <= right && target.CenterY >= top && target.CenterY <= bottom;
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
