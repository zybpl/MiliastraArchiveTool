using System.IO;
using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed class GameUploadExecutor : IUploadExecutor
{
    private static readonly TimeSpan ShortWait = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MediumWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan LongWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SandboxWait = TimeSpan.FromMinutes(1);

    private readonly ArchiveStorageService _storage;
    private readonly StablePageWaiter _pageWaiter;
    private readonly ITargetLocator _targetLocator;
    private readonly GameInputAutomationService _input;
    private readonly WindowsFileDialogAutomationService _fileDialog;
    private readonly GameWindowActivationService _activation;
    private readonly SandboxProcessWatcher _sandboxWatcher;
    private readonly UploadRunArchiveService _runArchiveService;

    public GameUploadExecutor()
    {
        _storage = new ArchiveStorageService();
        _pageWaiter = new StablePageWaiter(new GameSnapshotProvider(), new FakePageRecognizer());
        _targetLocator = new ManualTargetLocator();
        _input = new GameInputAutomationService();
        _fileDialog = new WindowsFileDialogAutomationService();
        _activation = new GameWindowActivationService();
        _sandboxWatcher = new SandboxProcessWatcher();
        _runArchiveService = new UploadRunArchiveService(_storage);
    }

    public GameUploadExecutor(
        ArchiveStorageService storage,
        StablePageWaiter pageWaiter,
        ITargetLocator targetLocator,
        GameInputAutomationService input,
        WindowsFileDialogAutomationService fileDialog,
        GameWindowActivationService activation,
        SandboxProcessWatcher? sandboxWatcher = null,
        UploadRunArchiveService? runArchiveService = null)
    {
        _storage = storage;
        _pageWaiter = pageWaiter;
        _targetLocator = targetLocator;
        _input = input;
        _fileDialog = fileDialog;
        _activation = activation;
        _sandboxWatcher = sandboxWatcher ?? new SandboxProcessWatcher();
        _runArchiveService = runArchiveService ?? new UploadRunArchiveService(storage);
    }

    public async Task<UploadExecutionResult> ExecuteAsync(
        UploadRequest request,
        IProgress<UploadTaskState> progress,
        CancellationToken cancellationToken)
    {
        if (request.IsUpdate)
            return UploadExecutionResult.Failure("Update-existing-archive workflow is not implemented yet.");

        var workflowLog = new List<string>();
        try
        {
            progress.Report(UploadTaskState.WaitingForGame);
            var start = await RecognizeStartPageAsync(request, cancellationToken);

            if (request.Server != UploadServer.None)
                return UploadExecutionResult.Failure("Specific-server switching workflow is not implemented yet. Start from login/server pages only for future switching support.");

            progress.Report(UploadTaskState.Recognizing);
            var world = await EnterWorldIfNeededAsync(request, start, cancellationToken);

            progress.Report(UploadTaskState.Navigating);
            var localArchive = await EnsureLocalArchivePageFromWorldAsync(world, cancellationToken);

            progress.Report(UploadTaskState.Filling);
            var runArchive = await _runArchiveService.CreateAsync(request.Archive, request.Server, cancellationToken);
            localArchive = await ImportArchiveAsync(runArchive, localArchive, cancellationToken);

            progress.Report(UploadTaskState.Submitting);
            var uploaded = await UploadNewArchiveWithSandboxWarmupAsync(runArchive, localArchive, workflowLog, cancellationToken);

            progress.Report(UploadTaskState.Verifying);
            uploaded = await DismissUploadCompletedDialogAsync(uploaded, workflowLog, cancellationToken);
            uploaded = await EditPreviewInfoAsync(uploaded, cancellationToken);
            uploaded = await UploadImageAssetsAsync(uploaded, cancellationToken);
            var final = await ReturnToWorldMainAsync(uploaded, workflowLog, cancellationToken);

            return UploadExecutionResult.Success(
                $"New archive upload workflow skeleton completed for run archive '{runArchive.RunArchiveDisplayName}'. Current page: {final.Recognition.Page}. {FormatWorkflowLog(workflowLog)}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return UploadExecutionResult.Failure($"New archive upload workflow failed: {ex.Message} {FormatWorkflowLog(workflowLog)}");
        }
    }

    private async Task<StablePageResult> RecognizeStartPageAsync(
        UploadRequest request,
        CancellationToken cancellationToken)
    {
        var current = await _pageWaiter.RecognizeOnceAsync(cancellationToken);
        var page = current.Recognition.Page;

        if (page == GamePage.Unknown)
            throw new InvalidOperationException("Start page is Unknown. Refusing to continue.");

        if (request.Server == UploadServer.None)
        {
            if (page != GamePage.WorldMain)
                throw new InvalidOperationException($"Current-server mode must start from WorldMain. Current page: {page}.");
            return current;
        }

        if (page == GamePage.WorldMain)
            throw new InvalidOperationException("Specific-server mode cannot start from WorldMain. Select current logged-in server mode instead.");

        if (page is GamePage.LoginStart or GamePage.LoginServerSelect or GamePage.LoginServerConfirm or GamePage.LoginEnter)
            return current;

        throw new InvalidOperationException($"Specific-server mode must start from a login/server page. Current page: {page}.");
    }

    private async Task<StablePageResult> EnterWorldIfNeededAsync(
        UploadRequest request,
        StablePageResult start,
        CancellationToken cancellationToken)
    {
        if (request.Server == UploadServer.None)
            return start.Recognition.Page == GamePage.WorldMain
                ? start
                : throw new InvalidOperationException($"Current-server mode cannot enter world from {start.Recognition.Page}.");

        // Future task: select request.Server, confirm, enter world, then verify WorldMain.
        await Task.CompletedTask;
        throw new NotSupportedException("Specific-server login and switching workflow is not implemented yet.");
    }

    private async Task<StablePageResult> OpenMiliastraFromWorldAsync(
        StablePageResult worldMain,
        CancellationToken cancellationToken)
    {
        if (worldMain.Recognition.Page != GamePage.WorldMain)
            throw new InvalidOperationException($"Expected WorldMain before opening Miliastra. Current page: {worldMain.Recognition.Page}.");

        var menuTarget = _targetLocator.FindTarget(worldMain.Recognition, WorkflowTargetNames.WorldPaimonIcon);
        if (menuTarget != null)
        {
            await ClickTargetAsync(
                worldMain,
                WorkflowTargetNames.WorldPaimonIcon,
                WorkflowFallbackCoordinates.WorldPaimonIcon,
                cancellationToken);
        }
        else
        {
            await _input.PressKeyAsync(worldMain.Capture.Window, GameVirtualKey.Escape, cancellationToken);
        }

        var menu = await _pageWaiter.WaitForAnyAsync(
            [GamePage.MainMenu],
            MediumWait,
            cancellationToken,
            requiredConsecutiveMatches: 1);

        if (menu.Recognition.Page == GamePage.MainMenu)
        {
            await _input.ScrollAsync(
                menu.Capture.Window,
                WorkflowFallbackCoordinates.QianxingRealmEntry.NormalizedX,
                WorkflowFallbackCoordinates.QianxingRealmEntry.NormalizedY,
                -600,
                cancellationToken);

            menu = await _pageWaiter.WaitForAnyAsync(
                [GamePage.MainMenu],
                ShortWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);
        }

        await ClickTargetAsync(
            menu,
            WorkflowTargetNames.QianxingRealmEntry,
            WorkflowFallbackCoordinates.QianxingRealmEntry,
            cancellationToken);

        return await _pageWaiter.WaitForAnyAsync(
            [GamePage.LocalArchive, GamePage.Transition],
            LongWait,
            cancellationToken,
            requiredConsecutiveMatches: 1);
    }

    private async Task<StablePageResult> EnsureLocalArchivePageFromWorldAsync(
        StablePageResult worldMain,
        CancellationToken cancellationToken)
    {
        var current = await OpenMiliastraFromWorldAsync(worldMain, cancellationToken);

        if (current.Recognition.Page == GamePage.Transition)
        {
            current = await _pageWaiter.WaitForAnyAsync(
                [GamePage.LocalArchive],
                LongWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);
        }

        if (current.Recognition.Page != GamePage.LocalArchive)
            throw new InvalidOperationException($"Failed to enter LocalArchive from WorldMain. Current page: {current.Recognition.Page}.");

        return current;
    }

    private async Task<StablePageResult> ImportArchiveAsync(
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        CancellationToken cancellationToken)
    {
        if (localArchive.Recognition.Page != GamePage.LocalArchive)
            throw new InvalidOperationException($"Import must start on LocalArchive. Current page: {localArchive.Recognition.Page}.");

        if (string.IsNullOrWhiteSpace(runArchive.RunArchivePath) || !File.Exists(runArchive.RunArchivePath))
            throw new FileNotFoundException(
                $"Run .gil archive file was not found for '{runArchive.RunArchiveDisplayName}'.",
                runArchive.RunArchivePath);

        await ClickTargetAsync(
            localArchive,
            WorkflowTargetNames.ImportArchiveButton,
            WorkflowFallbackCoordinates.ImportArchiveButton,
            cancellationToken);

        await _fileDialog.SelectFileAsync(localArchive.Capture.Window, runArchive.RunArchivePath, WindowsFileDialogAutomationService.SelectionTimeout, cancellationToken);
        _activation.RestoreAndActivate(localArchive.Capture.Window);

        var result = await _pageWaiter.WaitForAnyAsync(
            [GamePage.LocalArchive, GamePage.Transition],
            LongWait,
            cancellationToken,
            requiredConsecutiveMatches: 1);

        if (result.Recognition.Page == GamePage.Transition)
        {
            result = await _pageWaiter.WaitForAnyAsync(
                [GamePage.LocalArchive],
                LongWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);
        }

        return result;
    }

    private async Task<StablePageResult> UploadNewArchiveWithSandboxWarmupAsync(
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var first = await TryUploadNewArchiveOnceAsync(runArchive, localArchive, workflowLog, cancellationToken);
        workflowLog.Add($"First upload entered sandbox readiness wait from page {first.Recognition.Page}.");
        var ready = await WaitForSandboxReadyAsync(runArchive, first, workflowLog, cancellationToken);
        return await TryUploadNewArchiveOnceAsync(runArchive, ready, workflowLog, cancellationToken, requireFinalUpload: true);
    }

    private async Task<StablePageResult> TryUploadNewArchiveOnceAsync(
        UploadRunArchive runArchive,
        StablePageResult current,
        List<string> workflowLog,
        CancellationToken cancellationToken,
        bool requireFinalUpload = false)
    {
        if (current.Recognition.Page != GamePage.LocalArchive)
        {
            current = await _pageWaiter.WaitForAnyAsync(
                [GamePage.LocalArchive],
                MediumWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);
        }
        if (current.Recognition.Page != GamePage.LocalArchive)
            throw new InvalidOperationException(
                $"Cannot locate run archive '{runArchive.RunArchiveDisplayName}' because current page is {current.Recognition.Page}.");

        await ClickTargetAsync(
            current,
            WorkflowTargetNames.LocalArchiveCard,
            WorkflowFallbackCoordinates.LocalArchiveCard,
            cancellationToken);
        await WaitAfterActionAsync(cancellationToken);

        current = await _pageWaiter.WaitForAnyAsync(
            [GamePage.LocalArchive, GamePage.LocalArchiveMenu],
            ShortWait,
            cancellationToken,
            requiredConsecutiveMatches: 1);

        if (current.Recognition.Page == GamePage.LocalArchive)
        {
            await ClickTargetAsync(
                current,
                WorkflowTargetNames.LocalArchiveMoreButton,
                WorkflowFallbackCoordinates.LocalArchiveMoreButton,
                cancellationToken);

            current = await _pageWaiter.WaitForAnyAsync(
                [GamePage.LocalArchiveMenu],
                ShortWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);
        }

        await ClickTargetAsync(
            current,
            WorkflowTargetNames.LocalArchiveUploadMenuItem,
            WorkflowFallbackCoordinates.LocalArchiveUploadMenuItem,
            cancellationToken);

        var uploadEntry = await _pageWaiter.WaitForAnyAsync(
            [
                GamePage.UploadLocationDialog,
                GamePage.ArchiveUploading,
                GamePage.SandboxNotStarted,
                GamePage.UploadFailed,
                GamePage.UploadValidationFailed
            ],
            MediumWait,
            cancellationToken,
            requiredConsecutiveMatches: 1,
            throwOnBlocking: false);

        if (uploadEntry.Recognition.Page == GamePage.UploadValidationFailed)
            throw new InvalidOperationException($"Upload validation failed for run archive '{runArchive.RunArchiveDisplayName}'.");

        if (uploadEntry.Recognition.Page == GamePage.UploadLocationDialog)
        {
            workflowLog.Add("UploadLocationDialog appeared; clicking upload_new_button.");
            await ClickTargetAsync(
                uploadEntry,
                WorkflowTargetNames.UploadNewButton,
                WorkflowFallbackCoordinates.UploadNewButton,
                cancellationToken);

            uploadEntry = await _pageWaiter.WaitForAnyAsync(
                [
                    GamePage.ArchiveUploading,
                    GamePage.SandboxNotStarted,
                    GamePage.UploadFailed,
                    GamePage.UploadValidationFailed
                ],
                MediumWait,
                cancellationToken,
                requiredConsecutiveMatches: 1,
                throwOnBlocking: false);

            if (uploadEntry.Recognition.Page == GamePage.UploadValidationFailed)
                throw new InvalidOperationException($"Upload validation failed after UploadLocationDialog for run archive '{runArchive.RunArchiveDisplayName}'.");
        }
        else if (uploadEntry.Recognition.Page == GamePage.ArchiveUploading)
        {
            workflowLog.Add("UploadLocationDialog did not appear; upload entered ArchiveUploading directly.");
        }

        if (uploadEntry.Recognition.Page is GamePage.SandboxNotStarted or GamePage.UploadFailed)
        {
            if (requireFinalUpload)
                throw new InvalidOperationException($"Final upload failed on page {uploadEntry.Recognition.Page} for run archive '{runArchive.RunArchiveDisplayName}'.");

            workflowLog.Add($"First upload reached {uploadEntry.Recognition.Page}; continuing with sandbox readiness wait.");
            return uploadEntry;
        }

        var expected = requireFinalUpload
            ? new[]
            {
                GamePage.UploadedArchive,
                GamePage.UploadCompletedPrompt,
                GamePage.ArchiveUploading,
                GamePage.Transition,
                GamePage.UploadFailed,
                GamePage.UploadValidationFailed
            }
            : new[]
            {
                GamePage.ArchiveUploading,
                GamePage.UploadCompletedPrompt,
                GamePage.UploadedArchive,
                GamePage.LocalArchive,
                GamePage.Transition,
                GamePage.SandboxNotStarted,
                GamePage.UploadFailed,
                GamePage.UploadValidationFailed
            };

        return await _pageWaiter.WaitForAnyAsync(
            expected,
            requireFinalUpload ? LongWait : MediumWait,
            cancellationToken,
            requiredConsecutiveMatches: 1,
            throwOnBlocking: requireFinalUpload);
    }

    private async Task<StablePageResult> WaitForSandboxReadyAsync(
        UploadRunArchive runArchive,
        StablePageResult firstUploadResult,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + SandboxWait;
        var current = firstUploadResult;
        var sandboxWasSeen = _sandboxWatcher.IsSandboxRunning();
        var stableResult = new SandboxProcessWaitResult(false, sandboxWasSeen, TimeSpan.Zero);
        StablePageResult? localArchiveResult = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sandboxWatcher.IsSandboxRunning())
                sandboxWasSeen = true;

            if (current.Recognition.Page == GamePage.LocalArchive)
                localArchiveResult = current;

            if (current.Recognition.Page == GamePage.Unknown)
                throw new InvalidOperationException(
                    $"Sandbox warmup for '{runArchive.RunArchiveDisplayName}' reached Unknown page. Refusing to continue.");

            if (localArchiveResult != null)
            {
                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                stableResult = await _sandboxWatcher.WaitForStableAsync(
                    TimeSpan.FromSeconds(3),
                    remaining < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : remaining,
                    cancellationToken);
                if (stableResult.IsStable)
                {
                    workflowLog.Add("Sandbox process became stable for at least 3 seconds and page returned to LocalArchive.");
                    return localArchiveResult;
                }

                sandboxWasSeen = sandboxWasSeen || stableResult.WasSeen;
                break;
            }

            await Task.Delay(650, cancellationToken);
            current = await _pageWaiter.RecognizeOnceAsync(cancellationToken);
        }

        throw new TimeoutException(
            $"Timed out waiting for sandbox readiness for '{runArchive.RunArchiveDisplayName}'. " +
            $"Last page={current.Recognition.Page}, confidence={current.Recognition.Confidence:F2}, " +
            $"sandboxSeen={sandboxWasSeen || stableResult.WasSeen}, stableDuration={stableResult.StableDuration.TotalSeconds:F1}s.");
    }

    private async Task<StablePageResult> DismissUploadCompletedDialogAsync(
        StablePageResult current,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (current.Recognition.Page == GamePage.ArchiveUploading ||
            current.Recognition.Page == GamePage.Transition)
        {
            current = await _pageWaiter.WaitForAnyAsync(
                [GamePage.UploadedArchive, GamePage.UploadCompletedPrompt],
                LongWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);
        }

        if (current.Recognition.Page == GamePage.UploadCompletedPrompt)
        {
            workflowLog.Add("UploadCompletedPrompt appeared; dismissing by clicking safe outside area.");
            await ClickTargetAsync(
                current,
                WorkflowTargetNames.UploadCompletedDismissArea,
                WorkflowFallbackCoordinates.UploadCompletedDismissArea,
                cancellationToken);

            current = await _pageWaiter.WaitForAnyAsync(
                [GamePage.UploadedArchive, GamePage.Transition],
                MediumWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);
        }

        if (current.Recognition.Page == GamePage.Transition)
        {
            current = await _pageWaiter.WaitForAnyAsync(
                [GamePage.UploadedArchive],
                MediumWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);
        }

        if (current.Recognition.Page != GamePage.UploadedArchive)
            throw new InvalidOperationException($"Expected UploadedArchive after final upload. Current page: {current.Recognition.Page}.");

        workflowLog.Add("Final upload reached UploadedArchive without clicking publish.");
        return current;
    }

    private static Task<StablePageResult> EditPreviewInfoAsync(
        StablePageResult uploadedArchive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(uploadedArchive);
    }

    private static Task<StablePageResult> UploadImageAssetsAsync(
        StablePageResult uploadedArchive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(uploadedArchive);
    }

    private async Task<StablePageResult> ReturnToWorldMainAsync(
        StablePageResult current,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (current.Recognition.Page != GamePage.UploadedArchive &&
            current.Recognition.Page != GamePage.ArchiveEditor &&
            current.Recognition.Page != GamePage.ArchiveEditorMenu)
        {
            throw new InvalidOperationException($"Expected UploadedArchive, ArchiveEditor, or ArchiveEditorMenu before returning to WorldMain. Current page: {current.Recognition.Page}.");
        }

        if (current.Recognition.Page == GamePage.UploadedArchive)
        {
            await _input.PressKeyAsync(current.Capture.Window, GameVirtualKey.Escape, cancellationToken);
            workflowLog.Add("After preview/image handling, pressed ESC from UploadedArchive to return to ArchiveEditor.");
            current = await _pageWaiter.WaitForAnyAsync(
                [GamePage.ArchiveEditor, GamePage.Transition],
                MediumWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);

            if (current.Recognition.Page == GamePage.Transition)
            {
                current = await _pageWaiter.WaitForAnyAsync(
                    [GamePage.ArchiveEditor],
                    MediumWait,
                    cancellationToken,
                    requiredConsecutiveMatches: 1);
            }
        }

        if (current.Recognition.Page == GamePage.ArchiveEditor)
        {
            await _input.PressKeyAsync(current.Capture.Window, GameVirtualKey.Escape, cancellationToken);
            workflowLog.Add("Pressed ESC on ArchiveEditor to open ArchiveEditorMenu.");
            current = await _pageWaiter.WaitForAnyAsync(
                [GamePage.ArchiveEditorMenu, GamePage.Transition],
                MediumWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);

            if (current.Recognition.Page == GamePage.Transition)
            {
                current = await _pageWaiter.WaitForAnyAsync(
                    [GamePage.ArchiveEditorMenu],
                    MediumWait,
                    cancellationToken,
                    requiredConsecutiveMatches: 1);
            }
        }

        if (current.Recognition.Page != GamePage.ArchiveEditorMenu)
            throw new InvalidOperationException($"Expected ArchiveEditorMenu before exiting to WorldMain. Current page: {current.Recognition.Page}.");

        await ClickTargetAsync(
            current,
            WorkflowTargetNames.ArchiveEditorExitButton,
            WorkflowFallbackCoordinates.ArchiveEditorExitButton,
            cancellationToken);

        current = await _pageWaiter.WaitForAnyAsync(
            [GamePage.WorldMain, GamePage.Transition],
            LongWait,
            cancellationToken,
            requiredConsecutiveMatches: 1);

        if (current.Recognition.Page == GamePage.Transition)
        {
            current = await _pageWaiter.WaitForAnyAsync(
                [GamePage.WorldMain],
                LongWait,
                cancellationToken,
                requiredConsecutiveMatches: 1);
        }

        workflowLog.Add("Clicked ArchiveEditorMenu exit button and returned to WorldMain.");
        return current;
    }

    private async Task ClickTargetAsync(
        StablePageResult current,
        string targetName,
        WorkflowFallbackCoordinate? fallback,
        CancellationToken cancellationToken,
        double minimumConfidence = 0.3)
    {
        if (current.Recognition.Page == GamePage.Unknown)
            throw new InvalidOperationException($"Cannot click {targetName} on Unknown page.");
        if (string.IsNullOrWhiteSpace(targetName))
            throw new ArgumentException("Target name is required.", nameof(targetName));

        var target = _targetLocator.FindTarget(current.Recognition, targetName, minimumConfidence);
        if (target != null)
        {
            await _input.ClickAsync(
                current.Capture.Window,
                Math.Clamp(target.CenterX, 0, 1),
                Math.Clamp(target.CenterY, 0, 1),
                cancellationToken);
            return;
        }

        if (fallback == null)
            throw new InvalidOperationException($"Page {current.Recognition.Page} is missing target {targetName}, and no fallback coordinate is configured.");

        await _input.ClickAsync(
            current.Capture.Window,
            fallback.Value.NormalizedX,
            fallback.Value.NormalizedY,
            cancellationToken);
    }

    private static async Task WaitAfterActionAsync(CancellationToken cancellationToken) =>
        await Task.Delay(650, cancellationToken);

    private static string FormatWorkflowLog(IReadOnlyCollection<string> workflowLog) =>
        workflowLog.Count == 0
            ? string.Empty
            : $"Workflow log: {string.Join(" | ", workflowLog)}";
}

