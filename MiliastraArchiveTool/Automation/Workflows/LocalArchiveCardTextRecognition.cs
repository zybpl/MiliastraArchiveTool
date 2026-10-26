using MiliastraArchiveTool.Infrastructure;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using RapidOcrNet;
using SkiaSharp;

namespace MiliastraArchiveTool.Automation.Workflows;

public interface ILocalArchiveCardTextRecognizer
{
    Task<IReadOnlyList<LocalArchiveCardTextResult>> RecognizeCardsAsync(
        GameCaptureResult capture,
        CancellationToken ct);

    string EngineInfo { get; }
}

public sealed record NormalizedBox(
    double CenterX,
    double CenterY,
    double Width,
    double Height);

public sealed record LocalArchiveCardTextResult(
    string Text,
    NormalizedBox NormalizedBox,
    double Confidence,
    string? MatchedTargetName = null,
    double MatchScore = 0);

public sealed class LocalArchiveOcrOptions
{
    public string Engine { get; init; } = "RapidOcrOnnx";
    public string? ModelDirectory { get; init; }
    // Legacy configuration fields are retained for compatibility; OCR crops always use native pixels.
    public bool EnableRoiUpscale { get; init; } = false;
    public int RoiScale { get; init; } = 1;
    public double MinConfidence { get; init; } = 0.35;
    public bool PreferRunCodeMatch { get; init; } = true;

    public static LocalArchiveOcrOptions LoadDefault()
    {
        var defaultDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Automation",
            "Recognition",
            "Ocr",
            "Assets",
            "rapidocr");
        var optionsPath = Path.Combine(defaultDirectory, "ocr-options.json");
        if (!File.Exists(optionsPath))
            return new LocalArchiveOcrOptions { ModelDirectory = defaultDirectory };

        var options = JsonSerializer.Deserialize<LocalArchiveOcrOptions>(File.ReadAllText(optionsPath)) ??
                      new LocalArchiveOcrOptions();
        return options.WithModelDirectory(defaultDirectory);
    }

    private LocalArchiveOcrOptions WithModelDirectory(string defaultDirectory) =>
        string.IsNullOrWhiteSpace(ModelDirectory)
            ? new LocalArchiveOcrOptions
            {
                Engine = Engine,
                ModelDirectory = defaultDirectory,
                EnableRoiUpscale = EnableRoiUpscale,
                RoiScale = RoiScale,
                MinConfidence = MinConfidence,
                PreferRunCodeMatch = PreferRunCodeMatch
            }
            : this;
}

public sealed class RapidOcrLocalArchiveCardTextRecognizer : ILocalArchiveCardTextRecognizer, IDisposable
{
    private const string DetectionModelFileName = "ch_PP-OCRv5_mobile_det.onnx";
    private const string ClassificationModelFileName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";
    private const string RecognitionModelFileName = "ch_PP-OCRv5_rec_server.onnx";
    private const string KeysFileName = "ppocrv5_dict.txt";
    private const float TextBoxUnClipRatio = 1.0f;

    private readonly LocalArchiveOcrOptions _options;
    private readonly object _syncRoot = new();
    private RapidOcr? _ocr;
    private bool _disposed;

    public RapidOcrLocalArchiveCardTextRecognizer(LocalArchiveOcrOptions? options = null)
    {
        _options = options ?? LocalArchiveOcrOptions.LoadDefault();
        ModelDirectory = ResolveModelDirectory(_options.ModelDirectory);
        EngineInfo = $"RapidOcrNet PP-OCRv5 Chinese/English; RecognitionModel={RecognitionModelFileName}; Dictionary={KeysFileName}; ModelDirectory={ModelDirectory}; RoiScale={RoiScale}; UnClipRatio={TextBoxUnClipRatio.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public string ModelDirectory { get; }
    public int RoiScale => 1;
    public string EngineInfo { get; private set; }

    public async Task<IReadOnlyList<LocalArchiveCardTextResult>> RecognizeCardsAsync(
        GameCaptureResult capture,
        CancellationToken ct)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("OCR", new { Width = capture.Bitmap.PixelWidth, Height = capture.Bitmap.PixelHeight });
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(capture);
        ct.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            using var bitmap = DecodeBitmapSource(capture.Bitmap);
            ct.ThrowIfCancellationRequested();
            var ocr = EnsureInitialized();
            var result = ocr.Detect(bitmap, CreateOcrOptions(), ct);
            var width = Math.Max(1, bitmap.Width);
            var height = Math.Max(1, bitmap.Height);

            return (IReadOnlyList<LocalArchiveCardTextResult>)result.TextBlocks
                .Select(block =>
                {
                    var box = ToNormalizedBox(block.BoxPoints, width, height);
                    var confidence = block.CharScores is { Length: > 0 }
                        ? block.CharScores.Average(score => (double)score)
                        : block.BoxScore;
                    return new LocalArchiveCardTextResult(block.Text, box, confidence);
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .Where(item => item.Confidence >= _options.MinConfidence)
                .ToList();
        }, ct);
    }

