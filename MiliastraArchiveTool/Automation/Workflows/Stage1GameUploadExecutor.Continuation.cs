using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Services;
using System.IO;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed partial class Stage1GameUploadExecutor
{
    public UploadContinuation? Continuation { get; init; }
    private string _operation = "Upload";
    private int _continuationAtomicDepth;
    private ImageAssetResourceSpec? _continuationImageResource;
    private async Task<T> AtomicContinuationAsync<T>(Func<Task<T>> action)
    {
        _continuationAtomicDepth++;
        try { return await action(); }
        finally { _continuationAtomicDepth--; }
    }
    private Task<StablePageResult> ReturnToWorldMainConservativelyAsync(StablePageResult uploadedArchive,
        List<string> workflowLog, CancellationToken cancellationToken) =>
        ContinueStepAsync("Return to open world", async fresh =>
        {
            var current = fresh ?? uploadedArchive;
            if (current.Recognition.Page == GamePage.WorldMain) return current;
            if (current.Recognition.Page == GamePage.ArchiveEditorMenu)
                return await ExitArchiveEditorMenuToWorldMainAsync(current, workflowLog, cancellationToken);
            if (fresh != null && current.Recognition.Page == GamePage.ArchiveEditor)
                return await OpenArchiveEditorMenuAndExitToWorldMainAsync(current, workflowLog, cancellationToken);
            return await ReturnToWorldMainConservativelyCoreAsync(current, workflowLog, cancellationToken);
        }, cancellationToken);

    private Task<StablePageResult> SubmitPreviewInfoAsync(UploadRequest request, StablePageResult preview,
        List<string> workflowLog, CancellationToken cancellationToken) =>
        ContinueStepAsync("Submit preview information", async fresh =>
        {
            var current = fresh ?? preview;
            if (current.Recognition.Page == GamePage.UploadedArchive) return current;
            if (current.Recognition.Page != GamePage.PreviewEditor)
                throw new InvalidOperationException("Submission outcome is uncertain. Keep the preview or submission result page visible.");
            return await SubmitPreviewInfoCoreAsync(request, current, workflowLog, cancellationToken);
        }, cancellationToken);

    private Task<StablePageResult> SubmitImageAssetDraftAsync(UploadRequest request, StablePageResult draftBox,
        ImageAssetResourceSpec resource, List<string> workflowLog, CancellationToken cancellationToken) =>
        ContinueStepAsync("Submit image resource: " + resource.FieldName, async fresh =>
        {
            var current = fresh ?? draftBox;
            if (current.Recognition.Page == GamePage.ImageAssetEditor) return current;
            return await SubmitImageAssetDraftCoreAsync(request, current, resource, workflowLog, cancellationToken);
        }, cancellationToken);
    private sealed class ContinuationClock(IStage1WorkflowClock inner, Func<UploadContinuation?> control) : IStage1WorkflowClock
    {
        public DateTimeOffset UtcNow => inner.UtcNow - (control()?.SuspendedTime ?? TimeSpan.Zero);
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => inner.DelayAsync(delay, ct);
    }

    // Only read/locate operations or explicitly idempotent field writes belong here.
    // The caller's stack (resource index, language row, media baseline, run archive) stays alive.
    private async Task<T> ContinueStepAsync<T>(string step, Func<StablePageResult?, Task<T>> action,
        CancellationToken ct, bool retrySafe = true, GamePage? expectedPage = null)
    {
        if (Continuation == null || _continuationAtomicDepth > 0) return await action(null);
        StablePageResult? refreshed = null;
        async Task Hold(string? error)
        {
            StablePageResult? stopped = null;
            try { stopped = await CaptureContinuationPageAsync(ct, expectedPage); }
            catch (OperationCanceledException) { throw; }
            catch { /* Keep an unreadable scene blocked until it can be confirmed. */ }
            var expected = stopped;
            // A missed frame does not erase the known page contract of a read/locate step.
            // Continue still requires two fresh confirmations of that exact page.
            if (expected?.Recognition.Page == GamePage.Unknown && expectedPage is { } hint && hint != GamePage.Unknown)
                expected = WithContextualPage(expected, hint);
            var expectedTitle = expected == null ? null : await ReadContinuationTitleAsync(expected, ct);
            var expectedTab = expected?.Recognition.Page == GamePage.ImageAssetEditor ? SelectedContinuationTab(expected) : null;
            var description = $"{_hudServer} / {_hudArchive ?? "Server navigation"} / {_operation} [{step}]";
            LogWorkflow(_activeDiagnostics?.WorkflowLog as List<string>, $"step=Continuation boundary={step} action=Wait page={expected?.Recognition.Page} error={error}");
            if (error != null) SaveDiagnostic(stopped, "stage1-interrupted", isFinalFailure: false, recoverable: true, phase: step);
            await Continuation.WaitAsync(description, error, async token =>
            {
                if (expected == null || expected.Recognition.Page == GamePage.Unknown)
                    return "The interrupted page could not be identified. Terminate this task and restart from a supported page.";
                var window = _windowFinder.FindGameWindow();
                if (window.ProcessId != expected.Capture.Window.ProcessId || window.Handle != expected.Capture.Window.Handle)
                    return "The game window/process changed. Terminate this task before starting again.";
                _activation.RestoreAndActivate(window);
                for (var i = 0; i < 2; i++)
                {
                    var page = await CaptureContinuationPageAsync(token, expected.Recognition.Page);
                    if (page.Recognition.Page != expected.Recognition.Page)
                        return $"Return to {expected.Recognition.Page} before continuing. Current page: {page.Recognition.Page}.";
                    refreshed = page;
                    if (i == 0) await Task.Delay(200, token);
                }
                if (expectedTitle != null && await ReadContinuationTitleAsync(refreshed!, token) != expectedTitle)
                    return "The save title changed or could not be confirmed. Keep the original save open before continuing.";
                if (expectedTab != null && SelectedContinuationTab(refreshed!) != expectedTab)
                    return "The selected image resource changed. Return to the original resource tab before continuing.";
                LogWorkflow(_activeDiagnostics?.WorkflowLog as List<string>, $"step=Continuation boundary={step} action=Resume page={refreshed!.Recognition.Page}");
                return null;
            }, ct);
        }

        if (Continuation.State == UploadContinuationState.PauseRequested) await Hold(null);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return await action(refreshed); }
            catch (Exception ex) when (retrySafe && ex is TimeoutException or InvalidOperationException or IOException)
            {
                await Hold(ex.Message);
            }
        }
    }

    private async Task<StablePageResult> CaptureContinuationPageAsync(CancellationToken ct, GamePage? expectedPage = null)
    {
        var page = await CapturePageOnceAsync(ct);
        if (page.Recognition.Page == GamePage.Unknown)
        {
            var exit = ArchiveEditorMenuFallback.LocateExit(page.Capture.Bitmap, ct);
            if (exit != null && ArchiveEditorMenuFallback.ConfirmsMenu(await _workflowOcr.RecognizeBoxAsync(
                    page.Capture, ArchiveEditorMenuFallback.MenuTextBox, "Continuation/EditorMenu", ct)))
                return new(page.Capture, page.Recognition with { Page = GamePage.ArchiveEditorMenu,
                    Targets = page.Recognition.Targets.Append(exit).ToArray() });
            if (FindImageAssetSideTabs(page).Count >= 3)
            {
                var editor = await DetectImageAssetEditorContextAsync(page, ct);
                if (editor.Evidence.Accepted) return WithContextualPage(page, GamePage.ImageAssetEditor);
            }
            if (_continuationImageResource != null && CountTargets(page.Recognition, WorkflowTargetNames.ImageUploadArea, .25) > 0)
            {
                var draft = await DetectImageAssetDraftBoxContextAsync(page, _continuationImageResource, ct);
                if (draft.Evidence.Accepted) return WithContextualPage(page, GamePage.ImageAssetDraftBox);
            }
            if (expectedPage == GamePage.MultiLanguageEditor)
            {
                var editor = await DetectMultilanguageEditorContextAsync(page, ct);
                if (editor.Evidence.Accepted && editor.Evidence.ContextualPage == GamePage.MultiLanguageEditor)
                    return WithContextualPage(page, GamePage.MultiLanguageEditor);
            }
            if (expectedPage == GamePage.LanguageSelectDialog)
            {
                var dialog = await DetectAddLanguageDialogContextAsync(page, ct);
                if (dialog.Evidence.Accepted && dialog.Evidence.ContextualPage == GamePage.LanguageSelectDialog)
                    return WithContextualPage(page, GamePage.LanguageSelectDialog);
            }
        }
        return page;
    }

    private async Task<string?> ReadContinuationTitleAsync(StablePageResult page, CancellationToken ct)
    {
        if (page.Recognition.Page != GamePage.PreviewEditor) return null;
        var edit = FindPreviewLevelNameEditTarget(page.Recognition);
        if (edit == null) return null;
        try
        {
            var texts = await _workflowOcr.RecognizeBoxAsync(page.Capture, CreatePreviewLevelNameOcrBox(edit), "Continuation/SaveTitle", ct);
            var title = string.Join("|", texts.Where(t => t.Confidence >= .75 && t.NormalizedText.Length >= 3)
                .OrderBy(t => t.NormalizedBox.CenterX).Select(t => t.NormalizedText));
            return title.Length == 0 ? null : title;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static int? SelectedContinuationTab(StablePageResult page)
    {
        var selected = FindImageAssetSideTabs(page).Select((target, index) => (target, index))
            .Where(t => ImageAssetTabVisualState.IsSelected(page.Capture.Bitmap, t.target)).ToArray();
        return selected.Length == 1 ? selected[0].index : null;
    }
}
