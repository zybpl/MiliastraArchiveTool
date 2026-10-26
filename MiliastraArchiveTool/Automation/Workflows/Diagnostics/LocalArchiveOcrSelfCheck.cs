using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Automation.Recognition.Yolo;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Automation.Workflows.Diagnostics;

public static class LocalArchiveOcrSelfCheck
{
    public static async Task<LocalArchiveOcrSelfCheckReport> RunAsync(
        string imagePath,
        string targetRunArchiveCode,
        string? targetRunArchiveDisplayName = null,
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path is required.", nameof(imagePath));
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"LocalArchive OCR self-check image was not found: {imagePath}", imagePath);
        if (string.IsNullOrWhiteSpace(targetRunArchiveCode))
            throw new ArgumentException("Target run archive code is required.", nameof(targetRunArchiveCode));

        outputDirectory ??= Path.Combine(ApplicationPaths.DiagnosticsDirectory, "local-archive-ocr-self-check");
        Directory.CreateDirectory(outputDirectory);

        var bitmap = LoadBitmap(imagePath);
        var window = new GameWindowInfo(nint.Zero, 0, "LocalArchiveOcrSelfCheck", bitmap.PixelWidth, bitmap.PixelHeight, 0, 0, 96, false);
        var capture = new GameCaptureResult(
            window,
            bitmap,
            "File",
            GameWindowCaptureService.CalculateVisualQuality(bitmap),
            null,
            null,
            null);

        using var pageRecognizer = new Stage1GatedPageRecognizer();
        using var ocr = new RapidOcrLocalArchiveCardTextRecognizer();
        var recognition = await pageRecognizer.RecognizeAsync(bitmap, cancellationToken);
        var candidateProvider = new LocalArchiveCardCandidateProvider();
        var candidates = candidateProvider.GetCandidates(recognition, capture);

        var cardResults = new List<LocalArchiveOcrSelfCheckCardResult>();
        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            var roi = CreateCardRoi(capture, candidate, ocr.RoiScale);
            var roiPath = Path.Combine(outputDirectory, $"local-archive-card-roi-{candidate.Source}-{candidate.Index:00}-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
            GameWindowCaptureService.SavePng(roi.Capture.Bitmap, roiPath);

            var texts = (await ocr.RecognizeCardsAsync(roi.Capture, cancellationToken))
                .Select(result =>
                {
                    var mapped = MapRoiResultToOriginal(result, roi.Roi);
                    var score = RapidOcrLocalArchiveCardTextRecognizer.CalculateMatchScore(
                        mapped.Text,
                        targetRunArchiveDisplayName ?? string.Empty,
                        targetRunArchiveCode);
                    return mapped with
                    {
                        MatchedTargetName = score > 0 ? targetRunArchiveDisplayName ?? targetRunArchiveCode : null,
                        MatchScore = score
                    };
                })
                .ToList();
            var combinedText = string.Concat(texts.Select(text => text.Text));
            var combinedScore = RapidOcrLocalArchiveCardTextRecognizer.CalculateMatchScore(
                combinedText,
                targetRunArchiveDisplayName ?? string.Empty,
                targetRunArchiveCode);

            cardResults.Add(new LocalArchiveOcrSelfCheckCardResult(
                ToCandidateInfo(candidate),
                roiPath,
                combinedText,
                RapidOcrLocalArchiveCardTextRecognizer.NormalizeForMatch(combinedText),
                Math.Max(combinedScore, texts.Count == 0 ? 0 : texts.Max(text => text.MatchScore)),
                texts));
        }

