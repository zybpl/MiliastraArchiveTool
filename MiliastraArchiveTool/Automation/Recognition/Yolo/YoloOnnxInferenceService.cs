using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed class YoloOnnxInferenceService : IYoloInferenceService, IDisposable
{
    private const int InputSize = 960;
    private const double NmsIouThreshold = 0.45;
    private const float LetterboxFill = 114f / 255f;

    private readonly YoloModelAssets _assets;
    private readonly InferenceSession _session;
    private readonly string _inputName;

    private bool _disposed;

    public YoloOnnxInferenceService(YoloModelAssetService? assetService = null)
    {
        _assets = (assetService ?? new YoloModelAssetService()).LoadStage1Assets();
        _session = new InferenceSession(_assets.OnnxModelPath);
        _inputName = ValidateAndGetInputName(_session);
        ValidateOutputMetadata(_session);
    }

    public Task<YoloInferenceResult> PredictAsync(
        byte[] imageBytes,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(imageBytes);

        if (imageBytes.Length == 0)
            throw new ArgumentException("Image bytes are empty; YOLO inference cannot decode an empty image.", nameof(imageBytes));

        return Task.Run(
            () => PredictCore(imageBytes, sourceDescription, cancellationToken),
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _session.Dispose();
        _disposed = true;
    }

    private YoloInferenceResult PredictCore(
        byte[] imageBytes,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var image = DecodeImage(imageBytes, sourceDescription);
        var letterbox = CreateLetterboxedTensor(image, cancellationToken);

        var input = NamedOnnxValue.CreateFromTensor(_inputName, letterbox.Tensor);
        using var results = _session.Run([input]);
        cancellationToken.ThrowIfCancellationRequested();

        var output = results.FirstOrDefault()
            ?? throw new InvalidOperationException("YOLO ONNX model returned no outputs.");

        var outputTensor = output.AsTensor<float>();
        var candidates = DecodeOutput(
            outputTensor,
            letterbox,
            image.Width,
            image.Height,
            cancellationToken);

        var detections = ApplyPerClassNms(candidates);

        return new YoloInferenceResult(
            sourceDescription,
            image.Width,
            image.Height,
            detections);
    }

    private static DecodedImage DecodeImage(byte[] imageBytes, string sourceDescription)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes, writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            var frame = decoder.Frames.FirstOrDefault()
                ?? throw new InvalidOperationException("The image contains no frames.");

            BitmapSource bitmap = frame;
            if (bitmap.Format != PixelFormats.Bgra32)
            {
                bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                bitmap.Freeze();
            }

            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Decoded image has invalid size {width}x{height}.");

            var stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            return new DecodedImage(width, height, stride, pixels);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Image decode failed for '{sourceDescription}': {ex.Message}",
                ex);
        }
    }

    private static LetterboxInput CreateLetterboxedTensor(
        DecodedImage image,
        CancellationToken cancellationToken)
    {
        var scale = Math.Min((double)InputSize / image.Width, (double)InputSize / image.Height);
        var resizedWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
        var padX = (InputSize - resizedWidth) / 2.0;
        var padY = (InputSize - resizedHeight) / 2.0;
        var padLeft = (int)Math.Floor(padX);
        var padTop = (int)Math.Floor(padY);

        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        for (var i = 0; i < tensor.Length; i++)
            tensor.Buffer.Span[i] = LetterboxFill;

        for (var y = 0; y < resizedHeight; y++)
        {
            if ((y & 31) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var targetY = padTop + y;
            if (targetY < 0 || targetY >= InputSize)
                continue;

            for (var x = 0; x < resizedWidth; x++)
            {
                var targetX = padLeft + x;
                if (targetX < 0 || targetX >= InputSize)
                    continue;

                // Bilinear sampling matches the resize behavior used by YOLO/Ultralytics
                // more closely than nearest-neighbor sampling, especially for small UI targets.
                var (red, green, blue) = SampleBilinearRgb(image, x, y, scale);

                tensor[0, 0, targetY, targetX] = red;
                tensor[0, 1, targetY, targetX] = green;
                tensor[0, 2, targetY, targetX] = blue;
            }
        }

        return new LetterboxInput(tensor, scale, padX, padY);
    }

    private static (float Red, float Green, float Blue) SampleBilinearRgb(
        DecodedImage image,
        int resizedX,
        int resizedY,
        double scale)
    {
        var sourceX = ((resizedX + 0.5) / scale) - 0.5;
        var sourceY = ((resizedY + 0.5) / scale) - 0.5;

        sourceX = Clamp(sourceX, 0, image.Width - 1);
        sourceY = Clamp(sourceY, 0, image.Height - 1);

        var x0 = (int)Math.Floor(sourceX);
        var y0 = (int)Math.Floor(sourceY);
        var x1 = Math.Min(x0 + 1, image.Width - 1);
        var y1 = Math.Min(y0 + 1, image.Height - 1);
        var xWeight = sourceX - x0;
        var yWeight = sourceY - y0;

        var topLeft = GetBgraPixel(image, x0, y0);
        var topRight = GetBgraPixel(image, x1, y0);
        var bottomLeft = GetBgraPixel(image, x0, y1);
        var bottomRight = GetBgraPixel(image, x1, y1);

        var blue = Interpolate(topLeft.Blue, topRight.Blue, bottomLeft.Blue, bottomRight.Blue, xWeight, yWeight);
        var green = Interpolate(topLeft.Green, topRight.Green, bottomLeft.Green, bottomRight.Green, xWeight, yWeight);
        var red = Interpolate(topLeft.Red, topRight.Red, bottomLeft.Red, bottomRight.Red, xWeight, yWeight);

        return ((float)(red / 255.0), (float)(green / 255.0), (float)(blue / 255.0));
    }

    private static (byte Blue, byte Green, byte Red) GetBgraPixel(DecodedImage image, int x, int y)
    {
        var index = y * image.Stride + x * 4;
        return (image.Pixels[index], image.Pixels[index + 1], image.Pixels[index + 2]);
    }

    private static double Interpolate(
        double topLeft,
        double topRight,
        double bottomLeft,
        double bottomRight,
        double xWeight,
        double yWeight)
    {
        var top = topLeft + (topRight - topLeft) * xWeight;
        var bottom = bottomLeft + (bottomRight - bottomLeft) * xWeight;
        return top + (bottom - top) * yWeight;
    }

    private IReadOnlyList<YoloDetection> DecodeOutput(
        Tensor<float> outputTensor,
        LetterboxInput letterbox,
        int originalWidth,
        int originalHeight,
        CancellationToken cancellationToken)
    {
        var dimensions = outputTensor.Dimensions.ToArray();
        var featureCount = 4 + _assets.Classes.Count;

        if (dimensions.Length != 3 || dimensions[0] != 1)
        {
            throw new InvalidOperationException(
                $"YOLO ONNX output shape is unsupported. Expected [1,{featureCount},N] or [1,N,{featureCount}], actual [{string.Join(",", dimensions)}].");
        }

        var channelsFirst = dimensions[1] == featureCount;
        var channelsLast = dimensions[2] == featureCount;
        if (!channelsFirst && !channelsLast)
        {
            var detectedClassCount = Math.Max(dimensions[1], dimensions[2]) - 4;
            throw new InvalidOperationException(
                $"YOLO ONNX output class count does not match classes.txt. Expected {featureCount} features ({_assets.Classes.Count} classes), actual shape [{string.Join(",", dimensions)}], inferred classes {detectedClassCount}.");
        }

        var predictionCount = channelsFirst ? dimensions[2] : dimensions[1];
        var values = outputTensor.ToArray();
        var detections = new List<YoloDetection>();

        for (var predictionIndex = 0; predictionIndex < predictionCount; predictionIndex++)
        {
            if ((predictionIndex & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var bestClassId = -1;
            var bestConfidence = double.NegativeInfinity;

            for (var classId = 0; classId < _assets.Classes.Count; classId++)
            {
                var confidence = GetOutputValue(values, dimensions, channelsFirst, predictionIndex, 4 + classId);
                if (confidence > bestConfidence)
                {
                    bestClassId = classId;
                    bestConfidence = confidence;
                }
            }

            if (bestClassId < 0)
                continue;

            var className = _assets.Classes[bestClassId];
            if (bestConfidence < _assets.GetThreshold(className))
                continue;

            var centerX = GetOutputValue(values, dimensions, channelsFirst, predictionIndex, 0);
            var centerY = GetOutputValue(values, dimensions, channelsFirst, predictionIndex, 1);
            var width = GetOutputValue(values, dimensions, channelsFirst, predictionIndex, 2);
            var height = GetOutputValue(values, dimensions, channelsFirst, predictionIndex, 3);

            if (width <= 0 || height <= 0)
                continue;

            var originalCenterX = (centerX - letterbox.PadX) / letterbox.Scale;
            var originalCenterY = (centerY - letterbox.PadY) / letterbox.Scale;
            var originalBoxWidth = width / letterbox.Scale;
            var originalBoxHeight = height / letterbox.Scale;

            var x1 = Clamp(originalCenterX - originalBoxWidth / 2.0, 0, originalWidth);
            var y1 = Clamp(originalCenterY - originalBoxHeight / 2.0, 0, originalHeight);
            var x2 = Clamp(originalCenterX + originalBoxWidth / 2.0, 0, originalWidth);
            var y2 = Clamp(originalCenterY + originalBoxHeight / 2.0, 0, originalHeight);

            if (x2 <= x1 || y2 <= y1)
                continue;

            detections.Add(new YoloDetection(
                bestClassId,
                className,
                Clamp(bestConfidence, 0, 1),
                x1,
                y1,
                x2,
                y2,
                Clamp(((x1 + x2) / 2.0) / originalWidth, 0, 1),
                Clamp(((y1 + y2) / 2.0) / originalHeight, 0, 1),
                Clamp((x2 - x1) / originalWidth, 0, 1),
                Clamp((y2 - y1) / originalHeight, 0, 1)));
        }

        return detections;
    }

    private static IReadOnlyList<YoloDetection> ApplyPerClassNms(IReadOnlyList<YoloDetection> candidates)
    {
        var kept = new List<YoloDetection>();
        foreach (var classGroup in candidates.GroupBy(detection => detection.ClassId))
        {
            var pending = classGroup
                .OrderByDescending(detection => detection.Confidence)
                .ToList();

            while (pending.Count > 0)
            {
                var current = pending[0];
                kept.Add(current);
                pending.RemoveAt(0);
                pending.RemoveAll(candidate => CalculateIoU(current, candidate) > NmsIouThreshold);
            }
        }

        return kept
            .OrderByDescending(detection => detection.Confidence)
            .ToList();
    }

    private static double GetOutputValue(
        IReadOnlyList<float> values,
        IReadOnlyList<int> dimensions,
        bool channelsFirst,
        int predictionIndex,
        int featureIndex)
    {
        var offset = channelsFirst
            ? featureIndex * dimensions[2] + predictionIndex
            : predictionIndex * dimensions[2] + featureIndex;

        return values[offset];
    }

    private static string ValidateAndGetInputName(InferenceSession session)
    {
        var input = session.InputMetadata.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(input.Key))
            throw new InvalidOperationException("YOLO ONNX model has no inputs.");

        var dimensions = input.Value.Dimensions;
        if (dimensions.Length != 4)
        {
            throw new InvalidOperationException(
                $"YOLO ONNX input shape is unsupported. Expected [1,3,{InputSize},{InputSize}], actual [{string.Join(",", dimensions)}].");
        }

        var batch = dimensions[0];
        var channels = dimensions[1];
        var height = dimensions[2];
        var width = dimensions[3];
        if (!IsDimensionCompatible(batch, 1) ||
            !IsDimensionCompatible(channels, 3) ||
            !IsDimensionCompatible(height, InputSize) ||
            !IsDimensionCompatible(width, InputSize))
        {
            throw new InvalidOperationException(
                $"YOLO ONNX input shape is unsupported. Expected [1,3,{InputSize},{InputSize}], actual [{string.Join(",", dimensions)}].");
        }

        return input.Key;
    }

    private void ValidateOutputMetadata(InferenceSession session)
    {
        var output = session.OutputMetadata.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(output.Key))
            throw new InvalidOperationException("YOLO ONNX model has no outputs.");

        var featureCount = 4 + _assets.Classes.Count;
        var dimensions = output.Value.Dimensions;
        if (dimensions.Length != 3)
        {
            throw new InvalidOperationException(
                $"YOLO ONNX output shape is unsupported. Expected [1,{featureCount},N] or [1,N,{featureCount}], actual [{string.Join(",", dimensions)}].");
        }

        var firstCompatible = IsDimensionCompatible(dimensions[0], 1);
        var featuresCompatible = IsDimensionCompatible(dimensions[1], featureCount) ||
                                 IsDimensionCompatible(dimensions[2], featureCount);
        if (!firstCompatible || !featuresCompatible)
        {
            throw new InvalidOperationException(
                $"YOLO ONNX output shape is unsupported for classes.txt. Expected {featureCount} features ({_assets.Classes.Count} classes), actual [{string.Join(",", dimensions)}].");
        }
    }

    private static bool IsDimensionCompatible(int actual, int expected) =>
        actual == expected || actual <= 0;

    private static double CalculateIoU(YoloDetection first, YoloDetection second)
    {
        var intersectionX1 = Math.Max(first.X1, second.X1);
        var intersectionY1 = Math.Max(first.Y1, second.Y1);
        var intersectionX2 = Math.Min(first.X2, second.X2);
        var intersectionY2 = Math.Min(first.Y2, second.Y2);

        var intersectionWidth = Math.Max(0, intersectionX2 - intersectionX1);
        var intersectionHeight = Math.Max(0, intersectionY2 - intersectionY1);
        var intersectionArea = intersectionWidth * intersectionHeight;
        if (intersectionArea <= 0)
            return 0;

        var firstArea = (first.X2 - first.X1) * (first.Y2 - first.Y1);
        var secondArea = (second.X2 - second.X1) * (second.Y2 - second.Y1);
        var unionArea = firstArea + secondArea - intersectionArea;

        return unionArea <= 0 ? 0 : intersectionArea / unionArea;
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    private sealed record DecodedImage(int Width, int Height, int Stride, byte[] Pixels);

    private sealed record LetterboxInput(
        DenseTensor<float> Tensor,
        double Scale,
        double PadX,
        double PadY);
}
