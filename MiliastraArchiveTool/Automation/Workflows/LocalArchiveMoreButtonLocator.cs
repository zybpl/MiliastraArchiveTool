using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;
using OpenCvSharp;

namespace MiliastraArchiveTool.Automation.Workflows;

public interface ILocalArchiveMoreButtonLocator
{
    LocalArchiveMoreButtonLocationResult Locate(
        GameCaptureResult capture,
        LocalArchiveCardCandidate candidate,
        CancellationToken cancellationToken);
}

public sealed record LocalArchiveMoreButtonLocationResult(
    bool Matched,
    AutomationTarget? Target,
    double Confidence,
    NormalizedBox SearchRoi,
    string? RejectedReason);

public sealed class OpenCvLocalArchiveMoreButtonLocator : ILocalArchiveMoreButtonLocator
{
    public LocalArchiveMoreButtonLocationResult Locate(
        GameCaptureResult capture,
        LocalArchiveCardCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        var searchRoi = GetLocalArchiveMoreButtonSearchRoi(candidate);
        using var bgr = BitmapSourceToBgrMat(capture.Bitmap);
        var sourceWidth = bgr.Width;
        var sourceHeight = bgr.Height;
        var search = RectFromNormalized(sourceWidth, sourceHeight, searchRoi);
        if (search.Width <= 2 || search.Height <= 2)
        {
            return new LocalArchiveMoreButtonLocationResult(
                false,
                null,
                0,
                searchRoi,
                "OpenCV search ROI is too small.");
        }

        using var roi = new Mat(bgr, search);
        using var gray = new Mat();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

        var byDarkCircle = FindMoreButtonByDarkCircle(gray, search, sourceWidth, sourceHeight, searchRoi);
        if (byDarkCircle != null)
            return byDarkCircle;

        cancellationToken.ThrowIfCancellationRequested();
        var byDotCluster = FindMoreButtonByDotCluster(gray, search, sourceWidth, sourceHeight, searchRoi);
        if (byDotCluster != null)
            return byDotCluster;

        return new LocalArchiveMoreButtonLocationResult(
            false,
            null,
            0,
            searchRoi,
            "OpenCV did not find a dark circular more button or a bright three-dot cluster inside the OCR-confirmed card left-bottom ROI.");
    }

    public static NormalizedBox GetLocalArchiveMoreButtonSearchRoi(LocalArchiveCardCandidate candidate)
    {
        var card = candidate.CardBox;
        var cardLeft = card.CenterX - card.Width / 2;
        var cardTop = card.CenterY - card.Height / 2;
        var cardBottom = candidate.IsSynthetic
            ? card.CenterY + Math.Max(card.Height / 2, 0.16)
            : card.CenterY + card.Height / 2;

        var left = Math.Clamp(cardLeft - card.Width * 0.04, 0, 1);
        var right = Math.Clamp(cardLeft + card.Width * 0.32, 0, 1);
        var top = Math.Clamp(cardTop + (cardBottom - cardTop) * 0.48, 0, 1);
        var bottom = Math.Clamp(cardBottom + (cardBottom - cardTop) * 0.08, 0, 1);

        if (right <= left)
            right = Math.Min(1, left + 0.08);
        if (bottom <= top)
            bottom = Math.Min(1, top + 0.06);

        return new NormalizedBox(
            (left + right) / 2,
            (top + bottom) / 2,
            right - left,
            bottom - top);
    }