        var matched = cardResults.Where(card => card.MatchScore >= 0.7).ToList();
        var reportPath = Path.Combine(outputDirectory, "local-archive-ocr-self-check-report.json");
        var summaryPath = Path.Combine(outputDirectory, "local-archive-ocr-self-check-summary.md");
        var report = new LocalArchiveOcrSelfCheckReport(
            DateTimeOffset.Now,
            imagePath,
            targetRunArchiveDisplayName,
            targetRunArchiveCode,
            ocr.EngineInfo,
            ocr.ModelDirectory,
            ocr.RoiScale,
            recognition.Page,
            recognition.Confidence,
            candidates.Count,
            candidates.Any(candidate => candidate.IsSynthetic),
            matched.Count == 1,
            matched.Count,
            matched.Count == 1 ? matched[0].Candidate : null,
            cardResults,
            reportPath,
            summaryPath);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, options), cancellationToken);
        await File.WriteAllTextAsync(summaryPath, CreateSummary(report), cancellationToken);
        return report;
    }

    private static BitmapSource LoadBitmap(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var copy = new WriteableBitmap(frame);
        copy.Freeze();
        return copy;
    }

    private static CardRoiCapture CreateCardRoi(GameCaptureResult source, LocalArchiveCardCandidate candidate, int roiScale)
    {
        var box = candidate.NameRegion;
        var centerX = box?.CenterX ?? candidate.CardBox.CenterX;
        var centerY = box?.CenterY ?? candidate.CardBox.CenterY;
        var targetWidth = box?.Width ?? candidate.CardBox.Width;
        var targetHeight = box?.Height ?? candidate.CardBox.Height;
        var bitmap = CreateFrozenBitmapCopy(source.Bitmap);
        var width = Math.Max(1, bitmap.PixelWidth);
        var height = Math.Max(1, bitmap.PixelHeight);
        var paddingX = Math.Max(6, (int)Math.Round(targetWidth * width * 0.08));
        var paddingY = Math.Max(4, (int)Math.Round(targetHeight * height * 0.12));
        var x = Math.Clamp((int)Math.Floor((centerX - targetWidth / 2) * width) - paddingX, 0, width - 1);
        var y = Math.Clamp((int)Math.Floor((centerY - targetHeight / 2) * height) - paddingY, 0, height - 1);
        var x2 = Math.Clamp((int)Math.Ceiling((centerX + targetWidth / 2) * width) + paddingX, x + 1, width);
        var y2 = Math.Clamp((int)Math.Ceiling((centerY + targetHeight / 2) * height) + paddingY, y + 1, height);
        var rect = new Int32Rect(x, y, x2 - x, y2 - y);
        var cropped = new CroppedBitmap(bitmap, rect);
        cropped.Freeze();
        BitmapSource roiBitmap = cropped;


        return new CardRoiCapture(
            source with { Bitmap = roiBitmap, CaptureMethod = source.CaptureMethod + " / local_archive_card ROI" },
            new NormalizedPixelRoi(x, y, rect.Width, rect.Height, width, height, roiScale));
    }

    private static LocalArchiveCardTextResult MapRoiResultToOriginal(LocalArchiveCardTextResult result, NormalizedPixelRoi roi)
    {
        var box = result.NormalizedBox;
        return result with
        {
            NormalizedBox = new NormalizedBox(
                Math.Clamp((roi.X + box.CenterX * roi.Width) / roi.SourceWidth, 0, 1),
                Math.Clamp((roi.Y + box.CenterY * roi.Height) / roi.SourceHeight, 0, 1),
                Math.Clamp(box.Width * roi.Width / roi.SourceWidth, 0, 1),
                Math.Clamp(box.Height * roi.Height / roi.SourceHeight, 0, 1))
        };
    }

    private static BitmapSource CreateFrozenBitmapCopy(BitmapSource source)
    {
        var copy = new WriteableBitmap(source);
        copy.Freeze();
        return copy;
    }

    private static LocalArchiveOcrCandidateInfo ToCandidateInfo(LocalArchiveCardCandidate candidate) =>
        new(
            new Stage1PreflightTargetInfo(
                candidate.CardBox.Name,
                candidate.CardBox.Confidence,
                candidate.CardBox.CenterX,
                candidate.CardBox.CenterY,
                candidate.CardBox.Width,
                candidate.CardBox.Height),
            candidate.Source,
            candidate.Index,
            candidate.NameRegion,
            candidate.IsSynthetic);

    private static string CreateSummary(LocalArchiveOcrSelfCheckReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Local Archive OCR Self-Check");
        builder.AppendLine();
        builder.AppendLine($"- ImagePath: `{report.ImagePath}`");
        builder.AppendLine($"- TargetRunArchiveDisplayName: `{report.TargetRunArchiveDisplayName ?? "<none>"}`");
        builder.AppendLine($"- TargetRunArchiveCode: `{report.TargetRunArchiveCode}`");
        builder.AppendLine($"- OcrEngineInfo: `{report.OcrEngineInfo}`");
        builder.AppendLine($"- ModelDirectory: `{report.ModelDirectory}`");
        builder.AppendLine($"- RoiScale: `{report.RoiScale}`");
        builder.AppendLine($"- RecognizedPage: `{report.RecognizedPage}` ({report.PageConfidence:0.###})");
        builder.AppendLine($"- CardCount: `{report.CardCount}`");
        builder.AppendLine($"- UsedLayoutFallback: `{report.UsedLayoutFallback}`");
        builder.AppendLine($"- IsUniqueMatch: `{report.IsUniqueMatch}`");
        builder.AppendLine($"- MatchCount: `{report.MatchCount}`");
        builder.AppendLine();
        builder.AppendLine("## Cards");
        foreach (var card in report.Cards)
        {
            builder.AppendLine();
            builder.AppendLine($"- Candidate: `{card.Candidate.Source}/{card.Candidate.Index}` synthetic=`{card.Candidate.IsSynthetic}`");
            builder.AppendLine($"  Roi: `{card.RoiScreenshotPath}`");
            builder.AppendLine($"  Text: `{card.CombinedText}`");
            builder.AppendLine($"  Normalized: `{card.NormalizedText}`");
            builder.AppendLine($"  MatchScore: `{card.MatchScore:0.###}`");
        }

        return builder.ToString();
    }

    private sealed record CardRoiCapture(GameCaptureResult Capture, NormalizedPixelRoi Roi);

    private sealed record NormalizedPixelRoi(
        int X,
        int Y,
        int Width,
        int Height,
        int SourceWidth,
        int SourceHeight,
        int RoiScale);
}

public sealed record LocalArchiveOcrSelfCheckReport(
    DateTimeOffset CreatedAt,
    string ImagePath,
    string? TargetRunArchiveDisplayName,
    string TargetRunArchiveCode,
    string OcrEngineInfo,
    string ModelDirectory,
    int RoiScale,
    GamePage RecognizedPage,
    double PageConfidence,
    int CardCount,
    bool UsedLayoutFallback,
    bool IsUniqueMatch,
    int MatchCount,
    LocalArchiveOcrCandidateInfo? SelectedCardBox,
    IReadOnlyList<LocalArchiveOcrSelfCheckCardResult> Cards,
    string ReportPath,
    string SummaryPath);

public sealed record LocalArchiveOcrSelfCheckCardResult(
    LocalArchiveOcrCandidateInfo Candidate,
    string RoiScreenshotPath,
    string CombinedText,
    string NormalizedText,
    double MatchScore,
    IReadOnlyList<LocalArchiveCardTextResult> OcrResults);

public sealed record LocalArchiveOcrCandidateInfo(
    Stage1PreflightTargetInfo CardBox,
    string Source,
    int Index,
    NormalizedBox? NameRegion,
    bool IsSynthetic);

