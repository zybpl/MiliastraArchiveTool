using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Workflows;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public static class YoloPageRecognizerSelfCheck
{
    public static async Task<PageRecognitionResult> RunAsync(
        string imagePath,
        string? outputJsonPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Self-check image does not exist: {imagePath}", imagePath);

        var bitmap = LoadBitmap(imagePath);
        using var inferenceService = new YoloOnnxInferenceService();
        var recognizer = new YoloPageRecognizer(inferenceService);
        var result = await recognizer.RecognizeAsync(bitmap, cancellationToken);

        if (!string.IsNullOrWhiteSpace(outputJsonPath))
        {
            var directory = Path.GetDirectoryName(outputJsonPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(
                outputJsonPath,
                JsonSerializer.Serialize(result, options),
                cancellationToken);
        }

        return result;
    }

    public static async Task<IReadOnlyList<YoloPageRecognizerSyntheticCheckResult>> RunSyntheticPriorityChecksAsync(
        string? outputJsonPath = null,
        CancellationToken cancellationToken = default)
    {
        var cases = new[]
        {
            new YoloPageRecognizerSyntheticCheckCase(
                "uploaded_archive_context_beats_generic_secondary_button",
                GamePage.UploadedArchive,
                [
                    Detection(WorkflowTargetNames.InfoPanelRow, 0.88),
                    Detection(WorkflowTargetNames.TextEditIcon, 0.86),
                    Detection(WorkflowTargetNames.UploadedImageAssetsButton, 0.81),
                    Detection(WorkflowTargetNames.SecondaryActionButton, 0.71)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "real_upload_failed_modal_secondary_still_recognized",
                GamePage.UploadFailed,
                [
                    Detection(WorkflowTargetNames.ModalDialog, 0.88),
                    Detection(WorkflowTargetNames.SecondaryActionButton, 0.82)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "exit_menu_beats_main_menu_background",
                GamePage.ExitMenu,
                [
                    Detection(WorkflowTargetNames.ExitMenuLogoutButton, 0.91),
                    Detection(WorkflowTargetNames.MainMenuSettingsButton, 0.70)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "main_menu_settings_button_recognizes_main_menu",
                GamePage.MainMenu,
                [
                    Detection(WorkflowTargetNames.MainMenuSettingsButton, 0.86)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "main_menu_qianxing_section_header_recognizes_main_menu",
                GamePage.MainMenu,
                [
                    Detection(WorkflowTargetNames.MainMenuQianxingSectionHeader, 0.86)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "login_start_new_targets_recognize_login_start",
                GamePage.LoginStart,
                [
                    Detection(WorkflowTargetNames.LoginStartGameButton, 0.90),
                    Detection(WorkflowTargetNames.LoginServerBadge, 0.88)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "server_option_recognizes_login_server_select",
                GamePage.LoginServerSelect,
                [
                    Detection(WorkflowTargetNames.LoginServerOption, 0.87)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "server_confirm_and_cancel_recognizes_login_server_confirm",
                GamePage.LoginServerConfirm,
                [
                    Detection(WorkflowTargetNames.LoginServerConfirmButton, 0.90),
                    Detection(WorkflowTargetNames.ServerCancelButton, 0.89)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "generic_modal_buttons_recognize_upload_location_dialog",
                GamePage.UploadLocationDialog,
                [
                    Detection(WorkflowTargetNames.ModalDialog, 0.91),
                    Detection(WorkflowTargetNames.LocalArchiveTab, 0.88),
                    Detection(WorkflowTargetNames.PrimaryActionButton, 0.87),
                    Detection(WorkflowTargetNames.SecondaryActionButton, 0.86)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "generic_modal_choice_primary_recognizes_update_dialog",
                GamePage.UpdateArchiveDialog,
                [
                    Detection(WorkflowTargetNames.ModalDialog, 0.91),
                    Detection(WorkflowTargetNames.UpdateTypeOption, 0.87),
                    Detection(WorkflowTargetNames.PrimaryActionButton, 0.86)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "uploaded_archive_card_primary_action_recognizes_update_select",
                GamePage.UpdateArchiveSelect,
                [
                    Detection(WorkflowTargetNames.UploadedArchiveCard, 0.91),
                    Detection(WorkflowTargetNames.PrimaryActionButton, 0.86)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "uploaded_archive_tab_card_primary_action_stays_uploaded_archive",
                GamePage.UploadedArchive,
                [
                    Detection(WorkflowTargetNames.UploadedArchiveTab, 0.92),
                    Detection(WorkflowTargetNames.UploadedArchiveCard, 0.91),
                    Detection(WorkflowTargetNames.PrimaryActionButton, 0.86)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "uploaded_archive_right_panel_context_stays_uploaded_archive",
                GamePage.UploadedArchive,
                [
                    Detection(WorkflowTargetNames.UploadedArchiveCard, 0.91),
                    Detection(WorkflowTargetNames.PrimaryActionButton, 0.86),
                    Detection(WorkflowTargetNames.InfoPanelRow, 0.84),
                    Detection(WorkflowTargetNames.TextEditIcon, 0.83)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "local_archive_tab_blocks_update_select_promotion",
                GamePage.LocalArchive,
                [
                    Detection(WorkflowTargetNames.LocalArchiveTab, 0.92),
                    Detection(WorkflowTargetNames.UploadedArchiveCard, 0.91),
                    Detection(WorkflowTargetNames.PrimaryActionButton, 0.86)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "local_archive_tab_menu_action_recognizes_local_archive_menu",
                GamePage.LocalArchiveMenu,
                [
                    Detection(WorkflowTargetNames.LocalArchiveTab, 0.90),
                    Detection(WorkflowTargetNames.MenuActionItem, 0.86)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "local_archive_tab_card_recognizes_local_archive",
                GamePage.LocalArchive,
                [
                    Detection(WorkflowTargetNames.LocalArchiveTab, 0.90),
                    Detection(WorkflowTargetNames.LocalArchiveCard, 0.86)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "local_archive_tab_beats_generic_primary_action_button",
                GamePage.LocalArchive,
                [
                    Detection(WorkflowTargetNames.LocalArchiveTab, 0.90),
                    Detection(WorkflowTargetNames.PrimaryActionButton, 0.86)
                ]),
            new YoloPageRecognizerSyntheticCheckCase(
                "preview_editor_beats_text_edit_dialog_like_targets",
                GamePage.PreviewEditor,
                [
                    Detection(WorkflowTargetNames.ImageUploadArea, 0.98),
                    Detection(WorkflowTargetNames.TextInputArea, 0.97),
                    Detection(WorkflowTargetNames.PrimaryActionButton, 0.96),
                    Detection(WorkflowTargetNames.TextEditIcon, 0.94)
                ])
        };

        var bitmap = CreateTinyBitmap();
        var results = new List<YoloPageRecognizerSyntheticCheckResult>();
        foreach (var testCase in cases)
        {
            using var recognizer = new YoloPageRecognizer(new FakeYoloInferenceService(testCase.Detections, 1, 1));
            var recognition = await recognizer.RecognizeAsync(bitmap, cancellationToken);
            results.Add(new YoloPageRecognizerSyntheticCheckResult(
                testCase.Name,
                testCase.ExpectedPage,
                recognition.Page,
                recognition.Page == testCase.ExpectedPage,
                recognition.Confidence,
                recognition.Targets.Select(target => target.Name).ToList()));
        }

        if (!string.IsNullOrWhiteSpace(outputJsonPath))
        {
            var directory = Path.GetDirectoryName(outputJsonPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(
                outputJsonPath,
                JsonSerializer.Serialize(results, options),
                cancellationToken);
        }

        return results;
    }

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

    private static BitmapSource CreateTinyBitmap()
    {
        var bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[] { 0, 0, 0, 255 },
            4);
        bitmap.Freeze();
        return bitmap;
    }

    private static YoloDetection Detection(string className, double confidence) =>
        new(
            0,
            className,
            confidence,
            0.4,
            0.4,
            0.6,
            0.6,
            0.5,
            0.5,
            0.2,
            0.2);

    private sealed record YoloPageRecognizerSyntheticCheckCase(
        string Name,
        GamePage ExpectedPage,
        IReadOnlyList<YoloDetection> Detections);
}

public sealed record YoloPageRecognizerSyntheticCheckResult(
    string Name,
    GamePage ExpectedPage,
    GamePage RecognizedPage,
    bool Passed,
    double Confidence,
    IReadOnlyList<string> Targets);
