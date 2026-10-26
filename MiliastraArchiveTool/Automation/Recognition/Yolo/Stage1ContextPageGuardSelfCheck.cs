using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Workflows;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public static class Stage1ContextPageGuardSelfCheck
{
    public static async Task<IReadOnlyList<Stage1ContextPageGuardSelfCheckCase>> RunAsync(
        string uploadedArchiveImagePath,
        string? outputJsonPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(uploadedArchiveImagePath))
            throw new FileNotFoundException($"Self-check image does not exist: {uploadedArchiveImagePath}", uploadedArchiveImagePath);

        var guard = new Stage1ContextPageGuard();
        var bitmap = LoadBitmap(uploadedArchiveImagePath);
        using var inferenceService = new YoloOnnxInferenceService();
        using var recognizer = new YoloPageRecognizer(inferenceService);
        var uploadedArchiveRecognition = await recognizer.RecognizeAsync(bitmap, cancellationToken);

        var results = new List<Stage1ContextPageGuardSelfCheckCase>
        {
            CreateCase(
                "uploaded_archive_expected_uploaded_archive",
                guard.Decide(
                    uploadedArchiveRecognition,
                    new HashSet<GamePage> { GamePage.UploadedArchive },
                    "self-check uploaded archive expected")),
            CreateCase(
                "uploaded_archive_expected_local_archive",
                guard.Decide(
                    uploadedArchiveRecognition,
                    new HashSet<GamePage> { GamePage.LocalArchive },
                    "self-check uploaded archive rejected"))
        };

        var fakeUploadFailed = new PageRecognitionResult(
            GamePage.UploadFailed,
            0.98,
            "FakeUploadFailed",
            [new AutomationTarget(WorkflowTargetNames.UploadFailedCloseButton, 0.5, 0.5, 0.1, 0.1, 0.98)]);
        results.Add(CreateCase(
            "fake_upload_failed_expected_local_archive",
            guard.Decide(
                fakeUploadFailed,
                new HashSet<GamePage> { GamePage.LocalArchive },
                "self-check blocking page")));

        var fakePreviewEditor = new PageRecognitionResult(
            GamePage.PreviewEditor,
            0.88,
            "FakePreviewEditor",
            [new AutomationTarget(WorkflowTargetNames.PreviewLevelNameEditButton, 0.5, 0.5, 0.1, 0.1, 0.88)]);
        results.Add(CreateCase(
            "fake_preview_editor_expected_uploaded_archive",
            guard.Decide(
                fakePreviewEditor,
                new HashSet<GamePage> { GamePage.UploadedArchive },
                "self-check unsafe page")));

        if (!string.IsNullOrWhiteSpace(outputJsonPath))
        {
            var directory = Path.GetDirectoryName(outputJsonPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
            await File.WriteAllTextAsync(
                outputJsonPath,
                JsonSerializer.Serialize(results, options),
                cancellationToken);
        }

        return results;
    }

    private static Stage1ContextPageGuardSelfCheckCase CreateCase(
        string name,
        Stage1ContextPageDecision decision) =>
        new(
            name,
            decision.IsAccepted,
            decision.IsExpected,
            decision.IsUnexpectedSafePage,
            decision.IsUnknown,
            decision.IsBlocking,
            decision.RecognizedPage,
            decision.Reason,
            decision.Recognition.Confidence,
            decision.Recognition.Targets.Count);

    private static BitmapSource LoadBitmap(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var bitmap = decoder.Frames.First();
        bitmap.Freeze();
        return bitmap;
    }
}

public sealed record Stage1ContextPageGuardSelfCheckCase(
    string Name,
    bool IsAccepted,
    bool IsExpected,
    bool IsUnexpectedSafePage,
    bool IsUnknown,
    bool IsBlocking,
    GamePage RecognizedPage,
    string Reason,
    double Confidence,
    int TargetCount);
