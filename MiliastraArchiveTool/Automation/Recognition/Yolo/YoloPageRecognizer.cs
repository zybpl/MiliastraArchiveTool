using MiliastraArchiveTool.Infrastructure;
using System.IO;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Workflows;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed class YoloPageRecognizer : IPageRecognizer, IDisposable
{
    private readonly IYoloInferenceService _inferenceService;
    private readonly bool _ownsInferenceService;

    private bool _disposed;

    public YoloPageRecognizer(IYoloInferenceService? inferenceService = null)
    {
        if (inferenceService == null)
        {
            _inferenceService = new YoloOnnxInferenceService();
            _ownsInferenceService = true;
        }
        else
        {
            _inferenceService = inferenceService;
        }
    }

    public async Task<PageRecognitionResult> RecognizeAsync(
        BitmapSource bitmap,
        CancellationToken cancellationToken)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("YOLO", new { Width = bitmap.PixelWidth, Height = bitmap.PixelHeight });
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bitmap);
        cancellationToken.ThrowIfCancellationRequested();

        var imageBytes = EncodeBitmapToPng(bitmap);
        var inference = await _inferenceService.PredictAsync(
            imageBytes,
            "BitmapSource",
            cancellationToken);

        var targets = inference.Detections
            .Select(ToAutomationTarget)
            .ToList();

        var page = RecognizePage(targets);
        WorkflowPerformanceTrace.Event("YOLO", "Observed", new { Page = page.Page.ToString(), page.Confidence, TargetCount = targets.Count });
        return page;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_ownsInferenceService && _inferenceService is IDisposable disposable)
            disposable.Dispose();

        _disposed = true;
    }

    private static PageRecognitionResult RecognizePage(IReadOnlyList<AutomationTarget> targets)
    {
        var index = targets
            .GroupBy(target => target.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(target => target.Confidence).ToList(),
                StringComparer.Ordinal);

        // Keep this priority explicit: blocking dialogs, contextual menus, and editor dialogs
        // beat ordinary pages.
        var uploadedArchiveContext = Match(
            GamePage.UploadedArchive,
            index,
            targets,
            WorkflowTargetNames.UploadedArchiveTab,
            WorkflowTargetNames.UploadedArchiveDismissArea,
            WorkflowTargetNames.InfoPanelRow,
            WorkflowTargetNames.UploadedImageAssetsButton);

        // The Stage1 model can confuse the UploadedArchive "Later" button with
        // upload_failed_close_button. Strong UploadedArchive targets identify the
        // page context, so they must win over that button-shaped false positive.
        if (uploadedArchiveContext != null)
            return uploadedArchiveContext;

        return Match(GamePage.ExitMenu, index, targets, WorkflowTargetNames.ExitMenuContinueButton, WorkflowTargetNames.ExitMenuLogoutButton, WorkflowTargetNames.ExitMenuDesktopButton)
            ?? Match(
                GamePage.UpdateArchiveDialog,
                index,
                targets,
                WorkflowTargetNames.UpdateArchiveDialogPanel,
                WorkflowTargetNames.UpdateTypeOption,
                WorkflowTargetNames.UpdateStartUploadButton)
            ?? MatchWhen(
                GamePage.UploadLocationDialog,
                index,
                targets,
                static byName => Has(byName, WorkflowTargetNames.ModalDialog) &&
                                 Has(byName, WorkflowTargetNames.PrimaryActionButton) &&
                                 Has(byName, WorkflowTargetNames.SecondaryActionButton) &&
                                 Has(byName, WorkflowTargetNames.LocalArchiveTab))
            ?? MatchWhen(
                GamePage.UploadFailed,
                index,
                targets,
                static byName => Has(byName, WorkflowTargetNames.ModalDialog) &&
                                 Has(byName, WorkflowTargetNames.SecondaryActionButton) &&
                                 !Has(byName, WorkflowTargetNames.PrimaryActionButton))
            ?? Match(GamePage.UploadValidationFailed, index, targets)
            // Stage1 classes.txt has no sandbox_not_started target yet, so this remains a non-matching placeholder.
            ?? Match(GamePage.SandboxNotStarted, index, targets)
            ?? MatchAll(GamePage.LocalArchiveMenu, index, targets, WorkflowTargetNames.LocalArchiveTab, WorkflowTargetNames.MenuActionItem)
            ?? Match(GamePage.LocalArchive, index, targets, WorkflowTargetNames.LocalArchiveTab)
            ?? Match(GamePage.ArchiveEditorMenu, index, targets, WorkflowTargetNames.ArchiveEditorExitButton)
            ?? MatchWhen(GamePage.UpdateArchiveSelect, index, targets, IsUpdateArchiveSelectVisualContext)
            ?? MatchWhen(GamePage.ImageAssetDraftBox, index, targets, IsImageAssetDraftBoxVisualContext)
            ?? MatchWhen(GamePage.ImageAssetEditor, index, targets, IsImageAssetEditorVisualContext)
            ?? MatchWhen(GamePage.PreviewEditor, index, targets, IsPreviewEditorVisualContext)
            ?? MatchWhen(GamePage.TextEditDialog, index, targets, IsTextEditDialogVisualContext)
            ?? MatchWhen(GamePage.TagDialog, index, targets, IsTagDialogVisualContext)
            ?? MatchWhen(
                GamePage.TextEditDialog,
                index,
                targets,
                static byName => Has(byName, WorkflowTargetNames.TextInputArea) &&
                                 Has(byName, WorkflowTargetNames.TextConfirmButton) &&
                                 !IsPreviewEditorVisualContext(byName))
            ?? Match(GamePage.LanguageSelectDialog, index, targets, WorkflowTargetNames.LanguageOption, WorkflowTargetNames.LanguageConfirmButton)
            ?? MatchWhen(
                GamePage.MultiLanguageEditor,
                index,
                targets,
                IsMultilanguageEditorVisualContext)
            ?? Match(
                GamePage.MainMenu,
                index,
                targets,
                WorkflowTargetNames.QianxingRealmEntry,
                WorkflowTargetNames.MainMenuQianxingSectionHeader,
                WorkflowTargetNames.MainMenuSettingsButton)
            ?? Match(GamePage.WorldMain, index, targets, WorkflowTargetNames.WorldPaimonIcon)
            ?? MatchAll(GamePage.LoginServerConfirm, index, targets, WorkflowTargetNames.LoginServerConfirmButton, WorkflowTargetNames.ServerCancelButton)
            ?? Match(GamePage.LoginServerConfirm, index, targets, WorkflowTargetNames.LoginServerConfirmButton)
            ?? Match(GamePage.LoginServerSelect, index, targets, WorkflowTargetNames.LoginServerOption)
            ?? Match(
                GamePage.LoginStart,
                index,
                targets,
                WorkflowTargetNames.LoginStartGameButton,
                WorkflowTargetNames.LoginServerBadge)
            ?? Match(GamePage.LoginEnter, index, targets, WorkflowTargetNames.LoginClickEnter)
            // Stage1 classes.txt has no archive_uploading / transition / downloading-resource targets yet.
            ?? Unknown(targets);
    }

    private static bool Has(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName,
        string targetName) =>
        targetsByName.TryGetValue(targetName, out var targets) && targets.Count > 0;

    private static bool HasAny(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName,
        params string[] targetNames) =>
        targetNames.Any(targetName => Has(targetsByName, targetName));

    private static bool HasMatching(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName,
        string targetName,
        Func<AutomationTarget, bool> predicate) =>
        targetsByName.TryGetValue(targetName, out var targets) && targets.Any(predicate);

    private static bool IsUpdateArchiveSelectVisualContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        Has(targetsByName, WorkflowTargetNames.UploadedArchiveCard) &&
        Has(targetsByName, WorkflowTargetNames.PrimaryActionButton) &&
        !HasAny(
            targetsByName,
            WorkflowTargetNames.LocalArchiveTab,
            WorkflowTargetNames.UploadedArchiveTab,
            WorkflowTargetNames.UploadedArchiveDismissArea,
            WorkflowTargetNames.InfoPanelRow,
            WorkflowTargetNames.UploadedImageAssetsButton);

    private static bool IsPreviewEditorVisualContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        IsPreviewEditorDirectVisualContext(targetsByName) ||
        IsPreviewEditorGenericTextPanelContext(targetsByName) ||
        IsPreviewEditorGenericEditRowsContext(targetsByName) ||
        IsPreviewEditorGenericLowerPanelContext(targetsByName);

    private static bool IsPreviewEditorDirectVisualContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        HasAny(
            targetsByName,
            WorkflowTargetNames.ImageUploadArea,
            WorkflowTargetNames.PreviewCoverImageArea,
            WorkflowTargetNames.PreviewLevelNameEditButton,
            WorkflowTargetNames.PreviewGameplayDescription,
            WorkflowTargetNames.PreviewLevelDescription,
            WorkflowTargetNames.PreviewEarlyAccessCheckbox,
            WorkflowTargetNames.PreviewEarlyAccessDescription,
            WorkflowTargetNames.PreviewTagsArea,
            WorkflowTargetNames.PreviewVersionRecordArea,
            WorkflowTargetNames.PreviewMultilanguageArea,
            WorkflowTargetNames.PreviewExtraImagesArea,
            WorkflowTargetNames.PreviewVideoArea,
            WorkflowTargetNames.PreviewSubmitButton) &&
        HasAny(
            targetsByName,
            WorkflowTargetNames.PrimaryActionButton,
            WorkflowTargetNames.TextInputArea,
            WorkflowTargetNames.TextEditIcon,
            WorkflowTargetNames.EditRow);

    private static bool IsPreviewEditorGenericLowerPanelContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        Has(targetsByName, WorkflowTargetNames.MediaAddTile) &&
        Has(targetsByName, WorkflowTargetNames.PrimaryActionButton) &&
        HasAny(targetsByName, WorkflowTargetNames.EditRow, WorkflowTargetNames.TextEditIcon) &&
        !HasAny(
            targetsByName,
            WorkflowTargetNames.ModalDialog,
            WorkflowTargetNames.SideTabButton,
            WorkflowTargetNames.LanguageOption,
            WorkflowTargetNames.ChoiceOption);

    private static bool IsPreviewEditorGenericEditRowsContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        Has(targetsByName, WorkflowTargetNames.EditRow) &&
        Has(targetsByName, WorkflowTargetNames.TextEditIcon) &&
        HasMatching(
            targetsByName,
            WorkflowTargetNames.PrimaryActionButton,
            static target => target.CenterX >= 0.60 &&
                             target.CenterY >= 0.65) &&
        !HasAny(
            targetsByName,
            WorkflowTargetNames.ModalDialog,
            WorkflowTargetNames.SideTabButton,
            WorkflowTargetNames.LanguageOption,
            WorkflowTargetNames.ChoiceOption,
            WorkflowTargetNames.TextInputArea);

    private static bool IsPreviewEditorGenericTextPanelContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        HasMatching(
            targetsByName,
            WorkflowTargetNames.TextInputArea,
            static target => target.CenterX >= 0.50 &&
                             target.Width is >= 0.25 and <= 0.50 &&
                             target.Height is >= 0.08 and <= 0.25) &&
        HasMatching(
            targetsByName,
            WorkflowTargetNames.PrimaryActionButton,
            static target => target.CenterX >= 0.60 &&
                             target.CenterY >= 0.65) &&
        HasAny(targetsByName, WorkflowTargetNames.TextEditIcon, WorkflowTargetNames.ChoiceOption);

    private static bool IsMultilanguageEditorVisualContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        HasAny(targetsByName,
            WorkflowTargetNames.MultilanguageAddButton,
            WorkflowTargetNames.MultilanguageLevelNameCell,
            WorkflowTargetNames.MultilanguageFieldEditButton,
            WorkflowTargetNames.MultilanguageCloseButton) ||
        // Generic buttons also appear in the world HUD. Require the edit table
        // and a lower-panel action together, never a lone button/row/edit icon.
        (HasMatching(targetsByName, WorkflowTargetNames.PrimaryActionButton,
             static target => target.Confidence >= 0.35 &&
                              target.CenterX is >= 0.35 and <= 0.90 &&
                              target.CenterY is >= 0.65 and <= 0.95) &&
         targetsByName.TryGetValue(WorkflowTargetNames.TextEditIcon, out var icons) &&
         icons.Count(static target => target.Confidence >= 0.35 &&
                                      target.CenterX is >= 0.15 and <= 0.90 &&
                                      target.CenterY is >= 0.15 and <= 0.85) >= 2);

    private static bool IsImageAssetEditorVisualContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        Has(targetsByName, WorkflowTargetNames.SideTabButton) &&
        Has(targetsByName, WorkflowTargetNames.PrimaryActionButton);

    private static bool IsImageAssetDraftBoxVisualContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        Has(targetsByName, WorkflowTargetNames.ImageUploadArea) &&
        Has(targetsByName, WorkflowTargetNames.PrimaryActionButton) &&
        !Has(targetsByName, WorkflowTargetNames.SideTabButton) &&
        !HasAny(
            targetsByName,
            WorkflowTargetNames.EditRow,
            WorkflowTargetNames.TextEditIcon,
            WorkflowTargetNames.TextInputArea,
            WorkflowTargetNames.MediaAddTile,
            WorkflowTargetNames.PreviewCoverImageArea,
            WorkflowTargetNames.PreviewExtraImagesArea,
            WorkflowTargetNames.PreviewVideoArea,
            WorkflowTargetNames.PreviewSubmitButton);

    private static bool IsTextEditDialogVisualContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        HasMatching(
            targetsByName,
            WorkflowTargetNames.TextInputArea,
            static target => target.CenterX is >= 0.35 and <= 0.65 &&
                             target.CenterY is >= 0.25 and <= 0.70 &&
                             target.Width >= 0.45 &&
                             target.Height >= 0.25) &&
        HasMatching(
            targetsByName,
            WorkflowTargetNames.PrimaryActionButton,
            static target => target.CenterX is >= 0.35 and <= 0.65 &&
                             target.CenterY >= 0.65) &&
        !IsPreviewEditorVisualContext(targetsByName);

    private static bool IsTagDialogVisualContext(
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName) =>
        HasAny(
            targetsByName,
            WorkflowTargetNames.ChoiceOption,
            WorkflowTargetNames.TagOption,
            WorkflowTargetNames.TagConfirmButton) &&
        HasAny(
            targetsByName,
            WorkflowTargetNames.PrimaryActionButton,
            WorkflowTargetNames.TagConfirmButton) &&
        !Has(targetsByName, WorkflowTargetNames.TextInputArea);

    private static PageRecognitionResult? MatchWhen(
        GamePage page,
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName,
        IReadOnlyList<AutomationTarget> allTargets,
        Func<IReadOnlyDictionary<string, List<AutomationTarget>>, bool> predicate)
    {
        if (!predicate(targetsByName))
            return null;

        var confidence = targetsByName.Values
            .SelectMany(static targets => targets)
            .Select(static target => target.Confidence)
            .DefaultIfEmpty(0.25)
            .Max();
        return new PageRecognitionResult(page, confidence, "YoloPageRecognizer", allTargets);
    }

    private static PageRecognitionResult? Match(
        GamePage page,
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName,
        IReadOnlyList<AutomationTarget> allTargets,
        params string[] targetNames)
    {
        if (targetNames.Length == 0)
            return null;

        var confidence = targetNames
            .Select(targetName => targetsByName.TryGetValue(targetName, out var targets)
                ? targets[0].Confidence
                : 0)
            .DefaultIfEmpty(0)
            .Max();

        return confidence > 0
            ? new PageRecognitionResult(page, confidence, "YoloPageRecognizer", allTargets)
            : null;
    }

    private static PageRecognitionResult? MatchAll(
        GamePage page,
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName,
        IReadOnlyList<AutomationTarget> allTargets,
        params string[] targetNames)
    {
        if (targetNames.Length == 0)
            return null;

        var confidences = new List<double>();
        foreach (var targetName in targetNames)
        {
            if (!targetsByName.TryGetValue(targetName, out var targets) || targets.Count == 0)
                return null;

            confidences.Add(targets[0].Confidence);
        }

        return new PageRecognitionResult(page, confidences.Max(), "YoloPageRecognizer", allTargets);
    }

    private static PageRecognitionResult? MatchAtLeast(
        GamePage page,
        IReadOnlyDictionary<string, List<AutomationTarget>> targetsByName,
        IReadOnlyList<AutomationTarget> allTargets,
        string targetName,
        int minimumCount)
    {
        if (!targetsByName.TryGetValue(targetName, out var targets) || targets.Count < minimumCount)
            return null;

        return new PageRecognitionResult(page, targets[0].Confidence, "YoloPageRecognizer", allTargets);
    }

    private static PageRecognitionResult Unknown(IReadOnlyList<AutomationTarget> targets) =>
        new(GamePage.Unknown, 0, "YoloPageRecognizer", targets);

    private static AutomationTarget ToAutomationTarget(YoloDetection detection) =>
        new(
            detection.ClassName,
            detection.CenterXNormalized,
            detection.CenterYNormalized,
            detection.WidthNormalized,
            detection.HeightNormalized,
            detection.Confidence);

    private static byte[] EncodeBitmapToPng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
