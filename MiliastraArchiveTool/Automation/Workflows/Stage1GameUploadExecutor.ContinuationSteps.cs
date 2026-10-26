using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;
namespace MiliastraArchiveTool.Automation.Workflows;
public sealed partial class Stage1GameUploadExecutor
{
    private async Task<StablePageResult> WaitForExplicitServerStartReadyAsync(
        StablePageResult current, List<string> workflowLog, CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForExplicitServerStartReady", async fresh =>
        {
            var result = await WaitForExplicitServerStartReadyAsyncCore(fresh ?? current, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: current.Recognition.Page);
    }

    private async Task<StablePageResult> WaitForPageEvidenceAsync(StablePageResult current,
        IReadOnlySet<GamePage> expected, string step, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForPageEvidence", async fresh =>
        {
            var result = await WaitForPageEvidenceAsyncCore(fresh ?? current, expected, step, timeout, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: expected.Count == 1 ? expected.First() : null);
    }

    private async Task<StablePageResult> WaitForLoginStartServerBadgeAsync(
        StablePageResult initial,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForLoginStartServerBadge", async fresh =>
        {
            var result = await WaitForLoginStartServerBadgeAsyncCore(fresh ?? initial, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: initial.Recognition.Page);
    }

    private async Task<StablePageResult> WaitForRunArchiveCardVisibleAfterImportAsync(
        UploadRunArchive runArchive,
        StablePageResult initialLocalArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForRunArchiveCardVisibleAfterImport", async fresh =>
        {
            var result = await WaitForRunArchiveCardVisibleAfterImportAsyncCore(runArchive, fresh ?? initialLocalArchive, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: initialLocalArchive.Recognition.Page);
    }

    private async Task<StablePageResult> WaitForUpdateArchiveSelectByContextAsync(
        string stepName,
        TimeSpan timeout,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForUpdateArchiveSelectByContext", async fresh =>
        {
            var result = await WaitForUpdateArchiveSelectByContextAsyncCore(stepName, timeout, workflowLog, cancellationToken);
            return result;
        }, cancellationToken);
    }

    private async Task<StablePageResult> WaitForUpdateArchiveDialogByContextAsync(
        string stepName,
        TimeSpan timeout,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForUpdateArchiveDialogByContext", async fresh =>
        {
            var result = await WaitForUpdateArchiveDialogByContextAsyncCore(stepName, timeout, workflowLog, cancellationToken);
            return result;
        }, cancellationToken);
    }

    private async Task<StablePageResult> WaitForUploadedArchiveStableAfterDismissAsync(
        StablePageResult knownUploadedArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForUploadedArchiveStableAfterDismiss", async fresh =>
        {
            var result = await WaitForUploadedArchiveStableAfterDismissAsyncCore(fresh ?? knownUploadedArchive, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: knownUploadedArchive.Recognition.Page);
    }

    private async Task<StablePageResult> WaitForImageAssetEditorContextAsync(
        UploadRequest? request,
        string stepName,
        List<string> workflowLog,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForImageAssetEditorContext", async fresh =>
        {
            var result = await WaitForImageAssetEditorContextAsyncCore(request, stepName, workflowLog, timeout, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: GamePage.ImageAssetEditor);
    }

    private async Task<StablePageResult> WaitForImageAssetDraftBoxContextAsync(
        UploadRequest? request,
        ImageAssetResourceSpec resource,
        string stepName,
        List<string> workflowLog,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForImageAssetDraftBoxContext", async fresh =>
        {
            var result = await WaitForImageAssetDraftBoxContextAsyncCore(request, resource, stepName, workflowLog, timeout, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: GamePage.ImageAssetDraftBox);
    }

    private async Task<StablePageResult> WaitForImageAssetDraftSubmitResultContextAsync(
        UploadRequest request,
        ImageAssetResourceSpec resource,
        string stepName,
        List<string> workflowLog,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForImageAssetDraftSubmitResultContext", async fresh =>
        {
            var result = await WaitForImageAssetDraftSubmitResultContextAsyncCore(request, resource, stepName, workflowLog, timeout, cancellationToken);
            return result;
        }, cancellationToken);
    }

    private async Task<StablePageResult> ConfirmImageAssetTabAsync(
        UploadRequest request, StablePageResult editor, ImageAssetResourceSpec resource,
        List<string> workflowLog, CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("ConfirmImageAssetTab", async fresh =>
        {
            var result = await ConfirmImageAssetTabAsyncCore(request, fresh ?? editor, resource, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: editor.Recognition.Page);
    }

    private async Task<(StablePageResult Page, int Count)> WaitForPreviewMediaCountAsync(
        UploadRequest request, StablePageResult initial, string fieldName, int? expectedCount,
        TimeSpan timeout, List<string> workflowLog, CancellationToken cancellationToken,
        NormalizedBox? initialTitleBox = null)
    {
        return await ContinueStepAsync("WaitForPreviewMediaCount", async fresh =>
        {
            var result = await WaitForPreviewMediaCountAsyncCore(request, fresh ?? initial, fieldName, expectedCount, timeout, workflowLog, cancellationToken, fresh == null ? initialTitleBox : null);
            return result;
        }, cancellationToken, expectedPage: initial.Recognition.Page);
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewCoverTargetAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("LocatePreviewCoverTarget", async fresh =>
        {
            var result = await LocatePreviewCoverTargetAsyncCore(request, fresh ?? preview, workflowLog, cancellationToken);
            if (Continuation != null && result.Target == null) throw new InvalidOperationException("Required target was not found during LocatePreviewCoverTarget. Keep this page open and continue to scan again.");
            return result;
        }, cancellationToken, expectedPage: preview.Recognition.Page);
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewMediaTargetWithScrollAsync(
        UploadRequest request,
        StablePageResult preview,
        string fieldName,
        IReadOnlyList<string> preferredTargetNames,
        IReadOnlyList<string> labelTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("LocatePreviewMediaTargetWithScroll", async fresh =>
        {
            var result = await LocatePreviewMediaTargetWithScrollAsyncCore(request, fresh ?? preview, fieldName, preferredTargetNames, labelTokens, workflowLog, cancellationToken);
            if (Continuation != null && result.Target == null) throw new InvalidOperationException("Media upload target was not found: " + fieldName);
            return result;
        }, cancellationToken, expectedPage: preview.Recognition.Page);
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewMultilanguageTargetWithScrollAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("LocatePreviewMultilanguageTargetWithScroll", async fresh =>
        {
            var result = await LocatePreviewMultilanguageTargetWithScrollAsyncCore(request, fresh ?? preview, workflowLog, cancellationToken);
            if (Continuation != null && result.Target == null) throw new InvalidOperationException("Multilanguage entry was not found.");
            return result;
        }, cancellationToken, expectedPage: preview.Recognition.Page);
    }

    private async Task<(StablePageResult Dialog, Stage1MultilanguageContext Context)> WaitForAddLanguageDialogContextAsync(
        UploadRequest request,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForAddLanguageDialogContext", async fresh =>
        {
            var result = await WaitForAddLanguageDialogContextAsyncCore(request, context, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: GamePage.LanguageSelectDialog);
    }

    private async Task<(StablePageResult Editor, Stage1MultilanguageContext Context)> WaitForMultilanguageEditorContextAsync(
        UploadRequest request,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        string stepName,
        string diagnosticPrefix,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForMultilanguageEditorContext", async fresh =>
        {
            var result = await WaitForMultilanguageEditorContextAsyncCore(request, context, workflowLog, stepName, diagnosticPrefix, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: GamePage.MultiLanguageEditor);
    }

    private async Task<(StablePageResult Page, Stage1MultilanguageContext Context, bool SkipEdit)> WaitForMultilanguageCellEditorOrDialogAsync(
        UploadRequest request,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        string stepName,
        string language,
        string column,
        bool allowInlineEditor,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("WaitForMultilanguageCellEditorOrDialog", async fresh =>
        {
            var result = await WaitForMultilanguageCellEditorOrDialogAsyncCore(request, context, workflowLog, stepName, language, column, allowInlineEditor, cancellationToken);
            return result;
        }, cancellationToken);
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewFieldTargetWithScrollAsync(
        UploadRequest request,
        StablePageResult preview,
        string fieldName,
        IReadOnlyList<string> preferredTargetNames,
        string genericTargetName,
        IReadOnlyList<string> labelTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("LocatePreviewFieldTargetWithScroll", async fresh =>
        {
            var result = await LocatePreviewFieldTargetWithScrollAsyncCore(request, fresh ?? preview, fieldName, preferredTargetNames, genericTargetName, labelTokens, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: preview.Recognition.Page);
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewEarlyAccessDescriptionTargetAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("LocatePreviewEarlyAccessDescriptionTarget", async fresh =>
        {
            var result = await LocatePreviewEarlyAccessDescriptionTargetAsyncCore(request, fresh ?? preview, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: preview.Recognition.Page);
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewTagsTargetWithScrollAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("LocatePreviewTagsTargetWithScroll", async fresh =>
        {
            var result = await LocatePreviewTagsTargetWithScrollAsyncCore(request, fresh ?? preview, workflowLog, cancellationToken);
            if (Continuation != null && result.Target == null) throw new InvalidOperationException("Tags entry was not found.");
            return result;
        }, cancellationToken, expectedPage: preview.Recognition.Page);
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewVersionLogTargetWithScrollAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("LocatePreviewVersionLogTargetWithScroll", async fresh =>
        {
            var result = await LocatePreviewVersionLogTargetWithScrollAsyncCore(request, fresh ?? preview, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: preview.Recognition.Page);
    }

    private async Task<(StablePageResult Page, InfoPanelRowIconSelection Entry)> FindPreviewInfoEntryWithRetryAsync(
        StablePageResult current, List<string> workflowLog, CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("FindPreviewInfoEntryWithRetry", async fresh =>
        {
            var result = await FindPreviewInfoEntryWithRetryAsyncCore(fresh ?? current, workflowLog, cancellationToken);
            if (Continuation != null && result.Entry.Icon == null) throw new InvalidOperationException("Required target was not found during FindPreviewInfoEntryWithRetry. Keep this page open and continue to scan again.");
            return result;
        }, cancellationToken, expectedPage: current.Recognition.Page);
    }

    private async Task<TagChoiceSelection> FindTagChoiceWithScrollAsync(
        StablePageResult tagDialog,
        Stage1TagCatalogEntry tag,
        List<string> workflowLog,
        CancellationToken cancellationToken,
        Dictionary<string, AutomationTarget>? visibleTags = null)
    {
        return await ContinueStepAsync("Find tag: " + tag.ToolValue, async fresh =>
        {
            if (fresh != null)
            {
                visibleTags?.Clear();
                fresh = await ResetTagDialogScrollTopAsync(fresh, workflowLog, cancellationToken);
            }
            var result = await FindTagChoiceWithScrollAsyncCore(fresh ?? tagDialog, tag, workflowLog, cancellationToken, visibleTags);
            if (Continuation != null && result.Target == null) throw new InvalidOperationException("Required target was not found during FindTagChoiceWithScroll. Keep this page open and continue to scan again.");
            return result;
        }, cancellationToken, expectedPage: tagDialog.Recognition.Page);
    }

    private async Task<StablePageResult> WaitForExpectedAsync(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        TimeSpan timeout,
        int stableCount,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync(stepName, async fresh =>
        {
            var result = await WaitForExpectedAsyncCore(stepName, expectedPages, timeout, stableCount, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: expectedPages.Count == 1 ? expectedPages.First() : null);
    }

    private async Task<StablePageResult> WaitForAnyPageAsync(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool recoverableDiagnostic = false,
        string? diagnosticPhase = null)
    {
        if (recoverableDiagnostic)
            return await WaitForAnyPageAsyncCore(stepName, expectedPages, timeout, cancellationToken, recoverableDiagnostic, diagnosticPhase);
        return await ContinueStepAsync(stepName, async fresh =>
        {
            var result = await WaitForAnyPageAsyncCore(stepName, expectedPages, timeout, cancellationToken, recoverableDiagnostic, diagnosticPhase);
            return result;
        }, cancellationToken, expectedPage: expectedPages.Count == 1 ? expectedPages.First() : null);
    }

    private async Task<StablePageResult> WaitForPreviewSubmissionOutcomeAsync(
        List<string> workflowLog, CancellationToken cancellationToken, bool allowConfirmation = true)
    {
        return await ContinueStepAsync("WaitForPreviewSubmissionOutcome", async fresh =>
        {
            var result = await WaitForPreviewSubmissionOutcomeAsyncCore(workflowLog, cancellationToken, allowConfirmation);
            return result;
        }, cancellationToken);
    }

}