    private static LocalArchiveMoreButtonLocationResult? FindMoreButtonByDarkCircle(
        Mat grayRoi,
        Rect search,
        int sourceWidth,
        int sourceHeight,
        NormalizedBox searchRoi)
    {
        using var dark = new Mat();
        Cv2.InRange(grayRoi, 0, 76, dark);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(dark, dark, MorphTypes.Close, kernel);

        Cv2.FindContours(
            dark,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        (double X, double Y, int Width, int Height, double Score)? best = null;
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            var normalizedSize = Math.Max(rect.Width / (double)sourceWidth, rect.Height / (double)sourceHeight);
            if (normalizedSize < 0.010 || normalizedSize > 0.045)
                continue;

            var aspect = rect.Width / (double)Math.Max(1, rect.Height);
            if (aspect < 0.55 || aspect > 1.70)
                continue;

            var area = Cv2.ContourArea(contour);
            var fill = area / Math.Max(1, rect.Width * rect.Height);
            if (fill < 0.32)
                continue;

            using var button = new Mat(
                grayRoi,
                new Rect(
                    Math.Clamp(rect.X, 0, grayRoi.Width - 1),
                    Math.Clamp(rect.Y, 0, grayRoi.Height - 1),
                    Math.Clamp(rect.Width, 1, grayRoi.Width - rect.X),
                    Math.Clamp(rect.Height, 1, grayRoi.Height - rect.Y)));
            using var innerBright = new Mat();
            Cv2.InRange(button, 120, 255, innerBright);
            Cv2.FindContours(
                innerBright,
                out Point[][] dotContours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            var dotCount = dotContours.Count(dot =>
            {
                var dotRect = Cv2.BoundingRect(dot);
                var dotArea = Cv2.ContourArea(dot);
                if (dotRect.Width < 1 || dotRect.Height < 1)
                    return false;
                if (dotRect.Width > rect.Width * 0.48 || dotRect.Height > rect.Height * 0.48)
                    return false;
                return dotArea / Math.Max(1, dotRect.Width * dotRect.Height) >= 0.18;
            });

            if (dotCount < 2)
                continue;

            var centerX = search.X + rect.X + rect.Width / 2.0;
            var centerY = search.Y + rect.Y + rect.Height / 2.0;
            var score = fill * 0.45 + Math.Min(1.0, dotCount / 3.0) * 0.45 + SizeScore(rect, sourceWidth, sourceHeight) * 0.10;
            if (best == null || score > best.Value.Score)
                best = (centerX, centerY, rect.Width, rect.Height, score);
        }

        return best == null ? null : ToMatchedResult(best.Value, sourceWidth, sourceHeight, searchRoi, "opencv_dark_circle");
    }

    private static LocalArchiveMoreButtonLocationResult? FindMoreButtonByDotCluster(
        Mat grayRoi,
        Rect search,
        int sourceWidth,
        int sourceHeight,
        NormalizedBox searchRoi)
    {
        using var bright = new Mat();
        Cv2.InRange(grayRoi, 132, 255, bright);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(bright, bright, MorphTypes.Open, kernel);

        Cv2.FindContours(
            bright,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var dots = new List<(double X, double Y, int Width, int Height, double Score)>();
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            var area = Cv2.ContourArea(contour);
            var minSize = Math.Max(2, sourceWidth * 0.0010);
            var maxSize = Math.Max(12, sourceWidth * 0.0090);
            if (rect.Width < minSize || rect.Height < minSize ||
                rect.Width > maxSize || rect.Height > maxSize)
                continue;

            var aspect = rect.Width / (double)Math.Max(1, rect.Height);
            if (aspect < 0.45 || aspect > 1.90)
                continue;

            var fill = area / Math.Max(1, rect.Width * rect.Height);
            if (fill < 0.16)
                continue;

            dots.Add((rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0, rect.Width, rect.Height, fill));
        }

        (double X, double Y, int Width, int Height, double Score)? best = null;
        for (var i = 0; i < dots.Count; i++)
        for (var j = i + 1; j < dots.Count; j++)
        for (var k = j + 1; k < dots.Count; k++)
        {
            var trio = new[] { dots[i], dots[j], dots[k] }
                .OrderBy(dot => dot.X)
                .ToArray();

            var ySpread = trio.Max(dot => dot.Y) - trio.Min(dot => dot.Y);
            var gap1 = trio[1].X - trio[0].X;
            var gap2 = trio[2].X - trio[1].X;
            var avgGap = (gap1 + gap2) / 2.0;
            if (ySpread > sourceHeight * 0.014)
                continue;
            if (avgGap < sourceWidth * 0.0020 || avgGap > sourceWidth * 0.017)
                continue;

            var gapBalance = 1.0 - Math.Min(1.0, Math.Abs(gap1 - gap2) / Math.Max(1, avgGap));
            var score = gapBalance * 0.62 + trio.Average(dot => dot.Score) * 0.38;
            var localCenterX = trio.Average(dot => dot.X);
            var localCenterY = trio.Average(dot => dot.Y);
            var dotWidth = Math.Max(1, trio.Max(dot => dot.X + dot.Width / 2.0) - trio.Min(dot => dot.X - dot.Width / 2.0));
            var dotHeight = Math.Max(1, trio.Max(dot => dot.Y + dot.Height / 2.0) - trio.Min(dot => dot.Y - dot.Height / 2.0));
            var globalCenterX = search.X + localCenterX;
            var globalCenterY = search.Y + localCenterY;

            if (best == null || score > best.Value.Score)
                best = (globalCenterX, globalCenterY, (int)Math.Round(dotWidth), (int)Math.Round(dotHeight), score);
        }

        return best == null ? null : ToMatchedResult(best.Value, sourceWidth, sourceHeight, searchRoi, "opencv_dot_cluster");
    }

    private static LocalArchiveMoreButtonLocationResult ToMatchedResult(
        (double X, double Y, int Width, int Height, double Score) match,
        int sourceWidth,
        int sourceHeight,
        NormalizedBox searchRoi,
        string source)
    {
        var width = Math.Clamp(match.Width / (double)sourceWidth, 0.008, 0.050);
        var height = Math.Clamp(match.Height / (double)sourceHeight, 0.008, 0.050);
        var target = new AutomationTarget(
            WorkflowTargetNames.LocalArchiveMoreButton,
            Math.Clamp(match.X / sourceWidth, 0, 1),
            Math.Clamp(match.Y / sourceHeight, 0, 1),
            width,
            height,
            Math.Clamp(match.Score, 0, 1));

        return new LocalArchiveMoreButtonLocationResult(
            true,
            target,
            target.Confidence,
            searchRoi,
            source);
    }

    private static double SizeScore(Rect rect, int sourceWidth, int sourceHeight)
    {
        var normalized = Math.Max(rect.Width / (double)sourceWidth, rect.Height / (double)sourceHeight);
        var distance = Math.Abs(normalized - 0.022) / 0.022;
        return 1.0 - Math.Clamp(distance, 0, 1);
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
