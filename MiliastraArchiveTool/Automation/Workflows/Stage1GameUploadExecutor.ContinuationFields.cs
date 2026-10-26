using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;
namespace MiliastraArchiveTool.Automation.Workflows;
public sealed partial class Stage1GameUploadExecutor
{
    private async Task<(AutomationTarget? Target, IReadOnlyList<WorkflowOcrTextResult> OcrTexts, string SelectedBy)> FindImageAssetSideTabAsync(
        StablePageResult editor,
        ImageAssetResourceSpec resource,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("FindImageAssetSideTab", async fresh =>
        {
            var result = await FindImageAssetSideTabAsyncCore(fresh ?? editor, resource, workflowLog, cancellationToken);
            if (Continuation != null && result.Target == null) throw new InvalidOperationException("Required target was not found during FindImageAssetSideTab. Keep this page open and continue to scan again.");
            return result;
        }, cancellationToken, expectedPage: editor.Recognition.Page);
    }

    private async Task<OcrTargetMatch> FindImageAssetPrimaryButtonAsync(
        StablePageResult current,
        IReadOnlyList<string> acceptedTokens,
        bool preferBottom,
        CancellationToken cancellationToken,
        bool allowMissing = false)
    {
        return await ContinueStepAsync("FindImageAssetPrimaryButton", async fresh =>
        {
            LogWorkflow(_activeDiagnostics?.WorkflowLog as List<string>,
                $"step=ImageAssetButtonCheck action=Enter resource={_continuationImageResource?.FieldName ?? "UnknownResource"} " +
                $"resumed={fresh != null} originalPage={current.Recognition.Page} currentPage={(fresh ?? current).Recognition.Page} " +
                $"acceptedTokens=[{string.Join("|", acceptedTokens)}]");
            var result = await FindImageAssetPrimaryButtonAsyncCore(fresh ?? current, acceptedTokens, preferBottom, cancellationToken);
            if (!allowMissing && Continuation != null && result.Target == null) throw new InvalidOperationException("Required target was not found during FindImageAssetPrimaryButton. Keep this page open and continue to scan again.");
            return result;
        }, cancellationToken, expectedPage: current.Recognition.Page);
    }

    private async Task<StablePageResult> FindQianxingEntryInMainMenuAsync(
        StablePageResult mainMenu,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("FindQianxingEntryInMainMenu", async fresh =>
        {
            var result = await FindQianxingEntryInMainMenuAsyncCore(fresh ?? mainMenu, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: mainMenu.Recognition.Page);
    }

    private async Task<StablePageResult> WaitForUploadBranchAsync(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        TimeSpan timeout,
        List<string> workflowLog,
        CancellationToken cancellationToken,
        bool allowStableLocalArchive = false,
        string? attemptName = null,
        bool requireSandboxEvidenceForLocalArchive = false)
    {
        return await ContinueStepAsync("WaitForUploadBranch", async fresh =>
        {
            var result = await WaitForUploadBranchAsyncCore(stepName, expectedPages, timeout, workflowLog, cancellationToken, allowStableLocalArchive, attemptName, requireSandboxEvidenceForLocalArchive);
            return result;
        }, cancellationToken);
    }

    private async Task<RunArchiveCardReadyResult> EnsureRunArchiveCardReadyAsync(
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("EnsureRunArchiveCardReady", async fresh =>
        {
            var result = await EnsureRunArchiveCardReadyAsyncCore(runArchive, fresh ?? localArchive, attemptName, workflowLog, cancellationToken);
            return result;
        }, cancellationToken, expectedPage: localArchive.Recognition.Page);
    }

    private async Task<StablePageResult> FillPreviewLevelNameAsync(
        UploadRequest request,
        StablePageResult preview,
        string value,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("FillPreviewLevelName", async fresh =>
        {
            var result = await AtomicContinuationAsync(() => FillPreviewLevelNameAsyncCore(request, fresh ?? preview, value, workflowLog, cancellationToken));
            return result;
        }, cancellationToken, expectedPage: preview.Recognition.Page);
    }

    private async Task<StablePageResult> FillPreviewTextFieldAsync(
        UploadRequest request,
        StablePageResult preview,
        string fieldName,
        string value,
        IReadOnlyList<string> preferredTargetNames,
        string genericTargetName,
        IReadOnlyList<string> labelTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("FillPreviewTextField: " + fieldName, async fresh =>
        {
            var result = await AtomicContinuationAsync(() => FillPreviewTextFieldAsyncCore(request, fresh ?? preview, fieldName, value, preferredTargetNames, genericTargetName, labelTokens, workflowLog, cancellationToken));
            return result;
        }, cancellationToken, expectedPage: preview.Recognition.Page);
    }

    private async Task<StablePageResult> FillPreviewEarlyAccessDescriptionIfNeededAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await ContinueStepAsync("FillPreviewEarlyAccessDescriptionIfNeeded", async fresh =>
        {
            var result = await AtomicContinuationAsync(() => FillPreviewEarlyAccessDescriptionIfNeededAsyncCore(request, fresh ?? preview, workflowLog, cancellationToken));
            return result;
        }, cancellationToken, expectedPage: preview.Recognition.Page);
    }

}