    public static string NormalizeForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC).ToUpperInvariant())
        {
            if (char.IsWhiteSpace(character))
                continue;

            builder.Append(character);
        }

        return builder.ToString();
    }

    public static double CalculateMatchScore(string recognizedText, string runArchiveDisplayName, string runArchiveCode)
    {
        var normalizedText = NormalizeForMatch(recognizedText);
        var normalizedTarget = NormalizeForMatch(runArchiveDisplayName);
        var normalizedCode = NormalizeForMatch(runArchiveCode);
        if (normalizedText.Length == 0)
            return 0;

        if (string.Equals(normalizedText, normalizedTarget, StringComparison.Ordinal))
            return 1.0;

        if (ContainsRunCode(normalizedText, normalizedCode))
            return 0.85;

        var correctedText = ApplyCautiousCodeConfusionCorrections(normalizedText);
        var correctedCode = ApplyCautiousCodeConfusionCorrections(normalizedCode);
        return ContainsRunCode(correctedText, correctedCode) ? 0.7 : 0;
    }

    public static string ExtractRunCode(string runArchiveDisplayName)
    {
        var normalized = NormalizeForMatch(runArchiveDisplayName);
        var match = Regex.Match(normalized, @"_([A-Z0-9]{4})$");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_syncRoot)
        {
            _ocr?.Dispose();
            _ocr = null;
            _disposed = true;
        }
    }

    private RapidOcr EnsureInitialized()
    {
        if (_ocr != null)
            return _ocr;

        lock (_syncRoot)
        {
            if (_ocr != null)
                return _ocr;

            var det = RequireFile(DetectionModelFileName);
            var cls = RequireFile(ClassificationModelFileName);
            var rec = RequireFile(RecognitionModelFileName);
            var keys = RequireFile(KeysFileName);
            var ocr = new RapidOcr();
            ocr.InitModels(det, cls, rec, keys, RapidOcr.GetDefaultSessionOptions());
            _ocr = ocr;
            return _ocr;
        }
    }

    private RapidOcrOptions CreateOcrOptions() =>
        RapidOcrOptions.Default with
        {
            DoAngle = true,
            // Tight UI text lines need less DBNet expansion with the Chinese/English
            // server recognizer; the default 1.6 adds enough background to lose letters.
            UnClipRatio = TextBoxUnClipRatio,
            TextScore = (float)_options.MinConfidence,
            ReturnWordBox = true
        };

    private string RequireFile(string fileName)
    {
        var path = Path.Combine(ModelDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"RapidOCR model asset is missing: {path}", path);
        return path;
    }

    private static string ResolveModelDirectory(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "Automation",
                "Recognition",
                "Ocr",
                "Assets",
                "rapidocr");
        }

        return Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
    }

    private static SKBitmap DecodeBitmapSource(BitmapSource source)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream) ?? throw new InvalidOperationException("RapidOCR failed to decode the supplied bitmap.");
    }

    private static NormalizedBox ToNormalizedBox(SKPointI[] points, int width, int height)
    {
        if (points.Length == 0)
            return new NormalizedBox(0.5, 0.5, 1, 1);

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        return new NormalizedBox(
            Clamp01((minX + maxX) / 2.0 / width),
            Clamp01((minY + maxY) / 2.0 / height),
            Clamp01((maxX - minX) / (double)width),
            Clamp01((maxY - minY) / (double)height));
    }

    private static bool ContainsRunCode(string normalizedText, string normalizedCode) =>
        normalizedCode.Length == 4 &&
        (normalizedText.Contains("_" + normalizedCode, StringComparison.Ordinal) ||
         normalizedText.EndsWith(normalizedCode, StringComparison.Ordinal));

    private static string ApplyCautiousCodeConfusionCorrections(string value) =>
        value
            .Replace('O', '0')
            .Replace('I', '1')
            .Replace('S', '5')
            .Replace(')', 'J')
            .Replace(']', 'J')
            .Replace('}', 'J');

    private static double Clamp01(double value) =>
        Math.Clamp(value, 0, 1);
}

