using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Automation.Recognition.Yolo;
using MiliastraArchiveTool.Infrastructure;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed partial class Stage1GameUploadExecutor : IUploadExecutor, IUploadServerLifecycleExecutor, IDisposable
{
    public event Action<UploadOperationStatus>? OperationChanged;
    private UploadServer _hudServer;
    private string? _hudArchive;
    private void ReportOperation(string operation)
    {
        _operation = operation;
        WorkflowPerformanceTrace.Event("Operation", "Changed", new { Server = _hudServer.ToString(), Archive = _hudArchive, Action = operation });
        try { OperationChanged?.Invoke(new UploadOperationStatus(_hudServer, _hudArchive, operation)); }
        catch { /* Display updates must not interrupt game automation. */ }
    }
    private static void LogWorkflow(List<string>? workflowLog, string message)
    {
        WorkflowPerformanceTrace.Event("Workflow", "Progress", message);
        workflowLog?.Add(message);
    }

    private async Task DelayWithTimingAsync(TimeSpan delay, CancellationToken cancellationToken,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        using var timing = WorkflowPerformanceTrace.Measure("Delay", new { Caller = caller, RequestedMs = delay.TotalMilliseconds });
        await _clock.DelayAsync(delay, cancellationToken);
    }

    private static string HudField(string name) => name switch
    {
        "LevelName" => "Stage Name", "GameplayDescription" => "Gameplay Details",
        "LevelDescription" => "Stage Description", "VersionLog" => "Update Log",
        "EarlyAccessDescription" => "Early-access Description", "ExtraImages" => "More Showcase Images",
        "Video" => "Showcase Video", "MainImage" => "Loading Screen Image", "AchievementIcon" => "Ultimate Achievement Icon",
        "LuxuryGiftIcon" => "Gorgeous Performance Box Icon", "ColorfulGiftIcon" => "Colorful Surprise Box Icon",
        "QuestGiftIcon" => "Wonderland Commendation Gift Box Icon",
        _ => name
    };

    private enum UploadLocationChoice
    {
        UploadNew,
        UpdateUploaded
    }

    private sealed record UpdateArchiveCardSelection(
        StablePageResult Page,
        AutomationTarget Card);

    private sealed record PreviewFieldTargetSelection(
        StablePageResult Page,
        AutomationTarget? Target,
        IReadOnlyList<WorkflowOcrTextResult> OcrTexts,
        int ScrollAttempts,
        string SearchDirection = "down");

    private sealed record UploadedArchivePromptScan(
        AutomationTarget? LaterTarget,
        bool HasForbiddenPrimaryAction,
        bool HasPromptEvidence,
        IReadOnlyList<WorkflowOcrTextResult> OcrTexts);

    private sealed record TagChoiceSelection(
        StablePageResult Page,
        AutomationTarget? Target,
        IReadOnlyList<WorkflowOcrTextResult> OcrTexts,
        int ScrollAttempts);

    private sealed record Stage1TagCatalogEntry(
        string ToolValue,
        string CanonicalGameText,
        IReadOnlyList<string> OcrAliases,
        int DialogOrder);

    private sealed record TagsEditRowOcrMatch(
        AutomationTarget? Target,
        IReadOnlyList<AutomationTarget> Matches,
        IReadOnlyList<WorkflowOcrTextResult> OcrTexts,
        bool IsAmbiguous);

    private enum Stage1ClientLanguage
    {
        En,
        ZhCn
    }

    private sealed record Stage1LanguageSpec(
        string Code,
        string DisplayName);

    private sealed record Stage1AddLanguageOption(
        Stage1LanguageSpec Language,
        string Column,
        int RowIndex,
        AutomationTarget OptionTarget);

    private sealed record Stage1LanguageSelectionDiagnostic(
        string Language,
        string Column,
        int RowIndex,
        Stage1UploadDiagnosticTarget OptionBox,
        NormalizedBox RadioRoi,
        int GreenPixelCount,
        double BackgroundBrightness,
        bool Selected,
        string Evidence);

    private sealed record Stage1MultilanguageTableGeometry(
        double FirstRowCenterY,
        double RowHeight,
        IReadOnlyDictionary<string, double> ColumnCenters,
        string Source);

    private sealed record Stage1MultilanguageTableIconDiagnostic(
        Stage1UploadDiagnosticTarget Target,
        bool Used,
        string Reason);

    private sealed record Stage1MultilanguageTableCluster(
        int Index,
        double Center,
        int Count,
        IReadOnlyList<double> Members);

    private sealed record Stage1MultilanguageTableGeometryInference(
        Stage1MultilanguageTableGeometry? Geometry,
        IReadOnlyList<Stage1MultilanguageTableIconDiagnostic> TextEditIcons,
        IReadOnlyList<Stage1MultilanguageTableCluster> RowClusters,
        IReadOnlyList<Stage1MultilanguageTableCluster> ColumnClusters,
        string Source,
        string Reason);

    private sealed record Stage1MultilanguageComputedRow(
        int RowIndex,
        string Language,
        double CenterY);

    private sealed record Stage1MultilanguageCellEditDiagnostic(
        int RowIndex,
        string Language,
        string Column,
        double ClickX,
        double ClickY);

    private sealed record Stage1MultilanguagePageContextEvidence(
        GamePage RawPage,
        GamePage ContextualPage,
        string OcrTitle,
        IReadOnlyList<string> TableHeaderTokens,
        string AddLanguageButtonOcr,
        bool Accepted,
        string Reason);

    private sealed record Stage1AddLanguageDialogContextEvidence(
        GamePage RawPage,
        GamePage ContextualPage,
        string OcrTitle,
        string ButtonOcr,
        int ChoiceOptionCount,
        int ModalDialogCount,
        bool Accepted,
        string Reason);

    private sealed record Stage1AddLanguageClickDiagnostic(
        Stage1ClientLanguage ClientLanguage,
        int AttemptCount,
        Stage1UploadDiagnosticTarget InitialAddButton,
        Stage1UploadDiagnosticTarget? LastAddButton,
        GamePage LastRawPage,
        GamePage LastContextualPage,
        string? ForegroundBeforeClick,
        string? ForegroundAfterClick,
        string FailureReason);

    private sealed record Stage1MultilanguageContext(
        Stage1ClientLanguage? ClientLanguage,
        IReadOnlyList<string> DesiredLanguages,
        string DefaultLanguage,
        IReadOnlyList<string> SelectedLanguagesBefore,
        IReadOnlyList<string> MissingLanguages,
        IReadOnlyList<string> SelectedLanguagesAfter,
        IReadOnlyList<string> SkippedDefaultLanguages,
        IReadOnlyList<string> FillTargetLanguages,
        IReadOnlyList<string> AddLanguageLayout,
        IReadOnlyList<Stage1LanguageSelectionDiagnostic> LanguageSelectionDiagnostics,
        IReadOnlyList<string> TableLanguageOrder,
        IReadOnlyList<Stage1MultilanguageComputedRow> ComputedVisibleRows,
        Stage1MultilanguageTableGeometry? TableGeometry,
        bool EarlyAccessColumnEnabled,
        IReadOnlyList<WorkflowOcrTextResult> DetectedTableHeaders,
        IReadOnlyList<Stage1MultilanguageTableIconDiagnostic> TableTextEditIcons,
        IReadOnlyList<Stage1MultilanguageTableCluster> TableRowClusters,
        IReadOnlyList<Stage1MultilanguageTableCluster> TableColumnClusters,
        IReadOnlyList<Stage1MultilanguageCellEditDiagnostic> CellEdits,
        IReadOnlyList<WorkflowOcrTextResult> OcrTexts,
        IReadOnlyList<Stage1MultilanguagePageContextEvidence> PageContextEvidence,
        IReadOnlyList<Stage1AddLanguageDialogContextEvidence> AddLanguageDialogContextEvidence);

    private sealed record InfoPanelRowIconSelection(
        AutomationTarget? Row,
        AutomationTarget? Icon,
        string SelectedBy,
        IReadOnlyList<WorkflowOcrTextResult> OcrTexts);

    private sealed record ImageAssetResourceSpec(
        string FieldName,
        string RelativeOrAbsolutePath,
        int TabOrder,
        IReadOnlyList<string> TabTokens);

    private sealed record Stage1ImageAssetEditorContextEvidence(
        GamePage RawPage,
        GamePage ContextualPage,
        string OcrTitle,
        string SideTabOcr,
        string ButtonOcr,
        int SideTabCount,
        int BottomPrimaryButtonCount,
        bool Accepted,
        string Reason);

    private sealed record Stage1ImageAssetDraftBoxContextEvidence(
        GamePage RawPage,
        GamePage ContextualPage,
        string OcrTitle,
        string ResourceHeaderOcr,
        string ButtonOcr,
        int ImageUploadAreaCount,
        int CenteredBottomPrimaryButtonCount,
        bool Accepted,
        string Reason)
    {
        public bool EmptyUnavailable { get; init; }
    }

    private sealed record PreviewTextInputOcrProfile(
        IReadOnlyList<string> AcceptedTokens,
        IReadOnlyList<string> RejectedTokens);

    private static readonly TimeSpan ShortWait = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MediumWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ImageAssetEditorReadyWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LongWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan FirstUploadSandboxTransitionWait = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ImportVisibleWait = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan LoginToWorldMainWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan LoginStartReadyWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LoginActionRetryInterval = TimeSpan.FromSeconds(4);
    private const int LoginStartMaxClickAttempts = 3;
    private const int TagDialogConfirmMaxClickAttempts = 3;
    private static readonly TimeSpan SandboxWait = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SandboxProcessStableDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SandboxReadyCooldown = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan SandboxWindowWait = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan UploadResultSettleDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan UploadedArchiveDismissSettleDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan UploadedArchivePromptPreClickSettleDelay = TimeSpan.FromMilliseconds(900);
    private const int ImageAssetReadyStableCount = 1;
    private static readonly TimeSpan UploadedArchivePromptPostClickSettleDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan ReturnEscShortWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReturnEditorMenuWait = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ReturnArchiveEditorEscSettleDelay = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan LocalArchiveMenuProbeWait = TimeSpan.FromSeconds(2);
    private const double ReturnUploadedArchiveStrongConfidence = 0.70;
    private const int UploadedArchiveStableAfterDismissCount = 2;
    private const int UploadedArchiveDismissMaxAttempts = 30;
    private static readonly TimeSpan UploadedArchivePromptDismissTimeout = TimeSpan.FromSeconds(60);
    private const int ReturnArchiveEditorEscMaxAttempts = 3;
    private static readonly NormalizedBox UpdateTypeOcrRegion = new(0.50, 0.47, 0.42, 0.18);
    private const int MainMenuMaxScrollAttempts = 5;
    private const int PreviewEditorMaxScrollAttempts = 4;
    private const int TagDialogMaxScrollAttempts = 30;
    private const int LocalArchiveMaxScrollAttempts = 3;
    private const int LocalArchiveInitialPollsBeforeScroll = 3;
    private const int ListSearchScrollWheelDelta = -4 * 120; // Four standard wheel notches per search segment.
    private const int MainMenuScrollWheelDelta = ListSearchScrollWheelDelta;
    private const int LocalArchiveScrollWheelDelta = ListSearchScrollWheelDelta;
    private const int PreviewEditorScrollWheelDelta = ListSearchScrollWheelDelta;
    private const int TagDialogScrollWheelDelta = ListSearchScrollWheelDelta;
    private const int MultilanguageTableScrollWheelDelta = -5;
    private const double MainMenuScrollX = 0.23;
    private const double MainMenuScrollY = 0.78;
    private const double ScrollCenterX = 0.5;
    private const double ScrollCenterY = 0.5;
    private const double MultilanguageTableScrollX = 0.90;
    private const double MultilanguageTableScrollY = 0.52;
    private const double TagDialogScrollX = 0.78;
    private const double TagDialogScrollY = 0.62;
    private const string MlStageNameColumn = "Stage Name";
    private const string MlEarlyAccessDescriptionColumn = "Early Access Version Description";
    private const string MlGameplayDetailsColumn = "Gameplay Details";
    private const string MlStageDescriptionColumn = "Stage Description";
    private const string MlUpdateLogColumn = "Update Log";

    private static readonly IReadOnlyDictionary<string, Stage1LanguageSpec> Stage1Languages =
        new Dictionary<string, Stage1LanguageSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new("en", "English"),
            ["zh-CN"] = new("zh-CN", "Simplified Chinese"),
            ["zh-TW"] = new("zh-TW", "Traditional Chinese"),
            ["ko"] = new("ko", "Korean"),
            ["ja"] = new("ja", "Japanese"),
            ["es"] = new("es", "Spanish"),
            ["fr"] = new("fr", "French"),
            ["ru"] = new("ru", "Russian"),
            ["th"] = new("th", "Thai"),
            ["vi"] = new("vi", "Vietnamese"),
            ["de"] = new("de", "German"),
            ["id"] = new("id", "Indonesian"),
            ["it"] = new("it", "Italian"),
            ["pt"] = new("pt", "Portuguese"),
            ["tr"] = new("tr", "Turkish")
        };

    private static readonly IReadOnlyList<string> MultilanguageTableOrderEn =
        ["en", "zh-CN", "zh-TW", "ko", "ja", "es", "fr", "ru", "th", "vi", "de", "id", "it", "pt", "tr"];

    private static readonly IReadOnlyList<string> MultilanguageTableOrderZhCn =
        ["zh-CN", "en", "zh-TW", "ko", "ja", "es", "fr", "ru", "th", "vi", "de", "id", "it", "pt", "tr"];

    private const int MultilanguageTableMaxVisibleRowsWithoutScroll = 9;

    private static readonly IReadOnlyList<string> AddLanguageLeftOrderEn =
        ["en", "zh-TW", "ja", "fr", "th", "de", "it", "tr"];

    private static readonly IReadOnlyList<string> AddLanguageRightOrderEn =
        ["zh-CN", "ko", "es", "ru", "vi", "id", "pt"];

    private static readonly IReadOnlyList<string> AddLanguageLeftOrderZhCn =
        ["zh-CN", "en", "ja", "fr", "th", "de", "it", "tr"];

    private static readonly IReadOnlyList<string> AddLanguageRightOrderZhCn =
        ["zh-TW", "ko", "es", "ru", "vi", "id", "pt"];
    private readonly ArchiveStorageService _storage;
    private readonly IGameSnapshotProvider _snapshotProvider;
    private readonly IPageRecognizer _recognizer;
    private readonly Stage1ContextPageGuard _pageGuard;
    private readonly Stage1ContextPageWaiter _pageWaiter;
    private readonly ITargetLocator _targetLocator;
    private readonly IGameInputAutomationService _input;
    private readonly IWindowsFileDialogAutomationService _fileDialog;
    private readonly IGameWindowActivationService _activation;
    private readonly IGameWindowFinder _windowFinder;
    private readonly ISandboxProcessWatcher _sandboxWatcher;
    private readonly IUploadRunArchiveService _runArchiveService;
    private readonly ILocalArchiveCardTextRecognizer _cardTextRecognizer;
    private readonly ILocalArchiveCardCandidateProvider _cardCandidateProvider;
    private readonly ILocalArchiveMoreButtonLocator _moreButtonLocator;
    private readonly IUpdateArchiveButtonLocator _updateArchiveButtonLocator;
    private readonly IUpdateStartUploadButtonLocator _updateStartUploadButtonLocator;
    private readonly IUploadedArchiveLaterButtonLocator _uploadedArchiveLaterButtonLocator;
    private readonly IInfoPanelIconLocator _infoPanelIconLocator;
    private readonly IWorkflowOcrService _workflowOcr;
    private readonly IStage1WorkflowClock _clock;
    private readonly Action<string>? _recordAction;
    private readonly bool _ownsRecognizer;
    private readonly bool _ownsCardTextRecognizer;
    private readonly bool _ownsWorkflowOcr;

    private Stage1RunDiagnostics? _activeDiagnostics;
    private bool _disposed;

    public Stage1GameUploadExecutor()
        : this(
            new ArchiveStorageService(),
            new GameSnapshotProvider(GameCaptureStrategy.LiveGameplay),
            new Stage1GatedPageRecognizer(),
            ownsRecognizer: true)
    {
    }

    public Stage1GameUploadExecutor(
        ArchiveStorageService storage,
        IGameSnapshotProvider snapshotProvider,
        IPageRecognizer recognizer,
        ITargetLocator? targetLocator = null,
        IGameInputAutomationService? input = null,
        IWindowsFileDialogAutomationService? fileDialog = null,
        IGameWindowActivationService? activation = null,
        IGameWindowFinder? windowFinder = null,
        ISandboxProcessWatcher? sandboxWatcher = null,
        IUploadRunArchiveService? runArchiveService = null,
        ILocalArchiveCardTextRecognizer? cardTextRecognizer = null,
        ILocalArchiveCardCandidateProvider? cardCandidateProvider = null,
        ILocalArchiveMoreButtonLocator? moreButtonLocator = null,
        IUpdateArchiveButtonLocator? updateArchiveButtonLocator = null,
        IUpdateStartUploadButtonLocator? updateStartUploadButtonLocator = null,
        IUploadedArchiveLaterButtonLocator? uploadedArchiveLaterButtonLocator = null,
        IInfoPanelIconLocator? infoPanelIconLocator = null,
        IWorkflowOcrService? workflowOcr = null,
        IStage1WorkflowClock? clock = null,
        bool ownsRecognizer = false,
        Action<string>? recordAction = null)
    {
        using var initializationTiming = WorkflowPerformanceTrace.Measure("ExecutorInitialization");
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _pageGuard = new Stage1ContextPageGuard();
        _pageWaiter = new Stage1ContextPageWaiter(_snapshotProvider, _recognizer, _pageGuard);
        _targetLocator = targetLocator ?? new ManualTargetLocator();
        _input = input ?? new GameInputAutomationService();
        _fileDialog = fileDialog ?? new WindowsFileDialogAutomationService();
        _activation = activation ?? new GameWindowActivationService();
        _windowFinder = windowFinder ?? new GameWindowCaptureService();
        _sandboxWatcher = sandboxWatcher ?? new SandboxProcessWatcher();
        _runArchiveService = runArchiveService ?? new UploadRunArchiveService(_storage);
        if (cardTextRecognizer == null)
        {
            _cardTextRecognizer = new RapidOcrLocalArchiveCardTextRecognizer();
            _ownsCardTextRecognizer = true;
        }
        else
        {
            _cardTextRecognizer = cardTextRecognizer;
            _ownsCardTextRecognizer = false;
        }

        _cardCandidateProvider = cardCandidateProvider ?? new LocalArchiveCardCandidateProvider();
        _moreButtonLocator = moreButtonLocator ?? new OpenCvLocalArchiveMoreButtonLocator();
        _updateArchiveButtonLocator = updateArchiveButtonLocator ?? new OpenCvUpdateArchiveButtonLocator();
        _updateStartUploadButtonLocator = updateStartUploadButtonLocator ?? new OpenCvUpdateStartUploadButtonLocator();
        _uploadedArchiveLaterButtonLocator = uploadedArchiveLaterButtonLocator ?? new OpenCvUploadedArchiveLaterButtonLocator();
        _infoPanelIconLocator = infoPanelIconLocator ?? new OpenCvInfoPanelIconLocator();
        if (workflowOcr == null)
        {
            _workflowOcr = new RapidOcrWorkflowOcrService();
            _ownsWorkflowOcr = true;
        }
        else
        {
            _workflowOcr = workflowOcr;
            _ownsWorkflowOcr = false;
        }

        _clock = new ContinuationClock(clock ?? new SystemStage1WorkflowClock(), () => Continuation);
        _ownsRecognizer = ownsRecognizer;
        _recordAction = recordAction;
    }

    public async Task<UploadExecutionResult> ExecuteAsync(
        UploadRequest request,
        IProgress<UploadTaskState> progress,
        CancellationToken cancellationToken)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("Execute");
        _hudServer = request.Server; _hudArchive = request.Archive.LevelName; ReportOperation("Locating the game window");
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);

        var diagnostics = Stage1RunDiagnostics.Create();
        _activeDiagnostics = diagnostics;
        DiagnosticLogRetention.TryClean();
        if (_workflowOcr is RapidOcrWorkflowOcrService rapidOcr) rapidOcr.DiagnosticRunId = diagnostics.RunId;
        var workflowLog = new List<string>();
        diagnostics.WorkflowLog = workflowLog;
        UploadRunArchive? runArchive = null;

        try
        {
            LogWorkflow(workflowLog, 
                $"RunId={diagnostics.RunId} AppContext.BaseDirectory={diagnostics.AppBaseDirectory} " +
                $"DiagnosticsDirectory={diagnostics.DiagnosticsDirectory} ProcessId={diagnostics.ProcessId} IsElevated={diagnostics.IsElevated}");

            progress.Report(UploadTaskState.WaitingForGame);
            LogWorkflow(workflowLog, $"step=WaitingForGame server={request.Server}");
            ActivateGameWindow();

            progress.Report(UploadTaskState.Recognizing);
            var startPage = await RecognizeStage1StartAsync(request, workflowLog, cancellationToken);

            progress.Report(UploadTaskState.Navigating);
            var localArchive = await EnsureLocalArchiveFromSupportedStartAsync(startPage, request, workflowLog, cancellationToken);
            LogWorkflow(workflowLog, 
                $"step=LocalArchiveReady page={localArchive.Recognition.Page} confidence={localArchive.Recognition.Confidence:F2} " +
                $"targets=[{FormatTargets(localArchive.Recognition.Targets)}]");

            progress.Report(UploadTaskState.Filling);
            var createRunArchiveStopwatch = Stopwatch.StartNew();
            LogWorkflow(workflowLog, 
                $"step=CreateRunArchiveStart server={request.Server} archiveTitle=\"{request.Archive.LevelName}\" " +
                $"managedPath=\"{_storage.GetManagedArchivePath(request.Archive)}\"");
            runArchive = await _runArchiveService.CreateAsync(request.Archive, request.Server, cancellationToken);
            createRunArchiveStopwatch.Stop();
            LogWorkflow(workflowLog, 
                $"step=CreateRunArchiveEnd server={request.Server} runArchive={runArchive.RunArchiveDisplayName} " +
                $"code={runArchive.RunArchiveCode} elapsedMs={createRunArchiveStopwatch.ElapsedMilliseconds} " +
                $"sourceBytes={GetFileLengthOrMinusOne(runArchive.OriginalArchivePath)} " +
                $"runBytes={GetFileLengthOrMinusOne(runArchive.RunArchivePath)} path=\"{runArchive.RunArchivePath}\"");
            localArchive = await ImportRunArchiveAsync(runArchive, localArchive, workflowLog, cancellationToken);

            progress.Report(UploadTaskState.Submitting);
            var uploadedArchive = request.IsUpdate
                ? await UpdateExistingArchiveStage1Async(request, runArchive, localArchive, workflowLog, cancellationToken)
                : await UploadNewArchiveStage1Async(runArchive, localArchive, workflowLog, cancellationToken);

            progress.Report(UploadTaskState.Verifying);
            uploadedArchive = await DismissUploadedArchivePromptIfPresentAsync(uploadedArchive, workflowLog, cancellationToken);
            uploadedArchive = await EditPreviewInfoAsync(request, uploadedArchive, workflowLog, cancellationToken);
            uploadedArchive = await EditImageAssetsIfNeededAsync(request, uploadedArchive, workflowLog, cancellationToken);
            var final = await ReturnToWorldMainConservativelyAsync(uploadedArchive, workflowLog, cancellationToken);

            var mode = request.IsUpdate ? "update existing archive" : "new archive upload";
            return UploadExecutionResult.Success(
                $"Stage1 {mode} finished without publishing. server={request.Server}, runArchive={runArchive.RunArchiveDisplayName}, finalPage={final.Recognition.Page}. {FormatWorkflowLog(workflowLog)}");
        }
        catch (OperationCanceledException)
        {
            WorkflowPerformanceTrace.Event("Cancellation", "Cancelled", new { Server = _hudServer.ToString(), Archive = _hudArchive, RunId = _activeDiagnostics?.RunId });
            LogWorkflow(workflowLog, "step=Execute result=Cancelled action=SaveCancellationDiagnostic");
            var cancellationDiagnostic = await TryRecognizeForDiagnosticsAsync(CancellationToken.None);
            SaveDiagnostic(
                cancellationDiagnostic,
                "stage1-execute-cancelled",
                isFinalFailure: true,
                recoverable: false,
                phase: "ExecuteCancelled");
            throw;
        }
        catch (Exception ex)
        {
            var failureDiagnostic = await TryRecognizeForDiagnosticsAsync(CancellationToken.None);
            SaveDiagnostic(failureDiagnostic, "stage1-execute-failure", isFinalFailure: true, recoverable: false, phase: "Execute");
            return UploadExecutionResult.Failure(
                FormatStage1FailureMessage(diagnostics, request.Server, runArchive, $"{ex.GetType().Name}: {ex.Message}", workflowLog));
        }
        finally
        {
            _activeDiagnostics = null;
            if (_workflowOcr is RapidOcrWorkflowOcrService completedOcr) completedOcr.DiagnosticRunId = null;
            DiagnosticLogRetention.TryClean();
        }
    }

    public async Task BeginServerAsync(
        UploadServer server,
        bool isFirstServer,
        Action<UploadLogLevel, string> log,
        CancellationToken cancellationToken)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("BeginServer");
        _hudServer = server; _hudArchive = null; ReportOperation("Preparing server and entering the game");
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (server == UploadServer.None)
        {
            log(UploadLogLevel.Information, "Stage1 server setup skipped: current logged server mode.");
            return;
        }

        var diagnostics = Stage1RunDiagnostics.Create();
        _activeDiagnostics = diagnostics;
        DiagnosticLogRetention.TryClean();
        if (_workflowOcr is RapidOcrWorkflowOcrService rapidOcr) rapidOcr.DiagnosticRunId = diagnostics.RunId;
        var workflowLog = new List<string>
        {
            $"RunId={diagnostics.RunId} AppContext.BaseDirectory={diagnostics.AppBaseDirectory} " +
            $"DiagnosticsDirectory={diagnostics.DiagnosticsDirectory} ProcessId={diagnostics.ProcessId} IsElevated={diagnostics.IsElevated}",
            $"step=BeginServer server={server} isFirstServer={isFirstServer}"
        };
        diagnostics.WorkflowLog = workflowLog;

        try
        {
            ActivateGameWindow();
            var start = await RecognizeOnceAsync(cancellationToken);
            LogWorkflow(workflowLog, $"step=BeginServer recognized={start.Recognition.Page} confidence={start.Recognition.Confidence:F2}");

            var world = await EnsureWorldMainForExplicitServerAsync(server, start, workflowLog, cancellationToken);
            log(
                UploadLogLevel.Information,
                $"Stage1 selected server {server} and reached {world.Recognition.Page}. Workflow log: {string.Join(" | ", workflowLog)}");
        }
        catch (OperationCanceledException)
        {
            WorkflowPerformanceTrace.Event("Cancellation", "Cancelled", new { Server = _hudServer.ToString(), Archive = _hudArchive, RunId = _activeDiagnostics?.RunId });
            throw;
        }
        catch (Exception ex)
        {
            var failureDiagnostic = await TryRecognizeForDiagnosticsAsync(CancellationToken.None);
            SaveDiagnostic(failureDiagnostic, "stage1-begin-server-failure");
            throw new InvalidOperationException(
                FormatStage1FailureMessage(
                    diagnostics,
                    server,
                    null,
                    $"{ex.GetType().Name}: {ex.Message}",
                    workflowLog),
                ex);
        }
        finally
        {
            _activeDiagnostics = null;
            if (_workflowOcr is RapidOcrWorkflowOcrService completedOcr) completedOcr.DiagnosticRunId = null;
            DiagnosticLogRetention.TryClean();
        }
    }

    public async Task EndServerAsync(
        UploadServer server,
        bool hasMoreServers,
        Action<UploadLogLevel, string> log,
        CancellationToken cancellationToken)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("EndServer");
        _hudServer = server; _hudArchive = null; ReportOperation(hasMoreServers ? "Logging out to switch servers" : "Finishing server tasks");
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (server == UploadServer.None || !hasMoreServers)
        {
            log(UploadLogLevel.Information, $"Stage1 server teardown skipped. server={server}, hasMoreServers={hasMoreServers}.");
            return;
        }

        var diagnostics = Stage1RunDiagnostics.Create();
        _activeDiagnostics = diagnostics;
        DiagnosticLogRetention.TryClean();
        if (_workflowOcr is RapidOcrWorkflowOcrService rapidOcr) rapidOcr.DiagnosticRunId = diagnostics.RunId;
        var workflowLog = new List<string>
        {
            $"RunId={diagnostics.RunId} AppContext.BaseDirectory={diagnostics.AppBaseDirectory} " +
            $"DiagnosticsDirectory={diagnostics.DiagnosticsDirectory} ProcessId={diagnostics.ProcessId} IsElevated={diagnostics.IsElevated}",
            $"step=EndServer server={server} hasMoreServers={hasMoreServers}"
        };
        diagnostics.WorkflowLog = workflowLog;

        try
        {
            ActivateGameWindow();
            var current = await RecognizeOnceAsync(cancellationToken);
            LogWorkflow(workflowLog, $"step=EndServer recognized={current.Recognition.Page} confidence={current.Recognition.Confidence:F2}");
            current = await WaitForPageEvidenceAsync(current,
                Pages(GamePage.WorldMain, GamePage.MainMenu, GamePage.LocalArchive, GamePage.LoginStart, GamePage.LoginServerSelect),
                "EndServer.Start", LongWait, cancellationToken);
            await ReturnFromWorldMainToLoginAsync(current, workflowLog, cancellationToken);
            log(
                UploadLogLevel.Information,
                $"Stage1 returned to login after server {server}. Workflow log: {string.Join(" | ", workflowLog)}");
        }
        catch (OperationCanceledException)
        {
            WorkflowPerformanceTrace.Event("Cancellation", "Cancelled", new { Server = _hudServer.ToString(), Archive = _hudArchive, RunId = _activeDiagnostics?.RunId });
            throw;
        }
        catch (Exception ex)
        {
            var failureDiagnostic = await TryRecognizeForDiagnosticsAsync(CancellationToken.None);
            SaveDiagnostic(failureDiagnostic, "stage1-end-server-failure");
            throw new InvalidOperationException(
                FormatStage1FailureMessage(
                    diagnostics,
                    server,
                    null,
                    $"{ex.GetType().Name}: {ex.Message}",
                    workflowLog),
                ex);
        }
        finally
        {
            _activeDiagnostics = null;
            if (_workflowOcr is RapidOcrWorkflowOcrService completedOcr) completedOcr.DiagnosticRunId = null;
            DiagnosticLogRetention.TryClean();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _pageWaiter.Dispose();
        if (_ownsRecognizer && _recognizer is IDisposable disposable)
            disposable.Dispose();
        if (_ownsCardTextRecognizer && _cardTextRecognizer is IDisposable cardTextRecognizer)
            cardTextRecognizer.Dispose();
        if (_ownsWorkflowOcr && _workflowOcr is IDisposable workflowOcr)
            workflowOcr.Dispose();
        _disposed = true;
    }

    private void ActivateGameWindow()
    {
        var window = _windowFinder.FindGameWindow();
        _activation.RestoreAndActivate(window);
        _recordAction?.Invoke($"foreground_after_activate:{_activation.GetForegroundWindowInfo(window).ToDiagnosticString()}");
        _recordAction?.Invoke("activate_game_window");
    }

    private async Task<StablePageResult> RecognizeStage1StartAsync(
        UploadRequest request,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Identifying the current game page");
        var current = await RecognizeOnceAsync(cancellationToken);
        var supportedStartPages = Pages(
            GamePage.WorldMain,
            GamePage.MainMenu,
            GamePage.LocalArchive,
            GamePage.LoginStart,
            GamePage.LoginEnter,
            GamePage.LoginDownloadingResources,
            GamePage.Transition,
            GamePage.LoginServerConfirm,
            GamePage.LoginServerSelect);
        var decision = _pageGuard.Decide(current.Recognition, supportedStartPages, "StartPage");

        current = await WaitForPageEvidenceAsync(current, supportedStartPages, "StartPage", LongWait, cancellationToken);
        decision = _pageGuard.Decide(current.Recognition, supportedStartPages, "StartPage");

        LogWorkflow(workflowLog, $"step=StartPage server={request.Server} recognized={decision.RecognizedPage} accepted={decision.IsAccepted} reason={decision.Reason}");
        if (!decision.IsAccepted || !decision.IsExpected)
            throw new InvalidOperationException($"Stage1 must start from a supported page. {decision.Reason}");

        return current;
    }

    private async Task<StablePageResult> EnsureLocalArchiveFromSupportedStartAsync(
        StablePageResult startPage,
        UploadRequest request,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (startPage.Recognition.Page == GamePage.LocalArchive)
        {
            LogWorkflow(workflowLog, "step=EnsureLocalArchiveFromSupportedStart startPage=LocalArchive result=alreadyLocalArchive");
            return await WaitForLocalArchiveAgreementAsync(startPage, workflowLog, cancellationToken);
        }

        if (startPage.Recognition.Page == GamePage.MainMenu)
        {
            LogWorkflow(workflowLog, "step=EnsureLocalArchiveFromSupportedStart startPage=MainMenu action=EnterMiliastraLocalArchive");
            return await EnterMiliastraLocalArchiveFromMainMenuAsync(startPage, workflowLog, cancellationToken);
        }

        var worldMain = await EnsureWorldMainFromSupportedStartAsync(startPage, request, workflowLog, cancellationToken);
        return await OpenLocalArchiveFromWorldAsync(worldMain, workflowLog, cancellationToken);
    }

    private async Task<StablePageResult> EnsureWorldMainFromSupportedStartAsync(
        StablePageResult startPage,
        UploadRequest request,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (startPage.Recognition.Page == GamePage.WorldMain)
        {
            LogWorkflow(workflowLog, "step=EnsureWorldMainFromSupportedStart startPage=WorldMain result=alreadyInWorld");
            return startPage;
        }

        if (startPage.Recognition.Page == GamePage.LoginServerSelect)
            throw new InvalidOperationException(
                $"Stage1 recognized LoginServerSelect, but current task does not support choosing a server yet. " +
                $"Server={request.Server}; refusing to click server options blindly.");

        if (startPage.Recognition.Page == GamePage.LoginStart)
            return await EnterGameFromLoginStartAsync(startPage, workflowLog, cancellationToken);

        if (startPage.Recognition.Page == GamePage.LoginEnter ||
            startPage.Recognition.Page == GamePage.LoginDownloadingResources ||
            startPage.Recognition.Page == GamePage.Transition ||
            startPage.Recognition.Page == GamePage.LoginServerConfirm)
        {
            LogWorkflow(workflowLog, $"step=EnsureWorldMainFromSupportedStart startPage={startPage.Recognition.Page} action=WaitForWorldMainThroughLoginLoading");
            return await WaitForWorldMainThroughLoginLoadingAsync(startPage, workflowLog, cancellationToken);
        }

        throw new InvalidOperationException($"Stage1 does not support starting from page {startPage.Recognition.Page}.");
    }

    private async Task<StablePageResult> EnsureWorldMainForExplicitServerAsync(
        UploadServer server,
        StablePageResult startPage,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        startPage = await WaitForExplicitServerStartReadyAsync(startPage, workflowLog, cancellationToken);
        if (startPage.Recognition.Page is GamePage.WorldMain or GamePage.MainMenu or GamePage.LocalArchive)
        {
            LogWorkflow(workflowLog, 
                $"step=ExplicitServer action=NormalizeToLoginFromGamePage startPage={startPage.Recognition.Page} targetServer={server}");
            startPage = await ReturnFromWorldMainToLoginAsync(startPage, workflowLog, cancellationToken);
        }

        StablePageResult serverSelect;
        if (startPage.Recognition.Page == GamePage.LoginStart)
        {
            startPage = await WaitForLoginStartServerBadgeAsync(startPage, workflowLog, cancellationToken);
            LogWorkflow(workflowLog, $"step=ExplicitServer action=ClickServerBadge target={WorkflowTargetNames.LoginServerBadge}");
            await ClickRequiredTargetAsync(startPage, WorkflowTargetNames.LoginServerBadge, cancellationToken);
            serverSelect = await WaitForExpectedAsync(
                "ExplicitServer.WaitServerSelect",
                Pages(GamePage.LoginServerSelect),
                MediumWait,
                stableCount: 1,
                cancellationToken);
        }
        else if (startPage.Recognition.Page == GamePage.LoginServerSelect)
        {
            serverSelect = startPage;
        }
        else
        {
            throw new InvalidOperationException(
                $"Stage1 explicit server mode must start from LoginStart or LoginServerSelect. " +
                $"CurrentPage={startPage.Recognition.Page}, TargetServer={server}.");
        }

        await ClickLoginServerOptionAsync(serverSelect, server, workflowLog, cancellationToken);
        var selectionResult = await WaitForExpectedAsync(
            "ExplicitServer.WaitAfterServerOption",
            Pages(
                GamePage.LoginServerConfirm,
                GamePage.LoginStart,
                GamePage.LoginEnter,
                GamePage.LoginDownloadingResources,
                GamePage.Transition,
                GamePage.WorldMain),
            MediumWait,
            stableCount: 1,
            cancellationToken);

        LogWorkflow(workflowLog, 
            $"step=ExplicitServer action=AfterServerOption page={selectionResult.Recognition.Page} " +
            $"confidence={selectionResult.Recognition.Confidence:F2}");

        if (selectionResult.Recognition.Page == GamePage.LoginServerConfirm)
        {
            LogWorkflow(workflowLog, $"step=ExplicitServer action=ConfirmServer target={WorkflowTargetNames.LoginServerConfirmButton}");
            await ClickRequiredTargetAsync(selectionResult, WorkflowTargetNames.LoginServerConfirmButton, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);
            return await WaitForWorldMainThroughLoginLoadingAsync(null, workflowLog, cancellationToken);
        }

        if (selectionResult.Recognition.Page == GamePage.LoginStart)
        {
            LogWorkflow(workflowLog, 
                "step=ExplicitServer result=ReturnedToLoginStartWithoutConfirm " +
                "reason=selected server may already be current login server");
            return await EnterGameFromLoginStartAsync(selectionResult, workflowLog, cancellationToken);
        }

        if (selectionResult.Recognition.Page == GamePage.WorldMain)
            return selectionResult;

        return await WaitForWorldMainThroughLoginLoadingAsync(selectionResult, workflowLog, cancellationToken);
    }

    private async Task<StablePageResult> WaitForExplicitServerStartReadyAsyncCore(
        StablePageResult current, List<string> workflowLog, CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + LongWait;
        GamePage? previous = null;
        var stable = 0;
        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = current.Recognition.Page;
            if (page.IsBlocking())
                throw new InvalidOperationException($"Blocking page before server selection: {page}.");
            var supported = page is GamePage.LoginStart or GamePage.LoginServerSelect or
                GamePage.WorldMain or GamePage.MainMenu or GamePage.LocalArchive;
            stable = supported ? (page == previous ? stable + 1 : 1) : 0;
            previous = page;
            LogWorkflow(workflowLog, $"step=ExplicitServer action=WaitStartReady page={page} " +
                $"confidence={current.Recognition.Confidence:F2} stableCount={stable} requiredStableCount=2");
            if (stable >= 2) return current;
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            current = await RecognizeOnceAsync(cancellationToken);
        }
        var diagnostic = SaveDiagnostic(current, "stage1-explicit-server-start-timeout");
        throw new TimeoutException($"Server selection start page did not become ready within {LongWait.TotalSeconds:0}s; " +
            $"lastPage={current.Recognition.Page}, diagnostic={diagnostic}.");
    }

    private async Task<StablePageResult> WaitForPageEvidenceAsyncCore(StablePageResult current,
        IReadOnlySet<GamePage> expected, string step, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + timeout;
        var ready = new PageObservationStreak();
        var blocked = new PageObservationStreak();
        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = _pageGuard.Decide(current.Recognition, expected, step);
            LogWorkflow(_activeDiagnostics?.WorkflowLog as List<string>, 
                $"step={step} action=WaitPageEvidence page={current.Recognition.Page} accepted={decision.IsAccepted} expected={decision.IsExpected}");
            if (blocked.Observe(current.Recognition.Page, decision.IsBlocking))
                throw new InvalidOperationException($"Confirmed blocking page during {step}: {current.Recognition.Page}.");
            if (ready.Observe(current.Recognition.Page, decision.IsAccepted && decision.IsExpected && !decision.IsBlocking))
                return current;
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            current = await RecognizeOnceAsync(cancellationToken);
        }
        var diagnostic = SaveDiagnostic(current, $"stage1-{NormalizeFileNameSegment(step)}-page-evidence-timeout");
        throw new TimeoutException($"Page did not stabilize during {step}; lastPage={current.Recognition.Page}, diagnostic={diagnostic}.");
    }

    private async Task ClickLoginServerOptionAsync(
        StablePageResult serverSelect,
        UploadServer server,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Selecting the target server");
        if (serverSelect.Recognition.Page != GamePage.LoginServerSelect)
            throw new InvalidOperationException($"Stage1 refuses to choose server option on page {serverSelect.Recognition.Page}.");

        var optionIndex = GetServerOptionIndex(server);
        var options = FindTargets(serverSelect.Recognition, WorkflowTargetNames.LoginServerOption)
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .ToList();

        LogWorkflow(workflowLog, 
            $"step=ExplicitServer server={server} action=SelectServerOption optionIndex={optionIndex} detectedOptions={options.Count}");
        if (options.Count <= optionIndex)
        {
            SaveDiagnostic(serverSelect, "stage1-explicit-server-options-missing");
            throw new InvalidOperationException(
                $"Stage1 detected only {options.Count} server options, cannot select {server} at index {optionIndex}. " +
                "Refusing to use blind server coordinates.");
        }

        var current = serverSelect;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var currentOptions = FindTargets(current.Recognition, WorkflowTargetNames.LoginServerOption)
                .OrderBy(target => target.CenterY)
                .ThenBy(target => target.CenterX)
                .ToList();
            if (currentOptions.Count <= optionIndex)
            {
                SaveDiagnostic(current, "stage1-explicit-server-options-missing");
                throw new InvalidOperationException(
                    $"Stage1 detected only {currentOptions.Count} server options on click attempt {attempt}, " +
                    $"cannot select {server} at index {optionIndex}. Refusing to use blind server coordinates.");
            }

            await ClickLoginServerOptionCenterAsync(current, server, optionIndex, currentOptions, workflowLog, attempt, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);

            current = await RecognizeOnceAsync(cancellationToken);
            LogWorkflow(workflowLog, 
                $"step=ExplicitServer server={server} action=AfterServerOptionClick attempt={attempt} " +
                $"page={current.Recognition.Page} confidence={current.Recognition.Confidence:F2}");
            if (current.Recognition.Page != GamePage.LoginServerSelect)
                return;
        }

        SaveDiagnostic(current, "stage1-explicit-server-option-click-not-applied");
        throw new InvalidOperationException(
            $"Stage1 clicked server option center 3 times but page stayed LoginServerSelect. " +
            $"Server={server}, optionIndex={optionIndex}.");
    }

    private async Task ClickLoginServerOptionCenterAsync(
        StablePageResult serverSelect,
        UploadServer server,
        int optionIndex,
        IReadOnlyList<AutomationTarget> options,
        List<string> workflowLog,
        int attempt,
        CancellationToken cancellationToken)
    {
        LogWorkflow(workflowLog, 
            $"step=ExplicitServer server={server} action=ClickServerOptionCenter attempt={attempt} " +
            $"selected=({FormatTarget(options[optionIndex])}) optionOrder={string.Join(",", options.Select(FormatTarget))}");

        var selected = options[optionIndex];
        LogWorkflow(workflowLog, 
            $"step=ExplicitServer server={server} action=ClickServerOptionCenterPoint attempt={attempt} " +
            $"row=({FormatTarget(selected)}) click=({selected.CenterX:F3},{selected.CenterY:F3})");
        _recordAction?.Invoke($"click:server_option_center:{server}:{attempt}");
        await _input.ClickAsync(serverSelect.Capture.Window, selected.CenterX, selected.CenterY, cancellationToken);
    }

    private static int GetServerOptionIndex(UploadServer server) => server switch
    {
        UploadServer.Us => 0,
        UploadServer.Europe => 1,
        UploadServer.Asia => 2,
        UploadServer.HkTw => 3,
        _ => throw new InvalidOperationException($"Stage1 explicit server mode does not support server {server}.")
    };

    private async Task<StablePageResult> WaitForLoginStartServerBadgeAsyncCore(
        StablePageResult initial,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + LoginStartReadyWait;
        var current = initial;
        var useInitial = true;

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = useInitial ? initial : await RecognizeOnceAsync(cancellationToken);
            useInitial = false;

            var badge = _targetLocator.FindTarget(current.Recognition, WorkflowTargetNames.LoginServerBadge, minimumConfidence: 0.3);
            LogWorkflow(workflowLog, 
                $"step=ExplicitServer.WaitLoginStartServerBadge page={current.Recognition.Page} " +
                $"confidence={current.Recognition.Confidence:F2} badge={FormatOptionalTarget(badge)}");

            if (current.Recognition.Page == GamePage.LoginStart && badge != null)
                return current;

            if (current.Recognition.Page == GamePage.LoginServerSelect)
                return current;

            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        var diagnostic = SaveDiagnostic(current, "stage1-login-start-server-badge-timeout");
        throw new TimeoutException(
            $"Stage1 timed out waiting for LoginStart server badge before server selection. " +
            $"timeoutSeconds={LoginStartReadyWait.TotalSeconds:F0}, lastPage={current.Recognition.Page}, " +
            $"targets=[{FormatTargets(current.Recognition.Targets)}], diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<StablePageResult> EnterGameFromLoginStartAsync(
        StablePageResult loginStart,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        LogWorkflow(workflowLog, $"step=EnterGameFromLoginStart action=ClickStart attempt=1 target={WorkflowTargetNames.LoginStartGameButton}");
        await ClickLoginStartGameAsync(loginStart, workflowLog, "EnterGameFromLoginStart", attempt: 1, cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);
        return await WaitForWorldMainThroughLoginLoadingAsync(
            null,
            workflowLog,
            cancellationToken,
            initialStartClickAttempts: 1,
            initialLastStartClickAt: _clock.UtcNow);
    }

    private async Task<StablePageResult> WaitForWorldMainThroughLoginLoadingAsync(
        StablePageResult? initial,
        List<string> workflowLog,
        CancellationToken cancellationToken,
        int initialStartClickAttempts = 0,
        DateTimeOffset? initialLastStartClickAt = null)
    {
        ReportOperation("Waiting for login and the open world");
        var deadline = _clock.UtcNow + LoginToWorldMainWait;
        var startedAt = _clock.UtcNow;
        StablePageResult? lastResult = initial;
        DateTimeOffset? lastEnterClickAt = null;
        DateTimeOffset? lastConfirmClickAt = null;
        DateTimeOffset? lastStartClickAt = initialLastStartClickAt;
        var startClickAttempts = initialStartClickAttempts;
        var useInitial = initial != null;

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = useInitial ? initial! : await RecognizeOnceAsync(cancellationToken);
            useInitial = false;
            lastResult = current;
            var elapsed = _clock.UtcNow - startedAt;
            LogWorkflow(workflowLog, 
                $"step=WaitForWorldMainThroughLoginLoading elapsedSeconds={elapsed.TotalSeconds:F1} " +
                $"page={current.Recognition.Page} confidence={current.Recognition.Confidence:F2}");

            switch (current.Recognition.Page)
            {
                case GamePage.WorldMain:
                    LogWorkflow(workflowLog, $"step=WaitForWorldMainThroughLoginLoading result=WorldMain elapsedSeconds={elapsed.TotalSeconds:F1}");
                    return current;

                case GamePage.LoginStart:
                    if (FindLoginStartGameButton(current.Recognition) == null)
                    {
                        LogWorkflow(workflowLog, 
                            "step=WaitForWorldMainThroughLoginLoading state=LoginStartWithoutStartButton " +
                            $"action=Wait targets=[{FormatTargets(current.Recognition.Targets)}]");
                        break;
                    }

                    if (ShouldRetryLoginAction(lastStartClickAt))
                    {
                        if (startClickAttempts >= LoginStartMaxClickAttempts)
                        {
                            var maxAttemptDiagnostic = SaveDiagnostic(current, "stage1-login-start-click-max-attempts");
                            throw new InvalidOperationException(
                                $"Stage1 stayed on LoginStart after {LoginStartMaxClickAttempts} start click attempts. " +
                                $"lastConfidence={current.Recognition.Confidence:F2}, diagnostic={maxAttemptDiagnostic ?? "<not-saved>"}.");
                        }

                        startClickAttempts++;
                        LogWorkflow(workflowLog, 
                            $"step=WaitForWorldMainThroughLoginLoading action=ClickStart attempt={startClickAttempts} " +
                            $"target={WorkflowTargetNames.LoginStartGameButton}");
                        await ClickLoginStartGameAsync(
                            current,
                            workflowLog,
                            "WaitForWorldMainThroughLoginLoading",
                            startClickAttempts,
                            cancellationToken);
                        lastStartClickAt = _clock.UtcNow;
                    }

                    break;

                case GamePage.LoginEnter:
                    if (ShouldRetryLoginAction(lastEnterClickAt))
                    {
                        LogWorkflow(workflowLog, $"step=WaitForWorldMainThroughLoginLoading action=ClickEnter target={WorkflowTargetNames.LoginClickEnter}");
                        await ClickRequiredTargetAsync(current, WorkflowTargetNames.LoginClickEnter, cancellationToken);
                        lastEnterClickAt = _clock.UtcNow;
                    }

                    break;

                case GamePage.LoginServerConfirm:
                    if (ShouldRetryLoginAction(lastConfirmClickAt))
                    {
                        LogWorkflow(workflowLog, $"step=WaitForWorldMainThroughLoginLoading action=ConfirmServer target={WorkflowTargetNames.LoginServerConfirmButton}");
                        await ClickRequiredTargetAsync(current, WorkflowTargetNames.LoginServerConfirmButton, cancellationToken);
                        lastConfirmClickAt = _clock.UtcNow;
                    }

                    break;

                case GamePage.LoginServerSelect:
                    // Never choose a server here; a transient misclassification can recover.
                    LogWorkflow(workflowLog, "step=WaitForWorldMainThroughLoginLoading action=WaitUnexpectedServerSelect");
                    break;

                case GamePage.LoginDownloadingResources:
                case GamePage.Transition:
                case GamePage.Unknown:
                    break;

                default:
                    if (current.Recognition.Page.IsBlocking())
                        throw new InvalidOperationException(
                            $"Stage1 detected blocking page while waiting for WorldMain through login: {current.Recognition.Page}.");

                    LogWorkflow(workflowLog, $"step=WaitForWorldMainThroughLoginLoading action=WaitUnexpectedPage page={current.Recognition.Page}");
                    break;
            }

            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        var diagnostic = SaveDiagnostic(lastResult, "stage1-login-to-world-timeout");
        throw new TimeoutException(
            $"Stage1 timed out waiting for WorldMain through login. timeoutSeconds={LoginToWorldMainWait.TotalSeconds:F0}, " +
            $"lastPage={lastResult?.Recognition.Page.ToString() ?? "<none>"}, " +
            $"lastConfidence={lastResult?.Recognition.Confidence.ToString("F2") ?? "<none>"}, " +
            $"diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private bool ShouldRetryLoginAction(DateTimeOffset? lastClickAt) =>
        lastClickAt == null || _clock.UtcNow - lastClickAt.Value >= LoginActionRetryInterval;

    private async Task ClickLoginStartGameAsync(
        StablePageResult loginStart,
        List<string> workflowLog,
        string stepName,
        int attempt,
        CancellationToken cancellationToken)
    {
        if (loginStart.Recognition.Page != GamePage.LoginStart)
            throw new InvalidOperationException($"Stage1 refuses to click LoginStart game area from page {loginStart.Recognition.Page}.");

        var target = FindLoginStartGameButton(loginStart.Recognition);
        if (target == null)
            throw new InvalidOperationException(
                $"Stage1 detected LoginStart but could not locate a valid YOLO target {WorkflowTargetNames.LoginStartGameButton}. " +
                $"attempt={attempt}, targets=[{FormatTargets(loginStart.Recognition.Targets)}]");

        LogWorkflow(workflowLog, 
            $"step={stepName} action=ClickStartYolo attempt={attempt} " +
            $"target=({FormatTarget(target)})");
        await ClickSpecificTargetAsync(loginStart, target, cancellationToken);
    }

    private async Task<StablePageResult> OpenLocalArchiveFromWorldAsync(
        StablePageResult worldMain,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var beforeForeground = _activation.GetForegroundWindowInfo(worldMain.Capture.Window).ToDiagnosticString();
        LogWorkflow(workflowLog, $"step=OpenMainMenu action=PressEsc expected=MainMenu foregroundBefore=({beforeForeground})");
        _recordAction?.Invoke("press_key:Escape");
        await _input.PressKeyAsync(worldMain.Capture.Window, GameVirtualKey.Escape, cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);
        var afterForeground = _activation.GetForegroundWindowInfo(worldMain.Capture.Window).ToDiagnosticString();
        LogWorkflow(workflowLog, $"step=OpenMainMenu action=PressEsc completed foregroundAfter=({afterForeground})");

        StablePageResult mainMenu;
        try
        {
            mainMenu = await WaitForExpectedAsync(
                "OpenMainMenu",
                Pages(GamePage.MainMenu),
                MediumWait,
                stableCount: 1,
                cancellationToken);
        }
        catch (TimeoutException ex)
        {
            var latest = await TryRecognizeForDiagnosticsAsync(cancellationToken);
            var foregroundAfterFirstWait = _activation.GetForegroundWindowInfo(worldMain.Capture.Window).ToDiagnosticString();
            var firstDiagnostic = SaveDiagnostic(latest, "stage1-open-main-menu-timeout");
            var firstPage = latest?.Recognition.Page.ToString() ?? "<none>";
            var firstConfidence = latest?.Recognition.Confidence.ToString("F2") ?? "<none>";
            var firstClassification = BuildOpenMainMenuTimeoutClassification(latest?.Recognition);
            LogWorkflow(workflowLog, 
                $"step=OpenMainMenu firstEscTimedOut currentRecognizedPage={firstPage} currentConfidence={firstConfidence} " +
                $"classification=\"{firstClassification}\" foregroundAfterFirstWait=({foregroundAfterFirstWait}) diagnostic={firstDiagnostic ?? "<not-saved>"}");

            if (latest?.Recognition.Page != GamePage.WorldMain)
            {
                throw CreateOpenMainMenuTimeoutException(
                    ex,
                    null,
                    beforeForeground,
                    afterForeground,
                    foregroundAfterFirstWait,
                    "<not-attempted>",
                    firstPage,
                    firstConfidence,
                    firstClassification,
                    firstDiagnostic,
                    worldMain.Capture.Window,
                    latest);
            }

            var paimonTarget = _targetLocator.FindTarget(latest.Recognition, WorkflowTargetNames.WorldPaimonIcon);
            if (paimonTarget == null)
            {
                LogWorkflow(workflowLog, $"step=OpenMainMenu paimonFallbackSkipped reason=world_paimon_icon_not_detected");
                throw CreateOpenMainMenuTimeoutException(
                    ex,
                    null,
                    beforeForeground,
                    afterForeground,
                    foregroundAfterFirstWait,
                    "<not-attempted>",
                    firstPage,
                    firstConfidence,
                    firstClassification,
                    firstDiagnostic,
                    worldMain.Capture.Window,
                    latest);
            }

            LogWorkflow(workflowLog, 
                $"step=OpenMainMenu action=ClickPaimonFallback target={WorkflowTargetNames.WorldPaimonIcon} confidence={paimonTarget.Confidence:F2}");
            await ClickRequiredTargetAsync(latest, WorkflowTargetNames.WorldPaimonIcon, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);
            var foregroundAfterFallbackKey = _activation.GetForegroundWindowInfo(worldMain.Capture.Window).ToDiagnosticString();

            try
            {
                mainMenu = await WaitForExpectedAsync(
                    "OpenMainMenu.PaimonFallback",
                    Pages(GamePage.MainMenu),
                    ShortWait,
                    stableCount: 1,
                    cancellationToken);
                LogWorkflow(workflowLog, $"step=OpenMainMenu paimonFallbackResult=MainMenu foregroundAfterFallbackKey=({foregroundAfterFallbackKey})");
            }
            catch (TimeoutException retryEx)
            {
                var retryLatest = await TryRecognizeForDiagnosticsAsync(cancellationToken);
                throw CreateOpenMainMenuTimeoutException(
                    ex,
                    retryEx,
                    beforeForeground,
                    afterForeground,
                    foregroundAfterFirstWait,
                    foregroundAfterFallbackKey,
                    firstPage,
                    firstConfidence,
                    firstClassification,
                    firstDiagnostic,
                    worldMain.Capture.Window,
                    retryLatest);
            }
        }

        return await EnterMiliastraLocalArchiveFromMainMenuAsync(mainMenu, workflowLog, cancellationToken);
    }

    private async Task<StablePageResult> EnterMiliastraLocalArchiveFromMainMenuAsync(
        StablePageResult mainMenu,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (mainMenu.Recognition.Page != GamePage.MainMenu)
            throw new InvalidOperationException($"Stage1 refuses to enter Miliastra from page {mainMenu.Recognition.Page}.");

        mainMenu = await FindQianxingEntryInMainMenuAsync(mainMenu, workflowLog, cancellationToken);
        var qianxingEntry = _targetLocator.FindTarget(mainMenu.Recognition, WorkflowTargetNames.QianxingRealmEntry);
        if (qianxingEntry == null)
        {
            LogWorkflow(workflowLog, 
                $"step=EnterMiliastra result=MainMenuOpenedButEntryMissing target={WorkflowTargetNames.QianxingRealmEntry} " +
                "note=Stage1 7W can recognize MainMenu with generic targets, but the Miliastra entry is not currently detected.");
            throw new InvalidOperationException(
                $"MainMenu opened but target '{WorkflowTargetNames.QianxingRealmEntry}' was not detected. " +
                "Stage1 7W recognized the menu but cannot safely locate the Miliastra entry after scrolling attempts. " +
                "Consider scrolling the menu manually or adding more qianxing_realm_entry samples.");
        }

        LogWorkflow(workflowLog, $"step=EnterMiliastra action=ClickTarget target={WorkflowTargetNames.QianxingRealmEntry}");
        await ClickRequiredTargetAsync(mainMenu, WorkflowTargetNames.QianxingRealmEntry, cancellationToken);

        var localArchive = await WaitForLocalArchiveAgreementAsync(await RecognizeOnceAsync(cancellationToken), workflowLog, cancellationToken);
        LogWorkflow(workflowLog, $"step=EnterMiliastraLocalArchive recognized={localArchive.Recognition.Page}");
        return localArchive;
    }

    private async Task<StablePageResult> WaitForLocalArchiveAgreementAsync(
        StablePageResult current, List<string> workflowLog, CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + LongWait;
        var ready = new PageObservationStreak();
        AutomationTarget? previousAgree = null;
        bool clicked = false;
        ReportOperation("Checking the local-save agreement");
        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var texts = await _workflowOcr.RecognizeBoxAsync(current.Capture,
                new NormalizedBox(.5, .5, .54, .54), "LocalArchiveAgreement/dialog", cancellationToken);
            var notice = LocalArchiveAgreement.Read(texts);
            if (notice.NoticeVisible)
            {
                var header = await _workflowOcr.RecognizeBoxAsync(current.Capture,
                    new NormalizedBox(.19, .08, .36, .15), "LocalArchiveAgreement/header", cancellationToken);
                texts = texts.Concat(header).ToList();
                notice = LocalArchiveAgreement.Read(texts);
            }
            if (!notice.NoticeVisible && current.Recognition.Page.IsBlocking())
                throw new InvalidOperationException($"进入本地存档时出现错误页面：{current.Recognition.Page}。");
            LogWorkflow(workflowLog, $"step=LocalArchiveAgreement page={current.Recognition.Page} notice={notice.NoticeVisible} agreeFound={notice.Agree != null} clicked={clicked} ocr=\"{FormatRawOcrTexts(texts)}\"");
            if (notice.Agree is { } agree && previousAgree is { } previous && !clicked &&
                Math.Abs(agree.CenterX - previous.CenterX) < .025 && Math.Abs(agree.CenterY - previous.CenterY) < .025)
            {
                ReportOperation("Accepting the agreement; waiting for it to close");
                _recordAction?.Invoke("click:local_archive_agree");
                await _input.ClickAsync(current.Capture.Window, agree.CenterX, agree.CenterY, cancellationToken);
                clicked = true;
                ready = new PageObservationStreak();
            }
            else if (ready.Observe(current.Recognition.Page,
                !notice.NoticeVisible && current.Recognition.Page == GamePage.LocalArchive))
            {
                ReportOperation("Local saves ready");
                return current;
            }
            previousAgree = notice.Agree;
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            current = await RecognizeOnceAsync(cancellationToken);
        }
        var diagnostic = SaveDiagnostic(current, "stage1-local-archive-agreement-timeout");
        throw new TimeoutException($"本地存档页面或须知弹窗未处理完成；diagnostic={diagnostic}。");
    }

    private async Task<StablePageResult> ReturnFromWorldMainToLoginAsync(
        StablePageResult current,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Logging out to the start screen");
        if (current.Recognition.Page is GamePage.LoginStart or GamePage.LoginServerSelect)
        {
            LogWorkflow(workflowLog, $"step=ReturnToLogin result=AlreadyLoginPage page={current.Recognition.Page}");
            return current;
        }

        if (current.Recognition.Page == GamePage.LocalArchive)
        {
            LogWorkflow(workflowLog, "step=ReturnToLogin from=LocalArchive action=PressEsc expected=WorldMainOrMainMenu");
            _recordAction?.Invoke("press_key:Escape");
            await _input.PressKeyAsync(current.Capture.Window, GameVirtualKey.Escape, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);
            current = await WaitForExpectedAsync(
                "ReturnToLogin.LocalArchiveToWorldMainOrMainMenu",
                Pages(GamePage.WorldMain, GamePage.MainMenu),
                MediumWait,
                stableCount: 1,
                cancellationToken);
            LogWorkflow(workflowLog, $"step=ReturnToLogin from=LocalArchive afterEsc={current.Recognition.Page}");
        }

        StablePageResult mainMenu;
        if (current.Recognition.Page == GamePage.MainMenu)
        {
            LogWorkflow(workflowLog, "step=ReturnToLogin result=AlreadyMainMenu");
            mainMenu = current;
        }
        else
        {
            if (current.Recognition.Page != GamePage.WorldMain)
            {
                current = await WaitForExpectedAsync(
                    "ReturnToLogin.WaitWorldMain",
                    Pages(GamePage.WorldMain),
                    MediumWait,
                    stableCount: 1,
                    cancellationToken);
            }

            LogWorkflow(workflowLog, "step=ReturnToLogin action=PressEsc expected=MainMenu");
            _recordAction?.Invoke("press_key:Escape");
            await _input.PressKeyAsync(current.Capture.Window, GameVirtualKey.Escape, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);

            mainMenu = await WaitForExpectedAsync(
                "ReturnToLogin.WaitMainMenu",
                Pages(GamePage.MainMenu),
                MediumWait,
                stableCount: 1,
                cancellationToken);
        }

        LogWorkflow(workflowLog, $"step=ReturnToLogin action=ClickMainMenuExit target={WorkflowTargetNames.MainMenuExitButton}");
        await ClickRequiredTargetAsync(mainMenu, WorkflowTargetNames.MainMenuExitButton, cancellationToken);

        var exitMenu = await WaitForExpectedAsync(
            "ReturnToLogin.WaitExitMenu",
            Pages(GamePage.ExitMenu),
            MediumWait,
            stableCount: 1,
            cancellationToken);

        LogWorkflow(workflowLog, $"step=ReturnToLogin action=ClickLogout target={WorkflowTargetNames.ExitMenuLogoutButton}");
        await ClickRequiredTargetAsync(exitMenu, WorkflowTargetNames.ExitMenuLogoutButton, cancellationToken);

        var login = await WaitForExpectedAsync(
            "ReturnToLogin.WaitLoginStart",
            Pages(GamePage.LoginStart, GamePage.LoginServerSelect),
            LongWait,
            stableCount: 1,
            cancellationToken);
        LogWorkflow(workflowLog, $"step=ReturnToLogin result={login.Recognition.Page}");
        return login;
    }

    private async Task<StablePageResult> FindQianxingEntryInMainMenuAsyncCore(
        StablePageResult mainMenu,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Finding Miliastra Wonderland in the main menu");
        if (_targetLocator.FindTarget(mainMenu.Recognition, WorkflowTargetNames.QianxingRealmEntry) != null)
            return mainMenu;

        var current = mainMenu;
        for (var attempt = 1; attempt <= MainMenuMaxScrollAttempts; attempt++)
        {
            var detectedHeaderBeforeScroll = _targetLocator.FindTarget(
                current.Recognition,
                WorkflowTargetNames.MainMenuQianxingSectionHeader) != null;
            var detectedEntryBeforeScroll = _targetLocator.FindTarget(
                current.Recognition,
                WorkflowTargetNames.QianxingRealmEntry) != null;
            LogWorkflow(workflowLog, 
                $"step=EnterMiliastra target={WorkflowTargetNames.QianxingRealmEntry} notFound " +
                $"scrollAttempt={attempt} action=ScrollMainMenu x={MainMenuScrollX:F3} y={MainMenuScrollY:F3} " +
                $"amount={MainMenuScrollWheelDelta} detected_{WorkflowTargetNames.MainMenuQianxingSectionHeader}={detectedHeaderBeforeScroll} " +
                $"detected_{WorkflowTargetNames.QianxingRealmEntry}={detectedEntryBeforeScroll}");
            _recordAction?.Invoke($"scroll:main_menu:{MainMenuScrollWheelDelta}");
            await _input.ScrollAsync(
                current.Capture.Window,
                MainMenuScrollX,
                MainMenuScrollY,
                MainMenuScrollWheelDelta,
                cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);

            current = await WaitForExpectedAsync(
                $"EnterMiliastra.ScrollMainMenu{attempt}",
                Pages(GamePage.MainMenu),
                ShortWait,
                stableCount: 1,
                cancellationToken);

            if (_targetLocator.FindTarget(current.Recognition, WorkflowTargetNames.QianxingRealmEntry) != null)
            {
                var detectedHeaderAfterScroll = _targetLocator.FindTarget(
                    current.Recognition,
                    WorkflowTargetNames.MainMenuQianxingSectionHeader) != null;
                LogWorkflow(workflowLog, 
                    $"step=EnterMiliastra target={WorkflowTargetNames.QianxingRealmEntry} foundAfterScrollAttempt={attempt} " +
                    $"detected_{WorkflowTargetNames.MainMenuQianxingSectionHeader}={detectedHeaderAfterScroll} " +
                    $"detected_{WorkflowTargetNames.QianxingRealmEntry}=True");
                return current;
            }

            var detectedHeaderAfterMiss = _targetLocator.FindTarget(
                current.Recognition,
                WorkflowTargetNames.MainMenuQianxingSectionHeader) != null;
            LogWorkflow(workflowLog, 
                $"step=EnterMiliastra target={WorkflowTargetNames.QianxingRealmEntry} notFoundAfterScrollAttempt={attempt} " +
                $"detected_{WorkflowTargetNames.MainMenuQianxingSectionHeader}={detectedHeaderAfterMiss} " +
                $"detected_{WorkflowTargetNames.QianxingRealmEntry}=False");
        }

        return current;
    }

    private async Task<StablePageResult> ImportRunArchiveAsync(
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Importing the save file");
        if (!File.Exists(runArchive.RunArchivePath))
            throw new FileNotFoundException($"Stage1 run archive file does not exist: {runArchive.RunArchivePath}", runArchive.RunArchivePath);

        var importTarget = FindLocalArchiveImportButton(localArchive, 0.25);
        var primaryActionCount = CountTargets(localArchive.Recognition, WorkflowTargetNames.PrimaryActionButton, 0.25);
        LogWorkflow(workflowLog, 
            $"step=ImportRunArchiveBeforeClick runArchive={runArchive.RunArchiveDisplayName} " +
            $"page={localArchive.Recognition.Page} confidence={localArchive.Recognition.Confidence:F2} " +
            $"importTarget={FormatOptionalTarget(importTarget)} primaryActionButtonCount={primaryActionCount} " +
            $"targets=[{FormatTargets(localArchive.Recognition.Targets)}]");
        if (importTarget == null)
            throw new InvalidOperationException(
                $"Stage1 detected LocalArchive but could not locate the Import Save primary_action_button. " +
                $"runArchive={runArchive.RunArchiveDisplayName}, primaryActionButtonCount={primaryActionCount}.");

        var importClickStopwatch = Stopwatch.StartNew();
        LogWorkflow(workflowLog, $"step=ImportRunArchive runArchive={runArchive.RunArchiveDisplayName} action=ClickTarget target={WorkflowTargetNames.PrimaryActionButton} semantic=ImportSave target=({FormatTarget(importTarget)})");
        _recordAction?.Invoke($"click:{WorkflowTargetNames.ImportArchiveButton}");
        await ClickSpecificTargetAsync(localArchive, importTarget, cancellationToken);
        importClickStopwatch.Stop();
        LogWorkflow(workflowLog, 
            $"step=ImportRunArchiveAfterClick runArchive={runArchive.RunArchiveDisplayName} " +
            $"elapsedMs={importClickStopwatch.ElapsedMilliseconds} foreground=({SafeGetForegroundDiagnostic(localArchive.Capture.Window)})");

        _recordAction?.Invoke($"select_file:{runArchive.RunArchivePath}");
        var selectFileStopwatch = Stopwatch.StartNew();
        await _fileDialog.SelectFileAsync(localArchive.Capture.Window, runArchive.RunArchivePath, WindowsFileDialogAutomationService.SelectionTimeout, cancellationToken);
        selectFileStopwatch.Stop();
        LogWorkflow(workflowLog, 
            $"step=ImportRunArchiveFileDialogCompleted runArchive={runArchive.RunArchiveDisplayName} " +
            $"elapsedMs={selectFileStopwatch.ElapsedMilliseconds}");
        _activation.RestoreAndActivate(localArchive.Capture.Window);
        _recordAction?.Invoke("activate_game_window");

        var result = await WaitForExpectedAsync(
            "ImportRunArchive",
            Pages(GamePage.LocalArchive),
            LongWait,
            stableCount: 1,
            cancellationToken);
        LogWorkflow(workflowLog, $"step=ImportRunArchive runArchive={runArchive.RunArchiveDisplayName} recognized={result.Recognition.Page}");
        return await WaitForRunArchiveCardVisibleAfterImportAsync(
            runArchive,
            result,
            workflowLog,
            cancellationToken);
    }

    private async Task<StablePageResult> WaitForRunArchiveCardVisibleAfterImportAsyncCore(
        UploadRunArchive runArchive,
        StablePageResult initialLocalArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + ImportVisibleWait;
        var current = initialLocalArchive;
        IReadOnlyList<LocalArchiveCardOcrMatch> lastMatches = [];
        IReadOnlyList<LocalArchiveCardCandidate> lastCandidates = [];
        var scanPage = 0;
        var pollsOnCurrentPage = 0;
        var scrollAttempts = 0;

        LogWorkflow(workflowLog, 
            $"step=ImportRunArchiveVisible waitSeconds={ImportVisibleWait.TotalSeconds:F0} " +
            $"runArchive={runArchive.RunArchiveDisplayName} runArchiveCode={runArchive.RunArchiveCode}");

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Recognition.Page != GamePage.LocalArchive)
            {
                current = await WaitForPageEvidenceAsync(current, Pages(GamePage.LocalArchive), "ImportRunArchive.PageRecovery", ShortWait, cancellationToken);
            }


            lastCandidates = _cardCandidateProvider.GetCandidates(current.Recognition, current.Capture);
            if (lastCandidates.Count > 0)
            {
                lastMatches = await RecognizeCardRoisAsync(
                    current,
                    lastCandidates,
                    runArchive.RunArchiveDisplayName,
                    runArchive.RunArchiveCode,
                    "ImportRunArchiveVisible",
                    cancellationToken);
                var matchedCards = DeduplicateCardMatches(lastMatches
                    .Where(match => match.MatchScore >= 0.7)
                    .OrderByDescending(match => match.MatchScore)
                    .ToList());

                if (matchedCards.Count == 1)
                {
                    var match = matchedCards[0];
                    LogWorkflow(workflowLog, 
                        $"step=ImportRunArchiveVisible result=found runArchive={runArchive.RunArchiveDisplayName} " +
                        $"runArchiveCode={runArchive.RunArchiveCode} candidateSource={match.Candidate.Source} " +
                        $"candidateIndex={match.Candidate.Index} score={match.MatchScore:F2}");
                    return current;
                }

                if (matchedCards.Count > 1)
                {
                    var diagnostic = SaveLocalArchiveCardDiagnostic(
                        current,
                        "stage1-imported-run-archive-ambiguous",
                        runArchive.RunArchiveDisplayName,
                        runArchive.RunArchiveCode,
                        "ImportRunArchiveVisible",
                        lastMatches,
                        null,
                        lastCandidates.Any(candidate => !candidate.IsSynthetic),
                        FindTargets(current.Recognition, WorkflowTargetNames.LocalArchiveMoreButton).Count > 0,
                        true,
                        null,
                        null,
                        $"Imported run archive code '{runArchive.RunArchiveCode}' was visible in multiple LocalArchive card candidates.");
                    throw new InvalidOperationException(
                        $"Stage1 selected the .gil file but found multiple LocalArchive cards matching the imported run archive code. " +
                        $"runArchive={runArchive.RunArchiveDisplayName}, runArchiveCode={runArchive.RunArchiveCode}, diagnostic={diagnostic ?? "<not-saved>"}.");
                }

                LogWorkflow(workflowLog, 
                    $"step=ImportRunArchiveVisible scanPage={scanPage} result=notYetVisible runArchive={runArchive.RunArchiveDisplayName} " +
                    $"runArchiveCode={runArchive.RunArchiveCode} candidates={lastCandidates.Count}");
            }
            else
            {
                LogWorkflow(workflowLog, 
                    $"step=ImportRunArchiveVisible scanPage={scanPage} result=noCandidates runArchive={runArchive.RunArchiveDisplayName} " +
                    $"runArchiveCode={runArchive.RunArchiveCode}");
            }

            pollsOnCurrentPage++;
            if (pollsOnCurrentPage >= LocalArchiveInitialPollsBeforeScroll &&
                scrollAttempts < LocalArchiveMaxScrollAttempts)
            {
                scrollAttempts++;
                scanPage++;
                pollsOnCurrentPage = 0;
                LogWorkflow(workflowLog, 
                    $"step=ImportRunArchiveVisible action=ScrollLocalArchive direction=down " +
                    $"amount={LocalArchiveScrollWheelDelta} scrollAttempt={scrollAttempts}");
                _recordAction?.Invoke($"scroll:local_archive:{LocalArchiveScrollWheelDelta}");
                await _input.ScrollAsync(
                    current.Capture.Window,
                    ScrollCenterX,
                    ScrollCenterY,
                    LocalArchiveScrollWheelDelta,
                    cancellationToken);
                await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            else
            {
                await DelayWithTimingAsync(PollInterval, cancellationToken);
            }

            current = await WaitForExpectedAsync(
                "ImportRunArchiveVisible.PollLocalArchive",
                Pages(GamePage.LocalArchive),
                ShortWait,
                stableCount: 1,
                cancellationToken);
        }

        var timeoutDiagnostic = SaveLocalArchiveCardDiagnostic(
            current,
            "stage1-imported-run-archive-not-visible",
            runArchive.RunArchiveDisplayName,
            runArchive.RunArchiveCode,
            "ImportRunArchiveVisible",
            lastMatches,
            null,
            lastCandidates.Any(candidate => !candidate.IsSynthetic),
            FindTargets(current.Recognition, WorkflowTargetNames.LocalArchiveMoreButton).Count > 0,
            true,
            null,
            null,
            "Imported run archive code was not visible in LocalArchive after file dialog selection.");
        throw new TimeoutException(
            $"Stage1 selected the .gil file but could not confirm that the imported run archive appeared in LocalArchive. " +
            $"runArchive={runArchive.RunArchiveDisplayName}, runArchiveCode={runArchive.RunArchiveCode}, diagnostic={timeoutDiagnostic ?? "<not-saved>"}.");
    }

    private async Task<StablePageResult> UploadNewArchiveStage1Async(
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var sandboxRunningBeforeFirstUpload = _sandboxWatcher.IsSandboxRunning();
        LogWorkflow(workflowLog, 
            $"step=BeforeFirstUpload sandboxRunningBeforeFirstUpload={sandboxRunningBeforeFirstUpload}");

        var firstResult = await TryUploadNewArchiveOnceAsync(
            runArchive,
            localArchive,
            isSecondAttempt: false,
            workflowLog,
            cancellationToken);

        if (firstResult.Recognition.Page is GamePage.UploadedArchive or GamePage.WorldMain)
        {
            LogWorkflow(workflowLog, $"step=FirstUpload runArchive={runArchive.RunArchiveDisplayName} result={firstResult.Recognition.Page} noSecondAttemptNeeded");
            return firstResult;
        }

        var readyLocalArchive = firstResult.Recognition.Page switch
        {
            GamePage.UploadFailed => await CloseFailureAndWaitForSandboxReadyAsync(
                runArchive,
                firstResult,
                sandboxRunningBeforeFirstUpload,
                workflowLog,
                cancellationToken),
            GamePage.LocalArchive => await WaitForSandboxReadyAsync(
                runArchive,
                firstResult,
                sandboxRunningBeforeFirstUpload,
                workflowLog,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"First Stage1 upload returned unsupported page {firstResult.Recognition.Page} for runArchive={runArchive.RunArchiveDisplayName}.")
        };

        return await TryUploadNewArchiveOnceAsync(
            runArchive,
            readyLocalArchive,
            isSecondAttempt: true,
            workflowLog,
            cancellationToken);
    }

    private async Task<StablePageResult> TryUploadNewArchiveOnceAsync(
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        bool isSecondAttempt,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Selecting Upload Save");
        var attemptName = isSecondAttempt ? "SecondUpload" : "FirstUpload";
        var menu = await EnsureLocalArchiveMenuOpenForRunArchiveAsync(
            runArchive,
            localArchive,
            attemptName,
            workflowLog,
            cancellationToken);

        LogWorkflow(workflowLog, $"step={attemptName} action=ClickUploadMenu target={WorkflowTargetNames.MenuActionItem} semantic=UploadSave");
        await ClickLocalArchiveUploadMenuItemAsync(menu, attemptName, workflowLog, cancellationToken);

        var entry = await WaitForUploadBranchAsync(
            $"{attemptName}.WaitUploadEntry",
            GetImmediateUploadResultPages(allowUploadLocationDialog: true),
            MediumWait,
            workflowLog,
            cancellationToken,
            allowStableLocalArchive: !isSecondAttempt,
            attemptName, requireSandboxEvidenceForLocalArchive: true);

        if (entry.Recognition.Page == GamePage.UploadLocationDialog)
        {
            LogWorkflow(workflowLog, $"step={attemptName} uploadLocationDialog=appeared action=ClickUploadNew");
            var afterChoice = await ClickUploadLocationChoiceWithVerificationAsync(
                entry,
                UploadLocationChoice.UploadNew,
                attemptName,
                workflowLog,
                cancellationToken);
            entry = afterChoice.Recognition.Page is GamePage.UploadFailed or GamePage.UploadedArchive
                ? afterChoice
                : await WaitForUploadBranchAsync(
                    $"{attemptName}.WaitAfterUploadNew",
                    GetImmediateUploadResultPages(allowUploadLocationDialog: false),
                    isSecondAttempt ? LongWait : MediumWait,
                    workflowLog,
                    cancellationToken,
                    allowStableLocalArchive: !isSecondAttempt,
                    attemptName);
        }
        else
        {
            LogWorkflow(workflowLog, $"step={attemptName} uploadLocationDialog=notObserved recognized={entry.Recognition.Page}");
        }

        if (entry.Recognition.Page == GamePage.UploadFailed && isSecondAttempt)
            throw new InvalidOperationException($"Second Stage1 upload failed for runArchive={runArchive.RunArchiveDisplayName}.");

        if (entry.Recognition.Page == GamePage.LocalArchive && isSecondAttempt)
            throw new InvalidOperationException($"Second Stage1 upload returned to LocalArchive instead of UploadedArchive for runArchive={runArchive.RunArchiveDisplayName}.");

        return entry;
    }

    private async Task<StablePageResult> UpdateExistingArchiveStage1Async(
        UploadRequest request,
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ExistingArchiveIdentifier))
            throw new InvalidOperationException("Stage1 update requires ExistingArchiveIdentifier.");

        var sandboxRunningBeforeFirstUpload = _sandboxWatcher.IsSandboxRunning();
        LogWorkflow(workflowLog, 
            $"step=BeforeFirstUpdateUpload sandboxRunningBeforeFirstUpload={sandboxRunningBeforeFirstUpload} " +
            $"existingArchiveIdentifier=\"{request.ExistingArchiveIdentifier}\" isMajorVersionUpdate={request.IsMajorVersionUpdate}");

        var firstResult = await TryUpdateExistingArchiveOnceAsync(
            request,
            runArchive,
            localArchive,
            isSecondAttempt: false,
            workflowLog,
            cancellationToken);

        if (firstResult.Recognition.Page is GamePage.UploadedArchive or GamePage.WorldMain)
        {
            LogWorkflow(workflowLog, $"step=FirstUpdateUpload runArchive={runArchive.RunArchiveDisplayName} result={firstResult.Recognition.Page} noSecondAttemptNeeded");
            return firstResult;
        }

        var readyLocalArchive = firstResult.Recognition.Page switch
        {
            GamePage.UploadFailed => await CloseFailureAndWaitForSandboxReadyAsync(
                runArchive,
                firstResult,
                sandboxRunningBeforeFirstUpload,
                workflowLog,
                cancellationToken),
            GamePage.LocalArchive => await WaitForSandboxReadyAsync(
                runArchive,
                firstResult,
                sandboxRunningBeforeFirstUpload,
                workflowLog,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"First Stage1 update upload returned unsupported page {firstResult.Recognition.Page} for runArchive={runArchive.RunArchiveDisplayName}.")
        };

        return await TryUpdateExistingArchiveOnceAsync(
            request,
            runArchive,
            readyLocalArchive,
            isSecondAttempt: true,
            workflowLog,
            cancellationToken);
    }

    private async Task<StablePageResult> TryUpdateExistingArchiveOnceAsync(
        UploadRequest request,
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        bool isSecondAttempt,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var attemptName = isSecondAttempt ? "SecondUpdateUpload" : "FirstUpdateUpload";
        var menu = await EnsureLocalArchiveMenuOpenForRunArchiveAsync(
            runArchive,
            localArchive,
            attemptName,
            workflowLog,
            cancellationToken);

        LogWorkflow(workflowLog, $"step={attemptName} action=ClickUploadMenu target={WorkflowTargetNames.MenuActionItem} semantic=UploadSave");
        await ClickLocalArchiveUploadMenuItemAsync(menu, attemptName, workflowLog, cancellationToken);

        var entry = await WaitForUploadBranchAsync(
            $"{attemptName}.WaitUploadEntry",
            Pages(GamePage.UploadLocationDialog, GamePage.UpdateArchiveSelect, GamePage.UploadFailed, GamePage.UploadedArchive),
            MediumWait,
            workflowLog,
            cancellationToken,
            allowStableLocalArchive: !isSecondAttempt,
            attemptName, requireSandboxEvidenceForLocalArchive: true);

        if (entry.Recognition.Page == GamePage.UploadLocationDialog)
        {
            LogWorkflow(workflowLog, $"step={attemptName} uploadLocationDialog=appeared action=ClickUpdateUploadedArchive");
            var afterChoice = await ClickUploadLocationChoiceWithVerificationAsync(
                entry,
                UploadLocationChoice.UpdateUploaded,
                attemptName,
                workflowLog,
                cancellationToken);

            entry = afterChoice.Recognition.Page is GamePage.UpdateArchiveSelect or GamePage.UploadFailed or GamePage.UploadedArchive
                ? afterChoice
                : await WaitForUpdateArchiveSelectBranchAsync(
                    $"{attemptName}.WaitAfterUpdateUploadedArchive",
                    isSecondAttempt ? LongWait : MediumWait,
                    workflowLog,
                    cancellationToken,
                    allowStableLocalArchive: !isSecondAttempt,
                    attemptName);
        }
        else
        {
            LogWorkflow(workflowLog, $"step={attemptName} uploadLocationDialog=notObserved recognized={entry.Recognition.Page}");
        }

        if (entry.Recognition.Page == GamePage.UpdateArchiveSelect)
            entry = await SelectExistingArchiveAndStartUpdateAsync(request, entry, attemptName, workflowLog, cancellationToken);

        if (entry.Recognition.Page == GamePage.UploadFailed && isSecondAttempt)
            throw new InvalidOperationException($"Second Stage1 update upload failed for runArchive={runArchive.RunArchiveDisplayName}.");

        if (entry.Recognition.Page == GamePage.LocalArchive && isSecondAttempt)
            throw new InvalidOperationException($"Second Stage1 update upload returned to LocalArchive instead of UploadedArchive for runArchive={runArchive.RunArchiveDisplayName}.");

        return entry;
    }

    private async Task<StablePageResult> SelectExistingArchiveAndStartUpdateAsync(
        UploadRequest request,
        StablePageResult updateSelect,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Finding the uploaded save to update");
        var selection = await SelectExistingUpdateArchiveCardAsync(
            request,
            updateSelect,
            attemptName,
            workflowLog,
            cancellationToken);

        LogWorkflow(workflowLog, 
            $"step={attemptName}.SelectExistingArchive action=ClickUpdateArchiveCard target=({FormatTarget(selection.Card)}) " +
            $"identifier=\"{request.ExistingArchiveIdentifier}\"");
        await ClickSpecificTargetAsync(selection.Page, selection.Card, cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(650), cancellationToken);

        var afterCardClick = await WaitForUpdateArchiveSelectByContextAsync(
            $"{attemptName}.WaitUpdateArchiveSelectAfterCardClick",
            ShortWait,
            workflowLog,
            cancellationToken);

        LogWorkflow(workflowLog, $"step={attemptName}.SelectExistingArchive action=ClickUpdateButton target={WorkflowTargetNames.PrimaryActionButton} semantic=Update");
        await ClickUpdateArchiveButtonAsync(afterCardClick, attemptName, workflowLog, cancellationToken);

        var dialog = await WaitForUpdateArchiveDialogByContextAsync(
            $"{attemptName}.WaitUpdateArchiveDialog",
            MediumWait,
            workflowLog,
            cancellationToken);

        await ChooseUpdateTypeAndStartAsync(request, dialog, attemptName, workflowLog, cancellationToken);
        return await WaitForUploadBranchAsync(
            $"{attemptName}.WaitAfterUpdateStart",
            Pages(GamePage.UploadFailed, GamePage.UploadedArchive),
            LongWait,
            workflowLog,
            cancellationToken,
            allowStableLocalArchive: attemptName.StartsWith("First", StringComparison.Ordinal),
            attemptName);
    }

    private async Task<StablePageResult> WaitForUpdateArchiveSelectBranchAsync(
        string stepName,
        TimeSpan timeout,
        List<string> workflowLog,
        CancellationToken cancellationToken,
        bool allowStableLocalArchive,
        string attemptName)
    {
        var deadline = _clock.UtcNow + timeout;
        var firstObservedAt = _clock.UtcNow;
        StablePageResult? last = null;
        var localArchiveStableCount = 0;

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await RecognizeOnceAsync(cancellationToken);
            last = current;

            if (current.Recognition.Page is GamePage.UpdateArchiveSelect or GamePage.UploadFailed or GamePage.UploadedArchive)
            {
                LogWorkflow(workflowLog, 
                    $"step={stepName} acceptedPage={current.Recognition.Page} confidence={current.Recognition.Confidence:F2}");
                return current;
            }

            if (TryPromoteUpdateArchiveSelectByCardContext(current, out var promoted))
            {
                LogWorkflow(workflowLog, 
                    $"step={stepName} acceptedPage=UpdateArchiveSelect promotedFrom={current.Recognition.Page} " +
                    $"reason=update_archive_select_strong_workflow_context " +
                    $"cardCount={CountTargets(current.Recognition, WorkflowTargetNames.UploadedArchiveCard, 0.25)} " +
                    $"primaryButtonCount={CountTargets(current.Recognition, WorkflowTargetNames.PrimaryActionButton, 0.25)}");
                return promoted;
            }

            if (allowStableLocalArchive && current.Recognition.Page == GamePage.LocalArchive)
            {
                localArchiveStableCount++;
                var observedFor = _clock.UtcNow - firstObservedAt;
                if (localArchiveStableCount >= 3 && observedFor >= TimeSpan.FromSeconds(3))
                {
                    LogWorkflow(workflowLog, 
                        $"step={stepName} acceptedPage=LocalArchive reason=sandbox_return_candidate " +
                        $"stableCount={localArchiveStableCount} observedFor={observedFor.TotalSeconds:F1}s");
                    return current;
                }
            }
            else
            {
                localArchiveStableCount = 0;
            }

            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(650), cancellationToken);
        }

        var diagnostic = last == null ? null : SaveDiagnostic(last, "stage1-update-archive-select-branch-timeout");
        throw new TimeoutException(
            $"Timed out during {stepName} waiting for UpdateArchiveSelect/UploadFailed/UploadedArchive. " +
            $"attemptName={attemptName}, lastPage={last?.Recognition.Page.ToString() ?? "<none>"}, " +
            $"uploadedArchiveCardCount={(last == null ? 0 : CountTargets(last.Recognition, WorkflowTargetNames.UploadedArchiveCard, 0.25))}, " +
            $"diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<StablePageResult> WaitForUpdateArchiveSelectByContextAsyncCore(
        string stepName,
        TimeSpan timeout,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + timeout;
        StablePageResult? last = null;

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await RecognizeOnceAsync(cancellationToken);
            last = current;

            if (current.Recognition.Page == GamePage.UpdateArchiveSelect)
            {
                LogWorkflow(workflowLog, $"step={stepName} acceptedPage=UpdateArchiveSelect confidence={current.Recognition.Confidence:F2}");
                return current;
            }

            if (TryPromoteUpdateArchiveSelectByCardContext(current, out var promoted))
            {
                LogWorkflow(workflowLog, 
                    $"step={stepName} acceptedPage=UpdateArchiveSelect promotedFrom={current.Recognition.Page} " +
                    $"reason=uploaded_archive_card_context cardCount={CountTargets(current.Recognition, WorkflowTargetNames.UploadedArchiveCard, 0.25)}");
                return promoted;
            }

            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(650), cancellationToken);
        }

        var diagnostic = last == null ? null : SaveDiagnostic(last, "stage1-update-archive-select-context-timeout");
        throw new TimeoutException(
            $"Timed out during {stepName} waiting for UpdateArchiveSelect by context. " +
            $"lastPage={last?.Recognition.Page.ToString() ?? "<none>"}, " +
            $"uploadedArchiveCardCount={(last == null ? 0 : CountTargets(last.Recognition, WorkflowTargetNames.UploadedArchiveCard, 0.25))}, " +
            $"diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private static bool TryPromoteUpdateArchiveSelectByCardContext(
        StablePageResult current,
        out StablePageResult promoted)
    {
        if (current.Recognition.Page is (GamePage.Unknown or GamePage.UploadFailed) &&
            HasUpdateArchiveSelectStrongWorkflowContext(current.Recognition))
        {
            var confidence = current.Recognition.Targets
                .Where(static target =>
                    string.Equals(target.Name, WorkflowTargetNames.UploadedArchiveCard, StringComparison.Ordinal) ||
                    string.Equals(target.Name, WorkflowTargetNames.PrimaryActionButton, StringComparison.Ordinal))
                .Select(target => target.Confidence)
                .DefaultIfEmpty(0.25)
                .Max();
            promoted = current with
            {
                Recognition = new PageRecognitionResult(
                    GamePage.UpdateArchiveSelect,
                    confidence,
                    current.Recognition.Source + "|workflow_context:update_archive_select_strong_context",
                    current.Recognition.Targets)
            };
            return true;
        }

        promoted = current;
        return false;
    }

    private static bool HasUpdateArchiveSelectStrongWorkflowContext(PageRecognitionResult recognition) =>
        HasTarget(recognition, WorkflowTargetNames.UploadedArchiveCard, 0.25) &&
        HasTarget(recognition, WorkflowTargetNames.PrimaryActionButton, 0.25) &&
        !HasTarget(recognition, WorkflowTargetNames.LocalArchiveTab, 0.25) &&
        !HasTarget(recognition, WorkflowTargetNames.UploadedArchiveTab, 0.25) &&
        !HasStrongUploadedArchiveContext(recognition);

    private async Task ClickUpdateArchiveButtonAsync(
        StablePageResult updateSelect,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (updateSelect.Recognition.Page != GamePage.UpdateArchiveSelect)
            throw new InvalidOperationException(
                $"Stage1 refuses to click update_archive_button on page {updateSelect.Recognition.Page}.");

        var target = FindPrimaryActionButton(updateSelect, 0.3);
        if (target != null)
        {
            LogWorkflow(workflowLog, $"step={attemptName}.ClickUpdateButton selectedBy=yolo target={WorkflowTargetNames.PrimaryActionButton} semantic=Update targetInfo=({FormatTarget(target)})");
            await ClickSpecificTargetAsync(updateSelect, target, cancellationToken);
            return;
        }

        var openCvResult = _updateArchiveButtonLocator.Locate(updateSelect.Capture, cancellationToken);
        if (openCvResult.Matched && openCvResult.Target != null)
        {
            LogWorkflow(workflowLog, 
                $"step={attemptName}.ClickUpdateButton selectedBy=opencv target=({FormatTarget(openCvResult.Target)}) " +
                $"searchRoi=({FormatNormalizedBox(openCvResult.SearchRoi)}) reason=\"{openCvResult.RejectedReason}\"");
            _recordAction?.Invoke("click:update_archive_button_opencv");
            await ClickSpecificTargetAsync(updateSelect, openCvResult.Target, cancellationToken);
            return;
        }

        LogWorkflow(workflowLog, 
            $"step={attemptName}.ClickUpdateButton result=failed yoloMissing=True openCvMatched=False " +
            $"searchRoi=({FormatNormalizedBox(openCvResult.SearchRoi)}) reason=\"{openCvResult.RejectedReason}\"");
        _ = SaveDiagnostic(updateSelect, $"{attemptName.ToLowerInvariant()}-update-button-not-found");
        throw new InvalidOperationException(
            $"Stage1 update could not locate the Update primary_action_button by YOLO or OpenCV. " +
            $"OpenCV reason: {openCvResult.RejectedReason}");
    }

    private async Task<UpdateArchiveCardSelection> SelectExistingUpdateArchiveCardAsync(
        UploadRequest request,
        StablePageResult updateSelect,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var normalizedIdentifier = RapidOcrLocalArchiveCardTextRecognizer.NormalizeForMatch(request.ExistingArchiveIdentifier);
        var current = updateSelect;
        var sawAnyCandidates = false;
        var bestObservedScore = 0.0;
        var bestObservedText = string.Empty;

        for (var scrollAttempt = 0; scrollAttempt <= LocalArchiveMaxScrollAttempts; scrollAttempt++)
        {
            if (current.Recognition.Page != GamePage.UpdateArchiveSelect)
                throw new InvalidOperationException(
                    $"Stage1 update refuses to scan existing archive cards on page {current.Recognition.Page}.");

            var candidates = BuildUpdateArchiveYoloCardCandidates(current.Recognition);
            sawAnyCandidates |= candidates.Count > 0;
            LogWorkflow(workflowLog, 
                $"step={attemptName}.SelectExistingArchive scanPage={scrollAttempt + 1} strategy=yolo_uploaded_archive_card_ocr " +
                $"yoloUploadedArchiveCardCandidates={candidates.Count}");

            var matches = new List<(LocalArchiveCardCandidate Candidate, string Text, string NormalizedText, double Score)>();
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var roi = CreateCardRoiCapture(current.Capture, candidate);
                _ = SaveCardRoiScreenshot(roi.Capture, attemptName, candidate.Source, index);
                var textResults = await _cardTextRecognizer.RecognizeCardsAsync(roi.Capture, cancellationToken);
                var lines = ArchiveOcrTextLayout.CardLines(textResults.Select(t => MapRoiResultToOriginal(t, roi.Roi)),
                    candidate.NameRegion ?? ArchiveOcrTextLayout.Box(candidate.CardBox), current.Capture.Bitmap.PixelWidth, current.Capture.Bitmap.PixelHeight);
                var combinedText = lines.OrderByDescending(t => CalculateIdentifierMatchScore(
                    RapidOcrLocalArchiveCardTextRecognizer.NormalizeForMatch(t.Text), normalizedIdentifier)).FirstOrDefault()?.Text ?? string.Empty;
                var normalizedText = RapidOcrLocalArchiveCardTextRecognizer.NormalizeForMatch(combinedText);
                var score = CalculateIdentifierMatchScore(normalizedText, normalizedIdentifier);
                if (score > bestObservedScore)
                {
                    bestObservedScore = score;
                    bestObservedText = combinedText;
                }

                LogWorkflow(workflowLog, 
                    $"step={attemptName}.SelectExistingArchive ocrCandidate={candidate.Source}/{candidate.Index} " +
                    $"scanPage={scrollAttempt + 1} score={score:F2} text=\"{combinedText}\" normalized=\"{normalizedText}\" " +
                    $"card=({FormatTarget(candidate.CardBox)})");
                if (score > 0)
                    matches.Add((candidate, combinedText, normalizedText, score));
            }

            var bestMatches = matches
                .Where(match => match.Score >= 0.75)
                .OrderByDescending(match => match.Score)
                .ToList();
            if (bestMatches.Count == 1)
            {
                LogWorkflow(workflowLog, 
                    $"step={attemptName}.SelectExistingArchive selectedBy=yolo_uploaded_archive_card_ocr_identifier " +
                    $"scanPage={scrollAttempt + 1} source={bestMatches[0].Candidate.Source}/{bestMatches[0].Candidate.Index} " +
                    $"score={bestMatches[0].Score:F2} text=\"{bestMatches[0].Text}\"");
                return new UpdateArchiveCardSelection(current, bestMatches[0].Candidate.CardBox);
            }

            if (bestMatches.Count > 1)
                throw new InvalidOperationException(
                    $"Stage1 update matched ExistingArchiveIdentifier in multiple uploaded archive cards; refusing to choose one. identifier={request.ExistingArchiveIdentifier}");

            if (scrollAttempt < LocalArchiveMaxScrollAttempts)
            {
                LogWorkflow(workflowLog, 
                    $"step={attemptName}.SelectExistingArchive result=noOcrMatch action=ScrollUpdateArchiveSelect " +
                    $"direction=down amount={LocalArchiveScrollWheelDelta} scrollAttempt={scrollAttempt + 1} " +
                    $"bestObservedScore={bestObservedScore:F2} bestObservedText=\"{bestObservedText}\"");
                current = await ScrollUpdateArchiveSelectAndRecognizeAsync(
                    current,
                    $"{attemptName}.SelectExistingArchive.ScrollUpdateArchiveSelect{scrollAttempt + 1}",
                    workflowLog,
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Stage1 update could not uniquely match ExistingArchiveIdentifier in the uploaded archive list. " +
            $"identifier={request.ExistingArchiveIdentifier}, sawAnyYoloUploadedArchiveCardCandidates={sawAnyCandidates}, " +
            $"bestObservedScore={bestObservedScore:F2}, bestObservedText=\"{bestObservedText}\".");
    }

    private static IReadOnlyList<LocalArchiveCardCandidate> BuildUpdateArchiveYoloCardCandidates(PageRecognitionResult recognition)
    {
        return FindTargets(recognition, WorkflowTargetNames.UploadedArchiveCard, minimumConfidence: 0.25)
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .Select((target, index) => new LocalArchiveCardCandidate(
                target,
                "yolo_uploaded_archive_card",
                index,
                CreateUpdateArchiveNameRegion(target),
                IsSynthetic: false))
            .ToList();
    }

    private static NormalizedBox CreateUpdateArchiveNameRegion(AutomationTarget card) =>
        new(
            Math.Clamp(card.CenterX, 0, 1),
            Math.Clamp(card.CenterY + card.Height * 0.34, 0, 1),
            Math.Clamp(card.Width * 0.92, 0, 1),
            Math.Clamp(card.Height * 0.32, 0, 1));

    private async Task ChooseUpdateTypeAndStartAsync(
        UploadRequest request,
        StablePageResult dialog,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (!request.IsMajorVersionUpdate)
        {
            LogWorkflow(workflowLog, $"step={attemptName}.ChooseUpdateType result=defaultMinorSelected action=SkipTypeChoice");
            LogWorkflow(workflowLog, $"step={attemptName}.ChooseUpdateType action=ClickStartUpload target={WorkflowTargetNames.PrimaryActionButton} semantic=StartUpload");
            await ClickUpdateStartUploadButtonAsync(dialog, attemptName, workflowLog, cancellationToken);
            return;
        }

        var ocrChoice = await TryFindUpdateTypeOptionByOcrAsync(
            isMajorVersionUpdate: true,
            dialog,
            attemptName,
            workflowLog,
            cancellationToken);
        if (ocrChoice != null)
        {
            LogWorkflow(workflowLog, 
                $"step={attemptName}.ChooseUpdateType selectedBy=ocr isMajor=True " +
                $"text=\"{ocrChoice.Text}\" normalized=\"{RapidOcrLocalArchiveCardTextRecognizer.NormalizeForMatch(ocrChoice.Text)}\" " +
                $"confidence={ocrChoice.Confidence:F2} target=({FormatNormalizedBox(ocrChoice.NormalizedBox)})");
            await ClickNormalizedAsync(dialog.Capture.Window, ocrChoice.NormalizedBox.CenterX, ocrChoice.NormalizedBox.CenterY, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(450), cancellationToken);
            dialog = await WaitForUpdateArchiveDialogByContextAsync(
                $"{attemptName}.WaitUpdateArchiveDialogAfterTypeChoice",
                ShortWait,
                workflowLog,
                cancellationToken);
        }
        else
        {
            var options = FindTargets(dialog.Recognition, WorkflowTargetNames.UpdateTypeOption, minimumConfidence: 0.25)
                .OrderBy(target => target.CenterY)
                .ThenBy(target => target.CenterX)
                .ToList();
            if (options.Count > 0)
            {
                var optionIndex = options.Count > 1 ? 1 : 0;
                var option = options[Math.Clamp(optionIndex, 0, options.Count - 1)];
                LogWorkflow(workflowLog, 
                    $"step={attemptName}.ChooseUpdateType selectedBy=yolo isMajor=True " +
                    $"optionIndex={optionIndex} optionCount={options.Count} target=({FormatTarget(option)})");
                await ClickSpecificTargetAsync(dialog, option, cancellationToken);
                await DelayWithTimingAsync(TimeSpan.FromMilliseconds(350), cancellationToken);
                dialog = await WaitForUpdateArchiveDialogByContextAsync(
                    $"{attemptName}.WaitUpdateArchiveDialogAfterTypeChoice",
                    ShortWait,
                    workflowLog,
                    cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("Stage1 update could not find major update option by OCR or YOLO.");
            }
        }

        LogWorkflow(workflowLog, $"step={attemptName}.ChooseUpdateType action=ClickStartUpload target={WorkflowTargetNames.PrimaryActionButton} semantic=StartUpload");
        await ClickUpdateStartUploadButtonAsync(dialog, attemptName, workflowLog, cancellationToken);
    }

    private async Task<LocalArchiveCardTextResult?> TryFindUpdateTypeOptionByOcrAsync(
        bool isMajorVersionUpdate,
        StablePageResult dialog,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var roi = CreateBoxRoiCapture(dialog.Capture, UpdateTypeOcrRegion, "update_type_option ROI");
        var results = (await _cardTextRecognizer.RecognizeCardsAsync(roi.Capture, cancellationToken))
            .Select(result => MapRoiResultToOriginal(result, roi.Roi))
            .Where(result => IsRequestedUpdateTypeText(result.Text, isMajorVersionUpdate))
            .OrderByDescending(result => result.Confidence)
            .ToList();

        LogWorkflow(workflowLog, 
            $"step={attemptName}.ChooseUpdateTypeOcr isMajor={isMajorVersionUpdate} " +
            $"roi=({FormatNormalizedBox(UpdateTypeOcrRegion)}) matches={results.Count} " +
            $"texts=[{string.Join("; ", results.Select(result => $"{result.Text}/{result.Confidence:F2}"))}]");

        return results.FirstOrDefault();
    }

    private static bool IsRequestedUpdateTypeText(string text, bool isMajorVersionUpdate)
    {
        var normalized = RapidOcrLocalArchiveCardTextRecognizer.NormalizeForMatch(text);
        if (normalized.Length == 0)
            return false;

        if (isMajorVersionUpdate)
        {
            return normalized.Contains("\u5927\u7248\u672c\u66f4\u65b0", StringComparison.Ordinal) ||
                   normalized.Contains("\u5927\u7248\u672c", StringComparison.Ordinal) ||
                   normalized.Contains("MAJORVERSIONUPDATE", StringComparison.Ordinal) ||
                   normalized.Contains("MAJORUPDATE", StringComparison.Ordinal) ||
                   normalized.Contains("MAJOR", StringComparison.Ordinal);
        }

        return normalized.Contains("\u5c0f\u7248\u672c\u66f4\u65b0", StringComparison.Ordinal) ||
               normalized.Contains("\u5c0f\u7248\u672c", StringComparison.Ordinal) ||
               normalized.Contains("MINORVERSIONUPDATE", StringComparison.Ordinal) ||
               normalized.Contains("MINORUPDATE", StringComparison.Ordinal) ||
               normalized.Contains("MINOR", StringComparison.Ordinal);
    }

    private async Task<StablePageResult> WaitForUpdateArchiveDialogByContextAsyncCore(
        string stepName,
        TimeSpan timeout,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + timeout;
        StablePageResult? last = null;
        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await RecognizeOnceAsync(cancellationToken);
            last = current;

            if (current.Recognition.Page == GamePage.UpdateArchiveDialog)
            {
                LogWorkflow(workflowLog, 
                    $"step={stepName} acceptedPage=UpdateArchiveDialog confidence={current.Recognition.Confidence:F2}");
                return current;
            }

            if (TryPromoteUpdateArchiveDialogByWorkflowContext(current, out var promoted, out var promotionReason))
            {
                LogWorkflow(workflowLog, 
                    $"step={stepName} acceptedPage=UpdateArchiveDialog promotedFrom={current.Recognition.Page} " +
                    $"reason={promotionReason} targetSummary=[{FormatTargets(current.Recognition.Targets)}]");
                return promoted;
            }

            if (current.Recognition.Page.IsBlocking())
                throw new InvalidOperationException(
                    $"Stage1 detected blocking page {current.Recognition.Page} while waiting for UpdateArchiveDialog.");

            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        var diagnostic = SaveDiagnostic(last, "stage1-update-archive-dialog-timeout");
        throw new TimeoutException(
            $"Timed out waiting for UpdateArchiveDialog during step '{stepName}'. " +
            $"lastPage={last?.Recognition.Page.ToString() ?? "<none>"}, diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private bool TryPromoteUpdateArchiveDialogByWorkflowContext(
        StablePageResult current,
        out StablePageResult promoted,
        out string reason)
    {
        if (IsPossibleUpdateArchiveDialogContext(current.Recognition.Page) &&
            CountTargets(current.Recognition, WorkflowTargetNames.ModalDialog, 0.25) > 0 &&
            CountTargets(current.Recognition, WorkflowTargetNames.ChoiceOption, 0.25) > 0 &&
            CountTargets(current.Recognition, WorkflowTargetNames.PrimaryActionButton, 0.25) > 0)
        {
            promoted = current with
            {
                Recognition = new PageRecognitionResult(
                    GamePage.UpdateArchiveDialog,
                    current.Recognition.Confidence,
                    current.Recognition.Source + "|workflow_context:update_archive_dialog_by_modal_buttons",
                    current.Recognition.Targets)
            };
            reason = "workflow_context_update_dialog_by_modal_buttons";
            return true;
        }

        var openCvStartUpload = _updateStartUploadButtonLocator.Locate(current.Capture, CancellationToken.None);
        if (IsPossibleUpdateArchiveDialogContext(current.Recognition.Page) &&
            openCvStartUpload.Matched &&
            openCvStartUpload.Target != null)
        {
            var targets = current.Recognition.Targets
                .Concat([openCvStartUpload.Target])
                .ToList();
            promoted = current with
            {
                Recognition = new PageRecognitionResult(
                    GamePage.UpdateArchiveDialog,
                    Math.Max(current.Recognition.Confidence, openCvStartUpload.Confidence),
                    current.Recognition.Source + "|workflow_context:update_archive_dialog_by_opencv_start_upload",
                    targets)
            };
            reason = $"workflow_context_update_dialog_by_opencv_start_upload confidence={openCvStartUpload.Confidence:F2} opencvReason=\"{openCvStartUpload.RejectedReason}\"";
            return true;
        }

        promoted = current;
        reason = "not_promoted";
        return false;
    }

    private static bool IsPossibleUpdateArchiveDialogContext(GamePage page) =>
        page == GamePage.UploadLocationDialog ||
        page == GamePage.UploadFailed ||
        page == GamePage.Unknown;

    private async Task ClickUpdateStartUploadButtonAsync(
        StablePageResult dialog,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (dialog.Recognition.Page != GamePage.UpdateArchiveDialog)
            throw new InvalidOperationException(
                $"Stage1 refuses to click update_start_upload_button on page {dialog.Recognition.Page}.");

        var target = FindPrimaryActionButton(dialog, 0.3, preferRight: true);
        if (target == null)
        {
            LogWorkflow(workflowLog, 
                $"step={attemptName}.ClickUpdateStartUpload result=failed yoloMissing=True openCvSkipped=True " +
                "reason=\"primary_action_button not detected in update dialog\"");
            _ = SaveDiagnostic(dialog, $"{attemptName.ToLowerInvariant()}-update-start-upload-button-not-found");
            throw new InvalidOperationException(
                "Stage1 update could not locate the Start Upload primary_action_button by YOLO.");
        }

        var actionAreaAccepted = IsRightSideModalActionButton(dialog.Recognition, target);
        var ocrResult = await RecognizeButtonTextAsync(dialog, target, "update_start_upload_button OCR", cancellationToken);
        var ocrConfirmed = ocrResult.Any(result => IsStartUploadActionText(result.Text));
        LogWorkflow(workflowLog, 
            $"step={attemptName}.ClickUpdateStartUpload candidate=({FormatTarget(target)}) " +
            $"rightActionArea={actionAreaAccepted} ocrConfirmed={ocrConfirmed} " +
            $"ocrTexts=[{string.Join("; ", ocrResult.Select(result => $"{result.Text}/{result.Confidence:F2}"))}]");

        if (actionAreaAccepted && ocrConfirmed)
        {
            LogWorkflow(workflowLog, $"step={attemptName}.ClickUpdateStartUpload selectedBy=yolo_ocr target={WorkflowTargetNames.PrimaryActionButton} semantic=StartUpload targetInfo=({FormatTarget(target)})");
            await ClickSpecificTargetAsync(dialog, target, cancellationToken);
            return;
        }

        _ = SaveDiagnostic(dialog, $"{attemptName.ToLowerInvariant()}-update-start-upload-button-not-confirmed");
        throw new InvalidOperationException(
            "Stage1 refused to click the update Start Upload button because YOLO/OCR did not confirm the right-side Start Upload action.");
    }

    private async Task<IReadOnlyList<LocalArchiveCardTextResult>> RecognizeButtonTextAsync(
        StablePageResult page,
        AutomationTarget target,
        string captureMethodSuffix,
        CancellationToken cancellationToken)
    {
        var buttonRegion = ExpandTargetBox(target, widthScale: 1.18, heightScale: 1.45);
        var roi = CreateBoxRoiCapture(page.Capture, buttonRegion, captureMethodSuffix);
        var raw = await _cardTextRecognizer.RecognizeCardsAsync(roi.Capture, cancellationToken);
        return ArchiveOcrTextLayout.CardLines(raw.Select(result => MapRoiResultToOriginal(result, roi.Roi)),
            ArchiveOcrTextLayout.Box(target), page.Capture.Bitmap.PixelWidth, page.Capture.Bitmap.PixelHeight)
            .OrderByDescending(result => result.Confidence)
            .ToList();
    }

    private static bool IsStartUploadActionText(string text)
    {
        var normalized = RapidOcrLocalArchiveCardTextRecognizer.NormalizeForMatch(text);
        return normalized.Contains("STARTUPLOAD", StringComparison.Ordinal) ||
               normalized.Contains("\u5f00\u59cb\u4e0a\u4f20", StringComparison.Ordinal);
    }

    private async Task<StablePageResult> ClickUploadLocationChoiceWithVerificationAsync(
        StablePageResult uploadLocationDialog,
        UploadLocationChoice choice,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var current = uploadLocationDialog;
        for (var clickAttempt = 1; clickAttempt <= 3; clickAttempt++)
        {
            current = await RefreshUploadLocationTargetAsync(
                current,
                choice,
                attemptName,
                clickAttempt,
                workflowLog,
                cancellationToken);

            if (current.Recognition.Page != GamePage.UploadLocationDialog)
                throw new InvalidOperationException(
                    $"Stage1 refuses to click upload location choice '{choice}' on page {current.Recognition.Page}.");

            await ClickUploadLocationTargetAsync(current, choice, attemptName, clickAttempt, workflowLog, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);

            current = await RecognizeOnceAsync(cancellationToken);
            LogWorkflow(workflowLog, 
                $"step={attemptName}.ClickUploadLocationChoice choice={choice} afterClickAttempt={clickAttempt} " +
                $"page={current.Recognition.Page} confidence={current.Recognition.Confidence:F2}");
            if (current.Recognition.Page != GamePage.UploadLocationDialog)
                return current;
        }

        var diagnostic = SaveDiagnostic(current, "stage1-upload-location-click-not-accepted");
        throw new InvalidOperationException(
            $"Stage1 clicked upload location choice '{choice}' 3 times but the dialog is still open. " +
            $"attemptName={attemptName}, diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<StablePageResult> RefreshUploadLocationTargetAsync(
        StablePageResult current,
        UploadLocationChoice choice,
        string attemptName,
        int clickAttempt,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        for (var refreshAttempt = 0; refreshAttempt <= 3; refreshAttempt++)
        {
            if (current.Recognition.Page != GamePage.UploadLocationDialog)
            {
                LogWorkflow(workflowLog, 
                    $"step={attemptName}.RefreshUploadLocationTarget clickAttempt={clickAttempt} " +
                    $"refreshAttempt={refreshAttempt} page={current.Recognition.Page} result=pageChanged");
                return current;
            }

            var target = FindUploadLocationChoiceTarget(current, choice);
            LogWorkflow(workflowLog, 
                $"step={attemptName}.RefreshUploadLocationTarget clickAttempt={clickAttempt} " +
                $"refreshAttempt={refreshAttempt} choice={choice} found={target != null} " +
                $"page={current.Recognition.Page} confidence={current.Recognition.Confidence:F2} " +
                $"targets=[{FormatTargets(current.Recognition.Targets)}]");

            if (target != null)
                return current;

            if (refreshAttempt == 3)
                break;

            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(350), cancellationToken);
            current = await RecognizeOnceAsync(cancellationToken);
        }

        var diagnostic = SaveDiagnostic(current, "stage1-upload-location-target-not-found");
        throw new InvalidOperationException(
            $"Stage1 upload location dialog is open, but choice '{choice}' was not found after refresh. " +
            $"attemptName={attemptName}, diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task ClickUploadLocationTargetAsync(
        StablePageResult uploadLocationDialog,
        UploadLocationChoice choice,
        string attemptName,
        int clickAttempt,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var target = FindUploadLocationChoiceTarget(uploadLocationDialog, choice);
        if (target == null)
            throw new InvalidOperationException(
                $"Required upload location choice '{choice}' was not found on {uploadLocationDialog.Recognition.Page}.");

        var x = target.CenterX;
        var y = target.CenterY;
        var targetName = choice == UploadLocationChoice.UploadNew
            ? WorkflowTargetNames.SecondaryActionButton
            : WorkflowTargetNames.PrimaryActionButton;
        LogWorkflow(workflowLog, 
            $"step={attemptName}.ClickUploadLocationTarget attempt={clickAttempt} choice={choice} target={targetName} " +
            $"detected=({FormatTarget(target)}) click=({x:F3},{y:F3}) reason=target_center");
        if (choice == UploadLocationChoice.UploadNew)
            _recordAction?.Invoke($"click:{WorkflowTargetNames.UploadNewButton}");
        else
            _recordAction?.Invoke($"click:{WorkflowTargetNames.UpdateUploadedArchiveButton}");
        _recordAction?.Invoke($"click:{targetName}:{choice}");
        _recordAction?.Invoke($"click:upload_location:{choice}:{clickAttempt}:{x:F3}:{y:F3}");
        await _input.ClickAsync(uploadLocationDialog.Capture.Window, x, y, cancellationToken);
    }

    private async Task<StablePageResult> EnsureLocalArchiveMenuOpenForRunArchiveAsync(
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Opening the save card menu");
        LogWorkflow(workflowLog, 
            $"step={attemptName}.EnsureRunArchiveCardReady runArchive={runArchive.RunArchiveDisplayName} " +
            "strategy=ocr-confirmed run archive card");

        var ready = await EnsureRunArchiveCardReadyAsync(
            runArchive,
            localArchive,
            attemptName,
            workflowLog,
            cancellationToken);

        LogWorkflow(workflowLog, 
            $"step={attemptName} runArchive={runArchive.RunArchiveDisplayName} action=OpenLocalArchiveMenu " +
            $"target={WorkflowTargetNames.LocalArchiveMoreButton} openCvMoreButtonMatched={ready.OpenCvMoreButtonMatched}");
        if (ready.MoreButton != null)
        {
            await ClickSpecificTargetAsync(ready.LocalArchive, ready.MoreButton, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException(
                $"Stage1 has neither a YOLO more button nor an OpenCV-confirmed local archive more button. runArchive={runArchive.RunArchiveDisplayName}.");
        }

        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(450), cancellationToken);
        var menu = await WaitForExpectedAsync(
            $"{attemptName}.WaitLocalArchiveMenu",
            Pages(GamePage.LocalArchiveMenu),
            ShortWait,
            stableCount: 2,
            cancellationToken);
        LogWorkflow(workflowLog, $"step={attemptName}.EnsureRunArchiveCardReady result=LocalArchiveMenu");
        return menu;
    }

    private async Task<RunArchiveCardReadyResult> EnsureRunArchiveCardReadyAsyncCore(
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Finding the target local save");
        var runArchiveDisplayName = runArchive.RunArchiveDisplayName;
        var runArchiveCode = runArchive.RunArchiveCode;
        if (localArchive.Recognition.Page != GamePage.LocalArchive)
            throw new InvalidOperationException(
                $"Stage1 expected LocalArchive before opening run archive menu. runArchive={runArchiveDisplayName}, page={localArchive.Recognition.Page}.");

        var moreButtonsBeforeHoverClear = FindTargets(localArchive.Recognition, WorkflowTargetNames.LocalArchiveMoreButton).ToList();
        var current = localArchive;
        LogWorkflow(workflowLog, 
            $"step={attemptName}.EnsureRunArchiveCardReady action=SkipFixedHoverClear " +
            $"preExistingMoreButtons={moreButtonsBeforeHoverClear.Count} reason=stage1_uses_ocr_card_match_then_rehover");

        IReadOnlyList<LocalArchiveCardCandidate> candidates = [];
        IReadOnlyList<LocalArchiveCardOcrMatch> cardMatches = [];
        var hasYoloCardCandidates = false;

        for (var scrollAttempt = 0; scrollAttempt <= LocalArchiveMaxScrollAttempts; scrollAttempt++)
        {
            candidates = _cardCandidateProvider.GetCandidates(current.Recognition, current.Capture);
            hasYoloCardCandidates = candidates.Any(candidate => !candidate.IsSynthetic);
            if (candidates.Count == 0)
            {
                SaveLocalArchiveCardDiagnostic(
                    current,
                    "stage1-local-archive-card-candidate-missing",
                    runArchiveDisplayName,
                    runArchiveCode,
                    attemptName,
                    [],
                    null,
                    false,
                    FindTargets(current.Recognition, WorkflowTargetNames.LocalArchiveMoreButton).Count > 0,
                    false,
                    null,
                    null,
                    "Stage1 could not build any safe LocalArchive card candidate for OCR.",
                    clearedHover: false,
                    clearHoverX: null,
                    clearHoverY: null,
                    ignoredPreExistingMoreButtons: moreButtonsBeforeHoverClear.Count,
                    moreButtonsBeforeHover: moreButtonsBeforeHoverClear,
                    moreButtonsAfterHover: FindTargets(current.Recognition, WorkflowTargetNames.LocalArchiveMoreButton));
                throw new InvalidOperationException(
                    $"Stage1 could not build any safe LocalArchive card candidate for OCR. runArchive={runArchiveDisplayName}, runArchiveCode={runArchiveCode}.");
            }

            cardMatches = await RecognizeCardRoisAsync(
                current,
                candidates,
                runArchiveDisplayName,
                runArchiveCode,
                attemptName,
                cancellationToken);
            var matchedCards = DeduplicateCardMatches(cardMatches
                .Where(match => match.MatchScore >= 0.7)
                .OrderByDescending(match => match.MatchScore)
                .ToList());

            if (matchedCards.Count == 0 && scrollAttempt < LocalArchiveMaxScrollAttempts)
            {
                LogWorkflow(workflowLog, 
                    $"step={attemptName}.EnsureRunArchiveCardReady result=noOcrMatch action=ScrollLocalArchive " +
                    $"direction=down amount={LocalArchiveScrollWheelDelta} scrollAttempt={scrollAttempt + 1}");
                current = await ScrollLocalArchiveAndRecognizeAsync(
                    current,
                    $"{attemptName}.EnsureRunArchiveCardReady.ScrollLocalArchive{scrollAttempt + 1}",
                    cancellationToken);
                continue;
            }

            if (matchedCards.Count != 1)
            {
                SaveLocalArchiveCardDiagnostic(
                    current,
                    matchedCards.Count == 0 ? "stage1-local-archive-card-ocr-no-match" : "stage1-local-archive-card-ocr-ambiguous",
                    runArchiveDisplayName,
                    runArchiveCode,
                    attemptName,
                    cardMatches,
                    null,
                    hasYoloCardCandidates,
                    FindTargets(current.Recognition, WorkflowTargetNames.LocalArchiveMoreButton).Count > 0,
                    true,
                    null,
                    null,
                    matchedCards.Count == 0
                        ? $"OCR did not find the current run archive code '{runArchiveCode}' in any local_archive_card ROI."
                        : $"OCR matched the current run archive code '{runArchiveCode}' in multiple card ROIs.",
                    clearedHover: false,
                    clearHoverX: null,
                    clearHoverY: null,
                    ignoredPreExistingMoreButtons: moreButtonsBeforeHoverClear.Count,
                    moreButtonsBeforeHover: moreButtonsBeforeHoverClear,
                    moreButtonsAfterHover: FindTargets(current.Recognition, WorkflowTargetNames.LocalArchiveMoreButton));
                throw new InvalidOperationException(
                    matchedCards.Count == 0
                        ? $"OCR did not uniquely match the current run archive code in any LocalArchive card candidate. runArchive={runArchiveDisplayName}, runArchiveCode={runArchiveCode}, ocrEngine={_cardTextRecognizer.EngineInfo}."
                        : $"Stage1 found multiple LocalArchive cards matching runArchiveCode={runArchiveCode}; refusing to choose one.");
            }

            var selectedMatch = matchedCards[0];
            var card = selectedMatch.Candidate.CardBox;
            var ignoredPreExistingMoreButtons = moreButtonsBeforeHoverClear.Count + FindTargets(current.Recognition, WorkflowTargetNames.LocalArchiveMoreButton).Count;
            LogWorkflow(workflowLog, 
                $"step={attemptName}.EnsureRunArchiveCardReady runArchive={runArchiveDisplayName} runArchiveCode={runArchiveCode} " +
                $"selectedBy=ocr_card_match score={selectedMatch.MatchScore:F2} preExistingMoreButtons={ignoredPreExistingMoreButtons} " +
                "action=IgnorePreExistingMoreButtons reason=requires_rehover_bound_to_ocr_confirmed_card");

            var hoverPoint = GetLocalArchiveCardTextHoverPoint(selectedMatch);
            var hoverX = hoverPoint.X;
            var hoverY = hoverPoint.Y;
            LogWorkflow(workflowLog, 
                $"step={attemptName}.EnsureRunArchiveCardReady runArchive={runArchiveDisplayName} runArchiveCode={runArchiveCode} " +
                $"selectedBy=ocr_card_match score={selectedMatch.MatchScore:F2} action=HoverOcrConfirmedCardText x={hoverX:F3} y={hoverY:F3} source={hoverPoint.Source}");
            _recordAction?.Invoke("move:local_archive_card_action_area");
            await _input.MoveAsync(current.Capture.Window, hoverX, hoverY, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(400), cancellationToken);

            var refreshed = await WaitForExpectedAsync(
                $"{attemptName}.RefreshLocalArchiveAfterCardHover",
                Pages(GamePage.LocalArchive),
                ShortWait,
                stableCount: 1,
                cancellationToken);

            var moreButtonsAfterHover = FindTargets(refreshed.Recognition, WorkflowTargetNames.LocalArchiveMoreButton).ToList();
            var refreshedMoreButton = FindMoreButtonForCard(refreshed.Recognition, selectedMatch.Candidate);
            if (refreshedMoreButton != null)
            {
                LogWorkflow(workflowLog, 
                    $"step={attemptName}.EnsureRunArchiveCardReady runArchive={runArchiveDisplayName} runArchiveCode={runArchiveCode} " +
                    $"result=more_button_visible_after_hover selectedBy=ocr_card_match yoloMoreButtonMatched=True " +
                    $"selectedMoreButton=({FormatTarget(refreshedMoreButton)})");
                return new RunArchiveCardReadyResult(
                    refreshed,
                    selectedMatch.Candidate,
                    refreshedMoreButton,
                    false,
                    selectedMatch,
                    cardMatches,
                    moreButtonsBeforeHoverClear,
                    moreButtonsAfterHover,
                    ignoredPreExistingMoreButtons,
                    "Matched YOLO local_archive_more_button inside OCR-confirmed card action area.",
                    hasYoloCardCandidates,
                    OpenCvMoreButtonAttempted: false,
                    OpenCvMoreButtonResult: null);
            }

            var moreButtonSelectionReason = BuildMoreButtonSelectionFailureReason(moreButtonsAfterHover, card);
            var openCvResult = _moreButtonLocator.Locate(refreshed.Capture, selectedMatch.Candidate, cancellationToken);
            if (openCvResult.Matched && openCvResult.Target != null && IsInsideCardLeftBottomActionArea(openCvResult.Target, selectedMatch.Candidate))
            {
                LogWorkflow(workflowLog, 
                    $"step={attemptName}.EnsureRunArchiveCardReady runArchive={runArchiveDisplayName} runArchiveCode={runArchiveCode} " +
                    "selectedBy=ocr_card_match yoloMoreButtonMatched=False openCvMoreButtonAttempted=True openCvMoreButtonMatched=True " +
                    $"openCvConfidence={openCvResult.Confidence:F2} openCvClickX={openCvResult.Target.CenterX:F3} openCvClickY={openCvResult.Target.CenterY:F3} " +
                    $"openCvSearchRoi=({FormatNormalizedBox(openCvResult.SearchRoi)}) rejectedYoloReason=\"{moreButtonSelectionReason}\"");
                return new RunArchiveCardReadyResult(
                    refreshed,
                    selectedMatch.Candidate,
                    openCvResult.Target,
                    true,
                    selectedMatch,
                    cardMatches,
                    moreButtonsBeforeHoverClear,
                    moreButtonsAfterHover,
                    ignoredPreExistingMoreButtons,
                    "Matched OpenCV local_archive_more_button inside OCR-confirmed card left-bottom ROI.",
                    hasYoloCardCandidates,
                    OpenCvMoreButtonAttempted: true,
                    OpenCvMoreButtonResult: openCvResult);
            }

            var openCvFailureReason = openCvResult.Matched
                ? "OpenCV matched a candidate outside the OCR-confirmed card left-bottom action area; likely right-side Edit area or wrong card."
                : openCvResult.RejectedReason ?? "OpenCV local archive more button locator did not match.";
            LogWorkflow(workflowLog, 
                $"step={attemptName}.EnsureRunArchiveCardReady runArchive={runArchiveDisplayName} runArchiveCode={runArchiveCode} " +
                "selectedBy=ocr_card_match yoloMoreButtonMatched=False openCvMoreButtonAttempted=True openCvMoreButtonMatched=False " +
                $"openCvConfidence={openCvResult.Confidence:F2} openCvSearchRoi=({FormatNormalizedBox(openCvResult.SearchRoi)}) " +
                $"moreButtonSelectionReason=\"{moreButtonSelectionReason}\" openCvRejectedReason=\"{openCvFailureReason}\"");
            SaveLocalArchiveCardDiagnostic(
                refreshed,
                "stage1-local-archive-more-button-missing",
                runArchiveDisplayName,
                runArchiveCode,
                attemptName,
                cardMatches,
                selectedMatch,
                hasYoloCardCandidates,
                moreButtonsAfterHover.Count > 0,
                true,
                hoverX,
                hoverY,
                $"YOLO did not find a card-bound local_archive_more_button and OpenCV local ROI fallback failed. {openCvFailureReason}",
                clearedHover: false,
                clearHoverX: null,
                clearHoverY: null,
                ignoredPreExistingMoreButtons: ignoredPreExistingMoreButtons,
                moreButtonSelectionReason: moreButtonSelectionReason,
                selectedMoreButton: null,
                moreButtonsBeforeHover: moreButtonsBeforeHoverClear,
                moreButtonsAfterHover: moreButtonsAfterHover,
                usedCardGeometryFallbackClick: false,
                yoloMoreButtonMatched: false,
                rejectedMoreButtonCandidates: moreButtonsAfterHover,
                openCvMoreButtonAttempted: true,
                openCvMoreButtonMatched: false,
                openCvMoreButtonResult: openCvResult);
            throw new InvalidOperationException(
                $"Stage1 could not locate a safe local_archive_more_button for OCR-confirmed runArchive={runArchiveDisplayName}, runArchiveCode={runArchiveCode}. " +
                $"YOLO reason: {moreButtonSelectionReason}; OpenCV reason: {openCvFailureReason}");
        }

        throw new InvalidOperationException(
            $"Stage1 failed to locate runArchive={runArchiveDisplayName} after LocalArchive scroll attempts. runArchiveCode={runArchiveCode}.");
    }

    private async Task<StablePageResult> CloseFailureAndWaitForSandboxReadyAsync(
        UploadRunArchive runArchive,
        StablePageResult uploadFailed,
        bool sandboxRunningBeforeFirstUpload,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        LogWorkflow(workflowLog, $"step=FirstUpload result=UploadFailed action=CloseFailure target={WorkflowTargetNames.SecondaryActionButton} semantic=CloseFailure");
        var closeTarget = FindSecondaryActionButton(uploadFailed, 0.3);
        if (closeTarget == null)
            throw new InvalidOperationException("Stage1 detected UploadFailed but could not locate a close/secondary button.");

        _recordAction?.Invoke($"click:{WorkflowTargetNames.UploadFailedCloseButton}");
        await ClickSpecificTargetAsync(uploadFailed, closeTarget, cancellationToken);
        var localArchive = await WaitForExpectedAsync(
            "CloseUploadFailed",
            Pages(GamePage.LocalArchive),
            MediumWait,
            stableCount: 1,
            cancellationToken);

        return await WaitForSandboxReadyAsync(
            runArchive,
            localArchive,
            sandboxRunningBeforeFirstUpload,
            workflowLog,
            cancellationToken);
    }

    private async Task<StablePageResult> WaitForSandboxReadyAsync(
        UploadRunArchive runArchive,
        StablePageResult localArchive,
        bool sandboxRunningBeforeFirstUpload,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Waiting for the sandbox to start");
        LogWorkflow(workflowLog, 
            $"step=WaitForSandboxReady runArchive={runArchive.RunArchiveDisplayName} " +
            $"maxSeconds={SandboxWait.TotalSeconds:0} sandboxWasAlreadyRunningBeforeFirstUpload={sandboxRunningBeforeFirstUpload}");
        var deadline = _clock.UtcNow + SandboxWait;
        var pageReturned = localArchive.Recognition.Page == GamePage.LocalArchive;
        var sandboxSeen = _sandboxWatcher.IsSandboxRunning();
        var sandboxResult = new SandboxProcessWaitResult(false, sandboxSeen, TimeSpan.Zero);
        StablePageResult? pageAfterCooldown = null;
        bool? sandboxRunningAfterCooldown = null;
        SandboxWindowWaitResult? windowResult = null;
        string? foregroundBeforeRefocus = null;
        string? foregroundAfterRefocus = null;
        GamePage? pageAfterRefocus = null;

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!pageReturned)
            {
                var current = await RecognizeOnceAsync(cancellationToken);
                var decision = _pageGuard.Decide(
                    current.Recognition,
                    new HashSet<GamePage> { GamePage.LocalArchive },
                    "WaitForSandboxReady");
                pageReturned = decision.IsAccepted && decision.IsExpected;
                localArchive = current;
            }

            if (pageReturned)
            {
                LogWorkflow(workflowLog, 
                    $"step=WaitForSandboxReady pageReturned=LocalArchive confidence={localArchive.Recognition.Confidence:F2}");
                var remaining = deadline - _clock.UtcNow;
                sandboxResult = await _sandboxWatcher.WaitForStableAsync(
                    SandboxProcessStableDuration,
                    remaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : remaining,
                    cancellationToken);
                sandboxSeen = sandboxSeen || sandboxResult.WasSeen;
                if (sandboxResult.IsStable)
                {
                    LogWorkflow(workflowLog, 
                        $"step=WaitForSandboxReady processStable stableSeconds={sandboxResult.StableDuration.TotalSeconds:F1} " +
                        $"cooldownSeconds={SandboxReadyCooldown.TotalSeconds:F0}");
                    var cooldownResult = await WaitForSandboxCooldownAsync(
                        runArchive,
                        localArchive,
                        deadline,
                        sandboxRunningBeforeFirstUpload,
                        sandboxResult,
                        workflowLog,
                        cancellationToken);
                    pageAfterCooldown = cooldownResult.Page;
                    sandboxRunningAfterCooldown = _sandboxWatcher.IsSandboxRunning();
                    LogWorkflow(workflowLog, 
                        $"step=WaitForSandboxReady pageAfterCooldown={pageAfterCooldown.Recognition.Page} " +
                        $"sandboxRunningAfterCooldown={sandboxRunningAfterCooldown} " +
                        $"sawSandboxWindowDuringCooldown={cooldownResult.SawSandboxWindow}");
                    var readyAfterRefocus = await WaitForSandboxWindowAndRefocusAsync(
                        runArchive,
                        pageAfterCooldown,
                        deadline,
                        sandboxRunningBeforeFirstUpload,
                        sandboxSeen || sandboxResult.WasSeen,
                        cooldownResult.SawSandboxWindow,
                        sandboxResult,
                        workflowLog,
                        cancellationToken);
                    return readyAfterRefocus.Page;
                }

                break;
            }

            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        var diagnostic = SaveDiagnostic(localArchive, "stage1-sandbox-timeout");
        throw new TimeoutException(
            $"Stage1 timed out waiting for sandbox readiness. runArchive={runArchive.RunArchiveDisplayName}, " +
            $"sandboxWasAlreadyRunningBeforeFirstUpload={sandboxRunningBeforeFirstUpload}, " +
            $"pageReturned={pageReturned}, lastPage={localArchive.Recognition.Page}, confidence={localArchive.Recognition.Confidence:F2}, " +
            $"sandboxSeen={sandboxSeen || sandboxResult.WasSeen}, sandboxStableDuration={sandboxResult.StableDuration.TotalSeconds:F1}s, " +
            $"cooldownSeconds={SandboxReadyCooldown.TotalSeconds:F0}, pageAfterCooldown={pageAfterCooldown?.Recognition.Page.ToString() ?? "<none>"}, " +
            $"sandboxRunningAfterCooldown={sandboxRunningAfterCooldown?.ToString() ?? "<unknown>"}, " +
            $"windowFound={windowResult?.Found.ToString() ?? "<not-started>"}, windowDiagnostic={windowResult?.Diagnostic ?? "<none>"}, " +
            $"foregroundBeforeRefocus={foregroundBeforeRefocus ?? "<none>"}, foregroundAfterRefocus={foregroundAfterRefocus ?? "<none>"}, " +
            $"pageAfterRefocus={pageAfterRefocus?.ToString() ?? "<none>"}, diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<SandboxReadyRefocusResult> WaitForSandboxWindowAndRefocusAsync(
        UploadRunArchive runArchive,
        StablePageResult pageAfterCooldown,
        DateTimeOffset deadline,
        bool sandboxRunningBeforeFirstUpload,
        bool sandboxSeen,
        bool sawSandboxWindowDuringCooldown,
        SandboxProcessWaitResult sandboxResult,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var remaining = deadline - _clock.UtcNow;
        var windowTimeout = remaining <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(1)
            : remaining < SandboxWindowWait ? remaining : SandboxWindowWait;

        LogWorkflow(workflowLog, 
            $"step=WaitForSandboxReady waitWindow timeoutSeconds={windowTimeout.TotalSeconds:F1}");
        var windowResult = await _sandboxWatcher.WaitForWindowAsync(windowTimeout, cancellationToken);
        if (!windowResult.Found)
        {
            var diagnostic = SaveDiagnostic(pageAfterCooldown, "stage1-sandbox-window-timeout");
            throw new TimeoutException(
                $"Stage1 timed out waiting for sandbox window. runArchive={runArchive.RunArchiveDisplayName}, " +
                $"sandboxRunningBeforeFirstUpload={sandboxRunningBeforeFirstUpload}, sandboxSeen={sandboxSeen || windowResult.WasProcessSeen}, " +
                $"stableDuration={sandboxResult.StableDuration.TotalSeconds:F1}s, cooldownSeconds={SandboxReadyCooldown.TotalSeconds:F0}, " +
                $"sawSandboxWindowDuringCooldown={sawSandboxWindowDuringCooldown}, " +
                $"windowFound=False, windowDiagnostic={windowResult.Diagnostic}, foregroundBeforeRefocus=<not-started>, " +
                $"foregroundAfterRefocus=<not-started>, pageAfterRefocus=<none>, diagnostic={diagnostic ?? "<not-saved>"}.");
        }

        LogWorkflow(workflowLog, 
            $"step=WaitForSandboxReady waitWindow result=found processId={windowResult.Window!.ProcessId} " +
            $"title=\"{windowResult.Window.WindowTitle}\" size={windowResult.Window.WindowWidth}x{windowResult.Window.WindowHeight}");

        var foregroundBeforeRefocus = _activation.GetForegroundWindowInfo(pageAfterCooldown.Capture.Window).ToDiagnosticString();
        LogWorkflow(workflowLog, $"step=WaitForSandboxReady refocusGame before=({foregroundBeforeRefocus})");
        _activation.RestoreAndActivate(pageAfterCooldown.Capture.Window);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
        var foregroundAfterRefocus = _activation.GetForegroundWindowInfo(pageAfterCooldown.Capture.Window).ToDiagnosticString();
        LogWorkflow(workflowLog, $"step=WaitForSandboxReady refocusGame after=({foregroundAfterRefocus})");

        var confirmed = await WaitForLocalArchiveAfterSandboxRefocusAsync(
            runArchive,
            sandboxRunningBeforeFirstUpload,
            sandboxSeen || windowResult.WasProcessSeen,
            sawSandboxWindowDuringCooldown,
            sandboxResult,
            windowResult,
            foregroundBeforeRefocus,
            foregroundAfterRefocus,
            workflowLog,
            cancellationToken);

        return new SandboxReadyRefocusResult(confirmed, windowResult, foregroundBeforeRefocus, foregroundAfterRefocus);
    }

    private async Task<StablePageResult> WaitForLocalArchiveAfterSandboxRefocusAsync(
        UploadRunArchive runArchive,
        bool sandboxRunningBeforeFirstUpload,
        bool sandboxSeen,
        bool sawSandboxWindowDuringCooldown,
        SandboxProcessWaitResult sandboxResult,
        SandboxWindowWaitResult windowResult,
        string foregroundBeforeRefocus,
        string foregroundAfterRefocus,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + TimeSpan.FromSeconds(5);
        StablePageResult? latest = null;
        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            latest = await RecognizeOnceAsync(cancellationToken);
            LogWorkflow(workflowLog, 
                $"step=WaitForSandboxReady confirmLocalArchiveAfterRefocus page={latest.Recognition.Page} " +
                $"confidence={latest.Recognition.Confidence:F2}");

            if (latest.Recognition.Page == GamePage.LocalArchive)
                return latest;

            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        latest ??= await RecognizeOnceAsync(cancellationToken);
        var diagnostic = SaveDiagnostic(latest, "stage1-sandbox-refocus-confirm-failed");
        throw new TimeoutException(
            $"Stage1 could not confirm LocalArchive after sandbox window detection and game refocus. " +
            $"runArchive={runArchive.RunArchiveDisplayName}, sandboxRunningBeforeFirstUpload={sandboxRunningBeforeFirstUpload}, " +
            $"sandboxSeen={sandboxSeen}, sawSandboxWindowDuringCooldown={sawSandboxWindowDuringCooldown}, " +
            $"stableDuration={sandboxResult.StableDuration.TotalSeconds:F1}s, cooldownSeconds={SandboxReadyCooldown.TotalSeconds:F0}, " +
            $"windowFound=True, windowDiagnostic={windowResult.Diagnostic}, " +
            $"foregroundBeforeRefocus={foregroundBeforeRefocus}, foregroundAfterRefocus={foregroundAfterRefocus}, " +
            $"pageAfterRefocus={latest.Recognition.Page}, diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<SandboxCooldownResult> WaitForSandboxCooldownAsync(
        UploadRunArchive runArchive,
        StablePageResult cooldownStartPage,
        DateTimeOffset deadline,
        bool sandboxRunningBeforeFirstUpload,
        SandboxProcessWaitResult sandboxResult,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var cooldownDeadline = _clock.UtcNow + SandboxReadyCooldown;
        var latestObservation = cooldownStartPage;
        var sandboxSeen = sandboxResult.WasSeen;
        var sawSandboxWindow = false;

        while (_clock.UtcNow < cooldownDeadline && _clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sandboxRunning = _sandboxWatcher.IsSandboxRunning();
            sandboxSeen = sandboxSeen || sandboxRunning;
            var sandboxWindow = _sandboxWatcher.FindSandboxWindow();
            sawSandboxWindow = sawSandboxWindow || sandboxWindow != null;
            var foreground = SafeGetForegroundDiagnostic(cooldownStartPage?.Capture?.Window);
            latestObservation = await RecognizeOnceAsync(cancellationToken);

            LogWorkflow(workflowLog, 
                $"step=WaitForSandboxReady cooldownCheck sandboxRunning={sandboxRunning} " +
                $"sandboxWindowFound={sandboxWindow != null} page={latestObservation.Recognition.Page} " +
                $"confidence={latestObservation.Recognition.Confidence:F2} foreground=({foreground})");

            if (latestObservation.Recognition.Page == GamePage.Unknown &&
                foreground.Contains("BeyondEditor", StringComparison.OrdinalIgnoreCase))
            {
                LogWorkflow(workflowLog, 
                    "step=WaitForSandboxReady cooldown observed sandbox foreground; page recognition skipped/ignored.");
            }

            if (!sandboxRunning)
            {
                var diagnostic = SaveDiagnostic(latestObservation, "stage1-sandbox-process-exited-during-cooldown");
                throw new TimeoutException(
                    $"Stage1 sandbox process exited during readiness cooldown. runArchive={runArchive.RunArchiveDisplayName}, " +
                    $"sandboxWasAlreadyRunningBeforeFirstUpload={sandboxRunningBeforeFirstUpload}, sandboxSeen={sandboxSeen}, " +
                    $"sandboxStableDuration={sandboxResult.StableDuration.TotalSeconds:F1}s, cooldownSeconds={SandboxReadyCooldown.TotalSeconds:F0}, " +
                    $"pageAfterCooldown={latestObservation.Recognition.Page}, sandboxRunningAfterCooldown=False, " +
                    $"sawSandboxWindowDuringCooldown={sawSandboxWindow}, diagnostic={diagnostic ?? "<not-saved>"}.");
            }

            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        if (_clock.UtcNow < cooldownDeadline)
        {
            var diagnostic = SaveDiagnostic(latestObservation, "stage1-sandbox-cooldown-timeout");
            throw new TimeoutException(
                $"Stage1 sandbox readiness cooldown did not complete before the total timeout. runArchive={runArchive.RunArchiveDisplayName}, " +
                $"sandboxWasAlreadyRunningBeforeFirstUpload={sandboxRunningBeforeFirstUpload}, sandboxSeen={sandboxSeen}, " +
                $"sandboxStableDuration={sandboxResult.StableDuration.TotalSeconds:F1}s, cooldownSeconds={SandboxReadyCooldown.TotalSeconds:F0}, " +
                $"pageAfterCooldown={latestObservation.Recognition.Page}, confidence={latestObservation.Recognition.Confidence:F2}, " +
                $"sandboxRunningAfterCooldown={_sandboxWatcher.IsSandboxRunning()}, sawSandboxWindowDuringCooldown={sawSandboxWindow}, " +
                $"diagnostic={diagnostic ?? "<not-saved>"}.");
        }

        var finalSandboxRunning = _sandboxWatcher.IsSandboxRunning();
        LogWorkflow(workflowLog, 
            $"step=WaitForSandboxReady cooldownFinal page={latestObservation.Recognition.Page} " +
            $"confidence={latestObservation.Recognition.Confidence:F2} sandboxRunning={finalSandboxRunning} " +
            $"sawSandboxWindowDuringCooldown={sawSandboxWindow}");

        if (!finalSandboxRunning)
        {
            var diagnostic = SaveDiagnostic(latestObservation, "stage1-sandbox-not-ready-after-cooldown");
            throw new TimeoutException(
                $"Stage1 sandbox was not ready after cooldown. runArchive={runArchive.RunArchiveDisplayName}, " +
                $"sandboxWasAlreadyRunningBeforeFirstUpload={sandboxRunningBeforeFirstUpload}, sandboxSeen={sandboxSeen || finalSandboxRunning}, " +
                $"sandboxStableDuration={sandboxResult.StableDuration.TotalSeconds:F1}s, cooldownSeconds={SandboxReadyCooldown.TotalSeconds:F0}, " +
                $"pageAfterCooldown={latestObservation.Recognition.Page}, confidence={latestObservation.Recognition.Confidence:F2}, " +
                $"sandboxRunningAfterCooldown={finalSandboxRunning}, sawSandboxWindowDuringCooldown={sawSandboxWindow}, " +
                $"diagnostic={diagnostic ?? "<not-saved>"}.");
        }

        return new SandboxCooldownResult(latestObservation, sawSandboxWindow);
    }

    private async Task<StablePageResult> DismissUploadedArchivePromptIfPresentAsync(
        StablePageResult uploadedArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (uploadedArchive.Recognition.Page == GamePage.WorldMain)
        {
            LogWorkflow(workflowLog, "step=DismissUploadedArchivePrompt result=AlreadyWorldMain skip");
            return uploadedArchive;
        }

        if (uploadedArchive.Recognition.Page != GamePage.UploadedArchive)
            uploadedArchive = await WaitForPageEvidenceAsync(uploadedArchive, Pages(GamePage.UploadedArchive), "Edit.PageRecovery", ShortWait, cancellationToken);

        var current = uploadedArchive;
        var deadline = _clock.UtcNow + UploadedArchivePromptDismissTimeout;
        var absentFrames = 0;
        var attempts = 0;
        for (var attempt = 1; attempt <= UploadedArchiveDismissMaxAttempts && _clock.UtcNow < deadline; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DelayWithTimingAsync(UploadedArchivePromptPreClickSettleDelay, cancellationToken);
            if (_clock.UtcNow >= deadline) break;
            attempts = attempt;
            current = await RecognizeOnceAsync(cancellationToken);
            if (current.Recognition.Page == GamePage.WorldMain) return current;
            if (current.Recognition.Page.IsBlocking())
                throw new InvalidOperationException($"Blocking page while dismissing submission prompt: {current.Recognition.Page}.");
            if (current.Recognition.Page != GamePage.UploadedArchive)
            {
                absentFrames = 0;
                LogWorkflow(workflowLog, $"step=DismissUploadedArchivePrompt action=WaitPage attempt={attempt} page={current.Recognition.Page}");
                continue;
            }

            var prompt = await ScanUploadedArchiveSubmissionPromptAsync(current, cancellationToken);
            LogWorkflow(workflowLog,
                $"step=DismissUploadedArchivePrompt scan attempt={attempt} laterFound={prompt.LaterTarget != null} " +
                $"forbiddenPrimary={prompt.HasForbiddenPrimaryAction} promptEvidence={prompt.HasPromptEvidence} " +
                $"remainingMs={Math.Max(0, (deadline - _clock.UtcNow).TotalMilliseconds):0} ocr=\"{FormatOcrTexts(prompt.OcrTexts)}\"");
            if (_clock.UtcNow >= deadline) break;
            if (!prompt.HasPromptEvidence)
            {
                if (++absentFrames >= 2) return current;
                continue;
            }
            absentFrames = 0;
            if (prompt.LaterTarget == null)
            {
                // A temporary notification can cover the button. Refresh instead
                // of treating one unreadable frame as failure or guessing a click.
                LogWorkflow(workflowLog, $"step=DismissUploadedArchivePrompt action=WaitForLater attempt={attempt} reason=button_obscured_or_unreadable");
                continue;
            }

            LogWorkflow(workflowLog, $"step=DismissUploadedArchivePrompt action=ClickLaterViaOcrSecondaryButton attempt={attempt} target=({FormatTarget(prompt.LaterTarget)})");
            await ClickSpecificTargetAsync(current, prompt.LaterTarget, cancellationToken);
            await DelayWithTimingAsync(UploadedArchivePromptPostClickSettleDelay, cancellationToken);
            if (_clock.UtcNow >= deadline) break;
            current = await RecognizeOnceAsync(cancellationToken);
            if (current.Recognition.Page == GamePage.WorldMain) return current;
            if (current.Recognition.Page.IsBlocking())
                throw new InvalidOperationException($"Blocking page after dismissing submission prompt: {current.Recognition.Page}.");
            if (current.Recognition.Page != GamePage.UploadedArchive) continue;
            var after = await ScanUploadedArchiveSubmissionPromptAsync(current, cancellationToken);
            LogWorkflow(workflowLog, $"step=DismissUploadedArchivePrompt verifyAfterClick attempt={attempt} stillVisible={after.HasPromptEvidence} laterFound={after.LaterTarget != null}");
            // Recheck on the next fresh frame, even if this first post-click frame looks clear.
            absentFrames = after.HasPromptEvidence ? 0 : 1;
        }

        var diagnostic = SaveDiagnostic(current, "stage1-uploaded-archive-dismiss-prompt-still-visible");
        throw new TimeoutException(
            $"Stage1 could not confirm dismissal of the UploadedArchive submission prompt. " +
            $"attempts={attempts}, timeoutSeconds={UploadedArchivePromptDismissTimeout.TotalSeconds:0}, " +
            $"diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<UploadedArchivePromptScan> ScanUploadedArchiveSubmissionPromptAsync(
        StablePageResult current,
        CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        AutomationTarget? laterTarget = null;
        foreach (var target in FindTargets(current.Recognition, WorkflowTargetNames.SecondaryActionButton))
        {
            var rawTexts = await _workflowOcr.RecognizeTargetAsync(current.Capture, target, cancellationToken);
            var ocrTexts = ArchiveOcrTextLayout.Lines(rawTexts, ArchiveOcrTextLayout.Box(target),
                current.Capture.Bitmap.PixelWidth, current.Capture.Bitmap.PixelHeight);
            allOcrTexts.AddRange(ocrTexts);
            if (OcrTextsContainAny(ocrTexts, ["LATER", "\u6682\u4e0d\u524d\u5f80", "\u7a0d\u540e"]))
            {
                laterTarget = target;
                break;
            }
        }

        var hasForbiddenPrimary = false;
        foreach (var target in FindTargets(current.Recognition, WorkflowTargetNames.PrimaryActionButton))
        {
            var rawTexts = await _workflowOcr.RecognizeTargetAsync(current.Capture, target, cancellationToken);
            var ocrTexts = ArchiveOcrTextLayout.Lines(rawTexts, ArchiveOcrTextLayout.Box(target),
                current.Capture.Bitmap.PixelWidth, current.Capture.Bitmap.PixelHeight);
            allOcrTexts.AddRange(ocrTexts);
            if (OcrTextsContainForbiddenSubmitAction(ocrTexts))
                hasForbiddenPrimary = true;
        }

        var hasPromptEvidence =
            laterTarget != null ||
            hasForbiddenPrimary ||
            FindTargets(current.Recognition, WorkflowTargetNames.ModalDialog).Any(target =>
                target.Width >= 0.18 &&
                target.Height >= 0.12 &&
                target.CenterY is > 0.35 and < 0.75);

        return new UploadedArchivePromptScan(laterTarget, hasForbiddenPrimary, hasPromptEvidence, allOcrTexts);
    }

    private async Task<StablePageResult> WaitForUploadedArchiveStableAfterDismissAsyncCore(
        StablePageResult knownUploadedArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (knownUploadedArchive.Recognition.Page != GamePage.UploadedArchive)
            knownUploadedArchive = await WaitForPageEvidenceAsync(knownUploadedArchive, Pages(GamePage.UploadedArchive), "Edit.PageRecovery", ShortWait, cancellationToken);

        await DelayWithTimingAsync(UploadedArchiveDismissSettleDelay, cancellationToken);
        var deadline = _clock.UtcNow + TimeSpan.FromSeconds(10);
        var consecutive = 1;
        var last = knownUploadedArchive;
        while (_clock.UtcNow < deadline)
        {
            var current = await RecognizeOnceAsync(cancellationToken);
            last = current;
            if (current.Recognition.Page == GamePage.WorldMain)
            {
                LogWorkflow(workflowLog, 
                    $"step=DismissUploadedArchivePrompt result=AlreadyWorldMainAfterDismiss settleDelayMs={UploadedArchiveDismissSettleDelay.TotalMilliseconds:F0}");
                return current;
            }

            if (current.Recognition.Page == GamePage.UploadedArchive)
            {
                consecutive++;
                if (consecutive >= UploadedArchiveStableAfterDismissCount)
                {
                    LogWorkflow(workflowLog, 
                        $"step=DismissUploadedArchivePrompt result=UploadedArchiveStableAfterDismiss stableCount={UploadedArchiveStableAfterDismissCount} settleDelayMs={UploadedArchiveDismissSettleDelay.TotalMilliseconds:F0} source=direct");
                    return current;
                }
            }
            else if (TryPromoteUploadedArchiveByCardContext(current, out var promoted, out var reason))
            {
                consecutive++;
                if (consecutive >= UploadedArchiveStableAfterDismissCount)
                {
                    LogWorkflow(workflowLog, 
                        $"step=DismissUploadedArchivePrompt result=UploadedArchiveStableAfterDismiss stableCount={UploadedArchiveStableAfterDismissCount} settleDelayMs={UploadedArchiveDismissSettleDelay.TotalMilliseconds:F0} source=promoted reason={reason}");
                    return promoted;
                }
            }
            else if (current.Recognition.Page.IsBlocking())
            {
                var diagnostic = SaveDiagnostic(current, "stage1-uploaded-archive-stable-after-dismiss-blocked");
                throw new InvalidOperationException(
                    $"Blocking page appeared while waiting for UploadedArchive after dismiss prompt. " +
                    $"page={current.Recognition.Page}, confidence={current.Recognition.Confidence:F2}, diagnostic={diagnostic ?? "<not-saved>"}.");
            }
            else
            {
                consecutive = 0;
                LogWorkflow(workflowLog, 
                    $"step=DismissUploadedArchivePrompt waitStable ignoredPage={current.Recognition.Page} confidence={current.Recognition.Confidence:F2}");
            }

            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        var timeoutDiagnostic = SaveDiagnostic(last, "stage1-uploaded-archive-stable-after-dismiss-timeout");
        throw new TimeoutException(
            $"Timed out waiting for UploadedArchive stability after dismiss prompt. " +
            $"lastPage={last.Recognition.Page}, confidence={last.Recognition.Confidence:F2}, diagnostic={timeoutDiagnostic ?? "<not-saved>"}.");
    }

    private async Task<StablePageResult> EditPreviewInfoAsync(
        UploadRequest request,
        StablePageResult uploadedArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Opening preview details");
        if (!ShouldEditPreviewInfo(request.Archive))
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo result=Skipped reason=no_preview_metadata_fields");
            return uploadedArchive;
        }

        if (uploadedArchive.Recognition.Page != GamePage.UploadedArchive)
            uploadedArchive = await WaitForPageEvidenceAsync(uploadedArchive, Pages(GamePage.UploadedArchive), "Edit.PageRecovery", ShortWait, cancellationToken);

        LogWorkflow(workflowLog, $"step=EditPreviewInfo action=OpenPreviewEditor isUpdate={request.IsUpdate}");
        var entryResult = await FindPreviewInfoEntryWithRetryAsync(uploadedArchive, workflowLog, cancellationToken);
        uploadedArchive = entryResult.Page;
        var previewEntry = entryResult.Entry;
        if (previewEntry.Icon == null)
        {
            SavePreviewInfoDiagnostic(
                uploadedArchive,
                "stage1-uploaded-preview-info-entry-missing",
                request,
                "PreviewInfoEntry",
                "OCR info_panel_row as Preview Info, then use a row icon; or use the unique text_edit_icon inside an info_panel_row",
                previewEntry.OcrTexts,
                workflowLog,
                "UploadedArchive Preview Info row/icon was not confirmed.");
            throw new InvalidOperationException("UploadedArchive Preview Info row/icon was not confirmed.");
        }

        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo action=OpenPreviewEditor selectedBy={previewEntry.SelectedBy} row=({FormatTarget(previewEntry.Row!)}) " +
            $"icon=({FormatTarget(previewEntry.Icon)})");
        await ClickSpecificTargetAsync(uploadedArchive, previewEntry.Icon, cancellationToken);
        var preview = await WaitForExpectedAsync(
            "EditPreviewInfo.WaitPreviewEditor",
            Pages(GamePage.PreviewEditor),
            MediumWait,
            stableCount: 1,
            cancellationToken);

        preview = await FillPreviewLevelNameAsync(
            request,
            preview,
            request.Archive.LevelName,
            workflowLog,
            cancellationToken);

        preview = await UploadPreviewCoverIfNeededAsync(request, preview, workflowLog, cancellationToken);

        preview = await FillPreviewEarlyAccessIfNeededAsync(request, preview, workflowLog, cancellationToken);

        if (request.Archive.IsEarlyAccess)
        {
            preview = await FillPreviewEarlyAccessDescriptionIfNeededAsync(
                request,
                preview,
                workflowLog,
                cancellationToken);
        }

        preview = await FillPreviewTextFieldAsync(
            request,
            preview,
            "GameplayDescription",
            request.Archive.GameDescription,
            [WorkflowTargetNames.PreviewGameplayDescription],
            WorkflowTargetNames.TextInputArea,
            ["GAMEPLAYDETAILS", "GAMEPLAY", "玩法说明", "请输入玩法说明", "\u73a9\u6cd5"],
            workflowLog,
            cancellationToken);

        preview = await FillPreviewTextFieldAsync(
            request,
            preview,
            "LevelDescription",
            request.Archive.LevelDescription,
            [WorkflowTargetNames.PreviewLevelDescription],
            WorkflowTargetNames.TextInputArea,
            ["STAGEDESCRIPTION", "LEVELDESCRIPTION", "关卡描述", "请输入关卡描述"],
            workflowLog,
            cancellationToken);

        if (!request.IsUpdate)
            preview = await FillPreviewTagsIfNeededAsync(request, preview, workflowLog, cancellationToken);
        else
            LogWorkflow(workflowLog, "step=EditPreviewInfo field=Tags result=Skipped reason=update_flow");

        preview = await FillOptionalPreviewVersionLogIfNeededAsync(
            request,
            preview,
            workflowLog,
            cancellationToken);

        preview = await FillPreviewMultilanguageIfNeededAsync(request, preview, workflowLog, cancellationToken);
        preview = await UploadPreviewExtraImagesIfNeededAsync(request, preview, workflowLog, cancellationToken);
        preview = await UploadPreviewVideoIfNeededAsync(request, preview, workflowLog, cancellationToken);

        ValidatePreviewInfoSubmitPrerequisites(request, workflowLog);
        var submitted = await SubmitPreviewInfoAsync(request, preview, workflowLog, cancellationToken);
        LogWorkflow(workflowLog, "step=EditPreviewInfo result=PreviewInfoSubmitted");
        return submitted;
    }

    private static bool ShouldEditPreviewInfo(LevelArchive archive) =>
        !string.IsNullOrWhiteSpace(archive.LevelName) ||
        !string.IsNullOrWhiteSpace(archive.GameDescription) ||
        !string.IsNullOrWhiteSpace(archive.LevelDescription) ||
        !string.IsNullOrWhiteSpace(archive.LevelCoverPath) ||
        !string.IsNullOrWhiteSpace(archive.Tags) ||
        !string.IsNullOrWhiteSpace(archive.VersionLog) ||
        archive.IsEarlyAccess ||
        !string.IsNullOrWhiteSpace(archive.EarlyAccessDescription) ||
        archive.LocalizedInfos.Any(HasConfiguredLocalizedPreviewInfo) ||
        archive.ExtraImagePaths.Any(path => !string.IsNullOrWhiteSpace(path)) ||
        !string.IsNullOrWhiteSpace(archive.VideoPath);

    private static bool HasConfiguredLocalizedPreviewInfo(LocalizedArchiveInfo info) =>
        !string.IsNullOrWhiteSpace(info.LevelName) ||
        !string.IsNullOrWhiteSpace(info.GameDescription) ||
        !string.IsNullOrWhiteSpace(info.LevelDescription) ||
        !string.IsNullOrWhiteSpace(info.EarlyAccessDescription) ||
        !string.IsNullOrWhiteSpace(info.VersionLog);

    private async Task<StablePageResult> EditImageAssetsIfNeededAsync(
        UploadRequest request,
        StablePageResult uploadedArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Checking image resources");
        var resources = BuildConfiguredImageAssetResources(request.Archive);
        if (resources.Count == 0)
        {
            LogWorkflow(workflowLog, "step=EditImageAssets result=Skipped reason=no_image_asset_resources");
            return uploadedArchive;
        }

        if (uploadedArchive.Recognition.Page != GamePage.UploadedArchive)
            uploadedArchive = await WaitForPageEvidenceAsync(uploadedArchive, Pages(GamePage.UploadedArchive), "Edit.PageRecovery", ShortWait, cancellationToken);

        LogWorkflow(workflowLog, $"step=EditImageAssets action=OpenImageAssetEditor resourceCount={resources.Count}");
        var imageAssetEntry = await FindUploadedArchiveInfoPanelIconByOcrAsync(
            uploadedArchive,
            ["IMAGEASSETS", "IMAGE ASSETS", "\u56fe\u7247\u8d44\u4ea7"],
            [WorkflowTargetNames.UploadedImageAssetsButton, WorkflowTargetNames.TextEditIcon],
            WorkflowTargetNames.UploadedImageAssetsButton,
            cancellationToken);
        if (imageAssetEntry.Icon == null)
        {
            SavePreviewInfoDiagnostic(
                uploadedArchive,
                "stage1-image-assets-entry-missing",
                request,
                "ImageAssetsEntry",
                "OCR info_panel_row as Image Assets, then click the row action icon",
                imageAssetEntry.OcrTexts,
                workflowLog,
                "UploadedArchive Image Assets row/icon was not confirmed.");
            throw new InvalidOperationException("UploadedArchive Image Assets row/icon was not confirmed.");
        }

        LogWorkflow(workflowLog, 
            $"step=EditImageAssets action=OpenImageAssetEditor selectedBy={imageAssetEntry.SelectedBy} " +
            $"row=({FormatTarget(imageAssetEntry.Row!)}) icon=({FormatTarget(imageAssetEntry.Icon)})");
        _continuationImageResource = resources[0];
        await ClickSpecificTargetAsync(uploadedArchive, imageAssetEntry.Icon, cancellationToken);
        var editor = await WaitForImageAssetEditorContextAsync(
            request,
            "EditImageAssets.WaitImageAssetEditor",
            workflowLog,
            ImageAssetEditorReadyWait,
            cancellationToken);

        foreach (var resource in resources)
        {
            var resolvedPath = IsGiftBoxResource(resource) ? string.Empty : ResolveRequiredArchiveResourcePath(request, resource.RelativeOrAbsolutePath, resource.FieldName, workflowLog);
            editor = await UploadImageAssetResourceToDraftAsync(
                request,
                editor,
                resource,
                resolvedPath,
                workflowLog,
                cancellationToken);
        }

        LogWorkflow(workflowLog, "step=EditImageAssets action=PressEscapeToUploadedArchive");
        _activation.RestoreAndActivate(editor.Capture.Window);
        _recordAction?.Invoke("press_key:Escape");
        await _input.PressKeyAsync(editor.Capture.Window, GameVirtualKey.Escape, cancellationToken);
        var returned = await WaitForExpectedAsync(
            "EditImageAssets.WaitUploadedArchiveAfterEsc",
            Pages(GamePage.UploadedArchive),
            MediumWait,
            stableCount: 1,
            cancellationToken);
        LogWorkflow(workflowLog, "step=EditImageAssets result=ImageAssetsSubmittedToDrafts");
        return returned;
    }

    private static IReadOnlyList<ImageAssetResourceSpec> BuildConfiguredImageAssetResources(LevelArchive archive)
    {
        var resources = new List<ImageAssetResourceSpec>();
        Add(archive.MainImagePath, "MainImage", 0, ["LOADING SCREEN IMAGE", "LOADING IMAGE", "加载图片", "\u52a0\u8f7d\u4e3b\u56fe"]);
        Add(archive.AchievementIconPath, "AchievementIcon", 1, ["ULTIMATE ACHIEVEMENT ICON", "ACHIEVEMENT ICON", "\u6781\u81f4\u6210\u5c31\u56fe\u6807", "\u6210\u5c31\u56fe\u6807"]);
        Add(archive.SubmitLuxuryGiftIcon ? "submit-only" : "", "LuxuryGiftIcon", 2, ["GORGEOUS PERFORMANCE BOX ICON", "LUXURY GIFT ICON", "\u534e\u4e3d\u6f14\u7ece\u793c\u76d2\u56fe\u6807", "\u8c6a\u534e\u793c\u76d2\u56fe\u6807"]);
        Add(archive.SubmitColorfulGiftIcon ? "submit-only" : "", "ColorfulGiftIcon", 3, ["COLORFUL SURPRISE BOX ICON", "COLORFUL SURPRISE", "\u7f24\u7eb7\u60ca\u559c\u793c\u76d2\u56fe\u6807"]);
        Add(archive.SubmitQuestGiftIcon ? "submit-only" : "", "QuestGiftIcon", 4, ["WONDERLAND COMMENDATION GIFT BOX ICON", "QUEST REWARD ICON", "QUEST GIFT ICON", "\u5947\u57df\u5609\u5956\u793c\u76d2\u56fe\u6807", "\u4efb\u52a1\u793c\u76d2\u56fe\u6807"]);
        return resources;

        void Add(string path, string fieldName, int tabOrder, IReadOnlyList<string> tabTokens)
        {
            if (!string.IsNullOrWhiteSpace(path))
                resources.Add(new ImageAssetResourceSpec(fieldName, path, tabOrder, tabTokens));
        }
    }

    private async Task<StablePageResult> UploadImageAssetResourceToDraftAsync(
        UploadRequest request,
        StablePageResult editor,
        ImageAssetResourceSpec resource,
        string resolvedPath,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation($"Processing image resource: {HudField(resource.FieldName)}");
        _continuationImageResource = resource;
        if (editor.Recognition.Page != GamePage.ImageAssetEditor)
            throw new InvalidOperationException($"Expected ImageAssetEditor before editing {resource.FieldName}. Current page: {editor.Recognition.Page}.");

        var tab = await FindImageAssetSideTabAsync(editor, resource, workflowLog, cancellationToken);
        if (tab.Target == null)
        {
            SavePreviewInfoDiagnostic(
                editor,
                $"stage1-image-assets-{NormalizeFileNameSegment(resource.FieldName)}-tab-missing",
                request,
                resource.FieldName,
                "Detect side_tab_button and match the image asset tab by OCR or visible order",
                tab.OcrTexts,
                workflowLog,
                $"Image asset tab was not confirmed for {resource.FieldName}.");
            throw new InvalidOperationException($"Image asset tab was not confirmed for {resource.FieldName}.");
        }

        LogWorkflow(workflowLog, 
            $"step=EditImageAssets field={resource.FieldName} action=ClickSideTab selectedBy={tab.SelectedBy} " +
            $"tabOrder={resource.TabOrder} target=({FormatTarget(tab.Target)})");
        await ClickSpecificTargetAsync(editor, tab.Target, cancellationToken);
        editor = await WaitForImageAssetEditorContextAsync(
            request,
            $"EditImageAssets.{resource.FieldName}.WaitEditorAfterTab",
            workflowLog,
            ImageAssetEditorReadyWait,
            cancellationToken);

        editor = await ConfirmImageAssetTabAsync(request, editor, resource, workflowLog, cancellationToken);

        var openDrafts = await FindImageAssetPrimaryButtonAsync(
            editor,
            ["OPEN DRAFTS", "DRAFTS", "打开草稿箱", "\u6253\u5f00\u8349\u7a3f", "\u8349\u7a3f\u7bb1"],
            preferBottom: true,
            cancellationToken, allowMissing: IsGiftBoxResource(resource));
        if (openDrafts.Target == null && IsGiftBoxResource(resource))
        {
            LogWorkflow(workflowLog, $"step=EditImageAssets field={resource.FieldName} result=Skipped reason=NoOpenDraftsButton");
            return editor;
        }
        if (openDrafts.Target == null)
        {
            SavePreviewInfoDiagnostic(
                editor,
                $"stage1-image-assets-{NormalizeFileNameSegment(resource.FieldName)}-open-drafts-missing",
                request,
                resource.FieldName,
                "OCR or locate the bottom primary_action_button as Open Drafts",
                openDrafts.OcrTexts,
                workflowLog,
                $"Open Drafts button was not confirmed for {resource.FieldName}.");
            throw new InvalidOperationException($"Open Drafts button was not confirmed for {resource.FieldName}.");
        }

        LogWorkflow(workflowLog, 
            $"step=EditImageAssets field={resource.FieldName} action=ClickOpenDrafts " +
            $"target=({FormatTarget(openDrafts.Target)}) resolvedPath=\"{resolvedPath}\"");
        await ClickSpecificTargetAsync(editor, openDrafts.Target, cancellationToken);
        var draftBox = await WaitForImageAssetDraftBoxContextAsync(
            request,
            resource,
            $"EditImageAssets.{resource.FieldName}.WaitDraftBox",
            workflowLog,
            MediumWait,
            cancellationToken);

        if (IsGiftBoxResource(resource))
            return await SubmitOrReturnGiftBoxDraftAsync(request, draftBox, resource, workflowLog, cancellationToken);

        return await UploadImageAssetWithFailureRetryAsync(request, draftBox, resource, resolvedPath,
            workflowLog, cancellationToken);
    }

    private static bool IsGiftBoxResource(ImageAssetResourceSpec resource) =>
        resource.FieldName is "LuxuryGiftIcon" or "ColorfulGiftIcon" or "QuestGiftIcon";

    private Task<StablePageResult> SubmitOrReturnGiftBoxDraftAsync(
        UploadRequest request, StablePageResult draftBox, ImageAssetResourceSpec resource,
        List<string> workflowLog, CancellationToken cancellationToken) =>
        ContinueStepAsync("Submit gift box draft: " + resource.FieldName, async fresh =>
        {
            var current = fresh ?? draftBox;
            if (current.Recognition.Page == GamePage.ImageAssetEditor) return current;
            if (current.Recognition.Page != GamePage.ImageAssetDraftBox)
                throw new InvalidOperationException("Expected gift box draft before checking Submit for Review.");
            ReportOperation($"Checking gift box draft: {HudField(resource.FieldName)}");
            var submit = await FindImageAssetPrimaryButtonAsync(current,
                ["SUBMIT", "SUBMIT REVIEW", "SUBMIT FOR REVIEW", "提交审核"],
                preferBottom: true, cancellationToken, allowMissing: true);
            LogWorkflow(workflowLog, $"step=EditImageAssets field={resource.FieldName} " +
                $"action=CheckGiftBoxDraft submitFound={submit.Target != null} ocr=\"{FormatRawOcrTexts(submit.OcrTexts)}\"");
            if (submit.Target != null)
                return await SubmitImageAssetDraftAsync(request, current, resource, workflowLog, cancellationToken);
            return await ReturnFromImageAssetDraftBoxToEditorAsync(current, resource,
                "GiftBoxDraftHasNoSubmitButton", workflowLog, cancellationToken);
        }, cancellationToken);

    private async Task<StablePageResult> ReturnFromImageAssetDraftBoxToEditorAsync(
        StablePageResult draftBox,
        ImageAssetResourceSpec resource,
        string reason,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        LogWorkflow(workflowLog, 
            $"step=EditImageAssets field={resource.FieldName} action=PressEscapeFromDraftBox reason={reason}");
        _activation.RestoreAndActivate(draftBox.Capture.Window);
        _recordAction?.Invoke("press_key:Escape");
        await _input.PressKeyAsync(draftBox.Capture.Window, GameVirtualKey.Escape, cancellationToken);
        return await WaitForImageAssetEditorContextAsync(
            null,
            $"EditImageAssets.{resource.FieldName}.WaitEditorAfterDraftBoxEsc",
            workflowLog,
            ImageAssetEditorReadyWait,
            cancellationToken);
    }

    private async Task<StablePageResult> WaitForImageAssetEditorContextAsyncCore(
        UploadRequest? request,
        string stepName,
        List<string> workflowLog,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + timeout;
        StablePageResult? last = null;
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var attempt = 0;
        string? lastAcceptedSignature = null;
        var stableAcceptedCount = 0;

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            last = await RecognizeOnceAsync(cancellationToken);
            var context = await DetectImageAssetEditorContextAsync(last, cancellationToken);
            allOcrTexts.AddRange(context.OcrTexts);
            LogWorkflow(workflowLog, 
                $"step={stepName} action=WaitImageAssetEditor attempt={attempt} " +
                $"rawPage={context.Evidence.RawPage} contextualPage={context.Evidence.ContextualPage} " +
                $"ocrTitle=\"{context.Evidence.OcrTitle}\" sideTabOcr=\"{context.Evidence.SideTabOcr}\" " +
                $"buttonOcr=\"{context.Evidence.ButtonOcr}\" sideTabCount={context.Evidence.SideTabCount} " +
                $"bottomPrimaryButtonCount={context.Evidence.BottomPrimaryButtonCount} accepted={context.Evidence.Accepted} " +
                $"readyStableCount={stableAcceptedCount} reason=\"{context.Evidence.Reason}\"");

            if (_clock.UtcNow >= deadline)
            {
                LogWorkflow(workflowLog, $"step={stepName} action=ImageAssetEditorBudgetExpired " +
                    $"attempt={attempt} timeoutMs={timeout.TotalMilliseconds:0} readyStableCount={stableAcceptedCount}");
                break;
            }

            if (context.Evidence.Accepted)
            {
                var signature = "editor_layout_and_ocr_ready";
                stableAcceptedCount = string.Equals(signature, lastAcceptedSignature, StringComparison.Ordinal)
                    ? stableAcceptedCount + 1
                    : 1;
                lastAcceptedSignature = signature;
                LogWorkflow(workflowLog, 
                    $"step={stepName} action=ImageAssetEditorOcrReady attempt={attempt} " +
                    $"stableCount={stableAcceptedCount} requiredStableCount=2 signature=\"{signature}\"");
                if (stableAcceptedCount >= 2)
                    return WithContextualPage(last, GamePage.ImageAssetEditor);
            }
            else
            {
                lastAcceptedSignature = null;
                stableAcceptedCount = 0;
            }

            if (last.Recognition.Page.IsBlocking())
                throw new InvalidOperationException($"Blocking page appeared during {stepName}: {last.Recognition.Page}.");

            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        string? diagnostic;
        if (request != null)
        {
            diagnostic = SavePreviewInfoDiagnostic(
                last,
                $"stage1-{NormalizeFileNameSegment(stepName)}-context-timeout",
                request,
                "ImageAssets",
                "Confirm Stage Image Resources editor by YOLO layout or OCR",
                allOcrTexts,
                workflowLog,
                $"ImageAssetEditor was not contextually confirmed. lastPage={last?.Recognition.Page}.");
        }
        else
        {
            diagnostic = SaveDiagnostic(
                last,
                $"stage1-{NormalizeFileNameSegment(stepName)}-context-timeout",
                isFinalFailure: true,
                phase: "EditImageAssets");
        }

        throw new TimeoutException(
            $"ImageAssetEditor was not contextually confirmed during {stepName}. " +
            $"lastPage={last?.Recognition.Page}, diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<(Stage1ImageAssetEditorContextEvidence Evidence, IReadOnlyList<WorkflowOcrTextResult> OcrTexts)>
        DetectImageAssetEditorContextAsync(
            StablePageResult result,
            CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var sideTabs = FindImageAssetSideTabs(result);
        var bottomPrimaryButtonCount = CountRightBottomPrimaryButtons(result);
        var visualMatched = sideTabs.Count >= 3 && (bottomPrimaryButtonCount > 0 ||
            (_continuationImageResource != null && IsGiftBoxResource(_continuationImageResource)));

        // A transition frame can still contain the uploaded list. Do not spend
        // the readiness budget running OCR on a page whose layout cannot match.
        if (!visualMatched)
            return (new Stage1ImageAssetEditorContextEvidence(result.Recognition.Page,
                result.Recognition.Page, "", "", "", sideTabs.Count, bottomPrimaryButtonCount,
                false, "OCR skipped: image asset editor layout was not confirmed"), allOcrTexts);

        // Anchor the header strip to the first tab; its vertical position changes
        // on 4:3 windows. Include the centered draft title to retain the overlay guard.
        var aspect = (double)result.Capture.Bitmap.PixelWidth / result.Capture.Bitmap.PixelHeight;
        var titleBox = new NormalizedBox(.43,
            Math.Clamp(sideTabs[0].CenterY - .055 * aspect, .05, .40), .60, .055 * aspect);
        var titleTexts = await _workflowOcr.RecognizeBoxAsync(
            result.Capture,
            titleBox,
            "ImageAssetEditor/title",
            cancellationToken);
        allOcrTexts.AddRange(titleTexts);

        // The editor remains visible behind a closing draft dialog. Its title/layout
        // alone must not authorize clicks through that dialog or its transition.
        var draftOverlay = OcrTextsContainAny(titleTexts, ["UPLOAD", "UPLOAD TO DRAFTS", "上传", "上傳", "上传草稿箱"]);
        foreach (var button in FindTargets(result.Recognition, WorkflowTargetNames.PrimaryActionButton, .25)
            .Where(t => t.CenterX is >= .35 and <= .65 && t.CenterY >= .55))
        {
            var texts = await _workflowOcr.RecognizeTargetAsync(result.Capture, button, cancellationToken);
            allOcrTexts.AddRange(texts);
            // Open Drafts moves toward the center when Replace and Publish is also visible.
            // A real draft-submit button, or unreadable text, must still block clicks through it.
            if (!OcrTextsContainAny(texts, ["OPEN DRAFTS", "打开草稿箱"])) draftOverlay = true;
        }
        if (draftOverlay)
            return (new Stage1ImageAssetEditorContextEvidence(result.Recognition.Page,
                result.Recognition.Page, FormatRawOcrTexts(titleTexts), "", "",
                sideTabs.Count, bottomPrimaryButtonCount, false,
                "draft dialog or closing transition still visible"), allOcrTexts);

        // Once layout and title agree, avoid two more OCR passes over already-ready UI.
        var titleReady = OcrTextsContainAny(titleTexts,
            ["STAGE IMAGE RESOURCES", "STAGEIMAGERESOURCES", "IMAGE RESOURCES", "关卡图片资源"]);
        if (visualMatched && titleReady)
            return (new Stage1ImageAssetEditorContextEvidence(result.Recognition.Page,
                GamePage.ImageAssetEditor, FormatRawOcrTexts(titleTexts), "", "",
                sideTabs.Count, bottomPrimaryButtonCount, true,
                "accepted by image asset layout and title OCR"), allOcrTexts);

        var sideTabTexts = await _workflowOcr.RecognizeBoxAsync(
            result.Capture,
            new NormalizedBox(0.23, 0.42, 0.22, 0.40),
            "ImageAssetEditor/side_tabs",
            cancellationToken);
        allOcrTexts.AddRange(sideTabTexts);

        var buttonTexts = new List<WorkflowOcrTextResult>();
        foreach (var button in FindTargets(result.Recognition, WorkflowTargetNames.PrimaryActionButton, 0.25)
                     .Where(static target => target.CenterY >= 0.55)
                     .OrderByDescending(static target => target.CenterY)
                     .Take(2))
        {
            var texts = await _workflowOcr.RecognizeTargetAsync(result.Capture, button, cancellationToken);
            buttonTexts.AddRange(texts);
            allOcrTexts.AddRange(texts);
        }

        var titleMatched = OcrTextsContainAny(titleTexts, ["STAGE IMAGE RESOURCES", "STAGEIMAGERESOURCES", "IMAGE RESOURCES", "关卡图片资源"]);
        var sideTabMatched = OcrTextsContainAny(
            sideTabTexts,
            [
                "LOADING SCREEN IMAGE",
                "ULTIMATE ACHIEVEMENT ICON",
                "GORGEOUS PERFORMANCE BOX ICON",
                "COLORFUL SURPRISE BOX ICON",
                "WONDERLAND COMMENDATION GIFT BOX ICON",
                "加载图片", "极致成就图标", "华丽演绎礼盒图标", "缤纷惊喜礼盒图标", "奇域嘉奖礼盒图标"
            ]);
        var buttonMatched = OcrTextsContainAny(buttonTexts, ["OPEN DRAFTS", "OPENDRAFTS", "DRAFTS", "打开草稿箱"]);
        var hasReadyOcr = titleMatched || sideTabMatched || buttonMatched;
        var accepted = visualMatched && hasReadyOcr;
        var reason = accepted
            ? titleMatched
                ? "accepted by image asset layout and Stage Image Resources title OCR"
                : sideTabMatched
                    ? "accepted by image asset layout and side-tab OCR"
                    : "accepted by image asset layout and Open Drafts button OCR"
            : !visualMatched
                ? "rejected because image asset editor layout was not confirmed"
                : "rejected because image asset editor text OCR is not ready";

        var evidence = new Stage1ImageAssetEditorContextEvidence(
            result.Recognition.Page,
            accepted ? GamePage.ImageAssetEditor : result.Recognition.Page,
            FormatRawOcrTexts(titleTexts),
            FormatRawOcrTexts(sideTabTexts),
            FormatRawOcrTexts(buttonTexts),
            sideTabs.Count,
            bottomPrimaryButtonCount,
            accepted,
            reason);
        return (evidence, allOcrTexts);
    }

    private static int CountRightBottomPrimaryButtons(StablePageResult result) =>
        FindTargets(result.Recognition, WorkflowTargetNames.PrimaryActionButton, 0.25)
            .Count(static target => target.CenterX >= 0.60 && target.CenterY >= 0.55);

    private async Task<StablePageResult> WaitForImageAssetDraftBoxContextAsyncCore(
        UploadRequest? request,
        ImageAssetResourceSpec resource,
        string stepName,
        List<string> workflowLog,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + timeout;
        StablePageResult? last = null;
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var attempt = 0;
        string? lastAcceptedSignature = null;
        var stableAcceptedCount = 0;
        var unavailableFrames = 0;

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            last = await RecognizeOnceAsync(cancellationToken);
            var context = await DetectImageAssetDraftBoxContextAsync(last, resource, cancellationToken);
            allOcrTexts.AddRange(context.OcrTexts);
            unavailableFrames = context.Evidence.EmptyUnavailable ? unavailableFrames + 1 : 0;
            if (unavailableFrames >= 2 && !IsGiftBoxResource(resource))
            {
                var emptyDiagnostic = SaveDiagnostic(last, "stage1-image-assets-empty-unavailable", isFinalFailure: true, phase: "EditImageAssets");
                LogWorkflow(workflowLog, $"step={stepName} field={resource.FieldName} result=EmptyDraftUnavailable stableFrames={unavailableFrames}");
                throw new InvalidOperationException($"{HudField(resource.FieldName)}: 游戏草稿窗口显示暂无草稿（No drafts found），且未检测到上传入口或提交按钮，无法上传此资源。请检查游戏内资源状态。 diagnostic={emptyDiagnostic}");
            }
            LogWorkflow(workflowLog, 
                $"step={stepName} action=WaitImageAssetDraftBox field={resource.FieldName} attempt={attempt} " +
                $"rawPage={context.Evidence.RawPage} contextualPage={context.Evidence.ContextualPage} " +
                $"ocrTitle=\"{context.Evidence.OcrTitle}\" resourceHeaderOcr=\"{context.Evidence.ResourceHeaderOcr}\" " +
                $"buttonOcr=\"{context.Evidence.ButtonOcr}\" imageUploadAreaCount={context.Evidence.ImageUploadAreaCount} " +
                $"centeredBottomPrimaryButtonCount={context.Evidence.CenteredBottomPrimaryButtonCount} " +
                $"accepted={context.Evidence.Accepted} readyStableCount={stableAcceptedCount} reason=\"{context.Evidence.Reason}\"");

            if (context.Evidence.Accepted)
            {
                var signature = CreateImageAssetDraftBoxReadySignature(context.Evidence);
                stableAcceptedCount = string.Equals(signature, lastAcceptedSignature, StringComparison.Ordinal)
                    ? stableAcceptedCount + 1
                    : 1;
                lastAcceptedSignature = signature;
                LogWorkflow(workflowLog, 
                    $"step={stepName} action=ImageAssetDraftBoxOcrReady field={resource.FieldName} attempt={attempt} " +
                    $"stableCount={stableAcceptedCount} requiredStableCount={ImageAssetReadyStableCount} signature=\"{signature}\"");
                if (stableAcceptedCount >= ImageAssetReadyStableCount)
                    return WithContextualPage(last, GamePage.ImageAssetDraftBox);
            }
            else
            {
                lastAcceptedSignature = null;
                stableAcceptedCount = 0;
            }

            if (last.Recognition.Page.IsBlocking())
                throw new InvalidOperationException($"Blocking page appeared during {stepName}: {last.Recognition.Page}.");

            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        string? diagnostic;
        if (request != null)
        {
            diagnostic = SavePreviewInfoDiagnostic(
                last,
                $"stage1-{NormalizeFileNameSegment(stepName)}-context-timeout",
                request,
                resource.FieldName,
                "Confirm Upload to Drafts dialog by YOLO layout or OCR",
                allOcrTexts,
                workflowLog,
                $"ImageAssetDraftBox was not contextually confirmed for {resource.FieldName}. lastPage={last?.Recognition.Page}.");
        }
        else
        {
            diagnostic = SaveDiagnostic(
                last,
                $"stage1-{NormalizeFileNameSegment(stepName)}-context-timeout",
                isFinalFailure: true,
                phase: "EditImageAssets");
        }

        throw new TimeoutException(
            $"ImageAssetDraftBox was not contextually confirmed during {stepName}. " +
            $"field={resource.FieldName}, lastPage={last?.Recognition.Page}, diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<(Stage1ImageAssetDraftBoxContextEvidence Evidence, IReadOnlyList<WorkflowOcrTextResult> OcrTexts)>
        DetectImageAssetDraftBoxContextAsync(
            StablePageResult result,
            ImageAssetResourceSpec resource,
            CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var imageUploadAreaCount = CountTargets(result.Recognition, WorkflowTargetNames.ImageUploadArea, 0.25);
        var centeredBottomPrimaryButtonCount = CountCenteredBottomPrimaryButtons(result);
        var visualMatched = imageUploadAreaCount > 0 && centeredBottomPrimaryButtonCount > 0;

        var titleTexts = await _workflowOcr.RecognizeBoxAsync(
            result.Capture,
            new NormalizedBox(0.50, 0.19, 0.80, 0.20),
            "ImageAssetDraftBox/title",
            cancellationToken);
        allOcrTexts.AddRange(titleTexts);

        var resourceHeaderTexts = await _workflowOcr.RecognizeBoxAsync(
            result.Capture,
            new NormalizedBox(0.50, 0.25, 0.72, 0.18),
            "ImageAssetDraftBox/resource_header",
            cancellationToken);
        allOcrTexts.AddRange(resourceHeaderTexts);

        var buttonTexts = new List<WorkflowOcrTextResult>();
        foreach (var button in FindTargets(result.Recognition, WorkflowTargetNames.PrimaryActionButton, 0.25)
                     .Where(static target => target.CenterX is >= 0.35 and <= 0.65 && target.CenterY >= 0.55)
                     .OrderByDescending(static target => target.CenterY)
                     .Take(2))
        {
            var texts = await _workflowOcr.RecognizeTargetAsync(result.Capture, button, cancellationToken);
            buttonTexts.AddRange(texts);
            allOcrTexts.AddRange(texts);
        }

        var titleMatched = OcrTextsContainAny(titleTexts, ["UPLOAD TO DRAFTS", "UPLOADTODRAFTS", "上传草稿箱"]);
        var resourceHeaderMatched = ImageAssetResourceTextMatches(resourceHeaderTexts, resource.TabTokens);
        var emptyUnavailable = false;
        if (titleMatched && resourceHeaderMatched && imageUploadAreaCount == 0 && centeredBottomPrimaryButtonCount == 0)
        {
            var emptyTexts = await _workflowOcr.RecognizeBoxAsync(result.Capture,
                new NormalizedBox(.50, .63, .36, .10), "ImageAssetDraftBox/empty_state", cancellationToken);
            allOcrTexts.AddRange(emptyTexts);
            emptyUnavailable = OcrTextsContainAny(emptyTexts, ["NO DRAFTS FOUND", "暂无草稿", "暂无草稿箱资源"]);
        }
        var buttonMatched = OcrTextsContainAny(
            buttonTexts,
            ["SUBMIT FOR REVIEW", "SUBMITFORREVIEW", "SUBMIT REVIEW", "SUBMITREVIEW", "SUBMIT", "\u63d0\u4ea4\u5ba1\u6838"]);
        var uploadAreaLayoutMatched = visualMatched && (titleMatched || resourceHeaderMatched || buttonMatched);
        var uploadedImageLayoutMatched =
            centeredBottomPrimaryButtonCount > 0 &&
            titleMatched &&
            (resourceHeaderMatched || buttonMatched);
        // Gift box drafts have no upload control, and empty drafts have no primary button.
        var giftBoxLayoutMatched = IsGiftBoxResource(resource) && titleMatched && resourceHeaderMatched;
        var accepted = giftBoxLayoutMatched || (uploadAreaLayoutMatched || uploadedImageLayoutMatched) && resourceHeaderMatched;
        var reason = giftBoxLayoutMatched
            ? "accepted gift box draft by title and resource header OCR"
            : accepted
            ? uploadAreaLayoutMatched
                ? titleMatched
                    ? "accepted by draft upload-area layout and Upload to Drafts title OCR"
                    : resourceHeaderMatched
                        ? "accepted by draft upload-area layout and resource header OCR"
                        : "accepted by draft upload-area layout and Submit for Review button OCR"
                : resourceHeaderMatched
                    ? "accepted by uploaded-image layout with Upload to Drafts title and resource header OCR"
                    : "accepted by uploaded-image layout with Upload to Drafts title and Submit for Review button OCR"
            : centeredBottomPrimaryButtonCount <= 0
                ? "rejected because centered draft primary button was not detected"
                : !titleMatched && !visualMatched
                    ? "rejected because Upload to Drafts title OCR was not ready"
                    : !resourceHeaderMatched && !buttonMatched
                        ? "rejected because resource header and Submit for Review button OCR were not ready"
                : "rejected because draft upload text OCR is not ready";

        var evidence = new Stage1ImageAssetDraftBoxContextEvidence(
            result.Recognition.Page,
            accepted ? GamePage.ImageAssetDraftBox : result.Recognition.Page,
            FormatRawOcrTexts(titleTexts),
            FormatRawOcrTexts(resourceHeaderTexts),
            FormatRawOcrTexts(buttonTexts),
            imageUploadAreaCount,
            centeredBottomPrimaryButtonCount,
            accepted,
            reason) { EmptyUnavailable = emptyUnavailable };
        return (evidence, allOcrTexts);
    }

    private static int CountCenteredBottomPrimaryButtons(StablePageResult result) =>
        FindTargets(result.Recognition, WorkflowTargetNames.PrimaryActionButton, 0.25)
            .Count(static target => target.CenterX is >= 0.35 and <= 0.65 && target.CenterY >= 0.55);

    private static string CreateImageAssetEditorReadySignature(Stage1ImageAssetEditorContextEvidence evidence) =>
        string.Join(
            "|",
            "editor",
            NormalizeWorkflowOcrText(evidence.OcrTitle),
            NormalizeWorkflowOcrText(evidence.SideTabOcr),
            NormalizeWorkflowOcrText(evidence.ButtonOcr),
            evidence.SideTabCount,
            evidence.BottomPrimaryButtonCount);

    private static string CreateImageAssetDraftBoxReadySignature(Stage1ImageAssetDraftBoxContextEvidence evidence) =>
        string.Join(
            "|",
            "draft",
            NormalizeWorkflowOcrText(evidence.OcrTitle),
            NormalizeWorkflowOcrText(evidence.ResourceHeaderOcr),
            NormalizeWorkflowOcrText(evidence.ButtonOcr),
            evidence.ImageUploadAreaCount,
            evidence.CenteredBottomPrimaryButtonCount);

    private async Task<StablePageResult> SubmitImageAssetDraftCoreAsync(
        UploadRequest request,
        StablePageResult draftBox,
        ImageAssetResourceSpec resource,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation($"Submitting image resource for review: {HudField(resource.FieldName)}");
        var current = draftBox;
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        const int maxAttempts = 2;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (current.Recognition.Page != GamePage.ImageAssetDraftBox)
                throw new InvalidOperationException($"Expected ImageAssetDraftBox before submitting {resource.FieldName}. Current page: {current.Recognition.Page}.");

            if (!IsGiftBoxResource(resource))
                await ThrowIfImageUploadFailedAsync(current, resource, workflowLog, cancellationToken);
            var submit = await FindImageAssetPrimaryButtonAsync(
                current,
                ["SUBMIT", "SUBMIT REVIEW", "SUBMIT FOR REVIEW", "\u63d0\u4ea4", "\u63d0\u4ea4\u5ba1\u6838"],
                preferBottom: true,
                cancellationToken);
            allOcrTexts.AddRange(submit.OcrTexts);
            if (submit.Target == null)
            {
                SavePreviewInfoDiagnostic(
                    current,
                    $"stage1-image-assets-{NormalizeFileNameSegment(resource.FieldName)}-submit-missing",
                    request,
                    resource.FieldName,
                    "OCR or locate the bottom primary_action_button as Submit",
                    allOcrTexts,
                    workflowLog,
                    $"Draft submit button was not confirmed for {resource.FieldName}.");
                throw new InvalidOperationException($"Draft submit button was not confirmed for {resource.FieldName}.");
            }

            LogWorkflow(workflowLog, 
                $"step=EditImageAssets field={resource.FieldName} action=ClickSubmitDraft " +
                $"attempt={attempt} target=({FormatTarget(submit.Target)})");
            await ClickSpecificTargetAsync(current, submit.Target, cancellationToken);
            var afterSubmit = await WaitForImageAssetDraftSubmitResultContextAsync(
                request,
                resource,
                $"EditImageAssets.{resource.FieldName}.Submit.Attempt{attempt}",
                workflowLog,
                MediumWait,
                cancellationToken);
            if (afterSubmit.Recognition.Page == GamePage.ImageAssetEditor)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditImageAssets field={resource.FieldName} result=SubmittedToDraft " +
                    $"attempt={attempt} page={afterSubmit.Recognition.Page}");
                return afterSubmit;
            }

            if (!IsGiftBoxResource(resource))
                await ThrowIfImageUploadFailedAsync(afterSubmit, resource, workflowLog, cancellationToken);
            LogWorkflow(workflowLog, 
                $"step=EditImageAssets field={resource.FieldName} action=SubmitNoEffect " +
                $"attempt={attempt} page={afterSubmit.Recognition.Page} confidence={afterSubmit.Recognition.Confidence:F2}");
            current = afterSubmit;
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(1200), cancellationToken);
        }

        SavePreviewInfoDiagnostic(
            current,
            $"stage1-image-assets-{NormalizeFileNameSegment(resource.FieldName)}-submit-no-effect",
            request,
            resource.FieldName,
            "Click Submit in ImageAssetDraftBox and observe return to ImageAssetEditor",
            allOcrTexts,
            workflowLog,
            $"Draft submit did not return to ImageAssetEditor for {resource.FieldName} after {maxAttempts} attempts.");
        throw new InvalidOperationException(
            $"Draft submit did not return to ImageAssetEditor for {resource.FieldName} after {maxAttempts} attempts.");
    }

    private async Task<StablePageResult> WaitForImageAssetDraftSubmitResultContextAsyncCore(
        UploadRequest request,
        ImageAssetResourceSpec resource,
        string stepName,
        List<string> workflowLog,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + timeout;
        StablePageResult? last = null;
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var attempt = 0;
        GamePage? lastAcceptedPage = null;
        string? lastAcceptedSignature = null;
        var stableAcceptedCount = 0;

        // A slow OCR pass over a closing animation must not consume the entire
        // opportunity to observe the result. Allow at least three fresh captures.
        while (_clock.UtcNow < deadline || attempt < 3)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            last = await RecognizeOnceAsync(cancellationToken);

            var editorContext = await DetectImageAssetEditorContextAsync(last, cancellationToken);
            allOcrTexts.AddRange(editorContext.OcrTexts);
            LogWorkflow(workflowLog, 
                $"step={stepName} action=WaitSubmitResultEditorContext field={resource.FieldName} attempt={attempt} " +
                $"rawPage={editorContext.Evidence.RawPage} contextualPage={editorContext.Evidence.ContextualPage} " +
                $"ocrTitle=\"{editorContext.Evidence.OcrTitle}\" sideTabOcr=\"{editorContext.Evidence.SideTabOcr}\" " +
                $"buttonOcr=\"{editorContext.Evidence.ButtonOcr}\" sideTabCount={editorContext.Evidence.SideTabCount} " +
                $"rightBottomPrimaryButtonCount={editorContext.Evidence.BottomPrimaryButtonCount} " +
                $"accepted={editorContext.Evidence.Accepted} reason=\"{editorContext.Evidence.Reason}\"");
            if (editorContext.Evidence.Accepted)
            {
                var signature = "editor_layout_and_ocr_ready";
                stableAcceptedCount = lastAcceptedPage == GamePage.ImageAssetEditor &&
                                      string.Equals(signature, lastAcceptedSignature, StringComparison.Ordinal)
                    ? stableAcceptedCount + 1
                    : 1;
                lastAcceptedPage = GamePage.ImageAssetEditor;
                lastAcceptedSignature = signature;
                LogWorkflow(workflowLog, 
                    $"step={stepName} action=SubmitResultOcrReady field={resource.FieldName} page=ImageAssetEditor " +
                    $"attempt={attempt} stableCount={stableAcceptedCount} requiredStableCount=2 signature=\"{signature}\"");
                if (stableAcceptedCount >= 2)
                    return WithContextualPage(last, GamePage.ImageAssetEditor);
            }

            // Once the editor is confirmed, validate it on a new frame instead of
            // running draft OCR over the same image. With no centered submit button,
            // a normal image draft cannot be actionable during its closing animation.
            if (editorContext.Evidence.Accepted ||
                (!IsGiftBoxResource(resource) && CountCenteredBottomPrimaryButtons(last) == 0))
            {
                if (!editorContext.Evidence.Accepted)
                {
                    lastAcceptedPage = null;
                    lastAcceptedSignature = null;
                    stableAcceptedCount = 0;
                }
                if (last.Recognition.Page.IsBlocking())
                    throw new InvalidOperationException($"Blocking page appeared during {stepName}: {last.Recognition.Page}.");
                LogWorkflow(workflowLog, $"step={stepName} action=RecaptureSubmitResult attempt={attempt} " +
                    $"editorAccepted={editorContext.Evidence.Accepted} reason=SkipRedundantDraftOcr");
                await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
                continue;
            }

            var draftContext = await DetectImageAssetDraftBoxContextAsync(last, resource, cancellationToken);
            allOcrTexts.AddRange(draftContext.OcrTexts);
            LogWorkflow(workflowLog, 
                $"step={stepName} action=WaitSubmitResultDraftContext field={resource.FieldName} attempt={attempt} " +
                $"rawPage={draftContext.Evidence.RawPage} contextualPage={draftContext.Evidence.ContextualPage} " +
                $"ocrTitle=\"{draftContext.Evidence.OcrTitle}\" resourceHeaderOcr=\"{draftContext.Evidence.ResourceHeaderOcr}\" " +
                $"buttonOcr=\"{draftContext.Evidence.ButtonOcr}\" imageUploadAreaCount={draftContext.Evidence.ImageUploadAreaCount} " +
                $"centeredBottomPrimaryButtonCount={draftContext.Evidence.CenteredBottomPrimaryButtonCount} " +
                $"accepted={draftContext.Evidence.Accepted} reason=\"{draftContext.Evidence.Reason}\"");
            if (draftContext.Evidence.Accepted)
            {
                var signature = "draft_still_visible";
                stableAcceptedCount = lastAcceptedPage == GamePage.ImageAssetDraftBox &&
                                      string.Equals(signature, lastAcceptedSignature, StringComparison.Ordinal)
                    ? stableAcceptedCount + 1
                    : 1;
                lastAcceptedPage = GamePage.ImageAssetDraftBox;
                lastAcceptedSignature = signature;
                LogWorkflow(workflowLog, 
                    $"step={stepName} action=SubmitResultOcrReady field={resource.FieldName} page=ImageAssetDraftBox " +
                    $"attempt={attempt} stableCount={stableAcceptedCount} requiredStableCount=2 signature=\"{signature}\"");
                if (stableAcceptedCount >= 2)
                    return WithContextualPage(last, GamePage.ImageAssetDraftBox);
            }

            if (!editorContext.Evidence.Accepted &&
                !draftContext.Evidence.Accepted)
            {
                lastAcceptedPage = null;
                lastAcceptedSignature = null;
                stableAcceptedCount = 0;
            }

            if (last.Recognition.Page.IsBlocking())
                throw new InvalidOperationException($"Blocking page appeared during {stepName}: {last.Recognition.Page}.");

            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        var diagnostic = SavePreviewInfoDiagnostic(
            last,
            $"stage1-{NormalizeFileNameSegment(stepName)}-context-timeout",
            request,
            resource.FieldName,
            "Wait for Submit for Review to return to Stage Image Resources or remain on Upload to Drafts",
            allOcrTexts,
            workflowLog,
            $"Image asset draft submit result was not contextually confirmed for {resource.FieldName}. lastPage={last?.Recognition.Page}.");
        throw new TimeoutException(
            $"Image asset draft submit result was not contextually confirmed during {stepName}. " +
            $"field={resource.FieldName}, lastPage={last?.Recognition.Page}, diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<(AutomationTarget? Target, IReadOnlyList<WorkflowOcrTextResult> OcrTexts, string SelectedBy)> FindImageAssetSideTabAsyncCore(
        StablePageResult editor,
        ImageAssetResourceSpec resource,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var candidates = FindImageAssetSideTabs(editor)
            .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var ocrTexts = await _workflowOcr.RecognizeTargetAsync(editor.Capture, candidate, cancellationToken);
            allOcrTexts.AddRange(ocrTexts);
            var matched = ImageAssetResourceTextMatches(ocrTexts, resource.TabTokens);
            LogWorkflow(workflowLog, 
                $"step=EditImageAssets field={resource.FieldName} action=SideTabOcr candidateIndex={index} " +
                $"target=({FormatTarget(candidate)}) raw=\"{FormatRawOcrTexts(ocrTexts)}\" " +
                $"normalized=\"{FormatNormalizedOcrTexts(ocrTexts)}\" matched={matched}");
            if (matched)
                return (candidate, allOcrTexts, "side_tab_ocr");
        }

        if (resource.TabOrder >= 0 && candidates.Count > resource.TabOrder)
            return (candidates[resource.TabOrder], allOcrTexts, "side_tab_order");

        return (null, allOcrTexts, "not_found");
    }

    private static bool ImageAssetResourceTextMatches(
        IReadOnlyList<WorkflowOcrTextResult> texts, IReadOnlyList<string> tokens)
    {
        if (PreviewCandidateOcrMatches(texts, tokens)) return true;
        // Only join lines within this single side-tab/header crop, never across tabs.
        var joined = string.Concat(texts.Where(t => t.Confidence >= .6)
            .OrderBy(t => t.NormalizedBox.CenterY).ThenBy(t => t.NormalizedBox.CenterX)
            .Select(t => NormalizeWorkflowOcrText(t.Text)));
        return tokens.Any(t => string.Equals(joined, NormalizeWorkflowOcrText(t), StringComparison.Ordinal));
    }

    private async Task<StablePageResult> ConfirmImageAssetTabAsyncCore(
        UploadRequest request, StablePageResult editor, ImageAssetResourceSpec resource,
        List<string> workflowLog, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var match = await FindImageAssetSideTabAsync(editor, resource, workflowLog, cancellationToken);
            var selected = FindImageAssetSideTabs(editor)
                .Where(t => ImageAssetTabVisualState.IsSelected(editor.Capture.Bitmap, t)).ToList();
            var ready = match.Target != null && selected.Count == 1 &&
                Math.Abs(selected[0].CenterY - match.Target.CenterY) < match.Target.Height * .4;
            LogWorkflow(workflowLog, $"step=EditImageAssets field={resource.FieldName} action=ConfirmSelectedTab " +
                $"attempt={attempt} selectedCount={selected.Count} ready={ready}");
            if (ready) return editor;
            if (match.Target == null || attempt == 2) break;
            await ClickSpecificTargetAsync(editor, match.Target, cancellationToken);
            editor = await WaitForImageAssetEditorContextAsync(request,
                $"EditImageAssets.{resource.FieldName}.WaitSelectedTab{attempt + 1}", workflowLog,
                ImageAssetEditorReadyWait, cancellationToken);
        }
        SaveDiagnostic(editor, "stage1-image-asset-tab-not-selected");
        throw new InvalidOperationException($"Image asset tab was not selected: {resource.FieldName}. Draft upload was not started.");
    }

    private static IReadOnlyList<AutomationTarget> FindImageAssetSideTabs(StablePageResult editor) =>
        FindTargets(editor.Recognition, WorkflowTargetNames.SideTabButton, 0.25)
            .Where(target => target.CenterX < 0.45)
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .ThenByDescending(target => target.Confidence)
            .ToList();

    private async Task<OcrTargetMatch> FindImageAssetPrimaryButtonAsyncCore(
        StablePageResult current,
        IReadOnlyList<string> acceptedTokens,
        bool preferBottom,
        CancellationToken cancellationToken)
    {
        var match = await FindPrimaryButtonByOcrAsync(current, acceptedTokens, cancellationToken,
            "ImageAsset." + (_continuationImageResource?.FieldName ?? "UnknownResource"));
        if (match.Target != null)
            return match;

        // The same screen also contains "Overwrite and Publish". A missing OCR
        // match must not turn into a click on an arbitrary bottom action button.
        return match;
    }

    private static AutomationTarget? FindImageAssetDraftUploadArea(StablePageResult draftBox) =>
        FindTargets(draftBox.Recognition, WorkflowTargetNames.ImageUploadArea, 0.25)
            .Where(target => target.CenterX >= 0.25 && target.CenterY >= 0.20)
            .OrderByDescending(GetTargetArea)
            .ThenByDescending(target => target.Confidence)
            .FirstOrDefault();

    private async Task<StablePageResult> FillPreviewLevelNameAsyncCore(
        UploadRequest request,
        StablePageResult preview,
        string value,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Entering the stage name");
        const string fieldName = "LevelName";
        if (string.IsNullOrWhiteSpace(value))
        {
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} result=Skipped reason=empty");
            return preview;
        }

        if (preview.Recognition.Page != GamePage.PreviewEditor)
            preview = await WaitForPageEvidenceAsync(preview, Pages(GamePage.PreviewEditor), "Preview.PageRecovery", ShortWait, cancellationToken);

        var editTarget = FindPreviewLevelNameEditTarget(preview.Recognition);
        var titleDeadline = _clock.UtcNow + ShortWait;
        while (editTarget == null && _clock.UtcNow < titleDeadline)
        {
            await DelayWithTimingAsync(PollInterval, cancellationToken);
            preview = await WaitForPageEvidenceAsync(await RecognizeOnceAsync(cancellationToken),
                Pages(GamePage.PreviewEditor), "Preview.TitleTarget", ShortWait, cancellationToken);
            editTarget = FindPreviewLevelNameEditTarget(preview.Recognition);
        }
        if (editTarget == null)
        {
            SavePreviewInfoDiagnostic(
                preview,
                "stage1-preview-levelname-edit-icon-missing",
                request,
                fieldName,
                "Detect the title row edit icon near the level name",
                [],
                workflowLog,
                "Preview level-name title edit icon was not detected.");
            throw new InvalidOperationException("Preview level-name title edit icon was not detected.");
        }

        var beforeTexts = await ReadPreviewLevelNameByOcrAsync(
            request,
            preview,
            editTarget,
            "BeforeReplace",
            workflowLog,
            cancellationToken);
        var beforeSignature = CreateOcrChangeSignature(beforeTexts);
        if (preview.Recognition.Source.Contains("+EditIconTemplate", StringComparison.Ordinal) &&
            !beforeTexts.Any(text => text.Confidence >= .6 && !string.IsNullOrWhiteSpace(text.Text)))
            throw new InvalidOperationException("Stage-name edit icon was located, but title OCR could not confirm its context.");
        if (Continuation != null && beforeTexts.Any(text => text.Confidence >= .6 &&
                NormalizeWorkflowOcrText(text.Text) == NormalizeWorkflowOcrText(value)))
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo field=LevelName result=AlreadyMatches");
            return preview;
        }

        LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} action=ClickTitleEditIcon target=({FormatTarget(editTarget)})");
        await ClickSpecificTargetAsync(preview, editTarget, cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);

        var afterReplace = await ReplaceTextInPreviewInlineAsync(
            preview,
            fieldName,
            value,
            workflowLog,
            commitWithEnter: true,
            cancellationToken);
        var afterTexts = await ReadPreviewLevelNameByOcrAsync(
            request,
            afterReplace,
            editTarget,
            "AfterReplace",
            workflowLog,
            cancellationToken);
        var afterSignature = CreateOcrChangeSignature(afterTexts);

        if (afterSignature.Length == 0 ||
            string.Equals(beforeSignature, afterSignature, StringComparison.Ordinal))
        {
            var diagnosticTexts = beforeTexts.Concat(afterTexts).ToList();
            SavePreviewInfoDiagnostic(
                afterReplace,
                "stage1-preview-levelname-ocr-unchanged",
                request,
                fieldName,
                "OCR the title row before and after replacement, then confirm the OCR signature changed",
                diagnosticTexts,
                workflowLog,
                $"Preview level name OCR did not change after replacement. before='{beforeSignature}', after='{afterSignature}'.");
            throw new InvalidOperationException(
                $"Preview level name OCR did not change after replacement. before='{beforeSignature}', after='{afterSignature}'.");
        }

        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field={fieldName} result=OcrChanged before=\"{beforeSignature}\" after=\"{afterSignature}\"");
        return afterReplace;
    }

    private async Task<StablePageResult> UploadPreviewCoverIfNeededAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Uploading the stage cover");
        var coverPath = request.Archive.LevelCoverPath;
        if (string.IsNullOrWhiteSpace(coverPath))
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo field=Cover result=Skipped reason=empty_path");
            return preview;
        }

        var resolvedCoverPath = ResolveRequiredArchiveResourcePath(request, coverPath, "Cover", workflowLog);

        var selection = await LocatePreviewCoverTargetAsync(
            request,
            preview,
            workflowLog,
            cancellationToken);
        if (selection.Target == null)
        {
            SavePreviewInfoDiagnostic(
                selection.Page,
                "stage1-preview-cover-target-missing",
                request,
                "Cover",
                "Detect preview_cover_image_area or left-panel image_upload_area. Cover is a left fixed field and must not scroll the right panel.",
                selection.OcrTexts,
                workflowLog,
                "Preview cover upload area was not detected. Cover is a left fixed field and must not scroll the right panel.");
            throw new InvalidOperationException("Preview cover upload area was not detected. Cover is a left fixed field and must not scroll the right panel.");
        }

        LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Cover action=ClickUploadArea path=\"{coverPath}\" resolvedPath=\"{resolvedCoverPath}\" scrollAttempts={selection.ScrollAttempts} target=({FormatTarget(selection.Target)})");
        await ClickSpecificTargetAsync(selection.Page, selection.Target, cancellationToken);
        var fileDialogStopwatch = Stopwatch.StartNew();
        await _fileDialog.SelectFileAsync(selection.Page.Capture.Window, resolvedCoverPath, WindowsFileDialogAutomationService.SelectionTimeout, cancellationToken);
        fileDialogStopwatch.Stop();
        LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Cover action=FileDialogCompleted elapsedMs={fileDialogStopwatch.ElapsedMilliseconds}");
        _activation.RestoreAndActivate(selection.Page.Capture.Window);
        
        var stable = await WaitForExpectedAsync(
            "EditPreviewInfo.Cover.WaitPreviewEditor",
            Pages(GamePage.PreviewEditor),
            MediumWait,
            stableCount: 1,
            cancellationToken);
        LogWorkflow(workflowLog, "step=EditPreviewInfo field=Cover result=Selected");
        return stable;
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewCoverTargetAsyncCore(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (preview.Recognition.Page != GamePage.PreviewEditor)
            preview = await WaitForPageEvidenceAsync(preview, Pages(GamePage.PreviewEditor), "Preview.PageRecovery", ShortWait, cancellationToken);

        var deadline = _clock.UtcNow + ShortWait;
        while (true)
        {
            var previewCoverCandidates = FindTargets(preview.Recognition, WorkflowTargetNames.PreviewCoverImageArea, 0.25)
                .Where(IsLeftPanelPreviewCoverCandidate)
                .OrderByDescending(GetTargetArea)
                .ThenByDescending(target => target.Confidence)
                .ToList();
            if (previewCoverCandidates.Count > 0)
            {
                var target = previewCoverCandidates[0];
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field=Cover searchArea=LeftPanel scrollAllowed=false " +
                    $"result=targetFound target=({FormatTarget(target)})");
                return new PreviewFieldTargetSelection(preview, target, [], 0);
            }

            var imageUploadCandidates = FindTargets(preview.Recognition, WorkflowTargetNames.ImageUploadArea, 0.25)
                .Where(IsLeftPanelCoverImageUploadCandidate)
                .OrderByDescending(GetTargetArea)
                .ThenByDescending(target => target.Confidence)
                .ToList();
            if (imageUploadCandidates.Count > 0)
            {
                var target = imageUploadCandidates[0];
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field=Cover searchArea=LeftPanel scrollAllowed=false " +
                    $"result=targetFound target=({FormatTarget(target)})");
                return new PreviewFieldTargetSelection(preview, target, [], 0);
            }

            if (_clock.UtcNow < deadline)
            {
                await DelayWithTimingAsync(PollInterval, cancellationToken);
                preview = await WaitForPageEvidenceAsync(await RecognizeOnceAsync(cancellationToken),
                    Pages(GamePage.PreviewEditor), "Preview.CoverTarget", ShortWait, cancellationToken);
                continue;
            }
            var rejectedCandidates = FindTargets(preview.Recognition, WorkflowTargetNames.PreviewCoverImageArea, 0.25)
                .Concat(FindTargets(preview.Recognition, WorkflowTargetNames.ImageUploadArea, 0.25))
                .Select(FormatTarget)
                .ToList();
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Cover searchArea=LeftPanel scrollAllowed=false " +
                $"result=notFound candidates=[{string.Join("; ", rejectedCandidates)}]");
            return new PreviewFieldTargetSelection(preview, null, [], 0);
        }
    }

    private static bool IsLeftPanelPreviewCoverCandidate(AutomationTarget target) =>
        target.CenterX < 0.52 &&
        target.CenterY is >= 0.18 and <= 0.70 &&
        target.Width >= 0.12 &&
        target.Height >= 0.10;

    private static bool IsLeftPanelCoverImageUploadCandidate(AutomationTarget target) =>
        target.CenterX < 0.52 &&
        target.CenterY is >= 0.20 and <= 0.70 &&
        target.Width >= 0.15 &&
        target.Height >= 0.12;

    private static double GetTargetArea(AutomationTarget target) =>
        target.Width * target.Height;

    private async Task<StablePageResult> UploadPreviewExtraImagesIfNeededAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var extraImages = request.Archive.ExtraImagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
        if (extraImages.Count == 0)
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo field=ExtraImages result=Skipped reason=empty");
            return preview;
        }

        var resolvedPaths = extraImages
            .Select((imagePath, index) => ResolveRequiredArchiveResourcePath(request, imagePath, $"ExtraImages[{index}]", workflowLog))
            .ToList();
        return await UploadPreviewMediaFilesAsync(
            request,
            preview,
            "ExtraImages",
            resolvedPaths,
            [WorkflowTargetNames.PreviewExtraImagesArea],
            ["SHOWCASE", "IMAGE", "IMAGES", "\u66f4\u591a", "\u5c55\u793a", "\u56fe\u7247"],
            workflowLog,
            cancellationToken);
    }

    private async Task<StablePageResult> UploadPreviewVideoIfNeededAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Archive.VideoPath))
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo field=Video result=Skipped reason=empty");
            return preview;
        }

        var resolvedPath = ResolveRequiredArchiveResourcePath(request, request.Archive.VideoPath, "Video", workflowLog);
        var uploaded = await UploadPreviewMediaFileAsync(
            request,
            preview,
            "Video",
            resolvedPath,
            [WorkflowTargetNames.PreviewVideoArea],
            ["SHOWCASE", "VIDEO", "VIDEOS", "\u5c55\u793a", "\u89c6\u9891"],
            workflowLog,
            cancellationToken);
        return uploaded;
    }

    private async Task<StablePageResult> UploadPreviewMediaFileAsync(
        UploadRequest request,
        StablePageResult preview,
        string fieldName,
        string resolvedPath,
        IReadOnlyList<string> preferredTargetNames,
        IReadOnlyList<string> labelTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken) =>
        await UploadPreviewMediaFilesAsync(
            request,
            preview,
            fieldName,
            [resolvedPath],
            preferredTargetNames,
            labelTokens,
            workflowLog,
            cancellationToken);

    private async Task<StablePageResult> UploadPreviewMediaFilesAsync(
        UploadRequest request,
        StablePageResult preview,
        string fieldName,
        IReadOnlyList<string> resolvedPaths,
        IReadOnlyList<string> preferredTargetNames,
        IReadOnlyList<string> labelTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var fileInfos = resolvedPaths.Select(path => new FileInfo(path)).ToList();
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field={fieldName} action=MediaUploadResource " +
            $"fileCount={resolvedPaths.Count} resolvedPaths=\"{string.Join("|", resolvedPaths)}\" " +
            $"fileSizesBytes=\"{string.Join("|", fileInfos.Select(file => file.Exists ? file.Length : -1))}\"");
        var selection = await LocatePreviewMediaTargetWithScrollAsync(
            request,
            preview,
            fieldName,
            preferredTargetNames,
            labelTokens,
            workflowLog,
            cancellationToken);

        if (selection.Target == null)
        {
            SavePreviewInfoDiagnostic(
                selection.Page,
                $"stage1-preview-{fieldName.ToLowerInvariant().Replace("[", "-").Replace("]", string.Empty)}-target-missing",
                request,
                fieldName,
                $"Detect {string.Join("/", preferredTargetNames.Concat([WorkflowTargetNames.MediaAddTile]))}",
                selection.OcrTexts,
                workflowLog,
                $"Configured preview media field '{fieldName}' was not detected.");
            throw new InvalidOperationException($"Configured preview media field '{fieldName}' was not detected.");
        }

        var beforeUpload = await WaitForPreviewMediaCountAsync(
            request, selection.Page, fieldName, null, TimeSpan.FromSeconds(60), workflowLog, cancellationToken,
            PreviewMediaUploadCounter.FindTitleRetryBox(selection.OcrTexts, fieldName == "Video")
                ?? CreatePreviewMediaTileHeaderOcrBox(selection.Target));
        var expectedCount = beforeUpload.Count + resolvedPaths.Count;
        if (expectedCount > (fieldName == "Video" ? 1 : 4))
            throw new InvalidOperationException($"{fieldName} has insufficient capacity: existing={beforeUpload.Count}, adding={resolvedPaths.Count}.");
        ReportOperation($"Selecting upload files: {HudField(fieldName)}");
        // Re-detect the add tile after the baseline wait; never click a stale location.
        selection = await LocatePreviewMediaTargetWithScrollAsync(request, beforeUpload.Page, fieldName,
            preferredTargetNames, labelTokens, workflowLog, cancellationToken);
        if (selection.Target == null)
            throw new InvalidOperationException($"{fieldName} upload target disappeared after reading its count.");
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field={fieldName} action=ClickMediaUploadArea " +
            $"fileCount={resolvedPaths.Count} resolvedPaths=\"{string.Join("|", resolvedPaths)}\" selectedSource={selection.SearchDirection} " +
            $"scrollAttempts={selection.ScrollAttempts} beforeClickPage={selection.Page.Recognition.Page} " +
            $"baselineCount={beforeUpload.Count} expectedCount={expectedCount} frame={selection.Page.Capture.Bitmap.PixelWidth}x{selection.Page.Capture.Bitmap.PixelHeight} window=({selection.Page.Capture.Window}) " +
            $"target=({FormatTarget(selection.Target)})");
        await ClickSpecificTargetAsync(selection.Page, selection.Target, cancellationToken);
        var fileDialogStopwatch = Stopwatch.StartNew();
        LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} action=AwaitWindowsFileDialog opened=select_file_started");
        try
        {
            if (resolvedPaths.Count == 1)
                await _fileDialog.SelectFileAsync(selection.Page.Capture.Window, resolvedPaths[0], WindowsFileDialogAutomationService.SelectionTimeout, cancellationToken);
            else
                await _fileDialog.SelectFilesAsync(selection.Page.Capture.Window, resolvedPaths, WindowsFileDialogAutomationService.SelectionTimeout, cancellationToken);
            fileDialogStopwatch.Stop();
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=SelectFileCompleted " +
                $"success=true fileCount={resolvedPaths.Count} elapsedMs={fileDialogStopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            fileDialogStopwatch.Stop();
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=SelectFileCompleted " +
                $"success=false opened=false fileCount={resolvedPaths.Count} elapsedMs={fileDialogStopwatch.ElapsedMilliseconds} " +
                $"error=\"{ex.GetType().Name}: {ex.Message}\"");
            var diagnostic = SavePreviewInfoDiagnostic(
                selection.Page,
                GetPreviewMediaFailureDiagnosticPrefix(fieldName, "file-dialog-timeout"),
                request,
                fieldName,
                "Open Windows file dialog and select configured preview media file",
                selection.OcrTexts,
                workflowLog,
                $"Preview media file dialog did not complete for {fieldName}. resolvedPaths=\"{string.Join("|", resolvedPaths)}\".");
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=SaveMediaUploadFailureDiagnostic " +
                $"kind=file-dialog-timeout diagnostic={diagnostic ?? "<not-saved>"}");
            throw;
        }

        _activation.RestoreAndActivate(selection.Page.Capture.Window);

        try
        {
            var returned = await WaitForExpectedAsync(
                $"EditPreviewInfo.{fieldName}.WaitPreviewEditor",
                Pages(GamePage.PreviewEditor),
                MediumWait,
                stableCount: 1,
                cancellationToken);
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=ReturnToPreviewEditorAfterMediaUpload " +
                $"returned=true page={returned.Recognition.Page} confidence={returned.Recognition.Confidence:F2}");
            // A returned editor is not evidence that its selected files finished uploading.
            return (await WaitForPreviewMediaCountAsync(request, returned, fieldName, expectedCount,
                TimeSpan.FromSeconds(180), workflowLog, cancellationToken)).Page;
        }
        catch (OperationCanceledException)
        {
            WorkflowPerformanceTrace.Event("Cancellation", "Cancelled", new { Server = _hudServer.ToString(), Archive = _hudArchive, RunId = _activeDiagnostics?.RunId });
            throw;
        }
        catch (TimeoutException ex) when (ex.Message.Contains("upload count was not confirmed", StringComparison.Ordinal))
        {
            // The count waiter already saved the relevant screenshot and OCR evidence.
            throw;
        }
        catch (Exception ex)
        {
            var latest = await TryRecognizeForDiagnosticsAsync(CancellationToken.None);
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=ReturnToPreviewEditorAfterMediaUpload " +
                $"returned=false error=\"{ex.GetType().Name}: {ex.Message}\"");
            var diagnostic = SavePreviewInfoDiagnostic(
                latest,
                GetPreviewMediaFailureDiagnosticPrefix(fieldName, "return-preview-timeout"),
                request,
                fieldName,
                "Return to PreviewEditor after selecting configured preview media file",
                selection.OcrTexts,
                workflowLog,
                $"PreviewEditor did not return after selecting media for {fieldName}. resolvedPaths=\"{string.Join("|", resolvedPaths)}\".");
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=SaveMediaUploadFailureDiagnostic " +
                $"kind=return-preview-timeout diagnostic={diagnostic ?? "<not-saved>"}");
            throw;
        }
    }

    private async Task<(StablePageResult Page, int Count)> WaitForPreviewMediaCountAsyncCore(
        UploadRequest request, StablePageResult initial, string fieldName, int? expectedCount,
        TimeSpan timeout, List<string> workflowLog, CancellationToken cancellationToken,
        NormalizedBox? initialTitleBox = null)
    {
        ReportOperation($"{(expectedCount.HasValue ? "Waiting for upload completion" : "Reading the uploaded item count")}: {HudField(fieldName)}");
        var deadline = _clock.UtcNow + timeout;
        var current = initial;
        int? previous = null;
        var stable = 0;
        IReadOnlyList<WorkflowOcrTextResult> texts = [];
        NormalizedBox? knownCounterTitle = initialTitleBox;
        LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} action=UploadCountBudget " +
            $"timeoutSeconds={timeout.TotalSeconds} initialTitleBox={initialTitleBox?.ToString() ?? "none"}");
        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? count = null;
            if (current.Recognition.Page.IsBlocking())
                throw new InvalidOperationException($"Blocking page while waiting for {fieldName}: {current.Recognition.Page}.");
            if (current.Recognition.Page == GamePage.PreviewEditor)
            {
                // Reuse only the title region, never a count from an earlier frame.
                if (knownCounterTitle is { } knownBox)
                {
                    texts = await _workflowOcr.RecognizeBoxAsync(current.Capture, knownBox,
                        $"PreviewMedia/{fieldName}/counter-title", cancellationToken);
                    count = PreviewMediaUploadCounter.Read(texts, fieldName == "Video");
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_clock.UtcNow >= deadline) break;
                }
                if (!count.HasValue)
                {
                    // A reflow or unreadable title falls back to the visible panel.
                    texts = await _workflowOcr.RecognizeBoxAsync(current.Capture,
                    new NormalizedBox(0.658, 0.48, 0.39, 0.54),
                    $"PreviewMedia/{fieldName}/counter", cancellationToken);
                    count = PreviewMediaUploadCounter.Read(texts, fieldName == "Video");
                    knownCounterTitle = PreviewMediaUploadCounter.FindTitleRetryBox(texts, fieldName == "Video");
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_clock.UtcNow >= deadline) break;
                }
                if (!count.HasValue && PreviewMediaUploadCounter.FindTitleRetryBox(texts, fieldName == "Video") is { } titleBox)
                {
                    var focusedTexts = await _workflowOcr.RecognizeBoxAsync(current.Capture, titleBox,
                        $"PreviewMedia/{fieldName}/counter-title-retry", cancellationToken);
                    count = PreviewMediaUploadCounter.Read(focusedTexts, fieldName == "Video");
                    LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} action=RetryUploadCountTitle " +
                        $"box={titleBox} count={count?.ToString() ?? "unreadable"} ocr=\"{FormatRawOcrTexts(focusedTexts)}\"");
                    texts = texts.Concat(focusedTexts).ToArray();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_clock.UtcNow >= deadline) break;
                }
            }
            else knownCounterTitle = null;
            var ready = count.HasValue && (!expectedCount.HasValue || count == expectedCount);
            stable = ready ? (count == previous ? stable + 1 : 1) : 0;
            previous = count;
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} action=WaitUploadCount " +
                $"phase={(expectedCount.HasValue ? "after_upload" : "baseline")} count={count?.ToString() ?? "unreadable"} " +
                $"expected={expectedCount?.ToString() ?? "read_baseline"} stableCount={stable} page={current.Recognition.Page} " +
                $"ocr=\"{FormatRawOcrTexts(texts)}\"");
            if (stable >= 2)
                return (current, count!.Value);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            current = await RecognizeOnceAsync(cancellationToken);
        }
        var diagnostic = SavePreviewInfoDiagnostic(current,
            GetPreviewMediaFailureDiagnosticPrefix(fieldName, "upload-count-timeout"), request, fieldName,
            "Read the media title count and confirm all selected files finished uploading", texts, workflowLog,
            $"expected={expectedCount?.ToString() ?? "baseline"}, lastCount={previous?.ToString() ?? "unreadable"}");
        throw new TimeoutException($"{fieldName} upload count was not confirmed; expected={expectedCount?.ToString() ?? "baseline"}, diagnostic={diagnostic}.");
    }

    private static string GetPreviewMediaFailureDiagnosticPrefix(string fieldName, string failureKind)
    {
        if (fieldName.StartsWith("ExtraImages", StringComparison.Ordinal))
            return $"stage1-preview-extraimages-{failureKind}";

        if (string.Equals(fieldName, "Video", StringComparison.Ordinal))
            return $"stage1-preview-video-{failureKind}";

        return $"stage1-preview-{NormalizeFileNameSegment(fieldName)}-{failureKind}";
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewMediaTargetWithScrollAsyncCore(
        UploadRequest request,
        StablePageResult preview,
        string fieldName,
        IReadOnlyList<string> preferredTargetNames,
        IReadOnlyList<string> labelTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var current = preview;
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var acceptedTokens = GetPreviewMediaAcceptedTokens(fieldName, labelTokens);

        for (var scrollAttempt = 0; scrollAttempt <= PreviewEditorMaxScrollAttempts; scrollAttempt++)
        {
            if (current.Recognition.Page != GamePage.PreviewEditor)
                current = await WaitForPageEvidenceAsync(current, Pages(GamePage.PreviewEditor), "Preview.FieldPageRecovery", ShortWait, cancellationToken);

            var preferred = FindFirstAvailableTarget(current.Recognition, preferredTargetNames);
            if (preferred != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} selectedSource={preferred.Name} " +
                    $"result=preferredTargetFound target=({FormatTarget(preferred)})");
                return new PreviewFieldTargetSelection(current, preferred, allOcrTexts, scrollAttempt, preferred.Name);
            }

            var tileMatch = await FindPreviewMediaAddTileByContextOcrAsync(
                current,
                fieldName,
                acceptedTokens,
                workflowLog,
                cancellationToken);
            allOcrTexts.AddRange(tileMatch.OcrTexts);
            if (tileMatch.IsAmbiguous)
            {
                SavePreviewInfoDiagnostic(
                    current,
                    $"stage1-preview-{NormalizeFileNameSegment(fieldName)}-media-add-tile-ambiguous",
                    request,
                    fieldName,
                    "media_add_tile OCR must be a unique match for the configured media field",
                    allOcrTexts,
                    workflowLog,
                    $"{fieldName} media_add_tile OCR matched multiple candidates. matches=[{string.Join("; ", tileMatch.Matches.Select(FormatTarget))}]");
                throw new InvalidOperationException(
                    $"{fieldName} media_add_tile OCR matched multiple candidates. matches=[{string.Join("; ", tileMatch.Matches.Select(FormatTarget))}]");
            }

            if (tileMatch.Target != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} selectedSource=media_add_tile_ocr " +
                    $"result=genericTargetOcrConfirmed target=({FormatTarget(tileMatch.Target)})");
                return new PreviewFieldTargetSelection(current, tileMatch.Target, allOcrTexts, scrollAttempt, "media_add_tile_ocr");
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} " +
                $"result=notFound preferredTargets=[{string.Join(",", preferredTargetNames)}] genericTarget=media_add_tile selectedSource=<none>");

            if (scrollAttempt == PreviewEditorMaxScrollAttempts)
                break;

            _recordAction?.Invoke($"scroll:preview_editor:{PreviewEditorScrollWheelDelta}");
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=ScrollPreviewEditor " +
                $"amount={PreviewEditorScrollWheelDelta} scrollAttempt={scrollAttempt + 1}");
            await _input.ScrollAsync(
                current.Capture.Window,
                ScrollCenterX,
                ScrollCenterY,
                PreviewEditorScrollWheelDelta,
                cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
            current = await WaitForExpectedAsync(
                $"EditPreviewInfo.{fieldName}.ScrollPreviewEditor{scrollAttempt + 1}",
                Pages(GamePage.PreviewEditor),
                ShortWait,
                stableCount: 1,
                cancellationToken);
        }

        return new PreviewFieldTargetSelection(current, null, allOcrTexts, PreviewEditorMaxScrollAttempts, "not_found");
    }

    private async Task<TagsEditRowOcrMatch> FindPreviewMediaAddTileByContextOcrAsync(
        StablePageResult current,
        string fieldName,
        IReadOnlyList<string> acceptedTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var matches = new List<AutomationTarget>();
        var candidates = FindTargets(current.Recognition, WorkflowTargetNames.MediaAddTile, 0.25)
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .ThenByDescending(target => target.Confidence)
            .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var target = candidates[index];
            var ocrTexts = await _workflowOcr.RecognizeBoxAsync(
                current.Capture,
                CreatePreviewMediaTileHeaderOcrBox(target),
                $"{WorkflowTargetNames.MediaAddTile}/{fieldName}/header",
                cancellationToken);
            allOcrTexts.AddRange(ocrTexts);
            var matched = PreviewMediaHeaderMatches(ocrTexts, acceptedTokens, fieldName);
            var reason = matched ? "accepted_field_token" : "no_field_token";
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=MediaAddTileContextOcr candidateIndex={index} " +
                $"target=({FormatTarget(target)}) raw=\"{FormatRawOcrTexts(ocrTexts)}\" " +
                $"normalized=\"{FormatNormalizedOcrTexts(ocrTexts)}\" reason={reason}");
            if (matched)
                matches.Add(target);
        }

        if (matches.Count == 1)
            return new TagsEditRowOcrMatch(matches[0], matches, allOcrTexts, false);

        return new TagsEditRowOcrMatch(null, matches, allOcrTexts, matches.Count > 1);
    }

    private static bool PreviewMediaHeaderMatches(
        IReadOnlyList<WorkflowOcrTextResult> texts, IReadOnlyList<string> acceptedTokens, string fieldName)
    {
        // Upload counts are not part of the title. Remove only a valid trailing
        // count for this field, without relaxing other OCR matching rules.
        var pattern = fieldName.StartsWith("ExtraImages", StringComparison.Ordinal)
            ? @"\(\s*[0-4]\s*/\s*4\s*\)\s*$"
            : @"\(\s*[01]\s*/\s*1\s*\)\s*$";
        var titles = texts.Select(text => text with
        {
            NormalizedText = RapidOcrWorkflowOcrService.NormalizePreviewText(
                System.Text.RegularExpressions.Regex.Replace(
                    text.Text.Normalize(System.Text.NormalizationForm.FormKC), pattern, string.Empty))
        }).ToArray();
        return PreviewCandidateOcrMatches(titles, acceptedTokens);
    }

    private static NormalizedBox CreatePreviewMediaTileHeaderOcrBox(AutomationTarget target)
    {
        var width = Math.Clamp(target.Width * 2.6, 0.18, 0.72);
        var height = Math.Clamp(target.Height * 1.3, 0.05, 0.14);
        var top = target.CenterY - target.Height / 2;
        var centerY = Math.Clamp(top - height * 0.65, height / 2, 1 - height / 2);
        return new NormalizedBox(
            Math.Clamp(target.CenterX, width / 2, 1 - width / 2),
            centerY,
            width,
            height);
    }

    private static IReadOnlyList<string> GetPreviewMediaAcceptedTokens(
        string fieldName,
        IReadOnlyList<string> fallbackTokens)
    {
        if (fieldName.StartsWith("ExtraImages", StringComparison.Ordinal))
        {
            return
            [
                "MORE SHOWCASE IMAGES",
                "SHOWCASE IMAGES",
                "\u66f4\u591a\u5c55\u793a\u56fe\u7247"
            ];
        }

        if (string.Equals(fieldName, "Video", StringComparison.Ordinal))
        {
            return
            [
                "SHOWCASE VIDEOS",
                "VIDEO",
                "\u5c55\u793a\u89c6\u9891"
            ];
        }

        return fallbackTokens;
    }

    private async Task<StablePageResult> FillPreviewMultilanguageIfNeededAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var configuredLanguages = request.Archive.LocalizedInfos
            .Where(HasConfiguredLocalizedPreviewInfo)
            .Select(NormalizeConfiguredLocalizedInfo)
            .ToList();
        ReportOperation("Editing multilingual preview details");
        if (configuredLanguages.Count == 0)
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo field=Multilanguage action=SkipMultilanguage reason=no configured languages result=Skipped");
            return preview;
        }

        var desiredLanguages = configuredLanguages
            .Select(entry => entry.LanguageCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var context = CreateMultilanguageContext(desiredLanguages);

        var selection = await LocatePreviewMultilanguageTargetWithScrollAsync(
            request,
            preview,
            workflowLog,
            cancellationToken);
        context = context with { OcrTexts = selection.OcrTexts };
        if (selection.Target == null)
        {
            SaveMultilanguageDiagnostic(
                selection.Page,
                "stage1-preview-multilanguage-target-missing",
                request,
                context,
                workflowLog,
                "Multilanguage Settings target was not detected.");
            throw new InvalidOperationException("Multilanguage Settings target was not detected.");
        }

        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=ClickSettings " +
            $"selectedSource={selection.SearchDirection} target=({FormatTarget(selection.Target)})");
        var openResult = await ClickMultilanguageSettingsAndWaitEditorAsync(
            request,
            selection,
            context,
            workflowLog,
            cancellationToken);
        var editor = openResult.Editor;
        context = openResult.Context;

        var clientResult = await DetectMultilanguageClientLanguageAsync(request, editor, context, workflowLog, cancellationToken);
        context = clientResult.Context;
        var clientLanguage = clientResult.ClientLanguage;
        var defaultLanguage = GetClientDefaultLanguageCode(clientLanguage);
        context = context with { DefaultLanguage = defaultLanguage };

        var addButton = clientResult.AddButton;
        var dialogResult = await ClickAddLanguageButtonAndWaitDialogAsync(
            request,
            editor,
            addButton,
            clientLanguage,
            context,
            workflowLog,
            cancellationToken);
        var dialog = dialogResult.Dialog;
        context = dialogResult.Context;

        var addLanguage = BuildAddLanguageOptions(dialog, clientLanguage);
        context = context with { AddLanguageLayout = addLanguage.Select(option => $"{option.Language.Code}:{option.Column}:{option.RowIndex}").ToList() };
        var selectedDiagnostics = addLanguage
            .Select(option => InspectLanguageOptionSelection(dialog.Capture, option))
            .ToList();
        foreach (var item in selectedDiagnostics)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=OpenCvLanguageOptionSelection " +
                $"language={item.Language} column={item.Column} rowIndex={item.RowIndex} " +
                $"optionBox=({item.OptionBox.Name} conf={item.OptionBox.Confidence:F2} cx={item.OptionBox.CenterX:F3} cy={item.OptionBox.CenterY:F3} w={item.OptionBox.Width:F3} h={item.OptionBox.Height:F3}) " +
                $"radioRoi=({item.RadioRoi.CenterX:F3},{item.RadioRoi.CenterY:F3},{item.RadioRoi.Width:F3},{item.RadioRoi.Height:F3}) " +
                $"greenPixelCount={item.GreenPixelCount} backgroundBrightness={item.BackgroundBrightness:F1} selected={item.Selected} evidence=\"{item.Evidence}\"");
        }
        var selectedBefore = selectedDiagnostics
            .Where(item => item.Selected)
            .Select(item => item.Language)
            .Append(GetClientDefaultLanguageCode(clientLanguage))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var missing = desiredLanguages
            .Where(language => !selectedBefore.Contains(language, StringComparer.OrdinalIgnoreCase))
            .ToList();
        context = context with
        {
            SelectedLanguagesBefore = selectedBefore,
            MissingLanguages = missing,
            LanguageSelectionDiagnostics = selectedDiagnostics
        };
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=InspectAddLanguage clientLanguage={clientLanguage} " +
            $"desired=[{string.Join(",", desiredLanguages)}] selectedBefore=[{string.Join(",", selectedBefore)}] missing=[{string.Join(",", missing)}]");

        foreach (var missingLanguage in missing)
        {
            addLanguage = BuildAddLanguageOptions(dialog, clientLanguage);
            var option = addLanguage.FirstOrDefault(item => string.Equals(item.Language.Code, missingLanguage, StringComparison.OrdinalIgnoreCase));
            if (option == null)
            {
                SaveMultilanguageDiagnostic(
                    dialog,
                    "stage1-preview-multilanguage-language-option-missing",
                    request,
                    context,
                    workflowLog,
                    $"Configured language option was not found in Add Language layout. language={missingLanguage}.");
                throw new InvalidOperationException($"Configured language option was not found in Add Language layout. language={missingLanguage}.");
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=SelectMissingLanguage " +
                $"language={missingLanguage} column={option.Column} rowIndex={option.RowIndex} target=({FormatTarget(option.OptionTarget)})");
            await ClickSpecificTargetAsync(dialog, option.OptionTarget, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(400), cancellationToken);
            var refresh = await RefreshAddLanguageDialogAfterSelectionAsync(
                request,
                context,
                workflowLog,
                missingLanguage,
                cancellationToken);
            dialog = refresh.Dialog;
            context = refresh.Context;
        }

        // Confirm is disabled until a new language is selected, for both new
        // saves and updates. Existing selections need no submission.
        if (missing.Count == 0)
        {
            ReportOperation("All required languages already selected; closing the language picker");
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=CloseAddLanguage reason=no_missing_languages isUpdate={request.IsUpdate} key=Escape");
            _activation.RestoreAndActivate(dialog.Capture.Window);
            _recordAction?.Invoke("press_key:Escape");
            await _input.PressKeyAsync(dialog.Capture.Window, GameVirtualKey.Escape, cancellationToken);
        }
        else
        {
            var confirm = await FindLanguageDialogConfirmTargetAsync(dialog, cancellationToken);
            if (confirm.Target == null)
            {
                SaveMultilanguageDiagnostic(
                    dialog,
                    "stage1-preview-multilanguage-language-confirm-missing",
                    request,
                    context,
                    workflowLog,
                    "Add Language OK/Confirm button was not detected or OCR-confirmed.");
                throw new InvalidOperationException("Add Language OK/Confirm button was not detected or OCR-confirmed.");
            }

            context = context with { OcrTexts = context.OcrTexts.Concat(confirm.OcrTexts).ToList() };
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=ConfirmAddLanguage target=({FormatTarget(confirm.Target)})");
            await ClickSpecificTargetAsync(dialog, confirm.Target, cancellationToken);
        }

        var editorResult = await WaitForMultilanguageEditorContextAsync(
            request,
            context,
            workflowLog,
            "EditPreviewInfo.Multilanguage.WaitEditorAfterLanguageDialog",
            "stage1-preview-multilanguage-return-editor-failed",
            cancellationToken);
        editor = editorResult.Editor;
        context = editorResult.Context;

        var selectedAfter = selectedBefore
            .Concat(missing)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        context = context with { SelectedLanguagesAfter = selectedAfter };

        var tableHeaderOcr = await RecognizeMultilanguageTableHeaderOcrAsync(editor, cancellationToken);
        var earlyAccessColumnEnabled = request.Archive.IsEarlyAccess ||
            OcrTextsContainAny(tableHeaderOcr, GetMultilanguageEarlyAccessHeaderTokens());
        var tableColumns = GetMultilanguageTableColumns(earlyAccessColumnEnabled);
        context = context with
        {
            EarlyAccessColumnEnabled = earlyAccessColumnEnabled,
            DetectedTableHeaders = tableHeaderOcr,
            OcrTexts = context.OcrTexts.Concat(tableHeaderOcr).ToList()
        };
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=DetectTableColumns " +
            $"earlyAccessColumnEnabled={earlyAccessColumnEnabled} headers=\"{FormatRawOcrTexts(tableHeaderOcr)}\" " +
            $"columns=[{string.Join(",", tableColumns)}]");

        var tableOrder = GetMultilanguageTableOrder(clientLanguage);
        var selectedTableLanguages = tableOrder
            .Where(language => selectedAfter.Contains(language, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var skippedDefaultLanguages = configuredLanguages
            .Where(entry => string.Equals(entry.LanguageCode, defaultLanguage, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.LanguageCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var configuredByLanguage = configuredLanguages
            .Where(entry => !string.Equals(entry.LanguageCode, defaultLanguage, StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => entry.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var fillTargetLanguages = configuredByLanguage.Keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedFillLanguages = selectedTableLanguages
            .Where(language => configuredByLanguage.ContainsKey(language))
            .ToList();
        var configuredButNotSelected = fillTargetLanguages
            .Where(language => !selectedTableLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (configuredButNotSelected.Count > 0)
        {
            SaveMultilanguageDiagnostic(
                editor,
                "stage1-preview-multilanguage-configured-language-not-selected",
                request,
                context,
                workflowLog,
                $"Configured languages were not selected in the multilanguage table. selected=[{string.Join(",", selectedTableLanguages)}], missing=[{string.Join(",", configuredButNotSelected)}].");
            throw new InvalidOperationException(
                $"Configured languages were not selected in the multilanguage table: [{string.Join(",", configuredButNotSelected)}].");
        }

        var skippedUnconfiguredLanguages = configuredLanguages
            .Where(entry => !string.Equals(entry.LanguageCode, defaultLanguage, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.LanguageCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(language => !orderedFillLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
            .ToList();
        context = context with
        {
            SkippedDefaultLanguages = skippedDefaultLanguages,
            FillTargetLanguages = fillTargetLanguages
        };

        foreach (var language in skippedDefaultLanguages)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=SkipDefaultLanguage " +
                $"language={language} reason=default row already filled by main preview fields");
        }

        foreach (var language in skippedUnconfiguredLanguages)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=SkipConfiguredLanguage " +
                $"language={language} reason=not_in_selected_table_order");
        }

        await _input.ScrollAsync(editor.Capture.Window, .40, .52, 12, cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
        var topWindow = await WaitForMultilanguageEditorContextAsync(request, context, workflowLog,
            "Multilanguage.InitialViewport", "stage1-multilanguage-initial-viewport-failed", cancellationToken);
        editor = topWindow.Editor; context = topWindow.Context;
        var pendingLanguages = orderedFillLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Stage1MultilanguageTableGeometry? viewportGeometry = null;
        var geometryWindow = editor.Capture.Window;
        var scrollAttempts = 0;
        var unchangedScrolls = 0;
        while (pendingLanguages.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var labels = LanguageLabelTemplates.Locate(editor.Capture.Bitmap);
            LogWorkflow(workflowLog, $"step=Multilanguage action=MatchLanguageRows labels=[{string.Join(",", labels.Select(m => $"{m.Language}@{m.Y:F3}:{m.Score:F3}"))}] pending=[{string.Join(",",pendingLanguages)}]");
            var label = labels.FirstOrDefault(m => pendingLanguages.Contains(m.Language) && m.Y > .235 && m.Y < .755);
            if (label != null)
            {
                if (viewportGeometry == null || geometryWindow != editor.Capture.Window)
                {
                    editor = await AddMissingEditIconsAsync(editor, cancellationToken);
                    labels = LanguageLabelTemplates.Locate(editor.Capture.Bitmap);
                    label = labels.FirstOrDefault(m => pendingLanguages.Contains(m.Language) && m.Y > .235 && m.Y < .755);
                    if (label == null) continue;
                    var inferred = InferMultilanguageTableGeometry(editor, Math.Max(2, labels.Count), tableColumns, tableHeaderOcr, workflowLog);
                    viewportGeometry = inferred.Geometry ?? throw new InvalidOperationException("Cannot determine multilingual columns for template-confirmed language row.");
                    geometryWindow = editor.Capture.Window;
                    LogWorkflow(workflowLog, "step=Multilanguage action=ColumnGeometry source=inferred");
                }
                else LogWorkflow(workflowLog, "step=Multilanguage action=ColumnGeometry source=viewport_cache editIconSupplementSkipped=True");
                var row = new Stage1MultilanguageComputedRow(labels.ToList().IndexOf(label), label.Language, label.Y);
                context = context with { ComputedVisibleRows = labels.Select((m,i) => new Stage1MultilanguageComputedRow(i,m.Language,m.Y)).ToList(), TableGeometry = viewportGeometry };
                var rowResult = await FillConfiguredMultilanguageRowAsync(request, editor, configuredByLanguage[label.Language],
                    row, viewportGeometry, earlyAccessColumnEnabled, context, workflowLog, cancellationToken);
                editor = rowResult.Editor; context = rowResult.Context;
                // A continuation may have re-inferred geometry during this row.
                if (!ReferenceEquals(viewportGeometry, context.TableGeometry)) viewportGeometry = null;
                pendingLanguages.Remove(label.Language);
                continue;
            }
            if (++scrollAttempts > 20 || unchangedScrolls >= 3 || labels.Count == 0)
            {
                SaveMultilanguageDiagnostic(editor, "stage1-multilanguage-label-navigation-failed", request, context, workflowLog,
                    $"Language template navigation failed; pending={string.Join(",",pendingLanguages)}; unchangedScrolls={unchangedScrolls}.");
                throw new InvalidOperationException("Cannot locate remaining language rows reliably. Check the multilingual table and continue.");
            }
            viewportGeometry = null; // Scrolling starts a new viewport; infer its columns again.
            // Put the wheel inside the actual table, not outside its right edge.
            await _input.ScrollAsync(editor.Capture.Window, .40, .52, -2, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            var next = await WaitForMultilanguageEditorContextAsync(request, context, workflowLog,
                "Multilanguage.ScrollAndVerify", "stage1-multilanguage-scroll-failed", cancellationToken);
            editor = next.Editor; context = next.Context;
            var after = LanguageLabelTemplates.Locate(editor.Capture.Bitmap);
            var moved = LanguageLabelTemplates.HasMoved(labels, after);
            unchangedScrolls = moved ? 0 : unchangedScrolls + 1;
            LogWorkflow(workflowLog, $"step=Multilanguage action=VerifyScroll moved={moved} unchanged={unchangedScrolls} attempt={scrollAttempts}");
        }

        var close = FindTargets(editor.Recognition, WorkflowTargetNames.MultilanguageCloseButton).FirstOrDefault();
        if (close != null)
        {
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=CloseEditor target=({FormatTarget(close)})");
            await ClickSpecificTargetAsync(editor, close, cancellationToken);
        }
        else
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo field=Multilanguage action=CloseEditor key=Escape reason=close_button_missing");
            _activation.RestoreAndActivate(editor.Capture.Window);
            await _input.PressKeyAsync(editor.Capture.Window, GameVirtualKey.Escape, cancellationToken);
        }

        return await WaitForExpectedAsync(
            "EditPreviewInfo.Multilanguage.WaitPreviewEditorAfterClose",
            Pages(GamePage.PreviewEditor),
            MediumWait,
            stableCount: 1,
            cancellationToken);
    }

    private (List<Stage1MultilanguageComputedRow> VisibleRows, Stage1MultilanguageTableGeometry Geometry, Stage1MultilanguageContext Context)
        ComputeMultilanguageVisibleRowsAndGeometry(
            UploadRequest request,
            StablePageResult editor,
            Stage1MultilanguageContext context,
            IReadOnlyList<string> tableOrder,
            IReadOnlyList<string> selectedTableLanguages,
            int topVisibleIndex,
            IReadOnlyList<string> tableColumns,
            IReadOnlyList<WorkflowOcrTextResult> tableHeaderOcr,
            Stage1ClientLanguage clientLanguage,
            List<string> workflowLog)
    {
        var visibleRows = selectedTableLanguages
            .Skip(topVisibleIndex)
            .Take(MultilanguageTableMaxVisibleRowsWithoutScroll)
            .Select((language, index) => new Stage1MultilanguageComputedRow(index, language, 0))
            .ToList();
        if (visibleRows.Count == 0)
        {
            context = context with
            {
                TableLanguageOrder = tableOrder,
                ComputedVisibleRows = visibleRows,
                TableGeometry = null
            };
            SaveMultilanguageDiagnostic(
                editor,
                "stage1-preview-multilanguage-visible-window-empty",
                request,
                context,
                workflowLog,
                $"Multilanguage visible row window is empty. topVisibleIndex={topVisibleIndex}, selected=[{string.Join(",", selectedTableLanguages)}].");
            throw new InvalidOperationException($"Multilanguage visible row window is empty. topVisibleIndex={topVisibleIndex}.");
        }

        var geometryResult = InferMultilanguageTableGeometry(editor, visibleRows.Count, tableColumns, tableHeaderOcr, workflowLog);
        var geometry = geometryResult.Geometry;
        context = context with
        {
            TableTextEditIcons = geometryResult.TextEditIcons,
            TableRowClusters = geometryResult.RowClusters,
            TableColumnClusters = geometryResult.ColumnClusters
        };
        if (geometry == null)
        {
            context = context with
            {
                TableLanguageOrder = tableOrder,
                ComputedVisibleRows = visibleRows,
                TableGeometry = null
            };
            SaveMultilanguageDiagnostic(
                editor,
                "stage1-preview-multilanguage-table-geometry-missing",
                request,
                context,
                workflowLog,
                $"Multilanguage table geometry could not be inferred from detected table cells or text_edit_icon clusters. topVisibleIndex={topVisibleIndex}, reason={geometryResult.Reason}");
            throw new InvalidOperationException(
                $"Multilanguage table geometry could not be inferred from detected table cells or text_edit_icon clusters. topVisibleIndex={topVisibleIndex}, reason={geometryResult.Reason}");
        }

        visibleRows = visibleRows
            .Select(row => row with { CenterY = geometry.FirstRowCenterY + geometry.RowHeight * row.RowIndex })
            .ToList();
        context = context with
        {
            TableLanguageOrder = tableOrder,
            ComputedVisibleRows = visibleRows,
            TableGeometry = geometry
        };
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=ComputeTableRows clientLanguage={clientLanguage} " +
            $"topVisibleIndex={topVisibleIndex} selected=[{string.Join(",", selectedTableLanguages)}] " +
            $"visibleRows=[{string.Join(",", visibleRows.Select(row => $"{row.RowIndex}:{row.Language}@{row.CenterY:F3}"))}]");
        return (visibleRows, geometry, context);
    }

    private async Task<(StablePageResult Editor, Stage1MultilanguageContext Context)> ScrollMultilanguageTableDownOneRowAsync(
        UploadRequest request,
        StablePageResult editor,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        int topVisibleIndex,
        CancellationToken cancellationToken)
    {
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=ScrollTableDown " +
            $"fromTopVisibleIndex={topVisibleIndex} amount={MultilanguageTableScrollWheelDelta}");
        _recordAction?.Invoke($"scroll:multilanguage_table:{MultilanguageTableScrollWheelDelta}");
        await _input.ScrollAsync(
            editor.Capture.Window,
            MultilanguageTableScrollX,
            MultilanguageTableScrollY,
            MultilanguageTableScrollWheelDelta,
            cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(700), cancellationToken);
        return await WaitForMultilanguageEditorContextAsync(
            request,
            context,
            workflowLog,
            $"EditPreviewInfo.Multilanguage.ScrollTableDown{topVisibleIndex + 1}",
            "stage1-preview-multilanguage-scroll-table-down-failed",
            cancellationToken);
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewMultilanguageTargetWithScrollAsyncCore(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        const string fieldName = "Multilanguage";
        var current = preview;
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var acceptedTokens = GetPreviewMultilanguageEntryAcceptedTokens();
        var rejectedTokens = GetPreviewMultilanguageEntryRejectedTokens();

        for (var scrollAttempt = 0; scrollAttempt <= PreviewEditorMaxScrollAttempts; scrollAttempt++)
        {
            if (current.Recognition.Page != GamePage.PreviewEditor)
                current = await WaitForPageEvidenceAsync(current, Pages(GamePage.PreviewEditor), "Preview.FieldPageRecovery", ShortWait, cancellationToken);

            var preferred = FindTargets(current.Recognition, WorkflowTargetNames.PreviewMultilanguageArea, 0.25)
                .Where(IsRightPreviewEditorTarget)
                .OrderByDescending(target => target.Confidence)
                .ThenByDescending(target => target.Width * target.Height)
                .FirstOrDefault();
            if (preferred != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} scrollAttempt={scrollAttempt} " +
                    $"selectedSource=preview_multilanguage_area result=preferredTargetFound target=({FormatTarget(preferred)})");
                return new PreviewFieldTargetSelection(current, preferred, allOcrTexts, scrollAttempt, "preview_multilanguage_area");
            }

            var editRowMatch = await FindPreviewMultilanguageEditRowByOcrAsync(
                current,
                acceptedTokens,
                rejectedTokens,
                workflowLog,
                cancellationToken);
            allOcrTexts.AddRange(editRowMatch.OcrTexts);
            if (editRowMatch.IsAmbiguous)
            {
                SavePreviewInfoDiagnostic(
                    current,
                    "stage1-preview-multilanguage-edit-row-ambiguous",
                    request,
                    fieldName,
                    "Multilanguage edit_row OCR must be a unique match",
                    allOcrTexts,
                    workflowLog,
                    $"Multilanguage edit_row OCR matched multiple candidates. matches=[{string.Join("; ", editRowMatch.Matches.Select(FormatTarget))}]");
                throw new InvalidOperationException(
                    $"Multilanguage edit_row OCR matched multiple candidates. matches=[{string.Join("; ", editRowMatch.Matches.Select(FormatTarget))}]");
            }

            if (editRowMatch.Target != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} scrollAttempt={scrollAttempt} " +
                    $"selectedSource=edit_row_ocr result=genericTargetOcrConfirmed target=({FormatTarget(editRowMatch.Target)})");
                return new PreviewFieldTargetSelection(current, editRowMatch.Target, allOcrTexts, scrollAttempt, "edit_row_ocr");
            }

            var iconMatch = await FindPreviewMultilanguageTextEditIconByContextOcrAsync(
                current,
                acceptedTokens,
                rejectedTokens,
                workflowLog,
                cancellationToken);
            allOcrTexts.AddRange(iconMatch.OcrTexts);
            if (iconMatch.IsAmbiguous)
            {
                SavePreviewInfoDiagnostic(
                    current,
                    "stage1-preview-multilanguage-text-edit-icon-ambiguous",
                    request,
                    fieldName,
                    "Multilanguage text_edit_icon context OCR must be a unique match",
                    allOcrTexts,
                    workflowLog,
                    $"Multilanguage text_edit_icon context OCR matched multiple candidates. matches=[{string.Join("; ", iconMatch.Matches.Select(FormatTarget))}]");
                throw new InvalidOperationException(
                    $"Multilanguage text_edit_icon context OCR matched multiple candidates. matches=[{string.Join("; ", iconMatch.Matches.Select(FormatTarget))}]");
            }

            if (iconMatch.Target != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} scrollAttempt={scrollAttempt} " +
                    $"selectedSource=text_edit_icon_context_ocr result=contextOcrConfirmed target=({FormatTarget(iconMatch.Target)})");
                return new PreviewFieldTargetSelection(current, iconMatch.Target, allOcrTexts, scrollAttempt, "text_edit_icon_context_ocr");
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} scrollAttempt={scrollAttempt} " +
                "result=notFound preferredTargets=[preview_multilanguage_area] genericTargets=[edit_row,text_edit_icon] selectedSource=<none>");

            if (scrollAttempt == PreviewEditorMaxScrollAttempts)
                break;

            _recordAction?.Invoke($"scroll:preview_editor:{PreviewEditorScrollWheelDelta}");
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=ScrollPreviewEditor " +
                $"amount={PreviewEditorScrollWheelDelta} scrollAttempt={scrollAttempt + 1}");
            await _input.ScrollAsync(
                current.Capture.Window,
                ScrollCenterX,
                ScrollCenterY,
                PreviewEditorScrollWheelDelta,
                cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
            current = await WaitForExpectedAsync(
                $"EditPreviewInfo.{fieldName}.ScrollPreviewEditor{scrollAttempt + 1}",
                Pages(GamePage.PreviewEditor),
                ShortWait,
                stableCount: 1,
                cancellationToken);
        }

        return new PreviewFieldTargetSelection(current, null, allOcrTexts, PreviewEditorMaxScrollAttempts, "not_found");
    }

    private async Task<(StablePageResult Editor, Stage1MultilanguageContext Context)> ClickMultilanguageSettingsAndWaitEditorAsync(
        UploadRequest request,
        PreviewFieldTargetSelection selection,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        const string fieldName = "Multilanguage";
        var current = selection.Page;
        StablePageResult? last = current;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=ClickSettingsAttempt " +
                $"attempt={attempt} selectedSource={selection.SearchDirection} target=({FormatTarget(selection.Target!)})");
            await ClickSpecificTargetAsync(current, selection.Target!, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);
            last = await RecognizeOnceAsync(cancellationToken);
            var contextResult = await DetectMultilanguageEditorContextAsync(last, cancellationToken);
            context = context with
            {
                OcrTexts = context.OcrTexts.Concat(contextResult.OcrTexts).ToList(),
                PageContextEvidence = context.PageContextEvidence.Append(contextResult.Evidence).ToList()
            };
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=VerifyOpenAfterClick " +
                $"attempt={attempt} rawPage={contextResult.Evidence.RawPage} contextualPage={contextResult.Evidence.ContextualPage} " +
                $"pageAfterClick={contextResult.Evidence.ContextualPage} confidence={last.Recognition.Confidence:F2} " +
                $"ocrTitle=\"{contextResult.Evidence.OcrTitle}\" tableHeaderTokens=[{string.Join(", ", contextResult.Evidence.TableHeaderTokens)}] " +
                $"addLanguageButtonOcr=\"{contextResult.Evidence.AddLanguageButtonOcr}\" accepted={contextResult.Evidence.Accepted} " +
                $"reason=\"{contextResult.Evidence.Reason}\"");

            if (contextResult.Evidence.ContextualPage == GamePage.MultiLanguageEditor)
                return (WithContextualPage(last, GamePage.MultiLanguageEditor), context);

            if (last.Recognition.Page != GamePage.PreviewEditor)
                break;

            current = last;
        }

        var diagnostic = SaveMultilanguageDiagnostic(
            last,
            "stage1-preview-multilanguage-open-failed",
            request,
            context,
            workflowLog,
            $"Multilanguage Settings click did not open MultilanguageEditor. lastPage={last?.Recognition.Page}.");
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field={fieldName} action=OpenFailed " +
            $"failureReason=did_not_enter_multilanguage_editor screenshotPath={diagnostic ?? "<not-saved>"}");
        throw new InvalidOperationException(
            $"Multilanguage Settings click did not open MultilanguageEditor after 3 attempts. diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private static bool HasTemplateMultilanguageTable(StablePageResult result)
    {
        if (result.Capture.IsLikelyBlackFrame || result.Recognition.Page is not (GamePage.Unknown or GamePage.MultiLanguageEditor)) return false;
        if (result.Recognition.Targets.Any(t => t.Confidence >= .35 &&
            t.Name is WorkflowTargetNames.TextInputArea or WorkflowTargetNames.LanguageOption or WorkflowTargetNames.ChoiceOption)) return false;
        var labels = LanguageLabelTemplates.Locate(result.Capture.Bitmap);
        if (labels.Count < 3) return false;
        // Text labels must agree with actual table edit rows; labels alone behind a modal are insufficient.
        return labels.Count(label => result.Recognition.Targets.Any(t => t.Name == WorkflowTargetNames.TextEditIcon &&
            t.Confidence >= .35 && t.CenterX > .35 && Math.Abs(t.CenterY-label.Y)<.015)) >= 3;
    }

    private async Task<(Stage1MultilanguagePageContextEvidence Evidence, IReadOnlyList<WorkflowOcrTextResult> OcrTexts)>
        DetectMultilanguageEditorContextAsync(
            StablePageResult result,
            CancellationToken cancellationToken)
    {
        if (result.Recognition.Page == GamePage.MultiLanguageEditor)
        {
            var directEvidence = new Stage1MultilanguagePageContextEvidence(
                result.Recognition.Page,
                GamePage.MultiLanguageEditor,
                string.Empty,
                [],
                string.Empty,
                true,
                "raw page is MultiLanguageEditor");
            return (directEvidence, []);
        }

        if (HasTemplateMultilanguageTable(result))
            return (new Stage1MultilanguagePageContextEvidence(result.Recognition.Page, GamePage.MultiLanguageEditor,
                string.Empty, [], string.Empty, true, "accepted by language templates and edit rows"), []);

        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var titleTexts = await _workflowOcr.RecognizeBoxAsync(
            result.Capture,
            new NormalizedBox(0.50, 0.16, 0.78, 0.14),
            "MultilanguageEditor/title",
            cancellationToken);
        allOcrTexts.AddRange(titleTexts);

        var headerTexts = await _workflowOcr.RecognizeBoxAsync(
            result.Capture,
            new NormalizedBox(0.55, 0.30, 0.80, 0.12),
            "MultilanguageEditor/table_header",
            cancellationToken);
        allOcrTexts.AddRange(headerTexts);

        var addButtonTexts = new List<WorkflowOcrTextResult>();
        foreach (var addButton in FindMultilanguageAddLanguageButtonCandidates(result).Take(3))
        {
            var texts = await _workflowOcr.RecognizeTargetAsync(result.Capture, addButton, cancellationToken);
            addButtonTexts.AddRange(texts);
            allOcrTexts.AddRange(texts);
        }

        var titleMatched = OcrTextsContainAny(titleTexts, GetMultilanguageEditorTitleTokens());
        var headerTokens = GetMatchedMultilanguageEditorHeaderTokens(headerTexts);
        var headerMatched = headerTokens.Count >= 3;
        var addButtonMatched = OcrTextsContainAny(addButtonTexts, ["ADD LANGUAGE", "ADDLANGUAGE", "\u6dfb\u52a0\u8bed\u8a00"]);
        var accepted = titleMatched || headerMatched || addButtonMatched;
        var reason = accepted
            ? titleMatched
                ? "accepted by multilanguage title OCR"
                : headerMatched
                    ? "accepted by multilanguage table header OCR"
                    : "accepted by Add Language button OCR"
            : "rejected because no multilanguage title, table header, or Add Language OCR was confirmed";

        var evidence = new Stage1MultilanguagePageContextEvidence(
            result.Recognition.Page,
            accepted ? GamePage.MultiLanguageEditor : result.Recognition.Page,
            FormatRawOcrTexts(titleTexts),
            headerTokens,
            FormatRawOcrTexts(addButtonTexts),
            accepted,
            reason);
        return (evidence, allOcrTexts);
    }

    private async Task<(StablePageResult Dialog, Stage1MultilanguageContext Context)> WaitForAddLanguageDialogContextAsyncCore(
        UploadRequest request,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        StablePageResult? last = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            last = await RecognizeOnceAsync(cancellationToken);
            var contextResult = await DetectAddLanguageDialogContextAsync(last, cancellationToken);
            context = context with
            {
                OcrTexts = context.OcrTexts.Concat(contextResult.OcrTexts).ToList(),
                AddLanguageDialogContextEvidence = context.AddLanguageDialogContextEvidence.Append(contextResult.Evidence).ToList()
            };
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=WaitAddLanguageDialog attempt={attempt} " +
                $"rawPage={contextResult.Evidence.RawPage} contextualPage={contextResult.Evidence.ContextualPage} " +
                $"ocrTitle=\"{contextResult.Evidence.OcrTitle}\" buttonOcr=\"{contextResult.Evidence.ButtonOcr}\" " +
                $"choiceOptionCount={contextResult.Evidence.ChoiceOptionCount} modalDialogCount={contextResult.Evidence.ModalDialogCount} " +
                $"accepted={contextResult.Evidence.Accepted} reason=\"{contextResult.Evidence.Reason}\"");

            if (contextResult.Evidence.Accepted &&
                contextResult.Evidence.ContextualPage == GamePage.LanguageSelectDialog)
                return (WithContextualPage(last, GamePage.LanguageSelectDialog), context);

            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
        }

        var diagnostic = SaveMultilanguageDiagnostic(
            last,
            "stage1-preview-multilanguage-add-language-dialog-open-failed",
            request,
            context,
            workflowLog,
            $"Add Language dialog was not contextually confirmed. lastPage={last?.Recognition.Page}.");
        LogWorkflow(workflowLog, 
            "step=EditPreviewInfo field=Multilanguage action=WaitAddLanguageDialogFailed " +
            $"failureReason=add_language_dialog_not_contextually_confirmed screenshotPath={diagnostic ?? "<not-saved>"}");
        throw new InvalidOperationException(
            $"Add Language dialog was not contextually confirmed after clicking Add Language. diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<(StablePageResult Dialog, Stage1MultilanguageContext Context)> ClickAddLanguageButtonAndWaitDialogAsync(
        UploadRequest request,
        StablePageResult initialEditor,
        AutomationTarget initialAddButton,
        Stage1ClientLanguage clientLanguage,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        var editor = initialEditor;
        var lastRawPage = initialEditor.Recognition.Page;
        var lastContextualPage = initialEditor.Recognition.Page;
        var lastAddButton = initialAddButton;
        string? foregroundBeforeClick = null;
        string? foregroundAfterClick = null;
        var failureReason = "Add Language dialog did not open.";

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    var refreshed = await RecognizeOnceAsync(cancellationToken);
                    var editorContext = await DetectMultilanguageEditorContextAsync(refreshed, cancellationToken);
                    context = context with
                    {
                        OcrTexts = context.OcrTexts.Concat(editorContext.OcrTexts).ToList(),
                        PageContextEvidence = context.PageContextEvidence.Append(editorContext.Evidence).ToList()
                    };
                    lastRawPage = editorContext.Evidence.RawPage;
                    lastContextualPage = editorContext.Evidence.ContextualPage;
                    LogWorkflow(workflowLog, 
                        $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageRefreshEditor attempt={attempt} " +
                        $"rawPage={lastRawPage} contextualPage={lastContextualPage} confidence={refreshed.Recognition.Confidence:F2} " +
                        $"reason=\"{editorContext.Evidence.Reason}\"");

                    if (lastContextualPage != GamePage.MultiLanguageEditor)
                    {
                        failureReason = $"Expected MultilanguageEditor before retrying Add Language click, got {lastContextualPage}.";
                        SaveAddLanguageClickDiagnostic(
                            refreshed,
                            request,
                            context,
                            workflowLog,
                            new Stage1AddLanguageClickDiagnostic(
                                clientLanguage,
                                attempt,
                                ToDiagnosticTarget(initialAddButton),
                                lastAddButton == null ? null : ToDiagnosticTarget(lastAddButton),
                                lastRawPage,
                                lastContextualPage,
                                foregroundBeforeClick,
                                foregroundAfterClick,
                                failureReason));
                        throw new InvalidOperationException(failureReason);
                    }

                    editor = WithContextualPage(refreshed, GamePage.MultiLanguageEditor);
                    var addButtonMatch = await FindAddLanguageButtonForClickAsync(editor, workflowLog, cancellationToken);
                    context = context with { OcrTexts = context.OcrTexts.Concat(addButtonMatch.OcrTexts).ToList() };
                    if (addButtonMatch.Target == null)
                    {
                        failureReason = "Add Language button was not OCR-confirmed on retry.";
                        LogWorkflow(workflowLog, 
                            $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageRetry attempt={attempt} reason=add_language_button_not_ocr_confirmed");
                        continue;
                    }

                    lastAddButton = addButtonMatch.Target;
                }

                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageAttempt attempt={attempt} " +
                    $"target=({FormatTarget(lastAddButton)}) clientLanguage={clientLanguage}");
                _activation.RestoreAndActivate(editor.Capture.Window);
                foregroundBeforeClick = SafeGetForegroundDiagnostic(editor.Capture.Window);
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageBeforeClick attempt={attempt} foreground=({foregroundBeforeClick})");

                try
                {
                    await ClickSpecificTargetAsync(editor, lastAddButton, cancellationToken);
                    LogWorkflow(workflowLog, 
                        $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageReturned attempt={attempt}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failureReason = $"{ex.GetType().Name}: {ex.Message}";
                    LogWorkflow(workflowLog, 
                        $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageException attempt={attempt} " +
                        $"type={ex.GetType().Name} message=\"{ex.Message}\"");
                    LogWorkflow(workflowLog, 
                        $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageRetry attempt={attempt} reason=click_exception");
                    continue;
                }

                foregroundAfterClick = SafeGetForegroundDiagnostic(editor.Capture.Window);
                await DelayWithTimingAsync(TimeSpan.FromMilliseconds(700), cancellationToken);
                var dialogResult = await WaitForAddLanguageDialogContextAttemptAsync(context, workflowLog, cancellationToken);
                context = dialogResult.Context;
                lastRawPage = dialogResult.Evidence.RawPage;
                lastContextualPage = dialogResult.Evidence.ContextualPage;
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageVerify attempt={attempt} " +
                    $"rawPage={lastRawPage} contextualPage={lastContextualPage} confidence={dialogResult.Page.Recognition.Confidence:F2}");

                if (dialogResult.Evidence.Accepted &&
                    lastContextualPage == GamePage.LanguageSelectDialog)
                {
                    LogWorkflow(workflowLog, 
                        $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageSucceeded attempt={attempt}");
                    return (WithContextualPage(dialogResult.Page, GamePage.LanguageSelectDialog), context);
                }

                if (lastContextualPage == GamePage.MultiLanguageEditor ||
                    lastRawPage == GamePage.Unknown)
                {
                    failureReason = lastContextualPage == GamePage.MultiLanguageEditor
                        ? "still_on_multilanguage_editor"
                        : "unknown_after_click";
                    LogWorkflow(workflowLog, 
                        $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageRetry attempt={attempt} reason={failureReason}");
                    continue;
                }

                failureReason = $"unexpected_page_after_click:{lastContextualPage}";
                SaveAddLanguageClickDiagnostic(
                    dialogResult.Page,
                    request,
                    context,
                    workflowLog,
                    new Stage1AddLanguageClickDiagnostic(
                        clientLanguage,
                        attempt,
                        ToDiagnosticTarget(initialAddButton),
                        ToDiagnosticTarget(lastAddButton),
                        lastRawPage,
                        lastContextualPage,
                        foregroundBeforeClick,
                        foregroundAfterClick,
                        failureReason));
                throw new InvalidOperationException($"Add Language click reached unexpected page {lastContextualPage}.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageFailed attempts={maxAttempts} reason={failureReason}");
        var last = await RecognizeOnceAsync(cancellationToken);
        var finalContext = await DetectMultilanguageEditorContextAsync(last, cancellationToken);
        context = context with
        {
            OcrTexts = context.OcrTexts.Concat(finalContext.OcrTexts).ToList(),
            PageContextEvidence = context.PageContextEvidence.Append(finalContext.Evidence).ToList()
        };
        SaveAddLanguageClickDiagnostic(
            last,
            request,
            context,
            workflowLog,
            new Stage1AddLanguageClickDiagnostic(
                clientLanguage,
                maxAttempts,
                ToDiagnosticTarget(initialAddButton),
                ToDiagnosticTarget(lastAddButton),
                last.Recognition.Page,
                finalContext.Evidence.ContextualPage,
                foregroundBeforeClick,
                foregroundAfterClick,
                failureReason));
        throw new InvalidOperationException($"Add Language click failed after {maxAttempts} attempts. {failureReason}");
    }

    private async Task<(StablePageResult Page, Stage1MultilanguageContext Context, Stage1AddLanguageDialogContextEvidence Evidence)>
        WaitForAddLanguageDialogContextAttemptAsync(
            Stage1MultilanguageContext context,
            List<string> workflowLog,
            CancellationToken cancellationToken)
    {
        StablePageResult? last = null;
        Stage1AddLanguageDialogContextEvidence? lastEvidence = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            last = await RecognizeOnceAsync(cancellationToken);
            var contextResult = await DetectAddLanguageDialogContextAsync(last, cancellationToken);
            lastEvidence = contextResult.Evidence;
            context = context with
            {
                OcrTexts = context.OcrTexts.Concat(contextResult.OcrTexts).ToList(),
                AddLanguageDialogContextEvidence = context.AddLanguageDialogContextEvidence.Append(contextResult.Evidence).ToList()
            };
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=WaitAddLanguageDialog attempt={attempt} " +
                $"rawPage={contextResult.Evidence.RawPage} contextualPage={contextResult.Evidence.ContextualPage} " +
                $"ocrTitle=\"{contextResult.Evidence.OcrTitle}\" buttonOcr=\"{contextResult.Evidence.ButtonOcr}\" " +
                $"choiceOptionCount={contextResult.Evidence.ChoiceOptionCount} modalDialogCount={contextResult.Evidence.ModalDialogCount} " +
                $"accepted={contextResult.Evidence.Accepted} reason=\"{contextResult.Evidence.Reason}\"");

            if ((contextResult.Evidence.Accepted &&
                    contextResult.Evidence.ContextualPage == GamePage.LanguageSelectDialog) ||
                contextResult.Evidence.ContextualPage == GamePage.MultiLanguageEditor ||
                (contextResult.Evidence.RawPage != GamePage.Unknown &&
                    contextResult.Evidence.RawPage != GamePage.LanguageSelectDialog))
                return (last, context, contextResult.Evidence);

            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        last ??= await RecognizeOnceAsync(cancellationToken);
        lastEvidence ??= new Stage1AddLanguageDialogContextEvidence(
            last.Recognition.Page,
            last.Recognition.Page,
            string.Empty,
            string.Empty,
            0,
            0,
            false,
            "no contextual evidence captured");
        return (last, context, lastEvidence);
    }

    private async Task<(StablePageResult Dialog, Stage1MultilanguageContext Context)> RefreshAddLanguageDialogAfterSelectionAsync(
        UploadRequest request,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        string language,
        CancellationToken cancellationToken)
    {
        var refresh = await WaitForAddLanguageDialogContextAttemptAsync(context, workflowLog, cancellationToken);
        context = refresh.Context;
        var dialog = WithContextualPage(refresh.Page, refresh.Evidence.ContextualPage);
        AutomationTarget? confirmTarget = null;
        if (refresh.Evidence.Accepted &&
            refresh.Evidence.ContextualPage == GamePage.LanguageSelectDialog)
        {
            var confirm = await FindLanguageDialogConfirmTargetAsync(dialog, cancellationToken);
            confirmTarget = confirm.Target;
            context = context with { OcrTexts = context.OcrTexts.Concat(confirm.OcrTexts).ToList() };
        }

        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=RefreshAddLanguageDialogAfterSelect " +
            $"language={language} rawPage={refresh.Evidence.RawPage} contextualPage={refresh.Evidence.ContextualPage} " +
            $"confirmTarget={(confirmTarget == null ? "<none>" : FormatTarget(confirmTarget))}");

        if (!refresh.Evidence.Accepted ||
            refresh.Evidence.ContextualPage != GamePage.LanguageSelectDialog)
        {
            SaveMultilanguageDiagnostic(
                refresh.Page,
                "stage1-preview-multilanguage-language-dialog-refresh-failed",
                request,
                context,
                workflowLog,
                $"Add Language dialog was not contextually confirmed after selecting language={language}.");
            throw new InvalidOperationException($"Add Language dialog was not contextually confirmed after selecting language={language}.");
        }

        return (dialog, context);
    }

    private async Task<(StablePageResult Editor, Stage1MultilanguageContext Context)> WaitForMultilanguageEditorContextAsyncCore(
        UploadRequest request,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        string stepName,
        string diagnosticPrefix,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LanguageLabelTemplates.Match>? previousLabels = null;
        StablePageResult? last = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            last = await RecognizeOnceAsync(cancellationToken, supplementEditIcons: false);
            if (HasTemplateMultilanguageTable(last))
            {
                var templateLabels = LanguageLabelTemplates.Locate(last.Capture.Bitmap);
                var settled = LanguageLabelTemplates.IsStableTable(previousLabels, templateLabels, false);
                previousLabels = templateLabels;
                LogWorkflow(workflowLog, $"step={stepName} action=ConfirmTableSettled source=templates labels={templateLabels.Count} stable={settled} ocrSkipped=True");
                if (settled) return (WithContextualPage(last, GamePage.MultiLanguageEditor), context);
                await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
                continue;
            }
            var contextResult = await DetectMultilanguageEditorContextAsync(last, cancellationToken);
            context = context with
            {
                OcrTexts = context.OcrTexts.Concat(contextResult.OcrTexts).ToList(),
                PageContextEvidence = context.PageContextEvidence.Append(contextResult.Evidence).ToList()
            };
            LogWorkflow(workflowLog, 
                $"step={stepName} field=Multilanguage action=WaitMultilanguageEditor attempt={attempt} " +
                $"rawPage={contextResult.Evidence.RawPage} contextualPage={contextResult.Evidence.ContextualPage} " +
                $"ocrTitle=\"{contextResult.Evidence.OcrTitle}\" tableHeaderTokens=[{string.Join(", ", contextResult.Evidence.TableHeaderTokens)}] " +
                $"addLanguageButtonOcr=\"{contextResult.Evidence.AddLanguageButtonOcr}\" accepted={contextResult.Evidence.Accepted} " +
                $"reason=\"{contextResult.Evidence.Reason}\"");

            if (contextResult.Evidence.ContextualPage == GamePage.MultiLanguageEditor)
            {
                var dialogContext = await DetectAddLanguageDialogContextAsync(last, cancellationToken);
                context = context with
                {
                    OcrTexts = context.OcrTexts.Concat(dialogContext.OcrTexts).ToList(),
                    AddLanguageDialogContextEvidence = context.AddLanguageDialogContextEvidence.Append(dialogContext.Evidence).ToList()
                };
                LogWorkflow(workflowLog, 
                    $"step={stepName} field=Multilanguage action=CheckLingeringAddLanguageDialog attempt={attempt} " +
                    $"rawPage={dialogContext.Evidence.RawPage} contextualPage={dialogContext.Evidence.ContextualPage} " +
                    $"ocrTitle=\"{dialogContext.Evidence.OcrTitle}\" buttonOcr=\"{dialogContext.Evidence.ButtonOcr}\" " +
                    $"choiceOptionCount={dialogContext.Evidence.ChoiceOptionCount} modalDialogCount={dialogContext.Evidence.ModalDialogCount} " +
                    $"accepted={dialogContext.Evidence.Accepted} reason=\"{dialogContext.Evidence.Reason}\"");
                if (dialogContext.Evidence.Accepted &&
                    dialogContext.Evidence.ContextualPage == GamePage.LanguageSelectDialog)
                {
                    previousLabels = null;
                    await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
                    continue;
                }

                var labels = LanguageLabelTemplates.Locate(last.Capture.Bitmap);
                var textDialogVisible = last.Recognition.Targets.Any(t => t.Name == WorkflowTargetNames.TextInputArea && t.Confidence >= .35);
                var stableLabels = LanguageLabelTemplates.IsStableTable(previousLabels, labels, textDialogVisible);
                LogWorkflow(workflowLog, $"step={stepName} action=ConfirmTableSettled labels={labels.Count} textDialogVisible={textDialogVisible} stable={stableLabels}");
                previousLabels = textDialogVisible ? null : labels;
                if (!stableLabels)
                {
                    await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
                    continue;
                }
                return (WithContextualPage(last, GamePage.MultiLanguageEditor), context);
            }

            previousLabels = null;
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
        }

        var diagnostic = SaveMultilanguageDiagnostic(
            last,
            diagnosticPrefix,
            request,
            context,
            workflowLog,
            $"MultilanguageEditor was not contextually confirmed. lastPage={last?.Recognition.Page}.");
        throw new InvalidOperationException(
            $"MultilanguageEditor was not contextually confirmed. diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<(Stage1AddLanguageDialogContextEvidence Evidence, IReadOnlyList<WorkflowOcrTextResult> OcrTexts)>
        DetectAddLanguageDialogContextAsync(
            StablePageResult result,
            CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var buttonTexts = new List<WorkflowOcrTextResult>();
        foreach (var button in FindAddLanguageDialogButtonCandidates(result).Take(4))
        {
            var texts = await _workflowOcr.RecognizeTargetAsync(result.Capture, button, cancellationToken);
            buttonTexts.AddRange(texts);
            allOcrTexts.AddRange(texts);
        }

        var titleTexts = await _workflowOcr.RecognizeBoxAsync(
            result.Capture,
            new NormalizedBox(0.50, 0.18, 0.56, 0.12),
            "AddLanguageDialog/title",
            cancellationToken);
        allOcrTexts.AddRange(titleTexts);

        var choiceOptionCount =
            CountTargets(result.Recognition, WorkflowTargetNames.LanguageOption, 0.25) +
            CountTargets(result.Recognition, WorkflowTargetNames.ChoiceOption, 0.25);
        var modalDialogCount = CountTargets(result.Recognition, WorkflowTargetNames.ModalDialog, 0.25);
        var hasOptionContext = choiceOptionCount > 0 || modalDialogCount > 0;
        var titleMatched = OcrTextsContainAny(titleTexts, ["ADD LANGUAGE", "ADDLANGUAGE", "\u6dfb\u52a0\u8bed\u8a00"]);
        var buttonMatched = OcrTextsContainAny(buttonTexts, ["SELECT ALL", "SELECTALL", "\u5168\u9009", "OK", "CONFIRM", "\u786e\u8ba4", "\u786e\u5b9a"]);
        var accepted = hasOptionContext && (titleMatched || buttonMatched);
        var reason = accepted
            ? titleMatched
                ? "accepted by Add Language title OCR and option context"
                : "accepted by dialog button OCR and option context"
            : !hasOptionContext
                ? "rejected because no choice_option/language_option or modal_dialog option layout context was detected"
                : "rejected because neither Add Language title nor Select All/OK/Confirm button OCR was confirmed";

        var evidence = new Stage1AddLanguageDialogContextEvidence(
            result.Recognition.Page,
            accepted ? GamePage.LanguageSelectDialog : result.Recognition.Page,
            FormatRawOcrTexts(titleTexts),
            FormatRawOcrTexts(buttonTexts),
            choiceOptionCount,
            modalDialogCount,
            accepted,
            reason);
        return (evidence, allOcrTexts);
    }

    private static StablePageResult WithContextualPage(StablePageResult result, GamePage contextualPage) =>
        result.Recognition.Page == contextualPage
            ? result
            : new StablePageResult(
                result.Capture,
                result.Recognition with { Page = contextualPage, Source = $"{result.Recognition.Source}+ContextualOcr" });

    private static IReadOnlyList<AutomationTarget> FindAddLanguageDialogButtonCandidates(StablePageResult dialog) =>
        FindTargets(dialog.Recognition, WorkflowTargetNames.LanguageConfirmButton)
            .Concat(FindTargets(dialog.Recognition, WorkflowTargetNames.PrimaryActionButton))
            .OrderByDescending(target => target.Confidence)
            .ToList();

    private async Task<OcrTargetMatch> FindAddLanguageButtonForClickAsync(
        StablePageResult editor,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        foreach (var target in FindMultilanguageAddLanguageButtonCandidates(editor).Take(5))
        {
            var texts = await _workflowOcr.RecognizeTargetAsync(editor.Capture, target, cancellationToken);
            allOcrTexts.AddRange(texts);
            var accepted = OcrTextsContainAny(texts, ["ADD LANGUAGE", "ADDLANGUAGE", "\u6dfb\u52a0\u8bed\u8a00"]);
            var rejected = OcrTextsContainForbiddenAddLanguageButton(texts);
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=ClickAddLanguageButtonOcr " +
                $"target=({FormatTarget(target)}) raw=\"{FormatRawOcrTexts(texts)}\" normalized=\"{FormatNormalizedOcrTexts(texts)}\" " +
                $"accepted={accepted} rejected={rejected}");
            if (accepted && !rejected)
                return new OcrTargetMatch(target, allOcrTexts);
        }

        return new OcrTargetMatch(null, allOcrTexts);
    }

    private static bool OcrTextsContainForbiddenAddLanguageButton(IReadOnlyList<WorkflowOcrTextResult> ocrTexts) =>
        OcrTextsContainAny(
            ocrTexts,
            [
                "OK",
                "CONFIRM",
                "\u786e\u8ba4",
                "\u786e\u5b9a",
                "SUBMIT",
                "SUBMITNOW",
                "SUBMIT NOW",
                "POST",
                "PUBLISH",
                "\u53d1\u5e03",
                "\u524d\u5f80\u63d0\u4ea4"
            ]);

    private static IReadOnlyList<AutomationTarget> FindMultilanguageAddLanguageButtonCandidates(StablePageResult editor) =>
        FindTargets(editor.Recognition, WorkflowTargetNames.MultilanguageAddButton)
            .Concat(FindTargets(editor.Recognition, WorkflowTargetNames.PrimaryActionButton)
                .Where(target => target.CenterY < 0.35 || target.CenterY > 0.70))
            .OrderByDescending(target => target.Confidence)
            .ToList();

    private static IReadOnlyList<string> GetMultilanguageEditorTitleTokens() =>
    [
        "STAGE DETAILS MULTILANGUAGE SETTINGS",
        "MULTILANGUAGE SETTINGS",
        "\u5173\u5361\u8be6\u60c5\u9875\u591a\u8bed\u8a00\u8bbe\u7f6e",
        "\u591a\u8bed\u8a00\u8bbe\u7f6e"
    ];

    private static IReadOnlyList<string> GetMatchedMultilanguageEditorHeaderTokens(IReadOnlyList<WorkflowOcrTextResult> texts)
    {
        var tokens = new[]
        {
            "LANGUAGE",
            "STAGE NAME",
            "GAMEPLAY DETAILS",
            "STAGE DESCRIPTION",
            "UPDATE LOG",
            "\u8bed\u8a00",
            "\u5173\u5361\u540d\u79f0",
            "\u73a9\u6cd5\u8bf4\u660e",
            "\u5173\u5361\u63cf\u8ff0",
            "\u7248\u672c\u66f4\u65b0\u8bb0\u5f55"
        };

        var normalizedTexts = texts
            .Select(text => text.NormalizedText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        return tokens
            .Where(token =>
            {
                var normalizedToken = NormalizeWorkflowOcrText(token);
                return normalizedToken.Length > 0 &&
                       normalizedTexts.Any(text => text.Contains(normalizedToken, StringComparison.Ordinal));
            })
            .ToList();
    }

    private static Stage1MultilanguageContext CreateMultilanguageContext(IReadOnlyList<string> desiredLanguages) =>
        new(
            null,
            desiredLanguages,
            string.Empty,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            null,
            false,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);

    private static LocalizedArchiveInfo NormalizeConfiguredLocalizedInfo(LocalizedArchiveInfo source)
    {
        var normalizedCode = NormalizeStage1LanguageCode(source.LanguageCode, source.LanguageName);
        if (!Stage1Languages.ContainsKey(normalizedCode))
        {
            var configured = string.IsNullOrWhiteSpace(source.LanguageCode)
                ? source.LanguageName
                : source.LanguageCode;
            throw new NotSupportedException($"unsupported language code/name: {configured}");
        }

        var clone = source.Clone();
        clone.LanguageCode = normalizedCode;
        clone.LanguageName = Stage1Languages[normalizedCode].DisplayName;
        return clone;
    }

    private static string NormalizeStage1LanguageCode(string code, string name)
    {
        var value = string.IsNullOrWhiteSpace(code) ? name : code;
        var normalized = value.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "en" or "eng" or "english" => "en",
            "zh-cn" or "zh_cn" or "zh-hans" or "zh_hans" or "zh" or "chinese simplified" or "simplified chinese" => "zh-CN",
            "zh-tw" or "zh_tw" or "zh-hant" or "zh_hant" or "traditional chinese" => "zh-TW",
            "ko" or "kr" or "korean" => "ko",
            "ja" or "jp" or "japanese" => "ja",
            "es" or "spanish" => "es",
            "fr" or "french" => "fr",
            "ru" or "russian" => "ru",
            "th" or "thai" => "th",
            "vi" or "vietnamese" => "vi",
            "de" or "german" => "de",
            "id" or "indonesian" or "indonesia" => "id",
            "it" or "italian" => "it",
            "pt" or "portuguese" => "pt",
            "tr" or "turkish" => "tr",
            _ => normalized
        };
    }

    private async Task<(Stage1ClientLanguage ClientLanguage, AutomationTarget AddButton, Stage1MultilanguageContext Context)>
        DetectMultilanguageClientLanguageAsync(
            UploadRequest request,
            StablePageResult editor,
            Stage1MultilanguageContext context,
            List<string> workflowLog,
            CancellationToken cancellationToken)
    {
        var addButtons = FindMultilanguageAddLanguageButtonCandidates(editor)
            .ToList();
        if (addButtons.Count == 0)
        {
            SaveMultilanguageDiagnostic(
                editor,
                "stage1-preview-multilanguage-add-language-button-missing",
                request,
                context,
                workflowLog,
                "Add Language button was not detected in MultilanguageEditor.");
            throw new InvalidOperationException("Add Language button was not detected in MultilanguageEditor.");
        }

        var allOcrTexts = new List<WorkflowOcrTextResult>();
        foreach (var addButton in addButtons)
        {
            var texts = await _workflowOcr.RecognizeTargetAsync(editor.Capture, addButton, cancellationToken);
            allOcrTexts.AddRange(texts);
            var normalized = string.Join("|", texts.Select(text => text.NormalizedText));
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=DetectClientLanguage " +
                $"target=({FormatTarget(addButton)}) raw=\"{FormatRawOcrTexts(texts)}\" normalized=\"{normalized}\"");

            if (OcrTextsContainAny(texts, ["ADD LANGUAGE", "ADDLANGUAGE"]))
                return (Stage1ClientLanguage.En, addButton, context with { ClientLanguage = Stage1ClientLanguage.En, OcrTexts = context.OcrTexts.Concat(allOcrTexts).ToList() });

            if (OcrTextsContainAny(texts, ["\u6dfb\u52a0\u8bed\u8a00"]))
                return (Stage1ClientLanguage.ZhCn, addButton, context with { ClientLanguage = Stage1ClientLanguage.ZhCn, OcrTexts = context.OcrTexts.Concat(allOcrTexts).ToList() });
        }

        context = context with { OcrTexts = context.OcrTexts.Concat(allOcrTexts).ToList() };
        SaveMultilanguageDiagnostic(
            editor,
            "stage1-preview-multilanguage-client-language-undetected",
            request,
            context,
            workflowLog,
            "Client language could not be detected from Add Language button OCR.");
        throw new InvalidOperationException("Client language could not be detected from Add Language button OCR.");
    }

    private static IReadOnlyList<Stage1AddLanguageOption> BuildAddLanguageOptions(
        StablePageResult dialog,
        Stage1ClientLanguage clientLanguage)
    {
        var labels = LanguageLabelTemplates.Locate(dialog.Capture.Bitmap, options: true);
        if (labels.Count != Stage1Languages.Count)
            throw new InvalidOperationException($"Language option templates are incomplete ({labels.Count}/{Stage1Languages.Count}); refusing positional language selection.");
        var targets = FindTargets(dialog.Recognition, WorkflowTargetNames.LanguageOption, .25)
            .Concat(FindTargets(dialog.Recognition, WorkflowTargetNames.ChoiceOption, .25)).ToList();
        return labels.Select((label, index) =>
        {
            var target = targets.Where(t => Math.Abs(t.CenterY-label.Y)<.02 &&
                    Math.Abs(t.CenterX-label.X)<t.Width/2 && t.Width is > .15 and < .4 && t.Height is > .025 and < .09)
                .OrderBy(t => Math.Abs(t.CenterY-label.Y)).FirstOrDefault();
            // The label is inset slightly right of the option center to leave room for its radio icon.
            var aspect = dialog.Capture.Bitmap.PixelHeight/(double)dialog.Capture.Bitmap.PixelWidth;
            target ??= new AutomationTarget(WorkflowTargetNames.LanguageOption, label.X-.017*aspect, label.Y,
                .49*aspect,.047,label.Score);
            return new Stage1AddLanguageOption(Stage1Languages[label.Language], label.X<.5?"left":"right", index, target);
        }).ToList();
    }

    private static Dictionary<string, List<AutomationTarget>> CreateDerivedLanguageOptionTargets(StablePageResult dialog)
    {
        var panel = FindTargets(dialog.Recognition, WorkflowTargetNames.ModalDialog, 0.25).FirstOrDefault();
        var centerX = panel?.CenterX ?? 0.50;
        var centerY = panel?.CenterY ?? 0.50;
        var width = panel?.Width ?? 0.56;
        var height = panel?.Height ?? 0.62;
        var optionWidth = Math.Clamp(width * 0.38, 0.14, 0.32);
        var optionHeight = Math.Clamp(height / 10.0, 0.045, 0.085);
        var leftX = Math.Clamp(centerX - width * 0.23, optionWidth / 2, 1 - optionWidth / 2);
        var rightX = Math.Clamp(centerX + width * 0.23, optionWidth / 2, 1 - optionWidth / 2);
        var firstY = Math.Clamp(centerY - height * 0.32, optionHeight / 2, 1 - optionHeight / 2);
        var stepY = Math.Clamp(height * 0.095, optionHeight * 0.85, optionHeight * 1.5);

        return new Dictionary<string, List<AutomationTarget>>(StringComparer.Ordinal)
        {
            ["left"] = Enumerable.Range(0, 8)
                .Select(index => new AutomationTarget(WorkflowTargetNames.LanguageOption, leftX, Math.Clamp(firstY + stepY * index, optionHeight / 2, 1 - optionHeight / 2), optionWidth, optionHeight, 0.5))
                .ToList(),
            ["right"] = Enumerable.Range(0, 7)
                .Select(index => new AutomationTarget(WorkflowTargetNames.LanguageOption, rightX, Math.Clamp(firstY + stepY * index, optionHeight / 2, 1 - optionHeight / 2), optionWidth, optionHeight, 0.5))
                .ToList()
        };
    }

    private static IEnumerable<Stage1AddLanguageOption> CreateAddLanguageOptionsForColumn(
        IReadOnlyList<string> languageCodes,
        string column,
        IReadOnlyList<AutomationTarget> targets)
    {
        for (var index = 0; index < languageCodes.Count && index < targets.Count; index++)
        {
            var language = Stage1Languages[languageCodes[index]];
            yield return new Stage1AddLanguageOption(language, column, index, targets[index]);
        }
    }

    private static Stage1LanguageSelectionDiagnostic InspectLanguageOptionSelection(
        GameCaptureResult capture,
        Stage1AddLanguageOption option)
    {
        var selected = IsLanguageOptionVisuallySelected(capture, option.OptionTarget, out var evidence, out var radioRoi, out var greenPixels, out var brightness);
        return new Stage1LanguageSelectionDiagnostic(
            option.Language.Code,
            option.Column,
            option.RowIndex,
            ToDiagnosticTarget(option.OptionTarget),
            radioRoi,
            greenPixels,
            brightness,
            selected,
            evidence);
    }

    private static bool IsLanguageOptionVisuallySelected(
        GameCaptureResult capture,
        AutomationTarget target,
        out string evidence,
        out NormalizedBox radioRoi,
        out int greenPixels,
        out double backgroundBrightness)
    {
        radioRoi = new NormalizedBox(
            Math.Clamp(target.CenterX - target.Width * 0.42, 0, 1),
            target.CenterY,
            Math.Clamp(target.Width * 0.16, 0.01, 0.08),
            Math.Clamp(target.Height * 0.70, 0.01, 0.08));

        var bitmap = capture.Bitmap;
        if (bitmap.PixelWidth <= 1 && bitmap.PixelHeight <= 1)
        {
            var selectedByDryRun = target.Confidence >= 0.95;
            greenPixels = selectedByDryRun ? 12 : 0;
            backgroundBrightness = selectedByDryRun ? 150 : 245;
            evidence = $"dryRun confidence={target.Confidence:F2} selected={selectedByDryRun}";
            return selectedByDryRun;
        }

        if (bitmap.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            bitmap = converted;
        }

        var imageWidth = Math.Max(1, bitmap.PixelWidth);
        var imageHeight = Math.Max(1, bitmap.PixelHeight);
        var x = Math.Clamp((int)Math.Round((radioRoi.CenterX - radioRoi.Width / 2) * imageWidth), 0, imageWidth - 1);
        var y = Math.Clamp((int)Math.Round((radioRoi.CenterY - radioRoi.Height / 2) * imageHeight), 0, imageHeight - 1);
        var w = Math.Clamp((int)Math.Round(radioRoi.Width * imageWidth), 1, imageWidth - x);
        var h = Math.Clamp((int)Math.Round(radioRoi.Height * imageHeight), 1, imageHeight - y);

        var stride = w * 4;
        var pixels = new byte[stride * h];
        bitmap.CopyPixels(new Int32Rect(x, y, w, h), pixels, stride, 0);

        greenPixels = 0;
        var brightnessTotal = 0.0;
        var pixelCount = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var b = pixels[index];
            var g = pixels[index + 1];
            var r = pixels[index + 2];
            if (g >= 130 && g > r + 25 && g > b + 10)
                greenPixels++;
            brightnessTotal += (r + g + b) / 3.0;
            pixelCount++;
        }

        backgroundBrightness = pixelCount == 0 ? 0 : brightnessTotal / pixelCount;
        var selected = greenPixels >= Math.Max(3, pixelCount / 160);
        evidence = $"radioRoi=({radioRoi.CenterX:F3},{radioRoi.CenterY:F3},{radioRoi.Width:F3},{radioRoi.Height:F3}) greenPixelCount={greenPixels} backgroundBrightness={backgroundBrightness:F1} selected={selected}";
        return selected;
    }

    private async Task<OcrTargetMatch> FindLanguageDialogConfirmTargetAsync(
        StablePageResult dialog,
        CancellationToken cancellationToken)
    {
        var direct = FindTargets(dialog.Recognition, WorkflowTargetNames.LanguageConfirmButton).FirstOrDefault();
        if (direct != null)
            return new OcrTargetMatch(direct, []);

        return await FindPrimaryButtonByOcrAsync(dialog, ["OK", "CONFIRM", "\u786e\u8ba4", "\u786e\u5b9a"], cancellationToken);
    }

    private static string GetClientDefaultLanguageCode(Stage1ClientLanguage clientLanguage) =>
        clientLanguage == Stage1ClientLanguage.En ? "en" : "zh-CN";

    private static IReadOnlyList<string> GetMultilanguageTableOrder(Stage1ClientLanguage clientLanguage) =>
        clientLanguage == Stage1ClientLanguage.En ? MultilanguageTableOrderEn : MultilanguageTableOrderZhCn;

    private static bool IsRightPreviewEditorTarget(AutomationTarget target) =>
        target.CenterX >= 0.45 &&
        target.CenterY is >= 0.18 and <= 0.92 &&
        target.Width >= 0.04 &&
        target.Height >= 0.025;

    private static IReadOnlyList<string> GetPreviewMultilanguageEntryAcceptedTokens() =>
        [
            "STAGE DETAILS MULTILANGUAGE SETTINGS",
            "STAGEDETAILSMULTILANGUAGESETTINGS",
            "MULTILANGUAGE SETTINGS",
            "MULTILANGUAGE",
            "ENGLISH",
            "\u7b80\u4f53\u4e2d\u6587",
            "\u5173\u5361\u8be6\u60c5\u9875\u591a\u8bed\u8a00\u8bbe\u7f6e",
            "\u591a\u8bed\u8a00\u8bbe\u7f6e"
        ];

    private static IReadOnlyList<string> GetPreviewMultilanguageEntryRejectedTokens() =>
        [
            "TAGS",
            "STAGE TAGS",
            "UPDATE LOG",
            "VERSION LOG",
            "MORE SHOWCASE IMAGES",
            "SHOWCASE VIDEOS",
            "STAGE DESCRIPTION",
            "GAMEPLAY DETAILS",
            "EARLY ACCESS",
            "EARLY ACCESS VERSION DESCRIPTION",
            "\u6807\u7b7e",
            "\u5173\u5361\u6807\u7b7e",
            "\u66f4\u65b0\u8bb0\u5f55",
            "版本更新记录", "点击编辑版本更新记录", "\u7248\u672c\u8bb0\u5f55",
            "\u66f4\u591a\u5c55\u793a\u56fe\u7247",
            "\u5c55\u793a\u89c6\u9891",
            "\u5173\u5361\u63cf\u8ff0",
            "\u73a9\u6cd5\u8bf4\u660e",
            "\u5148\u884c\u4f53\u9a8c",
            "\u5148\u884c\u4f53\u9a8c\u8bf4\u660e"
        ];

    private async Task<TagsEditRowOcrMatch> FindPreviewMultilanguageEditRowByOcrAsync(
        StablePageResult current,
        IReadOnlyList<string> acceptedTokens,
        IReadOnlyList<string> rejectedTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var matches = new List<AutomationTarget>();
        var candidates = FindTargets(current.Recognition, WorkflowTargetNames.EditRow, 0.25)
            .Where(IsRightPreviewEditorTarget)
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .ThenByDescending(target => target.Confidence)
            .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var target = candidates[index];
            var ocrTexts = await _workflowOcr.RecognizeTargetAsync(current.Capture, target, cancellationToken);
            allOcrTexts.AddRange(ocrTexts);
            var accepted = PreviewCandidateOcrMatches(ocrTexts, acceptedTokens);
            var rejected = PreviewCandidateOcrMatches(ocrTexts, rejectedTokens);
            var reason = rejected
                ? "rejected_token"
                : accepted
                    ? "accepted_field_token"
                    : "no_field_token";
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=EditRowOcr candidateIndex={index} " +
                $"target=({FormatTarget(target)}) raw=\"{FormatRawOcrTexts(ocrTexts)}\" " +
                $"normalized=\"{FormatNormalizedOcrTexts(ocrTexts)}\" accepted={accepted} rejected={rejected} reason={reason}");
            if (accepted && !rejected)
                matches.Add(target);
        }

        if (matches.Count == 1)
            return new TagsEditRowOcrMatch(matches[0], matches, allOcrTexts, false);

        return new TagsEditRowOcrMatch(null, matches, allOcrTexts, matches.Count > 1);
    }

    private async Task<TagsEditRowOcrMatch> FindPreviewMultilanguageTextEditIconByContextOcrAsync(
        StablePageResult current,
        IReadOnlyList<string> acceptedTokens,
        IReadOnlyList<string> rejectedTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var matches = new List<AutomationTarget>();
        var candidates = FindTargets(current.Recognition, WorkflowTargetNames.TextEditIcon, 0.25)
            .Where(target => target.CenterX >= 0.45 && target.CenterY is >= 0.18 and <= 0.92)
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .ThenByDescending(target => target.Confidence)
            .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var target = candidates[index];
            var ocrTexts = await _workflowOcr.RecognizeBoxAsync(
                current.Capture,
                CreatePreviewTextEditIconContextOcrBox(target),
                $"{WorkflowTargetNames.TextEditIcon}/Multilanguage/context",
                cancellationToken);
            allOcrTexts.AddRange(ocrTexts);
            var accepted = PreviewCandidateOcrMatches(ocrTexts, acceptedTokens);
            var rejected = PreviewCandidateOcrMatches(ocrTexts, rejectedTokens);
            var reason = rejected
                ? "rejected_token"
                : accepted
                    ? "accepted_field_token"
                    : "no_field_token";
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=TextEditIconContextOcr candidateIndex={index} " +
                $"target=({FormatTarget(target)}) raw=\"{FormatRawOcrTexts(ocrTexts)}\" " +
                $"normalized=\"{FormatNormalizedOcrTexts(ocrTexts)}\" accepted={accepted} rejected={rejected} reason={reason}");
            if (accepted && !rejected)
                matches.Add(target);
        }

        if (matches.Count == 1)
            return new TagsEditRowOcrMatch(matches[0], matches, allOcrTexts, false);

        return new TagsEditRowOcrMatch(null, matches, allOcrTexts, matches.Count > 1);
    }

    private static NormalizedBox CreatePreviewTextEditIconContextOcrBox(AutomationTarget target)
    {
        var width = Math.Clamp(target.Width * 12.0, 0.22, 0.55);
        var height = Math.Clamp(target.Height * 2.3, 0.055, 0.14);
        var centerX = Math.Clamp(target.CenterX - width * 0.42, width / 2, 1 - width / 2);
        return new NormalizedBox(
            centerX,
            Math.Clamp(target.CenterY, height / 2, 1 - height / 2),
            width,
            height);
    }

    private async Task<IReadOnlyList<WorkflowOcrTextResult>> RecognizeMultilanguageTableHeaderOcrAsync(
        StablePageResult editor,
        CancellationToken cancellationToken)
    {
        var headerBox = new NormalizedBox(0.56, 0.27, 0.62, 0.10);
        return await _workflowOcr.RecognizeBoxAsync(
            editor.Capture,
            headerBox,
            "MultilanguageEditor/table_header/full",
            cancellationToken);
    }

    private static IReadOnlyList<string> GetMultilanguageTableColumns(bool earlyAccessColumnEnabled) =>
        earlyAccessColumnEnabled
            ? [MlStageNameColumn, MlEarlyAccessDescriptionColumn, MlGameplayDetailsColumn, MlStageDescriptionColumn, MlUpdateLogColumn]
            : [MlStageNameColumn, MlGameplayDetailsColumn, MlStageDescriptionColumn, MlUpdateLogColumn];

    private static IReadOnlyList<string> GetMultilanguageEarlyAccessHeaderTokens() =>
        [
            "EARLY ACCESS",
            "EARLY ACCESS VERSION DESCRIPTION",
            "\u5148\u884c",
            "\u5148\u884c\u7248\u672c",
            "\u5148\u884c\u7248\u672c\u8bf4\u660e",
            "\u5148\u884c\u4f53\u9a8c"
        ];

    private static Stage1MultilanguageTableGeometryInference InferMultilanguageTableGeometry(
        StablePageResult editor,
        int visibleRowCount,
        IReadOnlyList<string> columns,
        IReadOnlyList<WorkflowOcrTextResult> tableHeaderOcr,
        List<string> workflowLog)
    {
        var cells = FindTargets(editor.Recognition, WorkflowTargetNames.MultilanguageLevelNameCell, 0.25)
            .OrderBy(target => target.CenterY)
            .ToList();

        if (cells.Count > 0)
        {
            var first = cells[0];
            var rowHeight = cells.Count >= 2
                ? Math.Clamp(cells[1].CenterY - cells[0].CenterY, 0.035, 0.11)
                : Math.Clamp(first.Height * 1.35, 0.045, 0.10);
            var cellWidth = Math.Clamp(first.Width, 0.08, 0.20);
            var columnStep = Math.Clamp(cellWidth * 1.05, 0.09, 0.18);
            var centers = columns
                .Select((column, index) => (column, center: Math.Clamp(first.CenterX + columnStep * index, 0, 1)))
                .ToDictionary(item => item.column, item => item.center, StringComparer.Ordinal);
            var geometry = new Stage1MultilanguageTableGeometry(first.CenterY, rowHeight, centers, "multilanguage_level_name_cell");
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=InferTableGeometry source=multilanguage_level_name_cell " +
                $"cellCount={cells.Count} firstRowY={geometry.FirstRowCenterY:F3} rowHeight={geometry.RowHeight:F3} " +
                $"columnCenters=[{FormatColumnCenters(geometry.ColumnCenters)}]");
            return new Stage1MultilanguageTableGeometryInference(geometry, [], [], [], "multilanguage_level_name_cell", "inferred from level name cells");
        }

        var iconDiagnostics = FindTargets(editor.Recognition, WorkflowTargetNames.TextEditIcon, 0.25)
            .Select(target =>
            {
                var used = IsMultilanguageTableTextEditIconCandidate(target);
                var reason = used ? "text_edit_icon_size_ok" : "text_edit_icon_size_outlier";
                return new Stage1MultilanguageTableIconDiagnostic(ToDiagnosticTarget(target), used, reason);
            })
            .ToList();
        var icons = FindTargets(editor.Recognition, WorkflowTargetNames.TextEditIcon, 0.25)
            .Where(IsMultilanguageTableTextEditIconCandidate)
            .ToList();

        foreach (var diagnostic in iconDiagnostics)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=TableTextEditIconCandidate " +
                $"target=({diagnostic.Target.Name} conf={diagnostic.Target.Confidence:F2} cx={diagnostic.Target.CenterX:F3} cy={diagnostic.Target.CenterY:F3} w={diagnostic.Target.Width:F3} h={diagnostic.Target.Height:F3}) " +
                $"used={diagnostic.Used} reason={diagnostic.Reason}");
        }

        var rowClusters = ClusterMultilanguageTableCoordinates(icons.Select(icon => icon.CenterY), 0.025);
        var columnClusters = ClusterMultilanguageTableCoordinates(icons.Select(icon => icon.CenterX), 0.030);
        // A lone false pencil inside the name cell must not shift every column.
        // Require support from distinct rows rather than counting duplicate boxes.
        var minimumRows = Math.Min(2, visibleRowCount);
        var supportedColumns = columnClusters.Where(cluster =>
            ClusterMultilanguageTableCoordinates(icons.Where(icon =>
                Math.Abs(icon.CenterX - cluster.Center) <= .030).Select(icon => icon.CenterY), .025).Count >= minimumRows).ToList();
        if (supportedColumns.Count != columnClusters.Count)
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=RejectIsolatedIconColumns " +
                $"before=[{FormatClusters(columnClusters)}] after=[{FormatClusters(supportedColumns)}] requiredDistinctRows={minimumRows}");
        columnClusters = supportedColumns;
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=InferTableGeometry source=text_edit_icon " +
            $"iconCount={icons.Count} rowClusters=[{FormatClusters(rowClusters)}] columnClusters=[{FormatClusters(columnClusters)}] " +
            $"visibleRowCount={visibleRowCount} businessColumnCount={columns.Count} editableIconColumnCount={Math.Max(0, columns.Count - 1)}");

        if (visibleRowCount <= 0)
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo field=Multilanguage action=InferTableGeometry result=failed source=text_edit_icon reason=visible row count is zero");
            return new Stage1MultilanguageTableGeometryInference(null, iconDiagnostics, rowClusters, columnClusters, "text_edit_icon", "visible row count is zero");
        }
        if (rowClusters.Count < visibleRowCount)
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo field=Multilanguage action=InferTableGeometry result=failed source=text_edit_icon reason=not enough row clusters");
            return new Stage1MultilanguageTableGeometryInference(null, iconDiagnostics, rowClusters, columnClusters, "text_edit_icon", "not enough row clusters");
        }
        var editableColumnCount = Math.Max(0, columns.Count - 1);
        if (columnClusters.Count > editableColumnCount)
            return new Stage1MultilanguageTableGeometryInference(null, iconDiagnostics, rowClusters, columnClusters,
                "text_edit_icon", "ambiguous extra icon columns; refusing to shift table fields");
        if (columnClusters.Count < editableColumnCount)
        {
            var headerGeometry = TryInferMultilanguageTableGeometryFromHeaderAndPartialIcons(
                visibleRowCount,
                columns,
                tableHeaderOcr,
                rowClusters,
                columnClusters,
                iconDiagnostics,
                workflowLog);
            if (headerGeometry.Geometry != null)
                return headerGeometry;

            LogWorkflow(workflowLog, "step=EditPreviewInfo field=Multilanguage action=InferTableGeometry result=failed source=text_edit_icon reason=not enough column clusters");
            return headerGeometry.Reason.Contains("header", StringComparison.OrdinalIgnoreCase)
                ? headerGeometry
                : new Stage1MultilanguageTableGeometryInference(null, iconDiagnostics, rowClusters, columnClusters, "text_edit_icon", "not enough column clusters");
        }

        var effectiveRows = rowClusters.Take(visibleRowCount).ToList();
        var effectiveColumns = columnClusters.Take(editableColumnCount).ToList();
        var checkedHeaders = ExtractMultilanguageHeaderColumnCenters(tableHeaderOcr, columns);
        foreach (var item in columns.Skip(1).Select((column, index) => (column, index)))
        {
            if (checkedHeaders.TryGetValue(item.column, out var headerX) &&
                (effectiveColumns[item.index].Center < headerX - .025 ||
                 effectiveColumns[item.index].Center > headerX + .15))
                return new Stage1MultilanguageTableGeometryInference(null, iconDiagnostics, rowClusters, columnClusters,
                    "text_edit_icon", $"icon column disagrees with header {item.column}");
        }
        var inferredRowHeight = effectiveRows.Count >= 2
            ? Math.Clamp(effectiveRows[1].Center - effectiveRows[0].Center, 0.035, 0.11)
            : 0.07;
        var columnSteps = effectiveColumns.Zip(effectiveColumns.Skip(1),
                (left, right) => right.Center - left.Center).OrderBy(value => value).ToArray();
        var inferredColumnStep = columnSteps.Length > 0
            ? Math.Clamp(columnSteps[columnSteps.Length / 2], 0.07, 0.16)
            : 0.10;
        // Edit icons sit at the RIGHT edge of their cells. One column left of
        // the first icon is the name cell's right edge, not a safe text click.
        var stageNameHeader = tableHeaderOcr
            .Where(text => text.Confidence >= 0.6 &&
                text.NormalizedBox.CenterY < effectiveRows[0].Center - 0.01 &&
                HeaderOcrMatchesMultilanguageColumn(text, MlStageNameColumn))
            .OrderByDescending(text => text.Confidence).FirstOrDefault();
        var inferredNameCenter = effectiveColumns.Count > 0
            ? effectiveColumns[0].Center - inferredColumnStep * 1.5 : 0;
        var useNameHeader = stageNameHeader != null &&
            Math.Abs(stageNameHeader.NormalizedBox.CenterX - inferredNameCenter) < inferredColumnStep * 0.45;
        var stageNameCenter = useNameHeader ? stageNameHeader!.NormalizedBox.CenterX : inferredNameCenter;
        if (effectiveColumns.Count == 0 ||
            stageNameCenter < 0.20 ||
            stageNameCenter >= effectiveColumns[0].Center)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=InferTableGeometry result=failed source=text_edit_icon " +
                $"reason=stage name column could not be inferred stageNameCenter={stageNameCenter:F3}");
            return new Stage1MultilanguageTableGeometryInference(
                null,
                iconDiagnostics,
                rowClusters,
                columnClusters,
                "text_edit_icon",
                "stage name column could not be inferred");
        }

        var inferredCenters = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [MlStageNameColumn] = Math.Clamp(stageNameCenter, 0, 1)
        };
        foreach (var item in columns.Skip(1).Select((column, index) => (column, index)))
            inferredCenters[item.column] = Math.Clamp(effectiveColumns[item.index].Center, 0, 1);
        var inferredGeometry = new Stage1MultilanguageTableGeometry(effectiveRows[0].Center, inferredRowHeight, inferredCenters, "text_edit_icon");
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=InferTableGeometry result=success source=text_edit_icon " +
            $"firstRowY={inferredGeometry.FirstRowCenterY:F3} rowHeight={inferredGeometry.RowHeight:F3} " +
            $"stageNameSource={(useNameHeader ? "header_ocr" : "icon_right_edge_minus_one_and_half_columns")} columnStep={inferredColumnStep:F3} " +
            $"columnCenters=[{FormatColumnCenters(inferredGeometry.ColumnCenters)}]");
        return new Stage1MultilanguageTableGeometryInference(
            inferredGeometry,
            iconDiagnostics,
            rowClusters,
            columnClusters,
            "text_edit_icon",
            "inferred from text_edit_icon row/column clusters");
    }

    private static Stage1MultilanguageTableGeometryInference TryInferMultilanguageTableGeometryFromHeaderAndPartialIcons(
        int visibleRowCount,
        IReadOnlyList<string> columns,
        IReadOnlyList<WorkflowOcrTextResult> tableHeaderOcr,
        IReadOnlyList<Stage1MultilanguageTableCluster> rowClusters,
        IReadOnlyList<Stage1MultilanguageTableCluster> columnClusters,
        IReadOnlyList<Stage1MultilanguageTableIconDiagnostic> iconDiagnostics,
        List<string> workflowLog)
    {
        if (visibleRowCount <= 0 || rowClusters.Count < visibleRowCount)
            return new Stage1MultilanguageTableGeometryInference(
                null,
                iconDiagnostics,
                rowClusters,
                columnClusters,
                "header_ocr_plus_partial_icons",
                "not enough row clusters for header-based geometry");

        var headerCenters = ExtractMultilanguageHeaderColumnCenters(tableHeaderOcr, columns);
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=InferTableGeometry source=header_ocr_plus_partial_icons " +
            $"headerCenters=[{FormatColumnCenters(headerCenters)}] partialIconColumns={columnClusters.Count}");

        var minimumHeaderColumnCount = columnClusters.Count > 0
            ? 3
            : Math.Max(3, columns.Count - 1);
        if (headerCenters.Count < minimumHeaderColumnCount)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=InferTableGeometry result=failed source=header_ocr_plus_partial_icons " +
                $"reason=header OCR did not expose all required columns headerColumnCount={headerCenters.Count} minimumRequired={minimumHeaderColumnCount} " +
                $"missing=[{string.Join(",", columns.Where(column => !headerCenters.ContainsKey(column)))}]");
            return new Stage1MultilanguageTableGeometryInference(
                null,
                iconDiagnostics,
                rowClusters,
                columnClusters,
                "header_ocr_plus_partial_icons",
                "header OCR did not expose all required columns");
        }

        var centers = new Dictionary<string, double>(headerCenters, StringComparer.Ordinal);
        if (centers.Count < columns.Count)
        {
            var inferred = InferMissingHeaderCentersFromPartialIcons(columns, centers, columnClusters);
            centers = inferred.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        }

        if (columns.Any(column => !centers.ContainsKey(column)))
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=InferTableGeometry result=failed source=header_ocr_plus_partial_icons " +
                $"reason=header OCR did not expose all required columns missing=[{string.Join(",", columns.Where(column => !centers.ContainsKey(column)))}]");
            return new Stage1MultilanguageTableGeometryInference(
                null,
                iconDiagnostics,
                rowClusters,
                columnClusters,
                "header_ocr_plus_partial_icons",
                "header OCR did not expose all required columns");
        }

        var orderedCenters = columns
            .Select(column => Math.Clamp(centers[column], 0, 1))
            .ToList();
        if (!orderedCenters.SequenceEqual(orderedCenters.OrderBy(value => value)) ||
            orderedCenters[0] < 0.20 ||
            orderedCenters[^1] > 0.92)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=InferTableGeometry result=failed source=header_ocr_plus_partial_icons " +
                $"reason=header-derived column centers are outside table area or not ordered centers=[{string.Join(",", orderedCenters.Select(value => value.ToString("F3")))}]");
            return new Stage1MultilanguageTableGeometryInference(
                null,
                iconDiagnostics,
                rowClusters,
                columnClusters,
                "header_ocr_plus_partial_icons",
                "header-derived column centers are outside table area or not ordered");
        }

        var effectiveRows = rowClusters.Take(visibleRowCount).ToList();
        var rowHeight = effectiveRows.Count >= 2
            ? Math.Clamp(effectiveRows[1].Center - effectiveRows[0].Center, 0.035, 0.11)
            : 0.07;
        var geometry = new Stage1MultilanguageTableGeometry(
            effectiveRows[0].Center,
            rowHeight,
            columns.ToDictionary(column => column, column => Math.Clamp(centers[column], 0, 1), StringComparer.Ordinal),
            "header_ocr_plus_partial_icons");
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Multilanguage action=InferTableGeometry result=success source=header_ocr_plus_partial_icons " +
            $"firstRowY={geometry.FirstRowCenterY:F3} rowHeight={geometry.RowHeight:F3} columnCenters=[{FormatColumnCenters(geometry.ColumnCenters)}]");
        return new Stage1MultilanguageTableGeometryInference(
            geometry,
            iconDiagnostics,
            rowClusters,
            columnClusters,
            "header_ocr_plus_partial_icons",
            "inferred from header OCR and partial text_edit_icon row clusters");
    }

    private static IReadOnlyDictionary<string, double> ExtractMultilanguageHeaderColumnCenters(
        IReadOnlyList<WorkflowOcrTextResult> texts,
        IReadOnlyList<string> columns)
    {
        var centers = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            var match = texts
                .Where(text => HeaderOcrMatchesMultilanguageColumn(text, column))
                .OrderByDescending(text => text.Confidence)
                .FirstOrDefault();
            if (match != null)
                centers[column] = match.NormalizedBox.CenterX;
        }

        return centers;
    }

    private static bool HeaderOcrMatchesMultilanguageColumn(WorkflowOcrTextResult text, string column)
    {
        var normalized = text.NormalizedText;
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = NormalizeWorkflowOcrText(text.Text);
        return GetMultilanguageColumnHeaderTokens(column)
            .Select(NormalizeWorkflowOcrText)
            .Any(token => token.Length > 0 && normalized.Contains(token, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> GetMultilanguageColumnHeaderTokens(string column) =>
        column switch
        {
            MlStageNameColumn => ["STAGE NAME", "\u5173\u5361\u540d\u79f0"],
            MlEarlyAccessDescriptionColumn => ["「先行体验」版本说明", "先行体验版本说明","EARLY ACCESS VERSION DESCRIPTION", "EARLY ACCESS", "\u5148\u884c\u7248\u672c\u8bf4\u660e", "\u5148\u884c\u7248\u672c", "\u5148\u884c\u4f53\u9a8c\u8bf4\u660e"],
            MlGameplayDetailsColumn => ["GAMEPLAY DETAILS", "GAMEPLAY", "\u73a9\u6cd5\u8bf4\u660e"],
            MlStageDescriptionColumn => ["STAGE DESCRIPTION", "\u5173\u5361\u63cf\u8ff0"],
            MlUpdateLogColumn => ["UPDATE LOG", "\u7248\u672c\u66f4\u65b0\u8bb0\u5f55", "\u66f4\u65b0\u8bb0\u5f55"],
            _ => [column]
        };

    private static IReadOnlyDictionary<string, double> InferMissingHeaderCentersFromPartialIcons(
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, double> knownCenters,
        IReadOnlyList<Stage1MultilanguageTableCluster> columnClusters)
    {
        var centers = new Dictionary<string, double>(knownCenters, StringComparer.Ordinal);
        if (columns.Count == 0)
            return centers;

        var orderedKnown = columns
            .Select((column, index) => (column, index))
            .Where(item => centers.ContainsKey(item.column))
            .ToList();
        double? step = null;
        if (orderedKnown.Count >= 2)
        {
            var deltas = orderedKnown
                .Zip(orderedKnown.Skip(1), (left, right) => (centers[right.column] - centers[left.column]) / (right.index - left.index))
                .Where(delta => delta > 0.03 && delta < 0.25)
                .ToList();
            if (deltas.Count > 0)
                step = Math.Clamp(deltas.Average(), 0.07, 0.16);
        }

        if (step == null && columnClusters.Count > 0)
        {
            var rightMostIcon = columnClusters.Max(cluster => cluster.Center);
            var lastKnown = orderedKnown.LastOrDefault();
            if (lastKnown.column != null)
            {
                var inferred = (rightMostIcon - centers[lastKnown.column]) / Math.Max(1, columns.Count - 1 - lastKnown.index);
                if (inferred > 0.03 && inferred < 0.25)
                    step = Math.Clamp(inferred, 0.07, 0.16);
            }
        }

        step ??= 0.10;
        var anchor = orderedKnown.FirstOrDefault();
        if (anchor.column == null && columnClusters.Count > 0)
        {
            anchor = (columns[^1], columns.Count - 1);
            centers[anchor.column] = columnClusters.Max(cluster => cluster.Center);
        }

        if (anchor.column == null)
            return centers;

        var anchorCenter = centers[anchor.column];
        foreach (var (column, index) in columns.Select((column, index) => (column, index)))
        {
            if (!centers.ContainsKey(column))
                centers[column] = anchorCenter + (index - anchor.index) * step.Value;
        }

        return centers;
    }

    private static bool IsMultilanguageTableTextEditIconCandidate(AutomationTarget target) =>
        target.Width <= 0.08 &&
        target.Height <= 0.08;

    private static IReadOnlyList<Stage1MultilanguageTableCluster> ClusterMultilanguageTableCoordinates(
        IEnumerable<double> values,
        double tolerance)
    {
        var ordered = values
            .Where(value => !double.IsNaN(value))
            .OrderBy(value => value)
            .ToList();
        var clusters = new List<List<double>>();
        foreach (var value in ordered)
        {
            var last = clusters.LastOrDefault();
            if (last == null || Math.Abs(last.Average() - value) > tolerance)
                clusters.Add([value]);
            else
                last.Add(value);
        }

        return clusters
            .Select((members, index) => new Stage1MultilanguageTableCluster(
                index,
                members.Average(),
                members.Count,
                members))
            .ToList();
    }

    private static string FormatClusters(IReadOnlyList<Stage1MultilanguageTableCluster> clusters) =>
        string.Join("; ", clusters.Select(cluster =>
            $"{cluster.Index}@{cluster.Center:F3} count={cluster.Count} members=[{string.Join(",", cluster.Members.Select(member => member.ToString("F3")))}]"));

    private static string FormatColumnCenters(IReadOnlyDictionary<string, double> centers) =>
        string.Join(",", centers.Select(item => $"{item.Key}:{item.Value:F3}"));

    private async Task<(StablePageResult Editor, Stage1MultilanguageContext Context)> FillConfiguredMultilanguageRowAsync(
        UploadRequest request,
        StablePageResult editor,
        LocalizedArchiveInfo entry,
        Stage1MultilanguageComputedRow row,
        Stage1MultilanguageTableGeometry geometry,
        bool earlyAccessColumnEnabled,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var fieldValues = new List<(string Column, string Value)>
        {
            (MlStageNameColumn, entry.LevelName)
        };
        if (earlyAccessColumnEnabled)
            fieldValues.Add((MlEarlyAccessDescriptionColumn, entry.EarlyAccessDescription));
        fieldValues.Add((MlGameplayDetailsColumn, entry.GameDescription));
        fieldValues.Add((MlStageDescriptionColumn, entry.LevelDescription));
        fieldValues.Add((MlUpdateLogColumn, entry.VersionLog));

        var layoutWindow = editor.Capture.Window;
        foreach (var (column, value) in fieldValues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field=Multilanguage action=SkipCell language={entry.LanguageCode} " +
                    $"rowIndex={row.RowIndex} columnName=\"{column}\" reason=empty");
                continue;
            }

            var replaceResult = await ContinueStepAsync($"Multilanguage: {entry.LanguageCode} / {column}", fresh =>
                AtomicContinuationAsync(async () =>
                {
            if (fresh != null)
            {
                editor = fresh;
                if (editor.Recognition.Page == GamePage.MultiLanguageEditor)
                {
                    editor = await AddMissingEditIconsAsync(editor, cancellationToken);
                    var headers = await RecognizeMultilanguageTableHeaderOcrAsync(editor, cancellationToken);
                    var inferred = InferMultilanguageTableGeometry(editor, context.ComputedVisibleRows.Count,
                        geometry.ColumnCenters.Keys.ToList(), headers, workflowLog);
                    geometry = inferred.Geometry ?? throw new InvalidOperationException("Cannot re-locate the current language cell. Keep the same table viewport open.");
                    layoutWindow = editor.Capture.Window;
                    context = context with { TableGeometry = geometry };
                    // The language template below determines the row after resuming.
                }
            }
            if (editor.Recognition.Page != GamePage.TextEditDialog)
            {
                LanguageLabelTemplates.Match? confirmedLabel = null;
                for (var attempt = 1; attempt <= 5; attempt++)
                {
                    confirmedLabel = LanguageLabelTemplates.Locate(editor.Capture.Bitmap)
                        .SingleOrDefault(m => m.Language == entry.LanguageCode && Math.Abs(m.Y-row.CenterY) < .015);
                    LogWorkflow(workflowLog, $"step=Multilanguage action=ConfirmRowBeforeWrite language={entry.LanguageCode} column={column} attempt={attempt} found={confirmedLabel != null}");
                    if (confirmedLabel != null) break;
                    if (attempt == 5) break;
                    await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
                    var refreshed = await WaitForMultilanguageEditorContextAsync(request, context, workflowLog,
                        "Multilanguage.ReconfirmRow", "stage1-multilanguage-row-reconfirm-failed", cancellationToken);
                    editor = refreshed.Editor; context = refreshed.Context;
                }
                if (confirmedLabel == null)
                    throw new InvalidOperationException($"Language row could not be confirmed after retries: {entry.LanguageCode}/{column}. Keep the same table viewport open and continue.");
                row = row with { CenterY = confirmedLabel.Y };
            }
            if (editor.Capture.Window != layoutWindow)
                throw new InvalidOperationException("Game window changed during multilingual filling. Continue to re-locate the table columns.");
            var clickX = geometry.ColumnCenters[column];
            var clickY = row.CenterY;
            context = context with
            {
                CellEdits = context.CellEdits
                    .Append(new Stage1MultilanguageCellEditDiagnostic(row.RowIndex, entry.LanguageCode, column, clickX, clickY))
                    .ToList()
            };
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=ClickCellCenter language={entry.LanguageCode} " +
                $"rowIndex={row.RowIndex} columnName=\"{column}\" clickPoint=({clickX:F3},{clickY:F3})");
            if (editor.Recognition.Page != GamePage.TextEditDialog)
            {
                await ClickNormalizedAsync(editor.Capture.Window, clickX, clickY, cancellationToken);
                await DelayWithTimingAsync(TimeSpan.FromMilliseconds(350), cancellationToken);
            }
            return await ReplaceTextInMultilanguageCellAsync(
                request,
                editor,
                entry.LanguageCode,
                column,
                value,
                context,
                workflowLog,
                cancellationToken);
                }), cancellationToken);
            editor = replaceResult.Editor;
            context = replaceResult.Context;
        }

        return (editor, context);
    }

    private async Task<(StablePageResult Editor, Stage1MultilanguageContext Context)> ReplaceTextInMultilanguageCellAsync(
        UploadRequest request,
        StablePageResult previousEditor,
        string language,
        string column,
        string value,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var expectsDialog = !IsMultilanguageInlineCellColumn(column);
        var waitResult = await WaitForMultilanguageCellEditorOrDialogAsync(
            request,
            context,
            workflowLog,
            $"EditPreviewInfo.Multilanguage.{language}.{NormalizeFileNameSegment(column)}.WaitEditorOrDialog",
            language,
            column,
            allowInlineEditor: !expectsDialog,
            cancellationToken);
        var current = waitResult.Page;
        context = waitResult.Context;
        if (waitResult.SkipEdit)
            return (current, context);

        LogMultilanguageSource(language, column, value, workflowLog);

        if (current.Recognition.Page == GamePage.TextEditDialog)
        {
            if (!expectsDialog)
            {
                SaveMultilanguageDiagnostic(
                    current,
                    "stage1-preview-multilanguage-unexpected-text-dialog",
                    request,
                    context,
                    workflowLog,
                    $"Unexpected TextEditDialog for inline multilanguage cell. language={language}, column={column}.");
                throw new InvalidOperationException($"Unexpected TextEditDialog for inline multilanguage cell. language={language}, column={column}.");
            }

            var inputTarget = FindTargets(current.Recognition, WorkflowTargetNames.TextInputArea).FirstOrDefault();
            if (inputTarget == null)
            {
                SaveMultilanguageDiagnostic(
                    current,
                    "stage1-preview-multilanguage-text-input-missing",
                    request,
                    context,
                    workflowLog,
                    $"TextEditDialog did not expose text_input_area for language={language}, column={column}.");
                throw new InvalidOperationException($"TextEditDialog did not expose text_input_area for language={language}, column={column}.");
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Multilanguage action=FillTextDialog language={language} " +
                $"columnName=\"{column}\" target=({FormatTarget(inputTarget)})");
            await ClickTextInputAreaForFocusAsync(current, inputTarget, $"Multilanguage.{language}.{column}", workflowLog, cancellationToken);
            var counterBox = new NormalizedBox(
                Math.Clamp(inputTarget.CenterX + inputTarget.Width / 2 - .055, .05, .95),
                Math.Clamp(inputTarget.CenterY + inputTarget.Height / 2 - .025, .025, .975), .10, .05);
            var previousCount = ReadMultilanguageCharacterCount(await ReadMultilanguageEvidenceAsync(current,
                counterBox, $"multilanguage/{language}/{column}/counter-before", workflowLog, cancellationToken));
            _recordAction?.Invoke("shortcut:Ctrl+A");
            _recordAction?.Invoke($"type_text:Multilanguage.{language}.{column}");
            await _input.ReplaceMultilanguageTextAsync(current.Capture.Window, value, cancellationToken);
            current = await VerifyMultilanguageTextAsync(current,
                new NormalizedBox(inputTarget.CenterX, inputTarget.CenterY - inputTarget.Height * .1,
                    inputTarget.Width * .94, inputTarget.Height * .72),
                language, column, value, workflowLog, cancellationToken, counterBox, previousCount);
            var confirm = await FindMultilanguageConfirmAsync(current, workflowLog, cancellationToken);
            if (confirm.Target == null)
            {
                SaveMultilanguageDiagnostic(
                    current,
                    "stage1-preview-multilanguage-text-confirm-missing",
                    request,
                    context with { OcrTexts = context.OcrTexts.Concat(confirm.OcrTexts).ToList() },
                    workflowLog,
                    $"TextEditDialog confirm button was not OCR-confirmed for language={language}, column={column}.");
                throw new InvalidOperationException($"TextEditDialog confirm button was not OCR-confirmed for language={language}, column={column}.");
            }

            LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=ConfirmTextDialog language={language} columnName=\"{column}\" target=({FormatTarget(confirm.Target)})");
            await ClickSpecificTargetAsync(current, confirm.Target, cancellationToken);
            var editorResult = await WaitForMultilanguageEditorContextAsync(
                request,
                context,
                workflowLog,
                $"EditPreviewInfo.Multilanguage.{language}.{NormalizeFileNameSegment(column)}.WaitEditorAfterDialog",
                "stage1-preview-multilanguage-return-editor-after-text-dialog-failed",
                cancellationToken);
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=SaveReturnConfirmed " +
                $"language={language} column=\"{column}\" exactContentVerified=False");
            return (editorResult.Editor, editorResult.Context);
        }

        if (expectsDialog)
        {
            SaveMultilanguageDiagnostic(
                current,
                "stage1-preview-multilanguage-text-dialog-not-opened",
                request,
                context,
                workflowLog,
                $"Expected TextEditDialog for multilanguage cell, but stayed on editor. language={language}, column={column}.");
            throw new InvalidOperationException($"Expected TextEditDialog for multilanguage cell, but stayed on editor. language={language}, column={column}.");
        }

        _recordAction?.Invoke("shortcut:Ctrl+A");
        _recordAction?.Invoke($"type_text:Multilanguage.{language}.{column}");
        await _input.ReplaceMultilanguageTextAsync(current.Capture.Window, value, cancellationToken);
        await _input.PressKeyAsync(current.Capture.Window, GameVirtualKey.Enter, cancellationToken);
        LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=InlineCellTextReplaced language={language} columnName=\"{column}\"");
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(200), cancellationToken);
        var inlineEditorResult = await WaitForMultilanguageEditorContextAsync(
            request,
            context,
            workflowLog,
            $"EditPreviewInfo.Multilanguage.{language}.{NormalizeFileNameSegment(column)}.WaitEditorAfterInline",
            "stage1-preview-multilanguage-return-editor-after-inline-failed",
            cancellationToken);
        var cell = context.CellEdits.Last(edit => edit.Language == language && edit.Column == column);
        var verifiedEditor = await VerifyMultilanguageTextAsync(inlineEditorResult.Editor,
            new NormalizedBox(cell.ClickX, cell.ClickY, .10, .038),
            language, column, value, workflowLog, cancellationToken, reuseInitialFrame: true);
        LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Multilanguage action=InlineCommitReturnConfirmed " +
            $"language={language} column=\"{column}\" exactContentVerified=False");
        return (verifiedEditor, inlineEditorResult.Context);
    }

    private static bool IsMultilanguageInlineCellColumn(string column) =>
        string.Equals(column, MlStageNameColumn, StringComparison.Ordinal);

    private async Task<(StablePageResult Page, Stage1MultilanguageContext Context, bool SkipEdit)> WaitForMultilanguageCellEditorOrDialogAsyncCore(
        UploadRequest request,
        Stage1MultilanguageContext context,
        List<string> workflowLog,
        string stepName,
        string language,
        string column,
        bool allowInlineEditor,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + ShortWait;
        StablePageResult? last = null;
        StablePageResult? editorCandidate = null;
        var editorCandidateAt = DateTimeOffset.MinValue;
        var attempt = 0;

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            last = await RecognizeOnceAsync(cancellationToken, supplementEditIcons: false);
            if (last.Recognition.Page == GamePage.TextEditDialog)
            {
                LogWorkflow(workflowLog, 
                    $"step={stepName} field=Multilanguage action=WaitCellEditorOrDialog attempt={attempt} " +
                    $"language={language} columnName=\"{column}\" result=TextEditDialog rawPage={last.Recognition.Page} confidence={last.Recognition.Confidence:F2}");
                return (last, context, false);
            }

            if (string.Equals(column, MlUpdateLogColumn, StringComparison.Ordinal))
            {
                var unavailable = await DetectUnavailableMultilanguageUpdateLogDialogAsync(last, cancellationToken);
                context = context with { OcrTexts = context.OcrTexts.Concat(unavailable.OcrTexts).ToList() };
                LogWorkflow(workflowLog, 
                    $"step={stepName} field=Multilanguage action=CheckUnavailableUpdateLogDialog attempt={attempt} " +
                    $"language={language} raw=\"{FormatRawOcrTexts(unavailable.OcrTexts)}\" " +
                    $"normalized=\"{FormatNormalizedOcrTexts(unavailable.OcrTexts)}\" detected={unavailable.Detected}");
                if (unavailable.Detected)
                {
                    LogWorkflow(workflowLog, 
                        $"step=EditPreviewInfo field=Multilanguage action=SkipCell language={language} " +
                        $"columnName=\"{column}\" reason=update_log_not_available_for_editing");
                    await _input.PressKeyAsync(last.Capture.Window, GameVirtualKey.Escape, cancellationToken);
                    var editorResult = await WaitForMultilanguageEditorContextAsync(
                        request,
                        context,
                        workflowLog,
                        $"{stepName}.WaitEditorAfterUnavailableUpdateLog",
                        "stage1-preview-multilanguage-return-editor-after-unavailable-update-log-failed",
                        cancellationToken);
                    return (editorResult.Editor, editorResult.Context, true);
                }
            }

            var contextResult = await DetectMultilanguageEditorContextAsync(last, cancellationToken);
            context = context with
            {
                OcrTexts = context.OcrTexts.Concat(contextResult.OcrTexts).ToList(),
                PageContextEvidence = context.PageContextEvidence.Append(contextResult.Evidence).ToList()
            };
            LogWorkflow(workflowLog, 
                $"step={stepName} field=Multilanguage action=WaitCellEditorOrDialog attempt={attempt} " +
                $"language={language} columnName=\"{column}\" rawPage={contextResult.Evidence.RawPage} " +
                $"contextualPage={contextResult.Evidence.ContextualPage} ocrTitle=\"{contextResult.Evidence.OcrTitle}\" " +
                $"tableHeaderTokens=[{string.Join(", ", contextResult.Evidence.TableHeaderTokens)}] " +
                $"addLanguageButtonOcr=\"{contextResult.Evidence.AddLanguageButtonOcr}\" accepted={contextResult.Evidence.Accepted} " +
                $"reason=\"{contextResult.Evidence.Reason}\"");

            if (contextResult.Evidence.ContextualPage == GamePage.MultiLanguageEditor)
            {
                editorCandidate = WithContextualPage(last, GamePage.MultiLanguageEditor);
                if (editorCandidateAt == DateTimeOffset.MinValue)
                    editorCandidateAt = _clock.UtcNow;

                if (allowInlineEditor &&
                    _clock.UtcNow - editorCandidateAt >= TimeSpan.FromMilliseconds(1200))
                {
                    LogWorkflow(workflowLog, 
                        $"step={stepName} field=Multilanguage action=WaitCellEditorOrDialog " +
                        $"language={language} columnName=\"{column}\" result=InlineCellEditor reason=text_dialog_not_seen");
                    return (editorCandidate, context, false);
                }
            }

            if (last.Recognition.Page.IsBlocking())
                throw new InvalidOperationException($"Blocking page appeared during {stepName}: {last.Recognition.Page}.");

            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        if (allowInlineEditor && editorCandidate != null)
        {
            LogWorkflow(workflowLog, 
                $"step={stepName} field=Multilanguage action=WaitCellEditorOrDialog " +
                $"language={language} columnName=\"{column}\" result=InlineCellEditor reason=deadline_with_contextual_editor");
            return (editorCandidate, context, false);
        }

        var diagnostic = SaveMultilanguageDiagnostic(
            last,
            $"stage1-{NormalizeFileNameSegment(stepName)}-timeout",
            request,
            context,
            workflowLog,
            allowInlineEditor
                ? $"Timed out waiting for multilanguage inline cell editor or text dialog. language={language}, column={column}."
                : $"Timed out waiting for required multilanguage text dialog. language={language}, column={column}.");
        throw new TimeoutException(
            $"Timed out during {stepName}. expected=[{(allowInlineEditor ? $"{GamePage.MultiLanguageEditor}, " : string.Empty)}{GamePage.TextEditDialog}], " +
            $"lastPage={last?.Recognition.Page}, confidence={last?.Recognition.Confidence:F2}, diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<(bool Detected, IReadOnlyList<WorkflowOcrTextResult> OcrTexts)> DetectUnavailableMultilanguageUpdateLogDialogAsync(
        StablePageResult result,
        CancellationToken cancellationToken)
    {
        var titleTexts = await _workflowOcr.RecognizeBoxAsync(
            result.Capture,
            new NormalizedBox(0.50, 0.20, 0.42, 0.11),
            "MultilanguageEditor/update_log_unavailable/title",
            cancellationToken);
        var bodyTexts = await _workflowOcr.RecognizeBoxAsync(
            result.Capture,
            new NormalizedBox(0.50, 0.58, 0.58, 0.16),
            "MultilanguageEditor/update_log_unavailable/body",
            cancellationToken);
        var texts = titleTexts.Concat(bodyTexts).ToList();
        var hasTitle = OcrTextsContainAny(titleTexts, ["UPDATE LOG", "UPDATELOG", "版本更新记录", "\u66f4\u65b0\u8bb0\u5f55"]);
        var hasUnavailableBody = OcrTextsContainAny(
            bodyTexts,
            [
                "NO GAME VERSION UPDATE LOG AVAILABLE FOR EDITING",
                "NO GAME VERSION UPDATE LOG",
                "暂无游戏版本可编辑版本更新记录",
                "暂无游戏版本可编辑版更记录",
                "AVAILABLE FOR EDITING",
                "UPDATE LOG AVAILABLE",
                "\u6682\u65e0\u53ef\u7f16\u8f91",
                "\u4e0d\u53ef\u7f16\u8f91"
            ]);
        return (hasTitle && hasUnavailableBody, texts);
    }

    private static readonly string[] EarlyAccessOptionTexts =
    [
        "PUBLISH STAGE AS EARLY ACCESS", "EARLY ACCESS",
        "以先行体验版本发布关卡", "以先行體驗版本發布關卡",
        "先行体验", "先行版本"
    ];

    private async Task<StablePageResult> FillPreviewEarlyAccessIfNeededAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Setting the early-access option");
        const string fieldName = "EarlyAccess";
        if (preview.Recognition.Page != GamePage.PreviewEditor)
            preview = await WaitForPageEvidenceAsync(preview, Pages(GamePage.PreviewEditor), "Edit.PageRecovery", ShortWait, cancellationToken);

        var choice = await FindChoiceOptionByOcrAsync(preview, EarlyAccessOptionTexts, cancellationToken);

        if (choice.Target == null)
        {
            if (!request.Archive.IsEarlyAccess)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} result=Skipped reason=disabled_option_not_visible " +
                    $"ocr=\"{FormatOcrTexts(choice.OcrTexts)}\"");
                return preview;
            }

            SavePreviewInfoDiagnostic(
                preview,
                "stage1-preview-early-access-option-missing",
                request,
                fieldName,
                "OCR choice_option as Publish Stage as Early Access",
                choice.OcrTexts,
                workflowLog,
                "Preview early-access option was not OCR-confirmed.");
            throw new InvalidOperationException("Preview early-access option was not OCR-confirmed.");
        }

        var alreadySelected = IsChoiceOptionVisuallySelected(preview.Capture, choice.Target, out var selectedEvidence);
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field={fieldName} action=EnsureState desiredSelected={request.Archive.IsEarlyAccess} " +
            $"alreadySelected={alreadySelected} evidence=\"{selectedEvidence}\" target=({FormatTarget(choice.Target)}) " +
            $"ocr=\"{FormatOcrTexts(choice.OcrTexts)}\"");
        if (alreadySelected == request.Archive.IsEarlyAccess)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=RefreshAfterEnsureState " +
                $"reason={(request.Archive.IsEarlyAccess ? "already_selected" : "already_disabled")}");
            return await WaitForExpectedAsync(
                "EditPreviewInfo.EarlyAccess.RefreshPreviewEditorAlreadyDesiredState",
                Pages(GamePage.PreviewEditor),
                ShortWait,
                stableCount: 1,
                cancellationToken);
        }

        await ClickSpecificTargetAsync(preview, choice.Target, cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);
        return await WaitForExpectedAsync(
            request.Archive.IsEarlyAccess
                ? "EditPreviewInfo.EarlyAccess.WaitPreviewEditorAfterEnable"
                : "EditPreviewInfo.EarlyAccess.WaitPreviewEditorAfterDisable",
            Pages(GamePage.PreviewEditor),
            ShortWait,
            stableCount: 1,
            cancellationToken);
    }

    private string ResolveRequiredArchiveResourcePath(
        UploadRequest request,
        string relativeOrAbsolutePath,
        string fieldName,
        List<string> workflowLog)
    {
        var resolvedPath = _storage.ResolveResourcePath(request.Archive, relativeOrAbsolutePath);
        LogWorkflow(workflowLog, 
            $"step=ResolveArchiveResource field={fieldName} source=\"{relativeOrAbsolutePath}\" resolved=\"{resolvedPath ?? "<null>"}\"");

        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                $"Preview resource '{fieldName}' does not exist after resolving archive-managed path.",
                resolvedPath ?? relativeOrAbsolutePath);
        }

        return resolvedPath;
    }

    private async Task<StablePageResult> FillPreviewTextFieldAsyncCore(
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
        ReportOperation($"Entering {HudField(fieldName)}");
        if (string.IsNullOrWhiteSpace(value))
        {
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} result=Skipped reason=empty");
            return preview;
        }

        var selection = await LocatePreviewFieldTargetWithScrollAsync(
            request,
            preview,
            fieldName,
            preferredTargetNames,
            genericTargetName,
            labelTokens,
            workflowLog,
            cancellationToken);

        if (selection.Target == null)
        {
            SavePreviewInfoDiagnostic(
                selection.Page,
                $"stage1-preview-{fieldName.ToLowerInvariant()}-target-missing",
                request,
                fieldName,
                $"Detect {string.Join("/", preferredTargetNames.Concat([genericTargetName]))}",
                selection.OcrTexts,
                workflowLog,
                $"Preview field target was not detected or OCR did not match field '{fieldName}'.");
            throw new InvalidOperationException($"Preview field target was not detected or OCR did not match field '{fieldName}'.");
        }

        LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} action=ClickEditTarget scrollAttempts={selection.ScrollAttempts} target=({FormatTarget(selection.Target)})");
        return await FillPreviewInlineFieldConfirmedAsync(selection.Page, selection.Target,
            fieldName, value, workflowLog, cancellationToken);
    }

    private async Task<StablePageResult> FillPreviewEarlyAccessDescriptionIfNeededAsyncCore(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        const string fieldName = "EarlyAccessDescription";
        if (string.IsNullOrWhiteSpace(request.Archive.EarlyAccessDescription))
        {
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} result=Skipped reason=empty");
            return preview;
        }

        var selection = await LocatePreviewEarlyAccessDescriptionTargetAsync(
            request,
            preview,
            workflowLog,
            cancellationToken);

        if (selection.Target == null)
        {
            var ocrSummary = FormatOcrTexts(selection.OcrTexts);
            SavePreviewInfoDiagnostic(
                selection.Page,
                "stage1-preview-earlyaccessdescription-target-missing",
                request,
                fieldName,
                "Early Access was selected; search current viewport first, then scroll upward before any downward scan",
                selection.OcrTexts,
                workflowLog,
                $"field={fieldName}; Early Access was selected; current scroll/search direction={selection.SearchDirection}; candidates OCR summary=\"{ocrSummary}\".");
            throw new InvalidOperationException(
                $"field={fieldName}; Early Access was selected; current scroll/search direction={selection.SearchDirection}; candidates OCR summary=\"{ocrSummary}\".");
        }

        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field={fieldName} action=ClickEditTarget " +
            $"scrollAttempts={selection.ScrollAttempts} searchDirection={selection.SearchDirection} target=({FormatTarget(selection.Target)})");
        return await FillPreviewInlineFieldConfirmedAsync(selection.Page, selection.Target,
            fieldName, request.Archive.EarlyAccessDescription, workflowLog, cancellationToken);
    }

    private async Task<StablePageResult> FillOptionalPreviewTextFieldAsync(
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
        if (string.IsNullOrWhiteSpace(value))
        {
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} result=Skipped reason=empty");
            return preview;
        }

        try
        {
            return await FillPreviewTextFieldAsync(
                request,
                preview,
                fieldName,
                value,
                preferredTargetNames,
                genericTargetName,
                labelTokens,
                workflowLog,
                cancellationToken);
        }
        catch (Exception ex) when (IsSkippablePreviewField(fieldName))
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} result=Skipped reason=optional_field_unavailable " +
                $"error=\"{ex.GetType().Name}: {ex.Message}\"");
            return await RecoverPreviewEditorAfterOptionalFieldFailureAsync(
                request,
                fieldName,
                workflowLog,
                cancellationToken);
        }
    }

    private async Task<StablePageResult> FillOptionalPreviewVersionLogIfNeededAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Editing the version update log");
        const string fieldName = "VersionLog";
        if (string.IsNullOrWhiteSpace(request.Archive.VersionLog))
        {
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} result=Skipped reason=empty");
            return preview;
        }

        try
        {
            var selection = await LocatePreviewVersionLogTargetWithScrollAsync(
                request,
                preview,
                workflowLog,
                cancellationToken);

            if (selection.Target == null)
            {
                SavePreviewInfoDiagnostic(
                    selection.Page,
                    "stage1-preview-versionlog-target-missing",
                    request,
                    fieldName,
                    "Detect preview_version_record_area or a unique edit_row OCR-confirmed as VersionLog",
                    selection.OcrTexts,
                    workflowLog,
                    "VersionLog row was not detected or OCR-confirmed.",
                    isFinalFailure: false,
                    recoverable: true,
                    phase: fieldName,
                    updateLatest: false);
                throw new InvalidOperationException("VersionLog row was not detected or OCR-confirmed.");
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=ClickEditTarget selectedSource={selection.SearchDirection} " +
                $"scrollAttempts={selection.ScrollAttempts} target=({FormatTarget(selection.Target)})");
            await ClickSpecificTargetAsync(selection.Page, selection.Target, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            var afterClick = await RecognizeOnceAsync(cancellationToken);
            if (await TryDetectOptionalPreviewFieldUnavailableAsync(request, afterClick, fieldName, workflowLog, cancellationToken))
            {
                LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} result=Skipped reason=optional_unavailable");
                return await RecoverPreviewEditorAfterOptionalFieldFailureAsync(
                    request,
                    fieldName,
                    workflowLog,
                    cancellationToken);
            }

            return await ReplaceTextInPreviewInlineAsync(
                WithContextualPage(afterClick, GamePage.PreviewEditor),
                fieldName,
                request.Archive.VersionLog,
                workflowLog,
                commitWithEnter: false,
                cancellationToken);
        }
        catch (Exception ex) when (IsSkippablePreviewField(fieldName))
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} result=Skipped reason=optional_field_unavailable " +
                $"error=\"{ex.GetType().Name}: {ex.Message}\"");
            return await RecoverPreviewEditorAfterOptionalFieldFailureAsync(
                request,
                fieldName,
                workflowLog,
                cancellationToken);
        }
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewVersionLogTargetWithScrollAsyncCore(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        const string fieldName = "VersionLog";
        var current = preview;
        var allOcrTexts = new List<WorkflowOcrTextResult>();

        for (var scrollAttempt = 0; scrollAttempt <= PreviewEditorMaxScrollAttempts; scrollAttempt++)
        {
            if (current.Recognition.Page != GamePage.PreviewEditor)
                current = await WaitForPageEvidenceAsync(current, Pages(GamePage.PreviewEditor), "Preview.FieldPageRecovery", ShortWait, cancellationToken);

            var preferred = FindFirstAvailableTarget(current.Recognition, [WorkflowTargetNames.PreviewVersionRecordArea]);
            if (preferred != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} selectedSource=preview_version_record_area " +
                    $"result=preferredTargetFound target=({FormatTarget(preferred)})");
                return new PreviewFieldTargetSelection(current, preferred, allOcrTexts, scrollAttempt, "preview_version_record_area");
            }

            var editRowMatch = await FindPreviewEditRowByOcrAsync(
                current,
                fieldName,
                [
                    "UPDATE LOG",
                    "VERSION LOG",
                    "CLICK TO EDIT UPDATE LOG",
                    "版本更新记录", "点击编辑版本更新记录", "\u7248\u672c\u8bb0\u5f55",
                    "\u66f4\u65b0\u8bb0\u5f55"
                ],
                workflowLog,
                cancellationToken);
            allOcrTexts.AddRange(editRowMatch.OcrTexts);
            if (editRowMatch.IsAmbiguous)
            {
                SavePreviewInfoDiagnostic(
                    current,
                    "stage1-preview-versionlog-edit-row-ambiguous",
                    request,
                    fieldName,
                    "VersionLog edit_row OCR must be a unique match",
                    allOcrTexts,
                    workflowLog,
                    $"VersionLog edit_row OCR matched multiple candidates. matches=[{string.Join("; ", editRowMatch.Matches.Select(FormatTarget))}]",
                    isFinalFailure: false,
                    recoverable: true,
                    phase: fieldName,
                    updateLatest: false);
                throw new InvalidOperationException(
                    $"VersionLog edit_row OCR matched multiple candidates. matches=[{string.Join("; ", editRowMatch.Matches.Select(FormatTarget))}]");
            }

            if (editRowMatch.Target != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} selectedSource=edit_row_ocr " +
                    $"result=genericTargetOcrConfirmed target=({FormatTarget(editRowMatch.Target)})");
                return new PreviewFieldTargetSelection(current, editRowMatch.Target, allOcrTexts, scrollAttempt, "edit_row_ocr");
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} " +
                "result=notFound preferredTargets=[preview_version_record_area] genericTarget=edit_row selectedSource=<none>");

            if (scrollAttempt == PreviewEditorMaxScrollAttempts)
                break;

            _recordAction?.Invoke($"scroll:preview_editor:{PreviewEditorScrollWheelDelta}");
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=ScrollPreviewEditor " +
                $"amount={PreviewEditorScrollWheelDelta} scrollAttempt={scrollAttempt + 1}");
            await _input.ScrollAsync(
                current.Capture.Window,
                ScrollCenterX,
                ScrollCenterY,
                PreviewEditorScrollWheelDelta,
                cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
            current = await WaitForExpectedAsync(
                $"EditPreviewInfo.{fieldName}.ScrollPreviewEditor{scrollAttempt + 1}",
                Pages(GamePage.PreviewEditor),
                ShortWait,
                stableCount: 1,
                cancellationToken);
        }

        return new PreviewFieldTargetSelection(current, null, allOcrTexts, PreviewEditorMaxScrollAttempts, "not_found");
    }

    private static bool IsSkippablePreviewField(string fieldName) =>
        string.Equals(fieldName, "VersionLog", StringComparison.Ordinal);

    private async Task<StablePageResult> RecoverPreviewEditorAfterOptionalFieldFailureAsync(
        UploadRequest request,
        string fieldName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var current = await RecognizeOnceAsync(cancellationToken);
        if (current.Recognition.Page == GamePage.PreviewEditor)
        {
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} action=RecoverOptionalField result=AlreadyPreviewEditor");
            return current;
        }

        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field={fieldName} action=RecoverOptionalField currentPage={current.Recognition.Page} press=Escape");
        _activation.RestoreAndActivate(current.Capture.Window);
        await _input.PressKeyAsync(current.Capture.Window, GameVirtualKey.Escape, cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);

        try
        {
            return await WaitForExpectedAsync(
                $"EditPreviewInfo.{fieldName}.RecoverPreviewEditor",
                Pages(GamePage.PreviewEditor),
                ShortWait,
                stableCount: 1,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var failed = await RecognizeOnceAsync(cancellationToken);
            SavePreviewInfoDiagnostic(
                failed,
                $"stage1-preview-{fieldName.ToLowerInvariant()}-optional-recover-failed",
                request,
                fieldName,
                "Recover PreviewEditor after optional field failure",
                [],
                workflowLog,
                $"Optional preview field recovery failed: {ex.Message}");
            throw;
        }
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewFieldTargetWithScrollAsyncCore(
        UploadRequest request,
        StablePageResult preview,
        string fieldName,
        IReadOnlyList<string> preferredTargetNames,
        string genericTargetName,
        IReadOnlyList<string> labelTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var current = preview;
        var allOcrTexts = new List<WorkflowOcrTextResult>();

        for (var scrollAttempt = 0; scrollAttempt <= PreviewEditorMaxScrollAttempts; scrollAttempt++)
        {
            if (current.Recognition.Page != GamePage.PreviewEditor)
                current = await WaitForPageEvidenceAsync(current, Pages(GamePage.PreviewEditor), "Preview.FieldPageRecovery", ShortWait, cancellationToken);

            var preferred = string.Equals(genericTargetName, WorkflowTargetNames.TextInputArea, StringComparison.Ordinal)
                ? null
                : FindFirstAvailableTarget(current.Recognition, preferredTargetNames);
            if (preferred != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} " +
                    $"result=preferredTargetFound target={preferred.Name}");
                return new PreviewFieldTargetSelection(current, preferred, allOcrTexts, scrollAttempt);
            }

            var ocrMatch = string.Equals(genericTargetName, WorkflowTargetNames.TextInputArea, StringComparison.Ordinal)
                ? await FindPreviewTextInputAreaByContextOcrAsync(current, fieldName, preferredTargetNames, labelTokens, workflowLog, "down", cancellationToken)
                : await FindTargetByOcrAsync(current, genericTargetName, labelTokens, cancellationToken);
            allOcrTexts.AddRange(ocrMatch.OcrTexts);
            if (ocrMatch.Target != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} " +
                    $"result=genericTargetOcrConfirmed target={ocrMatch.Target.Name}");
                return new PreviewFieldTargetSelection(current, ocrMatch.Target, allOcrTexts, scrollAttempt);
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} scanPage={scrollAttempt} " +
                $"result=notFound preferredTargets=[{string.Join(",", preferredTargetNames)}] genericTarget={genericTargetName}");

            if (scrollAttempt == PreviewEditorMaxScrollAttempts)
                break;

            _recordAction?.Invoke($"scroll:preview_editor:{PreviewEditorScrollWheelDelta}");
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=ScrollPreviewEditor " +
                $"amount={PreviewEditorScrollWheelDelta} scrollAttempt={scrollAttempt + 1}");
            await _input.ScrollAsync(
                current.Capture.Window,
                ScrollCenterX,
                ScrollCenterY,
                PreviewEditorScrollWheelDelta,
                cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
            current = await WaitForExpectedAsync(
                $"EditPreviewInfo.{fieldName}.ScrollPreviewEditor{scrollAttempt + 1}",
                Pages(GamePage.PreviewEditor),
                ShortWait,
                stableCount: 1,
                cancellationToken);
        }

        return new PreviewFieldTargetSelection(current, null, allOcrTexts, PreviewEditorMaxScrollAttempts);
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewEarlyAccessDescriptionTargetAsyncCore(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        const string fieldName = "EarlyAccessDescription";
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var current = preview;

        if (current.Recognition.Page != GamePage.PreviewEditor)
        {
            SavePreviewInfoDiagnostic(
                current,
                "stage1-preview-earlyaccessdescription-unexpected-page",
                request,
                fieldName,
                "Stay on PreviewEditor while scanning for Early Access Version Description",
                allOcrTexts,
                workflowLog,
                $"field={fieldName}; Early Access was selected; current scroll/search direction=current; expected PreviewEditor, got {current.Recognition.Page}.");
            throw new InvalidOperationException(
                $"field={fieldName}; Early Access was selected; current scroll/search direction=current; expected PreviewEditor, got {current.Recognition.Page}.");
        }

        var currentMatch = await FindPreviewTextInputAreaByContextOcrAsync(
            current,
            fieldName,
            [WorkflowTargetNames.PreviewEarlyAccessDescription],
            ["EARLYACCESSVERSIONDESCRIPTION", "EARLYACCESS", "EARLY ACCESS", "先行版本说明", "请输入先行版本说明", "「先行体验」版本说明", "先行体验版本说明", "请输入先行体验版本说明"],
            workflowLog,
            "current",
            cancellationToken);
        allOcrTexts.AddRange(currentMatch.OcrTexts);
        if (currentMatch.Target != null)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} scanPage=0 searchDirection=current " +
                $"result=genericTargetOcrConfirmed target={currentMatch.Target.Name}");
            return new PreviewFieldTargetSelection(current, currentMatch.Target, allOcrTexts, 0, "current");
        }

        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field={fieldName} scanPage=0 searchDirection=current " +
            "result=notFound earlyAccessSelected=true");

        for (var upAttempt = 1; upAttempt <= 2; upAttempt++)
        {
            _recordAction?.Invoke($"scroll:preview_editor:{-PreviewEditorScrollWheelDelta}");
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=ScrollPreviewEditor " +
                $"direction=up amount={-PreviewEditorScrollWheelDelta} scrollAttempt={upAttempt}");
            await _input.ScrollAsync(
                current.Capture.Window,
                ScrollCenterX,
                ScrollCenterY,
                -PreviewEditorScrollWheelDelta,
                cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
            current = await WaitForExpectedAsync(
                $"EditPreviewInfo.{fieldName}.ScrollPreviewEditorUp{upAttempt}",
                Pages(GamePage.PreviewEditor),
                ShortWait,
                stableCount: 1,
                cancellationToken);

            var upMatch = await FindPreviewTextInputAreaByContextOcrAsync(
                current,
                fieldName,
                [WorkflowTargetNames.PreviewEarlyAccessDescription],
                ["EARLYACCESSVERSIONDESCRIPTION", "EARLYACCESS", "EARLY ACCESS", "先行版本说明", "请输入先行版本说明", "「先行体验」版本说明", "先行体验版本说明", "请输入先行体验版本说明"],
                workflowLog,
                "up",
                cancellationToken);
            allOcrTexts.AddRange(upMatch.OcrTexts);
            if (upMatch.Target != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field={fieldName} scanPage={upAttempt} searchDirection=up " +
                    $"result=genericTargetOcrConfirmed target={upMatch.Target.Name}");
                return new PreviewFieldTargetSelection(current, upMatch.Target, allOcrTexts, upAttempt, "up");
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} scanPage={upAttempt} searchDirection=up " +
                "result=notFound earlyAccessSelected=true");
        }

        var downSelection = await LocatePreviewFieldTargetWithScrollAsync(
            request,
            current,
            fieldName,
            [WorkflowTargetNames.PreviewEarlyAccessDescription],
            WorkflowTargetNames.TextInputArea,
            ["EARLYACCESSVERSIONDESCRIPTION", "EARLYACCESS", "EARLY ACCESS", "先行版本说明", "请输入先行版本说明", "「先行体验」版本说明", "先行体验版本说明", "请输入先行体验版本说明"],
            workflowLog,
            cancellationToken);
        allOcrTexts.AddRange(downSelection.OcrTexts);
        return downSelection with
        {
            OcrTexts = allOcrTexts,
            SearchDirection = downSelection.Target == null ? "up_then_down_not_found" : "up_then_down"
        };
    }

    private async Task<OcrTargetMatch> FindPreviewTextInputAreaByContextOcrAsync(
        StablePageResult current,
        string fieldName,
        IReadOnlyList<string> preferredTargetNames,
        IReadOnlyList<string> fallbackAcceptedTokens,
        List<string> workflowLog,
        string searchDirection,
        CancellationToken cancellationToken)
    {
        var profile = GetPreviewTextInputOcrProfile(fieldName, fallbackAcceptedTokens);
        var allOcrTexts = new List<WorkflowOcrTextResult>();

        var candidates = preferredTargetNames
            .Concat([WorkflowTargetNames.TextInputArea])
            .SelectMany(name => FindTargets(current.Recognition, name, 0.25))
            .GroupBy(target => (
                Name: target.Name,
                X: Math.Round(target.CenterX, 3),
                Y: Math.Round(target.CenterY, 3),
                W: Math.Round(target.Width, 3),
                H: Math.Round(target.Height, 3)))
            .Select(group => group.OrderByDescending(target => target.Confidence).First())
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .ThenByDescending(target => target.Confidence)
            .ToList();
        var matches = new List<AutomationTarget>();

        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            var target = candidates[candidateIndex];
            var headerTexts = await _workflowOcr.RecognizeBoxAsync(
                current.Capture,
                CreatePreviewTextInputHeaderOcrBox(target),
                $"{WorkflowTargetNames.TextInputArea}/{fieldName}/header",
                cancellationToken);
            var placeholderTexts = await _workflowOcr.RecognizeBoxAsync(
                current.Capture,
                CreatePreviewTextInputPlaceholderOcrBox(target),
                $"{WorkflowTargetNames.TextInputArea}/{fieldName}/placeholder",
                cancellationToken);
            var texts = headerTexts.Concat(placeholderTexts).ToList();
            allOcrTexts.AddRange(texts);
            var headerSignature = CreateOcrChangeSignature(headerTexts);
            var placeholderSignature = CreateOcrChangeSignature(placeholderTexts);
            var accepted = PreviewTextInputContextContainsAny(texts, profile.AcceptedTokens);
            var rejected = PreviewTextInputContextContainsAny(texts, profile.RejectedTokens);
            var reason = rejected
                ? "rejected_token"
                : accepted
                    ? "accepted_token"
                    : "no_accepted_token";

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=TextInputContextOcr " +
                $"searchDirection={searchDirection} candidateIndex={candidateIndex} target=({FormatTarget(target)}) " +
                $"header=\"{headerSignature}\" placeholder=\"{placeholderSignature}\" " +
                $"acceptedTokens=[{string.Join(",", profile.AcceptedTokens.Select(NormalizeWorkflowOcrText))}] " +
                $"rejectedTokens=[{string.Join(",", profile.RejectedTokens.Select(NormalizeWorkflowOcrText))}] " +
                $"accepted={accepted} rejected={rejected} reason={reason}");

            if (rejected)
                continue;

            if (!accepted)
                continue;

            matches.Add(target);
        }

        if (matches.Count == 1)
            return new OcrTargetMatch(matches[0], allOcrTexts);

        if (matches.Count > 1)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=TextInputContextOcr " +
                $"searchDirection={searchDirection} result=ambiguous matchCount={matches.Count} " +
                $"matches=[{string.Join("; ", matches.Select(FormatTarget))}]");
        }

        return new OcrTargetMatch(null, allOcrTexts);
    }

    private static bool PreviewTextInputContextContainsAny(
        IReadOnlyList<WorkflowOcrTextResult> ocrTexts,
        IReadOnlyList<string> tokens)
    {
        var normalizedTokens = tokens
            .Select(NormalizeWorkflowOcrText)
            .Where(token => token.Length > 0)
            .ToList();
        if (normalizedTokens.Count == 0)
            return false;

        return ocrTexts
            .Select(text => text.NormalizedText)
            .Where(text => text.Length > 0)
            .Any(text => normalizedTokens.Any(token =>
                string.Equals(text, token, StringComparison.Ordinal) ||
                text.Contains(token, StringComparison.Ordinal) ||
                token.Contains(text, StringComparison.Ordinal)));
    }

    private static PreviewTextInputOcrProfile GetPreviewTextInputOcrProfile(
        string fieldName,
        IReadOnlyList<string> fallbackAcceptedTokens)
    {
        if (string.Equals(fieldName, "GameplayDescription", StringComparison.Ordinal))
        {
            return new PreviewTextInputOcrProfile(
                ["GAMEPLAYDETAILS", "GAMEPLAY", "玩法说明", "请输入玩法说明", "\u73a9\u6cd5"],
                ["EARLYACCESS", "EARLY ACCESS", "STAGEDESCRIPTION", "LEVELDESCRIPTION", "UPDATELOG", "UPDATE LOG", "TAGS", "\u5173\u5361\u63cf\u8ff0", "\u5148\u884c"]);
        }

        if (string.Equals(fieldName, "LevelDescription", StringComparison.Ordinal))
        {
            return new PreviewTextInputOcrProfile(
                ["STAGEDESCRIPTION", "LEVELDESCRIPTION", "关卡描述", "请输入关卡描述"],
                ["EARLYACCESS", "EARLY ACCESS", "GAMEPLAYDETAILS", "GAMEPLAY", "UPDATELOG", "UPDATE LOG", "TAGS", "\u73a9\u6cd5", "\u5148\u884c"]);
        }

        if (string.Equals(fieldName, "EarlyAccessDescription", StringComparison.Ordinal))
        {
            return new PreviewTextInputOcrProfile(
                ["EARLYACCESSVERSIONDESCRIPTION", "EARLYACCESS", "EARLY ACCESS", "先行版本说明", "请输入先行版本说明", "「先行体验」版本说明", "先行体验版本说明", "请输入先行体验版本说明"],
                ["GAMEPLAYDETAILS", "GAMEPLAY", "STAGEDESCRIPTION", "LEVELDESCRIPTION", "TAGS", "UPDATELOG", "UPDATE LOG", "\u73a9\u6cd5", "\u5173\u5361\u63cf\u8ff0"]);
        }

        return new PreviewTextInputOcrProfile(fallbackAcceptedTokens, []);
    }

    private static NormalizedBox CreatePreviewTextInputHeaderOcrBox(AutomationTarget target)
    {
        var top = target.CenterY - target.Height / 2;
        var height = Math.Clamp(Math.Max(0.045, target.Height * 0.28), 0.045, 0.09);
        var width = Math.Clamp(target.Width, 0.10, 0.80);
        var centerY = Math.Clamp(top - height * 0.50, height / 2, 1 - height / 2);
        return new NormalizedBox(
            Math.Clamp(target.CenterX, width / 2, 1 - width / 2),
            centerY,
            width,
            height);
    }

    private static NormalizedBox CreatePreviewTextInputPlaceholderOcrBox(AutomationTarget target)
    {
        var top = target.CenterY - target.Height / 2;
        var height = Math.Clamp(target.Height * 0.40, 0.05, Math.Min(0.18, target.Height));
        var width = Math.Clamp(target.Width, 0.10, 0.80);
        var centerY = Math.Clamp(top + height * 0.55, height / 2, 1 - height / 2);
        return new NormalizedBox(
            Math.Clamp(target.CenterX, width / 2, 1 - width / 2),
            centerY,
            width,
            height);
    }

    private async Task<StablePageResult> ReplaceTextInPreviewInlineAsync(
        StablePageResult focusedPreview,
        string fieldName,
        string value,
        List<string> workflowLog,
        bool commitWithEnter,
        CancellationToken cancellationToken)
    {
        if (focusedPreview.Recognition.Page != GamePage.PreviewEditor)
            focusedPreview = await WaitForPageEvidenceAsync(focusedPreview, Pages(GamePage.PreviewEditor), "Preview.BeforeText", ShortWait, cancellationToken);

        _recordAction?.Invoke("shortcut:Ctrl+A");
        _recordAction?.Invoke($"type_text:{fieldName}");
        await _input.ReplaceTextAsync(focusedPreview.Capture.Window, value, cancellationToken);
        if (commitWithEnter)
        {
            _recordAction?.Invoke("key:Enter");
            await _input.PressKeyAsync(focusedPreview.Capture.Window, GameVirtualKey.Enter, cancellationToken);
        }

        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
        var refreshed = await WaitForPageEvidenceAsync(await RecognizeOnceAsync(cancellationToken),
            Pages(GamePage.PreviewEditor), $"EditPreviewInfo.{fieldName}.AfterText", ShortWait, cancellationToken);
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field={fieldName} result=TextInputSent mode=preview_inline_no_dialog " +
            $"commitWithEnter={commitWithEnter} refreshedPage={refreshed.Recognition.Page}");
        return refreshed;
    }

    private async Task<bool> TryDetectOptionalPreviewFieldUnavailableAsync(
        UploadRequest request,
        StablePageResult current,
        string fieldName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var texts = await _workflowOcr.RecognizeBoxAsync(
            current.Capture,
            new NormalizedBox(0.50, 0.50, 0.82, 0.62),
            $"optional_preview_field/{fieldName}/unavailable_state",
            cancellationToken);
        var unavailable = OcrTextsContainAny(
            texts,
            [
                "NO EDITABLE VERSION RECORD",
                "暂无游戏版本可编辑版本更新记录",
                "暂无游戏版本可编辑版更记录",
                "NO EDITABLE VERSION LOG",
                "NO VERSION RECORD",
                "NO VERSION LOG",
                "NO GAME VERSION UPDATE LOG AVAILABLE FOR EDITING",
                "GAME VERSION UPDATE LOG AVAILABLE FOR EDITING",
                "\u65e0\u53ef\u7f16\u8f91\u7248\u672c\u8bb0\u5f55",
                "\u6ca1\u6709\u53ef\u7f16\u8f91\u7248\u672c\u8bb0\u5f55",
                "\u6682\u65e0\u53ef\u7f16\u8f91\u7248\u672c\u8bb0\u5f55",
                "\u65e0\u53ef\u7f16\u8f91\u66f4\u65b0\u8bb0\u5f55",
                "\u6ca1\u6709\u53ef\u7f16\u8f91\u66f4\u65b0\u8bb0\u5f55"
            ]);
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field={fieldName} action=DetectOptionalUnavailable " +
            $"unavailable={unavailable} ocr=\"{FormatOcrTexts(texts)}\"");
        if (!unavailable)
            return false;

        SavePreviewInfoDiagnostic(
            current,
            $"stage1-preview-{fieldName.ToLowerInvariant()}-optional-unavailable",
            request,
            fieldName,
            "OCR empty/unavailable state after opening optional VersionLog",
            texts,
            workflowLog,
            $"{fieldName} is optional and the game reported no editable version record.",
            isFinalFailure: false,
            recoverable: true,
            phase: fieldName,
            updateLatest: false);
        LogWorkflow(workflowLog, $"step=EditPreviewInfo field={fieldName} action=OptionalUnavailablePressEsc");
        _activation.RestoreAndActivate(current.Capture.Window);
        await _input.PressKeyAsync(current.Capture.Window, GameVirtualKey.Escape, cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);
        return true;
    }

    private async Task ClickTextInputAreaForFocusAsync(
        StablePageResult current,
        AutomationTarget target,
        string fieldName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        // Inline preview fields accept focus in their upper body. The bottom-right
        // pencil is not reliable evidence that the text input received focus.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=FocusTextInputArea attempt={attempt} " +
                $"target=({FormatTarget(target)})");
            _recordAction?.Invoke($"click:{target.Name}:focus:{attempt}");
            await _input.ClickAsync(
                current.Capture.Window,
                Math.Clamp(target.CenterX, 0, 1),
                Math.Clamp(target.CenterY - target.Height * 0.15, 0, 1),
                cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(180), cancellationToken);
        }
    }

    private static AutomationTarget? FindPreviewLevelNameEditTarget(PageRecognitionResult recognition)
    {
        var preferred = FindTargets(recognition, WorkflowTargetNames.PreviewLevelNameEditButton).FirstOrDefault();
        if (preferred != null)
            return preferred;

        var textEditIcons = FindTargets(recognition, WorkflowTargetNames.TextEditIcon, 0.25).ToList();
        if (textEditIcons.Count == 0)
            return null;

        var coverTop = FindTargets(recognition, WorkflowTargetNames.ImageUploadArea, 0.25)
            .Select(target => target.CenterY - target.Height / 2)
            .DefaultIfEmpty(0.27)
            .Min();

        var titleIcons = textEditIcons
            .Where(target => target.CenterX is >= 0.12 and <= 0.58)
            .Where(target => target.CenterY < Math.Min(0.28, coverTop - 0.015))
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .ThenByDescending(target => target.Confidence)
            .ToList();
        return titleIcons.Count == 1 ? titleIcons[0] : null;
    }

    private async Task<IReadOnlyList<WorkflowOcrTextResult>> ReadPreviewLevelNameByOcrAsync(
        UploadRequest request,
        StablePageResult preview,
        AutomationTarget editTarget,
        string phase,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (preview.Recognition.Page != GamePage.PreviewEditor)
            preview = await WaitForPageEvidenceAsync(preview, Pages(GamePage.PreviewEditor), "Preview.PageRecovery", ShortWait, cancellationToken);

        var titleBox = CreatePreviewLevelNameOcrBox(editTarget);
        var texts = await _workflowOcr.RecognizeBoxAsync(
            preview.Capture,
            titleBox,
            "preview_level_name/title",
            cancellationToken);
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=LevelName action=ReadTitleOcr phase={phase} " +
            $"signature=\"{CreateOcrChangeSignature(texts)}\" ocr=\"{FormatOcrTexts(texts)}\"");
        return texts;
    }

    private static NormalizedBox CreatePreviewLevelNameOcrBox(AutomationTarget editTarget)
    {
        var iconLeft = editTarget.CenterX - editTarget.Width / 2;
        var left = 0.08;
        var right = Math.Clamp(iconLeft + 0.02, 0.22, 0.62);
        if (right <= left + 0.05)
            right = Math.Min(0.62, left + 0.20);

        var height = Math.Clamp(Math.Max(0.07, editTarget.Height * 3.2), 0.07, 0.12);
        return new NormalizedBox(
            (left + right) / 2,
            Math.Clamp(editTarget.CenterY, height / 2, 1 - height / 2),
            right - left,
            height);
    }

    private async Task<StablePageResult> FillPreviewTagsIfNeededAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Selecting save tags");
        var tags = request.Archive.Tags
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (tags.Count == 0)
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo field=Tags result=Skipped reason=empty");
            return preview;
        }

        var resolvedTags = ResolveTagCatalogEntries(tags, workflowLog);

        var selection = await LocatePreviewTagsTargetWithScrollAsync(
            request,
            preview,
            workflowLog,
            cancellationToken);
        preview = selection.Page;
        var target = selection.Target;
        IReadOnlyList<WorkflowOcrTextResult> ocrCandidates = selection.OcrTexts;

        if (target == null)
        {
            SavePreviewInfoDiagnostic(
                preview,
                "stage1-preview-tags-target-missing",
                request,
                "Tags",
                "Detect preview_tags_area or a unique edit_row OCR-confirmed as Tags",
                ocrCandidates,
                workflowLog,
                "Tags row was not detected or OCR-confirmed.");
            throw new InvalidOperationException("Tags row was not detected or OCR-confirmed.");
        }

        LogWorkflow(workflowLog, 
            $"step=Tags action=OpenDialog selectedSource={selection.SearchDirection} target=({FormatTarget(target)})");
        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo field=Tags action=OpenTagDialog " +
            $"selectedSource={selection.SearchDirection} target=({FormatTarget(target)})");
        await ClickSpecificTargetAsync(preview, target, cancellationToken);
        var tagDialog = await WaitForExpectedAsync(
            "EditPreviewInfo.Tags.WaitTagDialog",
            Pages(GamePage.TagDialog),
            MediumWait,
            stableCount: 1,
            cancellationToken);
        tagDialog = await ResetTagDialogScrollTopAsync(tagDialog, workflowLog, cancellationToken);
        var visibleTags = new Dictionary<string, AutomationTarget>(StringComparer.Ordinal);

        foreach (var tag in resolvedTags)
        {
            var choice = await FindTagChoiceWithScrollAsync(
                tagDialog,
                tag,
                workflowLog,
                cancellationToken,
                visibleTags);
            tagDialog = choice.Page;
            if (choice.Target == null)
            {
                SavePreviewInfoDiagnostic(
                    tagDialog,
                    "stage1-preview-tag-option-missing",
                    request,
                    "Tags",
                    $"OCR choice_option matching tag toolValue='{tag.ToolValue}' canonical='{tag.CanonicalGameText}' aliases=[{string.Join(", ", tag.OcrAliases)}] order={tag.DialogOrder}",
                    choice.OcrTexts,
                    workflowLog,
                    $"Required tag was not detected in TagDialog. ToolValue='{tag.ToolValue}', CanonicalGameText='{tag.CanonicalGameText}', Aliases=[{string.Join(", ", tag.OcrAliases)}], DialogOrder={tag.DialogOrder}, scannedPages={choice.ScrollAttempts + 1}, recentOcr=\"{FormatOcrTexts(choice.OcrTexts.TakeLast(12).ToList())}\".");
                throw new InvalidOperationException(
                    $"Required tag was not detected in TagDialog. ToolValue='{tag.ToolValue}', CanonicalGameText='{tag.CanonicalGameText}', Aliases=[{string.Join(", ", tag.OcrAliases)}], DialogOrder={tag.DialogOrder}, scannedPages={choice.ScrollAttempts + 1}.");
            }

            var alreadySelected = IsChoiceOptionVisuallySelected(tagDialog.Capture, choice.Target, out var selectedEvidence);
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Tags action=EnsureTagSelected toolValue=\"{tag.ToolValue}\" canonical=\"{tag.CanonicalGameText}\" aliases=\"{string.Join("|", tag.OcrAliases)}\" " +
                $"alreadySelected={alreadySelected} evidence=\"{selectedEvidence}\" target=({FormatTarget(choice.Target)})");
            if (!alreadySelected)
            {
                LogWorkflow(workflowLog, 
                    $"step=Tags action=ClickChoice toolValue=\"{tag.ToolValue}\" canonical=\"{tag.CanonicalGameText}\" " +
                    $"x={choice.Target.CenterX:F3} y={choice.Target.CenterY:F3}");
                await ClickSpecificTargetAsync(tagDialog, choice.Target, cancellationToken);
            }
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
            tagDialog = await WaitForPageEvidenceAsync(await RecognizeOnceAsync(cancellationToken),
                Pages(GamePage.TagDialog), "Tags.AfterChoice", ShortWait, cancellationToken);
        }

        return await ConfirmTagDialogAndWaitPreviewEditorAsync(
            request,
            tagDialog,
            workflowLog,
            cancellationToken);
    }

    private async Task<StablePageResult> ResetTagDialogScrollTopAsync(
        StablePageResult tagDialog,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var current = tagDialog;
        const int resetAttempts = 6;
        const int resetWheelDelta = -TagDialogScrollWheelDelta;
        for (var attempt = 1; attempt <= resetAttempts; attempt++)
        {
            if (current.Recognition.Page != GamePage.TagDialog)
                current = await WaitForPageEvidenceAsync(current, Pages(GamePage.TagDialog), "Tags.PageRecovery", ShortWait, cancellationToken);

            // Confirm both first-row labels before skipping the remaining upward scrolls.
            var topChoices = FindTargets(current.Recognition, WorkflowTargetNames.ChoiceOption)
                .OrderBy(t => t.CenterY).ThenBy(t => t.CenterX).Take(2).ToList();
            if (topChoices.Count == 2)
            {
                var topTexts = new List<WorkflowOcrTextResult>();
                foreach (var top in topChoices)
                    topTexts.AddRange(await _workflowOcr.RecognizeTargetAsync(current.Capture, top, cancellationToken));
                if (TagCatalog.Take(2).All(entry => TryMatchTagChoiceOcr(topTexts,
                    entry.OcrAliases.Select(NormalizeWorkflowOcrText).ToList(), out _)))
                {
                    LogWorkflow(workflowLog, $"step=Tags action=ResetScrollTop result=TopLabelsVerified attempt={attempt}");
                    return current;
                }
            }
            LogWorkflow(workflowLog, 
                $"step=Tags action=ResetScrollTop attempt={attempt} amount={resetWheelDelta}");
            _recordAction?.Invoke($"scroll:tag_dialog_reset_top:{resetWheelDelta}");
            await _input.ScrollAsync(
                current.Capture.Window,
                TagDialogScrollX,
                TagDialogScrollY,
                resetWheelDelta,
                cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(220), cancellationToken);
            current = await RecognizeOnceAsync(cancellationToken);
        }

        return await WaitForExpectedAsync(
            "EditPreviewInfo.Tags.ResetScrollTop",
            Pages(GamePage.TagDialog),
            ShortWait,
            stableCount: 1,
            cancellationToken);
    }

    private async Task<PreviewFieldTargetSelection> LocatePreviewTagsTargetWithScrollAsyncCore(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        const string fieldName = "Tags";
        var current = preview;
        var allOcrTexts = new List<WorkflowOcrTextResult>();

        for (var scrollAttempt = 0; scrollAttempt <= PreviewEditorMaxScrollAttempts; scrollAttempt++)
        {
            if (current.Recognition.Page != GamePage.PreviewEditor)
                current = await WaitForPageEvidenceAsync(current, Pages(GamePage.PreviewEditor), "Preview.FieldPageRecovery", ShortWait, cancellationToken);

            var preferred = FindFirstAvailableTarget(current.Recognition, [WorkflowTargetNames.PreviewTagsArea]);
            if (preferred != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field=Tags scanPage={scrollAttempt} selectedSource=preview_tags_area " +
                    $"result=preferredTargetFound target=({FormatTarget(preferred)})");
                return new PreviewFieldTargetSelection(current, preferred, allOcrTexts, scrollAttempt, "preview_tags_area");
            }

            var editRowMatch = await FindTagsEditRowByOcrAsync(current, workflowLog, cancellationToken);
            allOcrTexts.AddRange(editRowMatch.OcrTexts);
            if (editRowMatch.IsAmbiguous)
            {
                SavePreviewInfoDiagnostic(
                    current,
                    "stage1-preview-tags-edit-row-ambiguous",
                    request,
                    fieldName,
                    "Tags edit_row OCR must be a unique match",
                    allOcrTexts,
                    workflowLog,
                    $"Tags edit_row OCR matched multiple candidates. matches=[{string.Join("; ", editRowMatch.Matches.Select(FormatTarget))}]");
                throw new InvalidOperationException(
                    $"Tags edit_row OCR matched multiple candidates. matches=[{string.Join("; ", editRowMatch.Matches.Select(FormatTarget))}]");
            }

            if (editRowMatch.Target != null)
            {
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field=Tags scanPage={scrollAttempt} selectedSource=edit_row_ocr " +
                    $"result=genericTargetOcrConfirmed target=({FormatTarget(editRowMatch.Target)})");
                return new PreviewFieldTargetSelection(current, editRowMatch.Target, allOcrTexts, scrollAttempt, "edit_row_ocr");
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Tags scanPage={scrollAttempt} " +
                "result=notFound preferredTargets=[preview_tags_area] genericTarget=edit_row selectedSource=<none>");

            if (scrollAttempt == PreviewEditorMaxScrollAttempts)
                break;

            _recordAction?.Invoke($"scroll:preview_editor:{PreviewEditorScrollWheelDelta}");
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Tags action=ScrollPreviewEditor " +
                $"amount={PreviewEditorScrollWheelDelta} scrollAttempt={scrollAttempt + 1}");
            await _input.ScrollAsync(
                current.Capture.Window,
                ScrollCenterX,
                ScrollCenterY,
                PreviewEditorScrollWheelDelta,
                cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
            current = await WaitForExpectedAsync(
                $"EditPreviewInfo.Tags.ScrollPreviewEditor{scrollAttempt + 1}",
                Pages(GamePage.PreviewEditor),
                ShortWait,
                stableCount: 1,
                cancellationToken);
        }

        return new PreviewFieldTargetSelection(current, null, allOcrTexts, PreviewEditorMaxScrollAttempts, "not_found");
    }

    private async Task<TagsEditRowOcrMatch> FindTagsEditRowByOcrAsync(
        StablePageResult current,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        return await FindPreviewEditRowByOcrAsync(
            current,
            "Tags",
            [
                "STAGE TAGS",
                "TAGS",
                "CLICK TO ADD TAGS TO YOUR STAGE",
                "\u5173\u5361\u6807\u7b7e",
                "\u70b9\u51fb\u6dfb\u52a0\u5173\u5361\u6807\u7b7e"
            ],
            workflowLog,
            cancellationToken);
    }

    private async Task<TagsEditRowOcrMatch> FindPreviewEditRowByOcrAsync(
        StablePageResult current,
        string fieldName,
        IReadOnlyList<string> acceptedTokens,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var matches = new List<AutomationTarget>();
        var candidates = FindTargets(current.Recognition, WorkflowTargetNames.EditRow, 0.25)
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .ThenByDescending(target => target.Confidence)
            .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var target = candidates[index];
            var ocrTexts = await _workflowOcr.RecognizeTargetAsync(current.Capture, target, cancellationToken);
            allOcrTexts.AddRange(ocrTexts);
            var matched = PreviewCandidateOcrMatches(ocrTexts, acceptedTokens);
            var reason = matched ? "accepted_field_token" : "no_field_token";
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field={fieldName} action=EditRowOcr candidateIndex={index} " +
                $"target=({FormatTarget(target)}) raw=\"{FormatRawOcrTexts(ocrTexts)}\" " +
                $"normalized=\"{FormatNormalizedOcrTexts(ocrTexts)}\" reason={reason}");
            if (matched)
                matches.Add(target);
        }

        if (matches.Count == 1)
            return new TagsEditRowOcrMatch(matches[0], matches, allOcrTexts, false);

        return new TagsEditRowOcrMatch(null, matches, allOcrTexts, matches.Count > 1);
    }

    private async Task<StablePageResult> ConfirmTagDialogAndWaitPreviewEditorAsync(
        UploadRequest request,
        StablePageResult tagDialog,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var current = tagDialog;
        OcrTargetMatch? lastConfirm = null;

        for (var attempt = 1; attempt <= TagDialogConfirmMaxClickAttempts; attempt++)
        {
            if (current.Recognition.Page != GamePage.TagDialog)
            {
                if (current.Recognition.Page == GamePage.PreviewEditor)
                    return current;

                current = await WaitForPageEvidenceAsync(current, Pages(GamePage.TagDialog, GamePage.PreviewEditor), "Tags.BeforeConfirm", ShortWait, cancellationToken);
                if (current.Recognition.Page == GamePage.PreviewEditor) return current;
            }

            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(650), cancellationToken);
            lastConfirm = await FindPrimaryButtonByOcrAsync(
                current,
                ["OK", "CONFIRM", "\u786e\u5b9a", "\u786e\u8ba4"],
                cancellationToken);
            if (lastConfirm.Target == null)
                break;

            LogWorkflow(workflowLog, $"step=Tags action=ConfirmOK attempt={attempt} target=({FormatTarget(lastConfirm.Target)})");
            LogWorkflow(workflowLog, $"step=EditPreviewInfo field=Tags action=ClickConfirm attempt={attempt} target=({FormatTarget(lastConfirm.Target)})");
            await ClickSpecificTargetAsync(current, lastConfirm.Target, cancellationToken);

            for (var poll = 1; poll <= 4; poll++)
            {
                await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
                current = await RecognizeOnceAsync(cancellationToken);
                LogWorkflow(workflowLog, 
                    $"step=Tags action=VerifyConfirm attempt={attempt} poll={poll} page={current.Recognition.Page} confidence={current.Recognition.Confidence:F2}");
                LogWorkflow(workflowLog, 
                    $"step=EditPreviewInfo field=Tags action=VerifyConfirm attempt={attempt} poll={poll} " +
                    $"page={current.Recognition.Page} confidence={current.Recognition.Confidence:F2}");

                if (current.Recognition.Page == GamePage.PreviewEditor)
                    return current;

                if (current.Recognition.Page != GamePage.TagDialog)
                    break;
            }
        }

        SavePreviewInfoDiagnostic(
            current,
            lastConfirm?.Target == null
                ? "stage1-preview-tags-confirm-missing"
                : "stage1-preview-tags-confirm-no-effect",
            request,
            "Tags",
            "OCR primary_action_button as OK/Confirm and verify return to PreviewEditor",
            lastConfirm?.OcrTexts ?? [],
            workflowLog,
            lastConfirm?.Target == null
                ? "TagDialog confirm button was not OCR-confirmed."
                : $"TagDialog confirm did not return to PreviewEditor after {TagDialogConfirmMaxClickAttempts} attempts.");

        throw new InvalidOperationException(
            lastConfirm?.Target == null
                ? "TagDialog confirm button was not OCR-confirmed."
                : $"TagDialog confirm did not return to PreviewEditor after {TagDialogConfirmMaxClickAttempts} attempts.");
    }

    private async Task<StablePageResult> SubmitPreviewInfoCoreAsync(
        UploadRequest request,
        StablePageResult preview,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Submitting preview details");
        var current = preview;
        var allOcrCandidates = new List<WorkflowOcrTextResult>();
        const int maxSubmitAttempts = 3;
        for (var attempt = 1; attempt <= maxSubmitAttempts; attempt++)
        {
            ReportOperation($"Submitting preview details ({attempt}/{maxSubmitAttempts}); waiting for Uploaded Saves");
            var selection = await LocatePreviewFieldTargetWithScrollAsync(
                request,
                current,
                "Submit",
                [WorkflowTargetNames.PreviewSubmitButton],
                WorkflowTargetNames.PrimaryActionButton,
                ["SUBMIT", "\u63d0\u4ea4"],
                workflowLog,
                cancellationToken);
            allOcrCandidates.AddRange(selection.OcrTexts);
            var submit = selection.Target;
            current = selection.Page;

            if (submit == null)
            {
                SavePreviewInfoDiagnostic(
                    current,
                    "stage1-preview-submit-button-missing",
                    request,
                    "Submit",
                    "OCR primary_action_button as Submit",
                    allOcrCandidates,
                    workflowLog,
                    "PreviewEditor submit button was not OCR-confirmed.");
                throw new InvalidOperationException("PreviewEditor submit button was not OCR-confirmed.");
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo action=ClickSubmit attempt={attempt} " +
                $"scrollAttempts={selection.ScrollAttempts} target=({FormatTarget(submit)})");
            await ClickSpecificTargetAsync(current, submit, cancellationToken);
            var afterSubmit = await WaitForPreviewSubmissionOutcomeAsync(workflowLog, cancellationToken);

            if (afterSubmit.Recognition.Page == GamePage.UploadedArchive)
                return afterSubmit;

            if (FindTargets(afterSubmit.Recognition, WorkflowTargetNames.ModalDialog, 0.25).Count > 0)
            {
                var confirm = await FindPrimaryButtonByOcrAsync(afterSubmit, ["SUBMIT", "OK", "CONFIRM", "\u63d0\u4ea4", "\u786e\u5b9a", "\u786e\u8ba4"], cancellationToken);
                allOcrCandidates.AddRange(confirm.OcrTexts);
                if (confirm.Target == null)
                {
                    SavePreviewInfoDiagnostic(
                        afterSubmit,
                        "stage1-preview-submit-confirm-missing",
                        request,
                        "Submit",
                        "OCR modal primary_action_button as OK/Confirm/Submit",
                        allOcrCandidates,
                        workflowLog,
                        "Submit confirmation modal had no safe OCR-confirmed primary action.");
                    throw new InvalidOperationException("Submit confirmation modal had no safe OCR-confirmed primary action.");
                }

                LogWorkflow(workflowLog, $"step=EditPreviewInfo action=ClickSubmitConfirm attempt={attempt} target=({FormatTarget(confirm.Target)})");
                await ClickSpecificTargetAsync(afterSubmit, confirm.Target, cancellationToken);
                afterSubmit = await WaitForPreviewSubmissionOutcomeAsync(workflowLog, cancellationToken, allowConfirmation: false);
                if (afterSubmit.Recognition.Page == GamePage.UploadedArchive) return afterSubmit;
            }

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo action=SubmitNoEffect attempt={attempt} " +
                $"page={afterSubmit.Recognition.Page} confidence={afterSubmit.Recognition.Confidence:F2} " +
                $"reason=submission_did_not_reach_uploaded_saves");
            current = afterSubmit;
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(1200), cancellationToken);
        }

        SavePreviewInfoDiagnostic(
            current,
            "stage1-preview-submit-no-effect",
            request,
            "Submit",
            "Click Submit and observe UploadedArchive or a confirmation modal",
            allOcrCandidates,
            workflowLog,
            $"预览信息无法正常提交：已尝试 {maxSubmitAttempts} 次，均未返回已上传存档页面。请检查是否存在同名存档，或页面是否提示其他提交限制。");
        ReportOperation("Preview submission failed after 3 attempts; check duplicate save names or other restrictions");
        throw new InvalidOperationException(
            $"预览信息无法正常提交：已尝试 {maxSubmitAttempts} 次，均未返回已上传存档页面。请检查是否存在同名存档，或页面是否提示其他提交限制。");
    }

    private async Task<StablePageResult> WaitForPreviewSubmissionOutcomeAsyncCore(
        List<string> workflowLog, CancellationToken cancellationToken, bool allowConfirmation = true)
    {
        var deadline = _clock.UtcNow + TimeSpan.FromSeconds(8);
        var uploaded = new PageObservationStreak();
        var preview = new PageObservationStreak();
        var modal = new PageObservationStreak();
        StablePageResult? last = null;
        bool previewReady = false;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await RecognizeOnceAsync(cancellationToken);
            var page = last.Recognition.Page;
            if (page.IsBlocking())
                throw new InvalidOperationException($"预览信息提交失败：{page}。");
            var hasModal = FindTargets(last.Recognition, WorkflowTargetNames.ModalDialog, .25).Count > 0;
            LogWorkflow(workflowLog, $"step=PreviewSubmitOutcome page={page} modal={hasModal} allowConfirmation={allowConfirmation}");
            if (uploaded.Observe(page, page == GamePage.UploadedArchive)) return last;
            if (modal.Observe(page, allowConfirmation && page == GamePage.PreviewEditor && hasModal)) return last;
            previewReady = preview.Observe(page, page == GamePage.PreviewEditor && !hasModal);
            if (_clock.UtcNow >= deadline) break;
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
        } while (true);
        // Retry only from a fresh, stable preview page, never from Unknown or a lingering modal.
        if (previewReady) return last!;
        var diagnostic = SaveDiagnostic(last, "stage1-preview-submit-result-timeout");
        throw new TimeoutException($"预览信息提交后未能确认已上传存档页或可重试的预览页。diagnostic={diagnostic}");
    }

    private static void ValidatePreviewInfoSubmitPrerequisites(
        UploadRequest request,
        List<string> workflowLog)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Archive.GameDescription))
            missing.Add("GameplayDescription");
        if (string.IsNullOrWhiteSpace(request.Archive.LevelDescription))
            missing.Add("LevelDescription");
        if (!request.IsUpdate && string.IsNullOrWhiteSpace(request.Archive.Tags))
            missing.Add("Tags");
        if (string.IsNullOrWhiteSpace(request.Archive.LevelCoverPath))
            missing.Add("Cover");
        if (request.Archive.IsEarlyAccess && string.IsNullOrWhiteSpace(request.Archive.EarlyAccessDescription))
            missing.Add("EarlyAccessDescription");

        if (!request.Archive.IsEarlyAccess &&
            !string.IsNullOrWhiteSpace(request.Archive.EarlyAccessDescription))
        {
            LogWorkflow(workflowLog, 
                "step=EditPreviewInfo field=EarlyAccessDescription result=Ignored " +
                "reason=archive_not_early_access");
        }

        if (missing.Count == 0)
        {
            LogWorkflow(workflowLog, "step=EditPreviewInfo action=ValidateBeforeSubmit result=Passed");
            return;
        }

        LogWorkflow(workflowLog, 
            $"step=EditPreviewInfo action=ValidateBeforeSubmit result=Failed missing=[{string.Join(",", missing)}]");
        throw new InvalidOperationException(
            $"Preview info submit refused because required fields are missing: {string.Join(", ", missing)}.");
    }

    private async Task<StablePageResult> ReturnToWorldMainConservativelyCoreAsync(
        StablePageResult uploadedArchive,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Returning from the save editor to the open world");
        var current = uploadedArchive;
        if (current.Recognition.Page == GamePage.WorldMain)
        {
            LogWorkflow(workflowLog, "step=ReturnToWorldMain result=AlreadyWorldMain");
            return current;
        }

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            LogWorkflow(workflowLog, $"step=ReturnToWorldMain escAttempt={attempt} action=ActivateGameWindow");
            _activation.RestoreAndActivate(current.Capture.Window);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(300), cancellationToken);

            LogWorkflow(workflowLog, $"step=ReturnToWorldMain escAttempt={attempt} action=PressEscFromUploadedArchive expected=WorldMainOrExitPath");
            _recordAction?.Invoke("press_key:Escape");
            await _input.PressKeyAsync(current.Capture.Window, GameVirtualKey.Escape, cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);

            var waitResult = await WaitForReturnPageAfterEscAsync(
                $"ReturnToWorldMain.WaitAfterEsc{attempt}",
                Pages(
                    GamePage.WorldMain,
                    GamePage.ArchiveEditorMenu,
                    GamePage.ArchiveEditor,
                    GamePage.LocalArchive,
                    GamePage.UploadedArchive),
                ReturnEscShortWait,
                workflowLog,
                cancellationToken);
            if (waitResult.TimedOut)
            {
                if (waitResult.LastPage == GamePage.Unknown && waitResult.LastResult != null)
                {
            LogWorkflow(workflowLog, 
                $"step=ReturnToWorldMain escAttempt={attempt} result=UnknownPossibleArchiveEditor " +
                $"timeoutSeconds={ReturnEscShortWait.TotalSeconds:F0} " +
                "action=PressEscToOpenEditorMenu");
                    return await PressEscToOpenArchiveEditorMenuAndExitToWorldMainAsync(
                        waitResult.LastResult,
                        workflowLog,
                        cancellationToken);
                }

                var diagnostic = SaveDiagnostic(waitResult.LastResult, "stage1-return-to-world-timeout");
                var foreground = SafeGetForegroundDiagnostic(waitResult.LastResult?.Capture.Window ?? current.Capture.Window);
                throw new TimeoutException(
                    $"Stage1 timed out while returning to WorldMain. escAttempts={attempt}, " +
                    $"lastPage={waitResult.LastPage}, foreground=({foreground}), diagnostic={diagnostic ?? "<not-saved>"}.");
            }

            current = waitResult.Page!;

            if (current.Recognition.Page == GamePage.WorldMain)
            {
                LogWorkflow(workflowLog, $"step=ReturnToWorldMain escAttempt={attempt} result=WorldMain");
                return current;
            }

            if (current.Recognition.Page == GamePage.ArchiveEditorMenu)
                return await ExitArchiveEditorMenuToWorldMainAsync(current, workflowLog, cancellationToken);

            if (current.Recognition.Page == GamePage.ArchiveEditor ||
                current.Recognition.Page == GamePage.LocalArchive)
            {
                LogWorkflow(workflowLog, $"step=ReturnToWorldMain escAttempt={attempt} result={current.Recognition.Page} action=OpenArchiveEditorMenuByEsc");
                return await OpenArchiveEditorMenuAndExitToWorldMainAsync(current, workflowLog, cancellationToken);
            }

            if (current.Recognition.Page == GamePage.UploadedArchive)
            {
                if (current.Recognition.Confidence < ReturnUploadedArchiveStrongConfidence)
                {
                    LogWorkflow(workflowLog, 
                        $"step=ReturnToWorldMain escAttempt={attempt} result=LowConfidenceUploadedArchivePossibleArchiveEditor " +
                        $"confidence={current.Recognition.Confidence:F2} threshold={ReturnUploadedArchiveStrongConfidence:F2} " +
                        "action=PressEscToOpenEditorMenu");
                    return await PressEscToOpenArchiveEditorMenuAndExitToWorldMainAsync(
                        current,
                        workflowLog,
                        cancellationToken);
                }

                var retry = attempt < 2;
                LogWorkflow(workflowLog, 
                    $"step=ReturnToWorldMain escAttempt={attempt} result=StillUploadedArchive " +
                    $"confidence={current.Recognition.Confidence:F2} retry={retry}");
                if (retry)
                {
                    await DelayWithTimingAsync(TimeSpan.FromMilliseconds(1200), cancellationToken);
                    continue;
                }

                var diagnostic = SaveDiagnostic(current, "stage1-return-from-uploaded-archive-timeout");
                var foreground = SafeGetForegroundDiagnostic(current.Capture.Window);
                throw new TimeoutException(
                    $"Stage1 could not leave UploadedArchive after ESC retries. escAttempts=2, " +
                    $"lastPage={current.Recognition.Page}, foreground=({foreground}), diagnostic={diagnostic ?? "<not-saved>"}.");
            }

            throw new InvalidOperationException($"Unexpected page while returning to WorldMain: {current.Recognition.Page}.");
        }

        throw new InvalidOperationException("Unexpected Stage1 return loop exit.");
    }

    private async Task<StablePageResult> OpenArchiveEditorMenuAndExitToWorldMainAsync(
        StablePageResult current,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        _activation.RestoreAndActivate(current.Capture.Window);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(300), cancellationToken);
        _recordAction?.Invoke("press_key:Escape");
        await _input.PressKeyAsync(current.Capture.Window, GameVirtualKey.Escape, cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(800), cancellationToken);

        var menuResult = await WaitForReturnPageAfterEscAsync(
            "ReturnToWorldMain.WaitArchiveEditorMenu",
            Pages(GamePage.ArchiveEditorMenu),
            MediumWait,
            workflowLog,
            cancellationToken);
        if (menuResult.Page == null)
            throw new TimeoutException($"Editor menu was not confirmed after ESC. diagnostic={SaveDiagnostic(menuResult.LastResult, "stage1-return-editor-menu-timeout")}");
        return await ExitArchiveEditorMenuToWorldMainAsync(menuResult.Page, workflowLog, cancellationToken);
    }

    private async Task<StablePageResult> PressEscToOpenArchiveEditorMenuAndExitToWorldMainAsync(
        StablePageResult current,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReturnPageAfterEscResult? waitResult = null;
        for (var editorEscAttempt = 1; editorEscAttempt <= ReturnArchiveEditorEscMaxAttempts; editorEscAttempt++)
        {
            var foregroundBefore = SafeGetForegroundDiagnostic(current.Capture.Window);
            LogWorkflow(workflowLog, 
                $"step=ReturnToWorldMain action=PressEscFromPossibleArchiveEditor " +
                $"editorEscAttempt={editorEscAttempt} foregroundBefore=({foregroundBefore})");
            _activation.RestoreAndActivate(current.Capture.Window);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(300), cancellationToken);
            _recordAction?.Invoke("press_key:Escape");
            await _input.PressKeyAsync(current.Capture.Window, GameVirtualKey.Escape, cancellationToken);
            await DelayWithTimingAsync(ReturnArchiveEditorEscSettleDelay, cancellationToken);

            waitResult = await WaitForReturnPageAfterEscAsync(
                $"ReturnToWorldMain.WaitArchiveEditorMenuAfterUnknown.{editorEscAttempt}",
                Pages(GamePage.ArchiveEditorMenu, GamePage.WorldMain),
                ReturnEscShortWait,
                workflowLog,
                cancellationToken);
            if (waitResult.Page?.Recognition.Page == GamePage.WorldMain)
            {
                LogWorkflow(workflowLog, $"step=ReturnToWorldMain editorEscAttempt={editorEscAttempt} result=WorldMain");
                return waitResult.Page;
            }

            if (waitResult.Page?.Recognition.Page == GamePage.ArchiveEditorMenu)
            {
                LogWorkflow(workflowLog, $"step=ReturnToWorldMain editorEscAttempt={editorEscAttempt} result=ArchiveEditorMenu");
                return await ExitArchiveEditorMenuToWorldMainAsync(waitResult.Page, workflowLog, cancellationToken);
            }

            LogWorkflow(workflowLog, 
                $"step=ReturnToWorldMain editorEscAttempt={editorEscAttempt} result=StillUnknown " +
                $"lastPage={waitResult.LastPage} retry={editorEscAttempt < ReturnArchiveEditorEscMaxAttempts}");
        }

        var diagnostic = SaveDiagnostic(waitResult?.LastResult, "stage1-return-from-archive-editor-unknown-timeout");
        var foreground = SafeGetForegroundDiagnostic(waitResult?.LastResult?.Capture.Window ?? current.Capture.Window);
        throw new TimeoutException(
            $"Stage1 could not open ArchiveEditorMenu from an Unknown possible ArchiveEditor page. " +
            $"editorEscAttempts={ReturnArchiveEditorEscMaxAttempts}, lastPage={waitResult?.LastPage}, foreground=({foreground}), diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<StablePageResult> ExitArchiveEditorMenuToWorldMainAsync(
        StablePageResult archiveEditorMenu,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        ReportOperation("Clicking the editor exit icon; waiting for the open world");
        LogWorkflow(workflowLog, $"step=ReturnToWorldMain action=ClickExit target={WorkflowTargetNames.ArchiveEditorExitButton}");
        await ClickRequiredTargetAsync(archiveEditorMenu, WorkflowTargetNames.ArchiveEditorExitButton, cancellationToken);

        var world = await WaitForExpectedAsync(
            "ReturnToWorldMain.WaitWorldMain",
            Pages(GamePage.WorldMain),
            LongWait,
            stableCount: 1,
            cancellationToken);
        LogWorkflow(workflowLog, "step=ReturnToWorldMain result=WorldMain");
        return world;
    }

    private async Task<StablePageResult> WaitForExpectedAsyncCore(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        TimeSpan timeout,
        int stableCount,
        CancellationToken cancellationToken) =>
        await _pageWaiter.WaitForExpectedAsync(
            stepName,
            expectedPages,
            timeout,
            stableCount,
            cancellationToken);

    private async Task<StablePageResult> WaitForAnyPageAsyncCore(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool recoverableDiagnostic = false,
        string? diagnosticPhase = null)
    {
        var deadline = _clock.UtcNow + timeout;
        StablePageResult? last = null;
        var ready = new PageObservationStreak();
        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await RecognizeOnceAsync(cancellationToken);
            last = current;
            if (ready.Observe(current.Recognition.Page, expectedPages.Contains(current.Recognition.Page)))
                return current;

            if (current.Recognition.Page.IsBlocking())
                throw new InvalidOperationException($"Blocking page appeared during {stepName}: {current.Recognition.Page}.");

            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        var diagnostic = SaveDiagnostic(
            last,
            $"stage1-{NormalizeFileNameSegment(stepName)}-timeout",
            isFinalFailure: !recoverableDiagnostic,
            recoverable: recoverableDiagnostic,
            phase: diagnosticPhase,
            updateLatest: !recoverableDiagnostic);
        throw new TimeoutException(
            $"Timed out during {stepName}. expected=[{FormatPages(expectedPages)}], " +
            $"lastPage={last?.Recognition.Page}, confidence={last?.Recognition.Confidence:F2}, diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private async Task<StablePageResult> ScrollLocalArchiveAndRecognizeAsync(
        StablePageResult current,
        string stepName,
        CancellationToken cancellationToken)
    {
        _recordAction?.Invoke($"scroll:local_archive:{LocalArchiveScrollWheelDelta}");
        await _input.ScrollAsync(
            current.Capture.Window,
            ScrollCenterX,
            ScrollCenterY,
            LocalArchiveScrollWheelDelta,
            cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
        return await WaitForExpectedAsync(
            stepName,
            Pages(GamePage.LocalArchive),
            ShortWait,
            stableCount: 1,
            cancellationToken);
    }

    private async Task<StablePageResult> ScrollUpdateArchiveSelectAndRecognizeAsync(
        StablePageResult current,
        string stepName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        _recordAction?.Invoke($"scroll:update_archive_select:{LocalArchiveScrollWheelDelta}");
        await _input.ScrollAsync(
            current.Capture.Window,
            ScrollCenterX,
            ScrollCenterY,
            LocalArchiveScrollWheelDelta,
            cancellationToken);
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);

        var refreshed = await WaitForUpdateArchiveSelectByContextAsync(
            stepName,
            ShortWait,
            workflowLog,
            cancellationToken);
        LogWorkflow(workflowLog, 
            $"step={stepName} result=afterScroll page={refreshed.Recognition.Page} " +
            $"confidence={refreshed.Recognition.Confidence:F2} " +
            $"uploadedArchiveCardCount={CountTargets(refreshed.Recognition, WorkflowTargetNames.UploadedArchiveCard, 0.25)}");
        return refreshed;
    }

    private async Task<ReturnPageAfterEscResult> WaitForReturnPageAfterEscAsync(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        TimeSpan timeout,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        var fallbackDeadline = _clock.UtcNow + TimeSpan.FromSeconds(60);
        var deadline = _clock.UtcNow + (expectedPages.Contains(GamePage.ArchiveEditorMenu) && timeout < TimeSpan.FromSeconds(8)
            ? TimeSpan.FromSeconds(8) : timeout);
        AutomationTarget? previousFallbackExit = null;
        var fallbackMenuObserved = false;
        var exitEvidence = new ArchiveEditorExitEvidence();
        StablePageResult? lastResult = null;
        Stage1ContextPageDecision? lastDecision = null;
        var ready = new PageObservationStreak();

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await RecognizeOnceAsync(cancellationToken);
            if (expectedPages.Contains(GamePage.ArchiveEditorMenu) &&
                current.Recognition.Page is GamePage.Unknown or GamePage.ArchiveEditor &&
                !current.Recognition.Targets.Any(t => t.Name == WorkflowTargetNames.ArchiveEditorExitButton && t.Confidence >= .45))
            {
                var exit = ArchiveEditorMenuFallback.LocateExit(current.Capture.Bitmap, cancellationToken);
                if (exit != null)
                {
                    var texts = await _workflowOcr.RecognizeBoxAsync(current.Capture,
                        ArchiveEditorMenuFallback.MenuTextBox, "ReturnToWorldMain/EditorMenu", cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var confirmed = ArchiveEditorMenuFallback.ConfirmsMenu(texts);
                    fallbackMenuObserved |= confirmed;
                    LogWorkflow(workflowLog, $"step={stepName} fallback=editor_menu_template_ocr iconScore={exit.Confidence:F3} menuConfirmed={confirmed} ocr={FormatOcrTexts(texts)}");
                    if (confirmed && _clock.UtcNow < fallbackDeadline)
                    {
                        // Allow slow OCR a bounded second observation without toggling an already open menu.
                        deadline = fallbackDeadline;
                        if (previousFallbackExit != null &&
                            Math.Abs(exit.CenterX - previousFallbackExit.CenterX) < .01 &&
                            Math.Abs(exit.CenterY - previousFallbackExit.CenterY) < .01)
                        {
                            current = new StablePageResult(current.Capture, current.Recognition with
                            {
                                Page = GamePage.ArchiveEditorMenu,
                                Source = current.Recognition.Source + "+EditorMenuTemplateOcr",
                                Targets = current.Recognition.Targets.Append(exit).ToArray()
                            });
                            LogWorkflow(workflowLog, $"step={stepName} acceptedPage=ArchiveEditorMenu reason=template_and_menu_text_confirmed_twice target=({FormatTarget(exit)})");
                            return new ReturnPageAfterEscResult(current, false, GamePage.ArchiveEditorMenu, current, "Template and menu OCR confirmed twice");
                        }
                        previousFallbackExit = exit;
                        lastResult = current;
                        ready.Observe(GamePage.Unknown, false);
                        await DelayWithTimingAsync(PollInterval, cancellationToken);
                        continue;
                    }
                }
            }
            previousFallbackExit = null;
            if (expectedPages.Contains(GamePage.ArchiveEditorMenu) && exitEvidence.Observe(current.Recognition, _clock.UtcNow))
            {
                current = WithContextualPage(current, GamePage.ArchiveEditorMenu);
                LogWorkflow(workflowLog, $"step={stepName} acceptedPage=ArchiveEditorMenu reason=exit_icon_confirmed_across_short_missed_frames");
                return new ReturnPageAfterEscResult(current, false, GamePage.ArchiveEditorMenu, current, "Exit icon confirmed twice within four seconds");
            }
            lastResult = current;
            lastDecision = _pageGuard.Decide(current.Recognition, expectedPages, stepName);

            if (lastDecision.IsBlocking)
                throw new InvalidOperationException(
                    $"Stage1 return path detected blocking page {current.Recognition.Page}. " +
                    $"step={stepName}, reason={lastDecision.Reason}");

            if (ready.Observe(current.Recognition.Page, lastDecision.IsAccepted && lastDecision.IsExpected))
            {
                LogWorkflow(workflowLog, $"step={stepName} acceptedPage={current.Recognition.Page} confidence={current.Recognition.Confidence:F2}");
                return new ReturnPageAfterEscResult(current, false, current.Recognition.Page, current, lastDecision.Reason);
            }

            LogWorkflow(workflowLog, 
                $"step={stepName} waiting page={current.Recognition.Page} " +
                $"confidence={current.Recognition.Confidence:F2} reason={lastDecision.Reason}");
            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        if (fallbackMenuObserved)
        {
            var diagnostic = SaveDiagnostic(lastResult, "stage1-return-editor-menu-fallback-unconfirmed");
            throw new TimeoutException($"Editor menu was observed but could not be confirmed on two consecutive frames. " +
                $"Stopped without toggling ESC again. diagnostic={diagnostic}");
        }

        return new ReturnPageAfterEscResult(
            null,
            true,
            lastResult?.Recognition.Page ?? GamePage.Unknown,
            lastResult,
            lastDecision?.Reason);
    }

    private async Task<StablePageResult> WaitForUploadBranchAsyncCore(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        TimeSpan timeout,
        List<string> workflowLog,
        CancellationToken cancellationToken,
        bool allowStableLocalArchive = false,
        string? attemptName = null,
        bool requireSandboxEvidenceForLocalArchive = false)
    {
        ReportOperation("Waiting for the save upload result");
        var startedAt = _clock.UtcNow;
        var deadline = startedAt + timeout;
        var sandboxBudgetExtended = false;
        var nextProgressAt = startedAt;
        StablePageResult? lastResult = null;
        Stage1ContextPageDecision? lastDecision = null;
        var ready = new PageObservationStreak();
        DateTimeOffset? localArchiveFirstSeenAt = null;
        var localArchiveConsecutiveMatches = 0;
        var sandboxWasSeen = _sandboxWatcher.IsSandboxRunning();

        while (_clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await RecognizeOnceAsync(cancellationToken);
            lastResult = current;
            var now = _clock.UtcNow;
            var sandboxRunning = _sandboxWatcher.IsSandboxRunning();
            sandboxWasSeen = sandboxWasSeen || sandboxRunning;
            // First-upload branches may launch the sandbox while the game displays
            // an unclassified loading screen. Extend once, measured from entry,
            // without accepting the process itself as a successful upload result.
            if (allowStableLocalArchive && sandboxWasSeen && !sandboxBudgetExtended &&
                timeout < FirstUploadSandboxTransitionWait)
            {
                deadline = startedAt + FirstUploadSandboxTransitionWait;
                sandboxBudgetExtended = true;
                LogWorkflow(workflowLog, $"step={stepName} action=ExtendSandboxTransitionBudget " +
                    $"originalSeconds={timeout.TotalSeconds:0} totalSeconds={FirstUploadSandboxTransitionWait.TotalSeconds:0} " +
                    $"elapsedSeconds={(now - startedAt).TotalSeconds:F1} page={current.Recognition.Page} sandboxRunning={sandboxRunning}");
            }
            if (now >= nextProgressAt)
            {
                LogWorkflow(workflowLog, $"step={stepName} action=WaitUploadBranchProgress page={current.Recognition.Page} " +
                    $"elapsedSeconds={(now - startedAt).TotalSeconds:F1} remainingSeconds={Math.Max(0, (deadline - now).TotalSeconds):F1} " +
                    $"sandboxSeen={sandboxWasSeen} sandboxRunning={sandboxRunning} targets={current.Recognition.Targets.Count}");
                nextProgressAt = now + TimeSpan.FromSeconds(5);
            }
            if (now >= deadline) break;
            lastDecision = _pageGuard.Decide(current.Recognition, expectedPages, stepName);

            if (ShouldSettleUploadResultCandidate(current, expectedPages))
            {
                LogWorkflow(workflowLog, 
                    $"step={stepName} action=SettleAfterResultCandidate " +
                    $"candidatePage={current.Recognition.Page} confidence={current.Recognition.Confidence:F2} " +
                    $"uploadedArchiveCardCount={CountTargets(current.Recognition, WorkflowTargetNames.UploadedArchiveCard, 0.25)} " +
                    $"delayMs={UploadResultSettleDelay.TotalMilliseconds:F0}");
                await DelayWithTimingAsync(UploadResultSettleDelay, cancellationToken);

                current = await RecognizeOnceAsync(cancellationToken);
                lastResult = current;
                now = _clock.UtcNow;
                sandboxRunning = _sandboxWatcher.IsSandboxRunning();
                sandboxWasSeen = sandboxWasSeen || sandboxRunning;
                lastDecision = _pageGuard.Decide(current.Recognition, expectedPages, stepName);
                LogWorkflow(workflowLog, 
                    $"step={stepName} action=AfterResultCandidateSettle " +
                    $"page={current.Recognition.Page} confidence={current.Recognition.Confidence:F2} " +
                    $"uploadedArchiveCardCount={CountTargets(current.Recognition, WorkflowTargetNames.UploadedArchiveCard, 0.25)}");
                if (expectedPages.Contains(GamePage.UploadedArchive) &&
                    current.Recognition.Page == GamePage.WorldMain)
                {
                    LogWorkflow(workflowLog, 
                        $"step={stepName} acceptedPage=WorldMain reason=already_returned_to_world_after_upload_result_settle");
                    return current;
                }
            }

            if (expectedPages.Contains(GamePage.UploadedArchive) &&
                TryPromoteUploadedArchiveByCardContext(current, out var promotedUploadedArchive, out var uploadedArchivePromotionReason))
            {
                LogWorkflow(workflowLog, 
                    $"step={stepName} acceptedPage=UploadedArchive promotedFrom={current.Recognition.Page} " +
                    $"reason={uploadedArchivePromotionReason} cardCount={CountTargets(current.Recognition, WorkflowTargetNames.UploadedArchiveCard, 0.25)}");
                return promotedUploadedArchive;
            }

            if (lastDecision.IsAccepted && (lastDecision.IsExpected || lastDecision.IsBlocking))
            {
                LogWorkflow(workflowLog, $"step={stepName} acceptedPage={current.Recognition.Page} confidence={current.Recognition.Confidence:F2}");
                return current;
            }

            if (current.Recognition.Page == GamePage.LocalArchive)
            {
                if (allowStableLocalArchive)
                {
                    if (localArchiveFirstSeenAt.HasValue)
                    {
                        localArchiveConsecutiveMatches++;
                    }
                    else
                    {
                        localArchiveFirstSeenAt = now;
                        localArchiveConsecutiveMatches = 1;
                    }

                    var observedDuration = now - localArchiveFirstSeenAt.Value;
                    if (localArchiveConsecutiveMatches >= 3 &&
                        observedDuration >= TimeSpan.FromSeconds(3))
                    {
                        if (requireSandboxEvidenceForLocalArchive && !sandboxWasSeen)
                        {
                            LogWorkflow(workflowLog, $"step={stepName} result=RejectedLocalArchive reason=no_upload_entry_or_sandbox_evidence");
                            var rejectedDiagnostic = SaveDiagnostic(current, "stage1-upload-entry-unconfirmed");
                            throw new InvalidOperationException(
                                $"点击上传菜单后仍停留在本地存档页，未观察到上传入口或沙盒进程，已停止。diagnostic={rejectedDiagnostic}");
                        }
                        LogWorkflow(workflowLog, 
                            $"step={stepName} attempt={attemptName ?? "<unknown>"} LocalArchive accepted as sandbox-return candidate " +
                            $"consecutive={localArchiveConsecutiveMatches} observedSeconds={observedDuration.TotalSeconds:F1} " +
                            $"sandboxSeen={sandboxWasSeen} sandboxRunning={sandboxRunning}");
                        return current;
                    }
                }
                else
                {
                    LogWorkflow(workflowLog, 
                        $"step={stepName} attempt={attemptName ?? "<unknown>"} ignored unexpected LocalArchive during final upload wait");
                }
            }
            else
            {
                localArchiveFirstSeenAt = null;
                localArchiveConsecutiveMatches = 0;
            }

            await DelayWithTimingAsync(PollInterval, cancellationToken);
        }

        var diagnostic = SaveDiagnostic(lastResult, "stage1-upload-branch-timeout");
        var finalSandboxRunning = _sandboxWatcher.IsSandboxRunning();
        sandboxWasSeen = sandboxWasSeen || finalSandboxRunning;
        throw new TimeoutException(
            $"Stage1 timed out during upload attempt '{attemptName ?? stepName}'. Step={stepName}. " +
            $"ExpectedImmediatePages=[{FormatPages(expectedPages)}], " +
            $"lastPage={lastResult?.Recognition.Page.ToString() ?? "<none>"}, " +
            $"lastConfidence={lastResult?.Recognition.Confidence.ToString("F2") ?? "<none>"}, " +
            $"lastDecision={lastDecision?.Reason ?? "<none>"}, " +
            $"sandboxSeen={sandboxWasSeen}, sandboxRunning={finalSandboxRunning}, " +
            $"elapsedSeconds={(_clock.UtcNow - startedAt).TotalSeconds:F1}, budgetSeconds={(deadline - startedAt).TotalSeconds:0}, " +
            $"diagnostic={diagnostic ?? "<not-saved>"}.");
    }

    private static bool TryPromoteUploadedArchiveByCardContext(
        StablePageResult current,
        out StablePageResult promoted,
        out string reason)
    {
        if (current.Recognition.Page == GamePage.Unknown && HasStrongUploadedArchiveContext(current.Recognition))
        {
            var confidence = current.Recognition.Targets
                .Where(static target => IsStrongUploadedArchiveTarget(target.Name))
                .Select(target => target.Confidence)
                .DefaultIfEmpty(0.25)
                .Max();
            promoted = current with
            {
                Recognition = new PageRecognitionResult(
                    GamePage.UploadedArchive,
                    confidence,
                    current.Recognition.Source + "|workflow_context:uploaded_archive_by_strong_context",
                    current.Recognition.Targets)
            };
            reason = "workflow_context_uploaded_archive_by_strong_context";
            return true;
        }

        promoted = current;
        reason = "not_promoted";
        return false;
    }

    private static bool ShouldSettleUploadResultCandidate(
        StablePageResult current,
        IReadOnlySet<GamePage> expectedPages)
    {
        if (current.Recognition.Page == GamePage.UploadedArchive &&
            expectedPages.Contains(current.Recognition.Page))
            return true;

        return expectedPages.Contains(GamePage.UploadedArchive) &&
               current.Recognition.Page is GamePage.Unknown or GamePage.UploadFailed &&
               HasStrongUploadedArchiveContext(current.Recognition);
    }

    private static bool HasStrongUploadedArchiveContext(PageRecognitionResult recognition) =>
        recognition.Targets.Any(static target => IsStrongUploadedArchiveTarget(target.Name));

    private static bool IsStrongUploadedArchiveTarget(string targetName) =>
        string.Equals(targetName, WorkflowTargetNames.UploadedArchiveTab, StringComparison.Ordinal) ||
        string.Equals(targetName, WorkflowTargetNames.UploadedArchiveDismissArea, StringComparison.Ordinal) ||
        string.Equals(targetName, WorkflowTargetNames.InfoPanelRow, StringComparison.Ordinal) ||
        string.Equals(targetName, WorkflowTargetNames.UploadedImageAssetsButton, StringComparison.Ordinal);

    private async Task<StablePageResult> RecognizeOnceAsync(CancellationToken cancellationToken, bool supplementEditIcons = true)
    {
        var current = await CapturePageOnceAsync(cancellationToken, supplementEditIcons);
        if (!current.Recognition.Page.IsBlocking()) return current;
        // Error classifications need a second fresh frame before callers may fail
        // or click an error-dialog action. Ordinary/contextual OCR frames stay cheap.
        var previous = current.Recognition.Page;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
            current = await CapturePageOnceAsync(cancellationToken, supplementEditIcons);
            LogWorkflow(_activeDiagnostics?.WorkflowLog as List<string>, 
                $"step=ConfirmBlockingPage previous={previous} observed={current.Recognition.Page} attempt={attempt + 1}");
            if (!current.Recognition.Page.IsBlocking() || current.Recognition.Page == previous) return current;
            previous = current.Recognition.Page;
        }
        return WithContextualPage(current, GamePage.Unknown);
    }

    private async Task<StablePageResult> CapturePageOnceAsync(CancellationToken cancellationToken, bool supplementEditIcons = true)
    {
        var capture = await _snapshotProvider.CaptureAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var recognition = await _recognizer.RecognizeAsync(capture.Bitmap, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var page = new StablePageResult(capture, recognition);
        return supplementEditIcons ? await AddMissingEditIconsAsync(page, cancellationToken) : page;
    }

    private async Task<StablePageResult?> TryRecognizeForDiagnosticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await CapturePageOnceAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildOpenMainMenuTimeoutClassification(PageRecognitionResult? recognition)
    {
        if (recognition == null)
            return "No screenshot recognition result was available after ESC.";

        return recognition.Page switch
        {
            GamePage.WorldMain => "ESC after-state is still WorldMain; the key may not have taken effect or the recognizer sampled before the menu appeared.",
            GamePage.MainMenu => $"MainMenu was recognized, but target '{WorkflowTargetNames.QianxingRealmEntry}' was not detected; the entry may be hidden or missing from detections.",
            GamePage.Unknown => "Page recognition returned Unknown after ESC; the menu may be open but Stage1 has no reliable target evidence, or capture/recognition failed.",
            _ => $"Unexpected page after ESC: {recognition.Page}."
        };
    }

    private TimeoutException CreateOpenMainMenuTimeoutException(
        TimeoutException firstTimeout,
        TimeoutException? fallbackTimeout,
        string beforeForeground,
        string afterForeground,
        string foregroundAfterFirstWait,
        string foregroundAfterFallbackKey,
        string firstPage,
        string firstConfidence,
        string firstClassification,
        string? firstDiagnostic,
        GameWindowInfo targetWindow,
        StablePageResult? latest)
    {
        var foregroundAfterRetryWait = _activation.GetForegroundWindowInfo(latest?.Capture.Window ?? targetWindow).ToDiagnosticString();
        var retryDiagnostic = SaveDiagnostic(latest, "stage1-open-main-menu-paimon-fallback-timeout");
        var retryPage = latest?.Recognition.Page.ToString() ?? "<none>";
        var retryConfidence = latest?.Recognition.Confidence.ToString("F2") ?? "<none>";
        var retryClassification = BuildOpenMainMenuTimeoutClassification(latest?.Recognition);
        var fallbackText = fallbackTimeout == null ? "<not-attempted>" : fallbackTimeout.Message;

        return new TimeoutException(
            $"Stage1 OpenMainMenu did not reach a usable MainMenu state. classification=\"{retryClassification}\". " +
            $"foregroundBefore=({beforeForeground}), foregroundAfterKey=({afterForeground}), " +
            $"foregroundAfterFirstWait=({foregroundAfterFirstWait}), foregroundAfterFallbackKey=({foregroundAfterFallbackKey}), " +
            $"foregroundAfterRetryWait=({foregroundAfterRetryWait}), " +
            $"firstRecognizedPage={firstPage}, firstConfidence={firstConfidence}, firstClassification=\"{firstClassification}\", " +
            $"firstDiagnostic={firstDiagnostic ?? "<not-saved>"}, currentRecognizedPage={retryPage}, " +
            $"currentConfidence={retryConfidence}, diagnostic={retryDiagnostic ?? "<not-saved>"}. " +
            $"ESC timeout: {firstTimeout.Message}; Paimon fallback timeout: {fallbackText}",
            fallbackTimeout ?? firstTimeout);
    }

    private async Task ClickLocalArchiveUploadMenuItemAsync(
        StablePageResult menu,
        string attemptName,
        List<string> workflowLog,
        CancellationToken cancellationToken)
    {
        if (menu.Recognition.Page != GamePage.LocalArchiveMenu)
            throw new InvalidOperationException($"Stage1 refuses to click local archive menu item on page {menu.Recognition.Page}.");

        var menuItems = FindTargets(menu.Recognition, WorkflowTargetNames.MenuActionItem, 0.25)
            .OrderBy(target => target.CenterY)
            .ThenBy(target => target.CenterX)
            .ToList();
        var matches = new List<AutomationTarget>();
        foreach (var item in menuItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var texts = await _workflowOcr.RecognizeTargetAsync(menu.Capture, item, cancellationToken);
            var region = ArchiveOcrTextLayout.Box(item);
            var labels = ArchiveOcrTextLayout.Lines(texts, region, menu.Capture.Bitmap.PixelWidth, menu.Capture.Bitmap.PixelHeight)
                .Where(t => ArchiveOcrTextLayout.Left(t.NormalizedBox) <= ArchiveOcrTextLayout.Left(region) + region.Width * .25)
                .ToList();
            var normalized = string.Join("|", labels.Select(t => NormalizeWorkflowOcrText(t.Text)));
            var accepted = labels.Count == 1 && labels[0].Confidence >= .6 &&
                           normalized is "UPLOADSAVE" or "上传存档" or "上傳存檔";
            LogWorkflow(workflowLog, 
                $"step={attemptName}.InspectUploadMenu target=({FormatTarget(item)}) " +
                $"ocr=\"{FormatOcrTexts(texts)}\" scores=[{string.Join(",", texts.Select(text => text.Confidence.ToString("F3")))}] " +
                $"normalized=\"{normalized}\" keptBlocks={labels.Count} " +
                $"layout=[{string.Join(";", texts.Select(t => $"{t.Text}@{FormatNormalizedBox(t.NormalizedBox)}"))}] accepted={accepted}");
            if (accepted) matches.Add(item);
        }

        if (matches.Count == 1)
        {
            var uploadItem = matches[0];
            LogWorkflow(workflowLog, 
                $"step={attemptName}.ClickUploadMenu selectedBy=unique_upload_text itemCount={menuItems.Count} " +
                $"target=({FormatTarget(uploadItem)})");
            _recordAction?.Invoke($"click:{WorkflowTargetNames.LocalArchiveUploadMenuItem}");
            await ClickSpecificTargetAsync(menu, uploadItem, cancellationToken);
            return;
        }

        var diagnostic = SaveDiagnostic(menu, "stage1-upload-menu-unconfirmed");
        throw new InvalidOperationException(
            $"无法唯一确认“上传存档 / Upload Save”菜单项，已停止，未点击任何菜单项。" +
            $" menuItemCount={menuItems.Count}, uploadTextMatches={matches.Count}, diagnostic={diagnostic}.");
    }

    private AutomationTarget? FindUploadLocationChoiceTarget(
        StablePageResult uploadLocationDialog,
        UploadLocationChoice choice)
    {
        return choice switch
        {
            UploadLocationChoice.UploadNew => FindSecondaryActionButton(uploadLocationDialog, 0.25, preferLeft: true),
            UploadLocationChoice.UpdateUploaded => FindPrimaryActionButton(uploadLocationDialog, 0.25, preferRight: true),
            _ => null
        };
    }

    private static AutomationTarget? FindLocalArchiveImportButton(
        StablePageResult localArchive,
        double minimumConfidence = 0.25)
    {
        if (localArchive.Recognition.Page != GamePage.LocalArchive)
            return null;

        var genericImport = FindTargets(localArchive.Recognition, WorkflowTargetNames.PrimaryActionButton, minimumConfidence)
            .Where(target => target.CenterY >= 0.82)
            .OrderBy(target => target.CenterX)
            .ThenByDescending(target => target.Confidence)
            .FirstOrDefault();
        if (genericImport != null)
            return genericImport;

        return FindTargets(localArchive.Recognition, WorkflowTargetNames.ImportArchiveButton, minimumConfidence)
            .Where(target => target.CenterY >= 0.82)
            .OrderBy(target => target.CenterX)
            .ThenByDescending(target => target.Confidence)
            .FirstOrDefault();
    }

    private static AutomationTarget? FindPrimaryActionButton(
        StablePageResult current,
        double minimumConfidence = 0.3,
        bool preferRight = false) =>
        FindActionButton(current.Recognition, WorkflowTargetNames.PrimaryActionButton, minimumConfidence, preferRight);

    private static AutomationTarget? FindSecondaryActionButton(
        StablePageResult current,
        double minimumConfidence = 0.3,
        bool preferLeft = false) =>
        FindActionButton(current.Recognition, WorkflowTargetNames.SecondaryActionButton, minimumConfidence, preferRight: !preferLeft);

    private static AutomationTarget? FindActionButton(
        PageRecognitionResult recognition,
        string targetName,
        double minimumConfidence,
        bool preferRight)
    {
        var targets = FindTargets(recognition, targetName, minimumConfidence);
        var modalTargets = FindTargets(recognition, WorkflowTargetNames.ModalDialog, 0.25);
        if (modalTargets.Count > 0)
        {
            var modal = modalTargets
                .OrderByDescending(target => target.Width * target.Height)
                .First();
            var insideModal = targets
                .Where(target => IsTargetInside(target, modal, horizontalPadding: 0.02, verticalPadding: 0.02))
                .ToList();
            if (insideModal.Count > 0)
                targets = insideModal;
        }

        return preferRight
            ? targets.OrderByDescending(target => target.CenterX).ThenByDescending(target => target.Confidence).FirstOrDefault()
            : targets.OrderBy(target => target.CenterX).ThenByDescending(target => target.Confidence).FirstOrDefault();
    }

    private static bool IsTargetInside(
        AutomationTarget target,
        AutomationTarget container,
        double horizontalPadding = 0,
        double verticalPadding = 0)
    {
        var left = container.CenterX - container.Width / 2 - horizontalPadding;
        var right = container.CenterX + container.Width / 2 + horizontalPadding;
        var top = container.CenterY - container.Height / 2 - verticalPadding;
        var bottom = container.CenterY + container.Height / 2 + verticalPadding;
        return target.CenterX >= left &&
               target.CenterX <= right &&
               target.CenterY >= top &&
               target.CenterY <= bottom;
    }

    private static bool IsRightSideModalActionButton(PageRecognitionResult recognition, AutomationTarget target)
    {
        var modal = FindTargets(recognition, WorkflowTargetNames.ModalDialog, 0.25)
            .OrderByDescending(candidate => candidate.Width * candidate.Height)
            .FirstOrDefault();

        if (modal == null)
            return target.CenterX >= 0.5 && target.CenterY >= 0.45;

        return IsTargetInside(target, modal, horizontalPadding: 0.02, verticalPadding: 0.02) &&
               target.CenterX > modal.CenterX &&
               target.CenterY > modal.CenterY;
    }

    private static NormalizedBox ExpandTargetBox(AutomationTarget target, double widthScale, double heightScale)
    {
        var width = Math.Clamp(target.Width * widthScale, 0.01, 1.0);
        var height = Math.Clamp(target.Height * heightScale, 0.01, 1.0);
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        return new NormalizedBox(
            Math.Clamp(target.CenterX, halfWidth, 1 - halfWidth),
            Math.Clamp(target.CenterY, halfHeight, 1 - halfHeight),
            width,
            height);
    }

    private async Task ClickRequiredTargetAsync(
        StablePageResult current,
        string targetName,
        CancellationToken cancellationToken,
        double minimumConfidence = 0.3)
    {
        if (current.Recognition.Page == GamePage.Unknown)
            throw new InvalidOperationException($"Stage1 refuses to click target {targetName} on Unknown page.");

        var target = _targetLocator.FindTarget(current.Recognition, targetName, minimumConfidence);
        if (target == null)
        {
            var expectedPage = current.Recognition.Page;
            var deadline = _clock.UtcNow + ShortWait;
            var ready = new PageObservationStreak();
            AutomationTarget? previousTemplateExit = null;
            while (_clock.UtcNow < deadline)
            {
                await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
                current = await RecognizeOnceAsync(cancellationToken);
                if (current.Recognition.Page.IsBlocking())
                    throw new InvalidOperationException($"Confirmed blocking page while locating {targetName}: {current.Recognition.Page}.");
                target = current.Recognition.Page == expectedPage
                    ? _targetLocator.FindTarget(current.Recognition, targetName, minimumConfidence) : null;
                if (target == null && targetName == WorkflowTargetNames.MainMenuExitButton &&
                    expectedPage == GamePage.MainMenu && current.Recognition.Page == GamePage.MainMenu &&
                    !current.Capture.IsLikelyBlackFrame)
                {
                    var exit = MainMenuExitTemplate.LocateExit(current.Capture.Bitmap, cancellationToken);
                    var stable = exit != null && previousTemplateExit != null &&
                        Math.Abs(exit.CenterX - previousTemplateExit.CenterX) < .01 &&
                        Math.Abs(exit.CenterY - previousTemplateExit.CenterY) < .01;
                    LogWorkflow(_activeDiagnostics?.WorkflowLog as List<string>,
                        $"step=WaitRequiredTarget fallback=main_menu_exit_template found={exit != null} stable={stable} target=({(exit == null ? "none" : FormatTarget(exit))})");
                    previousTemplateExit = exit;
                    if (stable) { target = exit; break; }
                }
                else
                {
                    previousTemplateExit = null;
                }
                LogWorkflow(_activeDiagnostics?.WorkflowLog as List<string>, 
                    $"step=WaitRequiredTarget target={targetName} page={current.Recognition.Page} found={target != null}");
                if (ready.Observe(current.Recognition.Page, target != null)) break;
                target = null;
            }
            if (target == null)
            {
                var diagnostic = SaveDiagnostic(current, "stage1-required-target-timeout");
                throw new TimeoutException($"Target '{targetName}' did not stabilize on {expectedPage}; diagnostic={diagnostic}.");
            }
        }

        _recordAction?.Invoke($"click:{targetName}");
        await _input.ClickAsync(
            current.Capture.Window,
            Math.Clamp(target.CenterX, 0, 1),
            Math.Clamp(target.CenterY, 0, 1),
            cancellationToken);
    }

    private static AutomationTarget? FindFirstAvailableTarget(
        PageRecognitionResult recognition,
        IReadOnlyList<string> targetNames)
    {
        foreach (var targetName in targetNames)
        {
            var target = FindTargets(recognition, targetName).FirstOrDefault();
            if (target != null)
                return target;
        }

        return null;
    }

    private async Task<OcrTargetMatch> FindTargetByOcrAsync(
        StablePageResult current,
        string targetName,
        IReadOnlyList<string> acceptedTokens,
        CancellationToken cancellationToken)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        foreach (var target in FindTargets(current.Recognition, targetName))
        {
            var ocrTexts = await _workflowOcr.RecognizeTargetAsync(current.Capture, target, cancellationToken);
            allOcrTexts.AddRange(ocrTexts);
            if (OcrTextsContainForbiddenSubmitAction(ocrTexts))
                continue;
            if (OcrTextsContainAny(ocrTexts, acceptedTokens))
                return new OcrTargetMatch(target, allOcrTexts);

            if (TryCreatePreviewContextOcrBox(target, out var contextBox))
            {
                var contextTexts = await _workflowOcr.RecognizeBoxAsync(
                    current.Capture,
                    contextBox,
                    $"{target.Name}/context",
                    cancellationToken);
                allOcrTexts.AddRange(contextTexts);
                if (OcrTextsContainAny(contextTexts, acceptedTokens))
                    return new OcrTargetMatch(target, allOcrTexts);
            }
        }

        return new OcrTargetMatch(null, allOcrTexts);
    }

    private async Task<(StablePageResult Page, InfoPanelRowIconSelection Entry)> FindPreviewInfoEntryWithRetryAsyncCore(
        StablePageResult current, List<string> workflowLog, CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + TimeSpan.FromSeconds(8);
        InfoPanelRowIconSelection entry = new(null, null, "not_checked", []);
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Recognition.Page.IsBlocking())
                throw new InvalidOperationException($"Blocking page while finding Preview Info: {current.Recognition.Page}.");
            entry = await FindUploadedArchiveInfoPanelIconByOcrAsync(current,
                ["PREVIEWINFO", "PREVIEW INFO", "预览信息/待填写", "预览信息/审核中", "预览信息/审核通过", "预览信息"],
                [WorkflowTargetNames.TextEditIcon, WorkflowTargetNames.UploadedImageAssetsButton],
                WorkflowTargetNames.TextEditIcon, cancellationToken);
            LogWorkflow(workflowLog, $"step=PreviewInfoEntry attempt={attempt} page={current.Recognition.Page} " +
                $"found={entry.Icon != null} selectedBy={entry.SelectedBy} ocr=\"{FormatRawOcrTexts(entry.OcrTexts)}\"");
            if (entry.Icon != null || attempt == 4 || _clock.UtcNow >= deadline) break;
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            if (_clock.UtcNow >= deadline) break;
            current = await RecognizeOnceAsync(cancellationToken);
        }
        return (current, entry);
    }

    private static bool InfoPanelRowTextMatches(
        IReadOnlyList<WorkflowOcrTextResult> texts, IReadOnlyList<string> acceptedTokens)
    {
        if (OcrTextsContainAny(texts, acceptedTokens)) return true;
        // Only the Preview Info row supports these status suffixes. Do not relax
        // shared OCR matching for upload, copy, submit or publish actions.
        if (!acceptedTokens.Any(t => NormalizeWorkflowOcrText(t) is "PREVIEWINFO" or "预览信息")) return false;
        return texts.Any(text =>
        {
            if (text.Confidence < .6) return false;
            var parts = text.Text.Normalize(System.Text.NormalizationForm.FormKC).Split('/');
            return parts.Length == 2 && NormalizeWorkflowOcrText(parts[0]) is "PREVIEWINFO" or "预览信息" &&
                NormalizeWorkflowOcrText(parts[1]) is "PENDING" or "UNDERREVIEW" or "APPROVED" or "REJECTED" or
                    "待填写" or "审核中" or "审核通过" or "审核未通过";
        });
    }

    private async Task<InfoPanelRowIconSelection> FindUploadedArchiveInfoPanelIconByOcrAsync(
        StablePageResult current,
        IReadOnlyList<string> acceptedRowTokens,
        IReadOnlyList<string> rowIconTargetNames,
        string uniqueFallbackIconTargetName,
        CancellationToken cancellationToken)
    {
        if (current.Recognition.Page != GamePage.UploadedArchive)
            return new InfoPanelRowIconSelection(null, null, "not_uploaded_archive", []);

        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var rows = DeduplicateInfoPanelRows(FindTargets(current.Recognition, WorkflowTargetNames.InfoPanelRow, 0.25))
            .OrderBy(target => target.CenterY)
            .ToList();
        var rowIcons = rowIconTargetNames
            .SelectMany(targetName => FindTargets(current.Recognition, targetName, 0.25))
            .ToList();

        foreach (var row in rows)
        {
            var rowTexts = await _workflowOcr.RecognizeTargetAsync(current.Capture, row, cancellationToken);
            allOcrTexts.AddRange(rowTexts);
            if (!InfoPanelRowTextMatches(rowTexts, acceptedRowTokens))
                continue;

            var icon = rowIcons
                .Where(candidate => IsTargetInside(candidate, row, horizontalPadding: 0.015, verticalPadding: 0.02))
                .OrderByDescending(candidate => candidate.CenterX)
                .ThenByDescending(candidate => candidate.Confidence)
                .FirstOrDefault();
            if (icon != null)
                return new InfoPanelRowIconSelection(row, icon, $"row_ocr_{icon.Name}", allOcrTexts);

            var openCvIcon = _infoPanelIconLocator.Locate(
                current.Capture,
                row,
                uniqueFallbackIconTargetName,
                cancellationToken);
            if (openCvIcon.Matched && openCvIcon.Target != null)
                return new InfoPanelRowIconSelection(row, openCvIcon.Target, $"row_ocr_opencv:{openCvIcon.RejectedReason}", allOcrTexts);

            return new InfoPanelRowIconSelection(row, null, $"row_ocr_no_icon:opencv={openCvIcon.RejectedReason}", allOcrTexts);
        }

        var fallbackIcons = FindTargets(current.Recognition, uniqueFallbackIconTargetName, 0.25);
        var iconRowCandidates = rows
            .Select(row => new
            {
                Row = row,
                Icons = fallbackIcons
                    .Where(candidate => IsTargetInside(candidate, row, horizontalPadding: 0.015, verticalPadding: 0.02))
                    .OrderByDescending(candidate => candidate.CenterX)
                    .ThenByDescending(candidate => candidate.Confidence)
                    .ToList()
            })
            .Where(candidate => candidate.Icons.Count > 0)
            .ToList();

        if (iconRowCandidates.Count == 1)
            return new InfoPanelRowIconSelection(iconRowCandidates[0].Row, iconRowCandidates[0].Icons[0], "unique_icon_in_info_row", allOcrTexts);

        var rowOrderFallback = TrySelectUploadedArchiveInfoPanelRowByOrder(rows, acceptedRowTokens);
        if (rowOrderFallback != null)
        {
            var icon = rowIcons
                .Where(candidate => IsTargetInside(candidate, rowOrderFallback, horizontalPadding: 0.015, verticalPadding: 0.02))
                .OrderByDescending(candidate => candidate.CenterX)
                .ThenByDescending(candidate => candidate.Confidence)
                .FirstOrDefault();
            if (icon != null)
                return new InfoPanelRowIconSelection(rowOrderFallback, icon, $"row_order_{icon.Name}", allOcrTexts);

            var openCvIcon = _infoPanelIconLocator.Locate(
                current.Capture,
                rowOrderFallback,
                uniqueFallbackIconTargetName,
                cancellationToken);
            if (openCvIcon.Matched && openCvIcon.Target != null)
                return new InfoPanelRowIconSelection(rowOrderFallback, openCvIcon.Target, $"row_order_opencv:{openCvIcon.RejectedReason}", allOcrTexts);

            return new InfoPanelRowIconSelection(rowOrderFallback, null, $"row_order_no_icon:opencv={openCvIcon.RejectedReason}", allOcrTexts);
        }

        return new InfoPanelRowIconSelection(null, null, "not_found", allOcrTexts);
    }

    private static IReadOnlyList<AutomationTarget> DeduplicateInfoPanelRows(IReadOnlyList<AutomationTarget> rows)
    {
        var deduplicated = new List<AutomationTarget>();
        foreach (var row in rows
                     .OrderByDescending(target => target.Width * target.Height)
                     .ThenByDescending(target => target.Confidence))
        {
            if (deduplicated.Any(existing => IsSameInfoPanelRow(existing, row)))
                continue;

            deduplicated.Add(row);
        }

        return deduplicated
            .OrderBy(target => target.CenterY)
            .ToList();
    }

    private static bool IsSameInfoPanelRow(AutomationTarget left, AutomationTarget right)
    {
        var leftTop = left.CenterY - left.Height / 2;
        var leftBottom = left.CenterY + left.Height / 2;
        var rightTop = right.CenterY - right.Height / 2;
        var rightBottom = right.CenterY + right.Height / 2;
        var overlap = Math.Max(0, Math.Min(leftBottom, rightBottom) - Math.Max(leftTop, rightTop));
        var minHeight = Math.Min(left.Height, right.Height);
        var maxHeight = Math.Max(left.Height, right.Height);
        return minHeight > 0 &&
               maxHeight > 0 &&
               overlap / minHeight >= 0.55 &&
               Math.Abs(left.CenterY - right.CenterY) <= maxHeight * 0.5;
    }

    private static AutomationTarget? TrySelectUploadedArchiveInfoPanelRowByOrder(
        IReadOnlyList<AutomationTarget> rows,
        IReadOnlyList<string> acceptedRowTokens)
    {
        if (rows.Count < 3)
            return null;

        var normalizedTokens = acceptedRowTokens
            .Select(NormalizeWorkflowOcrText)
            .Where(token => token.Length > 0)
            .ToList();

        if (normalizedTokens.Any(token => token.Contains("PREVIEWINFO", StringComparison.Ordinal) ||
                                          token.Contains("\u9884\u89c8\u4fe1\u606f", StringComparison.Ordinal)))
            return rows[1];

        if (normalizedTokens.Any(token => token.Contains("IMAGEASSETS", StringComparison.Ordinal) ||
                                          token.Contains("\u56fe\u7247\u8d44\u4ea7", StringComparison.Ordinal)))
            return rows[2];

        return null;
    }

    private static bool TryCreatePreviewContextOcrBox(AutomationTarget target, out NormalizedBox box)
    {
        if (string.Equals(target.Name, WorkflowTargetNames.TextEditIcon, StringComparison.Ordinal))
        {
            var width = Math.Clamp(target.Width * 7.0, 0.12, 0.45);
            var height = Math.Clamp(target.Height * 3.0, 0.06, 0.18);
            var centerX = Math.Clamp(target.CenterX - width * 0.35, width / 2, 1 - width / 2);
            box = new NormalizedBox(centerX, target.CenterY, width, height);
            return true;
        }

        if (string.Equals(target.Name, WorkflowTargetNames.EditRow, StringComparison.Ordinal) ||
            string.Equals(target.Name, WorkflowTargetNames.TextInputArea, StringComparison.Ordinal) ||
            string.Equals(target.Name, WorkflowTargetNames.ImageUploadArea, StringComparison.Ordinal))
        {
            box = ExpandTargetBox(target, widthScale: 1.6, heightScale: 1.4);
            return true;
        }

        box = new NormalizedBox(target.CenterX, target.CenterY, target.Width, target.Height);
        return false;
    }

    private async Task<OcrTargetMatch> FindChoiceOptionByOcrAsync(
        StablePageResult current,
        string expectedText,
        CancellationToken cancellationToken) =>
        await FindChoiceOptionByOcrAsync(current, [expectedText], cancellationToken);

    private async Task<TagChoiceSelection> FindTagChoiceWithScrollAsyncCore(
        StablePageResult tagDialog,
        Stage1TagCatalogEntry tag,
        List<string> workflowLog,
        CancellationToken cancellationToken,
        Dictionary<string, AutomationTarget>? visibleTags = null)
    {
        var current = tagDialog;
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var normalizedAliases = tag.OcrAliases
            .Select(NormalizeWorkflowOcrText)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        for (var scrollAttempt = 0; scrollAttempt <= TagDialogMaxScrollAttempts; scrollAttempt++)
        {
            if (current.Recognition.Page != GamePage.TagDialog)
                current = await WaitForPageEvidenceAsync(current, Pages(GamePage.TagDialog), "Tags.PageRecovery", ShortWait, cancellationToken);

            var candidates = FindTargets(current.Recognition, WorkflowTargetNames.ChoiceOption)
                .OrderBy(target => target.CenterY)
                .ThenBy(target => target.CenterX)
                .ThenByDescending(target => target.Confidence)
                .ToList();
            if (visibleTags != null && visibleTags.TryGetValue(tag.ToolValue, out var hint))
            {
                var near = candidates.Where(t => Math.Abs(t.CenterX - hint.CenterX) < .015 &&
                    Math.Abs(t.CenterY - hint.CenterY) < .015).ToList();
                if (near.Count == 1)
                {
                    var freshTexts = await _workflowOcr.RecognizeTargetAsync(current.Capture, near[0], cancellationToken);
                    allOcrTexts.AddRange(freshTexts);
                    if (TryMatchTagChoiceOcr(freshTexts, normalizedAliases, out _))
                    {
                        LogWorkflow(workflowLog, $"step=Tags action=VerifyMappedChoice toolValue={tag.ToolValue} result=FreshOcrMatched");
                        return new TagChoiceSelection(current, near[0], allOcrTexts, scrollAttempt);
                    }
                }
                visibleTags.Clear();
            }
            AutomationTarget? found = null;
            var ambiguousTags = new HashSet<string>(StringComparer.Ordinal);
            visibleTags?.Clear();
            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                var candidate = candidates[candidateIndex];
                var ocrTexts = await _workflowOcr.RecognizeTargetAsync(current.Capture, candidate, cancellationToken);
                allOcrTexts.AddRange(ocrTexts);
                var match = TryMatchTagChoiceOcr(ocrTexts, normalizedAliases, out var reason);
                if (visibleTags != null)
                    foreach (var entry in TagCatalog)
                        if (TryMatchTagChoiceOcr(ocrTexts, entry.OcrAliases.Select(NormalizeWorkflowOcrText).ToList(), out _))
                        {
                            if (!visibleTags.TryAdd(entry.ToolValue, candidate)) ambiguousTags.Add(entry.ToolValue);
                        }
                LogWorkflow(workflowLog, 
                    $"step=Tags action=ScanChoice toolValue=\"{tag.ToolValue}\" canonical=\"{tag.CanonicalGameText}\" " +
                    $"scrollAttempt={scrollAttempt} candidateIndex={candidateIndex} target=({FormatTarget(candidate)}) " +
                    $"candidateText=\"{FormatRawOcrTexts(ocrTexts)}\" normalized=\"{FormatNormalizedOcrTexts(ocrTexts)}\" " +
                    $"accepted={match} reason={reason}");
                if (match)
                {
                    LogWorkflow(workflowLog, 
                        $"step=EditPreviewInfo field=Tags scanPage={scrollAttempt} result=tagFound " +
                        $"toolValue=\"{tag.ToolValue}\" canonical=\"{tag.CanonicalGameText}\" aliases=\"{string.Join("|", tag.OcrAliases)}\" " +
                        $"target=({FormatTarget(candidate)}) ocr=\"{FormatOcrTexts(ocrTexts)}\"");
                    found ??= candidate;
                }
            }
            foreach (var ambiguous in ambiguousTags) visibleTags?.Remove(ambiguous);
            if (found != null)
                return new TagChoiceSelection(current, found, allOcrTexts, scrollAttempt);

            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Tags scanPage={scrollAttempt} result=tagNotFound " +
                $"toolValue=\"{tag.ToolValue}\" canonical=\"{tag.CanonicalGameText}\" aliases=\"{string.Join("|", tag.OcrAliases)}\" " +
                $"ocr=\"{FormatOcrTexts(allOcrTexts.TakeLast(12).ToList())}\"");

            if (scrollAttempt == TagDialogMaxScrollAttempts)
                break;

            _recordAction?.Invoke($"scroll:tag_dialog:{TagDialogScrollWheelDelta}");
            visibleTags?.Clear();
            LogWorkflow(workflowLog, 
                $"step=EditPreviewInfo field=Tags action=ScrollTagDialog " +
                $"toolValue=\"{tag.ToolValue}\" amount={TagDialogScrollWheelDelta} scrollAttempt={scrollAttempt + 1}");
            await _input.ScrollAsync(
                current.Capture.Window,
                TagDialogScrollX,
                TagDialogScrollY,
                TagDialogScrollWheelDelta,
                cancellationToken);
            await DelayWithTimingAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
            current = await WaitForExpectedAsync(
                $"EditPreviewInfo.Tags.ScrollTagDialog{scrollAttempt + 1}",
                Pages(GamePage.TagDialog),
                ShortWait,
                stableCount: 1,
                cancellationToken);
        }

        return new TagChoiceSelection(current, null, allOcrTexts, TagDialogMaxScrollAttempts);
    }

    private static bool TryMatchTagChoiceOcr(
        IReadOnlyList<WorkflowOcrTextResult> ocrTexts,
        IReadOnlyList<string> normalizedAliases,
        out string reason)
    {
        foreach (var text in ocrTexts)
        {
            var observed = string.IsNullOrWhiteSpace(text.NormalizedText)
                ? NormalizeWorkflowOcrText(text.Text)
                : text.NormalizedText;
            foreach (var expected in normalizedAliases)
            {
                if (NormalizedChoiceOptionOcrMatches(observed, expected))
                {
                    reason = string.Equals(observed, expected, StringComparison.Ordinal)
                        ? $"normalized_exact:{expected}"
                        : $"strict_coverage_match observed={observed} expected={expected}";
                    return true;
                }
            }
        }

        reason = "no_alias_strict_match";
        return false;
    }

    private async Task<OcrTargetMatch> FindChoiceOptionByOcrAsync(
        StablePageResult current,
        IReadOnlyList<string> expectedTexts,
        CancellationToken cancellationToken)
    {
        var normalizedExpected = expectedTexts
            .Select(NormalizeWorkflowOcrText)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        if (normalizedExpected.Count == 0)
            return new OcrTargetMatch(null, allOcrTexts);

        foreach (var target in FindTargets(current.Recognition, WorkflowTargetNames.ChoiceOption))
        {
            var ocrTexts = await _workflowOcr.RecognizeTargetAsync(current.Capture, target, cancellationToken);
            allOcrTexts.AddRange(ocrTexts);
            if (ocrTexts.Any(text => normalizedExpected.Any(expected =>
                    NormalizedChoiceOptionOcrMatches(text.NormalizedText, expected))))
                return new OcrTargetMatch(target, allOcrTexts);
        }

        return new OcrTargetMatch(null, allOcrTexts);
    }

    private static bool NormalizedChoiceOptionOcrMatches(string observedText, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(observedText) || string.IsNullOrWhiteSpace(expectedToken))
            return false;

        if (string.Equals(observedText, expectedToken, StringComparison.Ordinal))
            return true;

        if (observedText.Contains(expectedToken, StringComparison.Ordinal))
            return expectedToken.Length / (double)observedText.Length >= 0.92;

        if (expectedToken.Contains(observedText, StringComparison.Ordinal))
            return observedText.Length / (double)expectedToken.Length >= 0.92;

        return false;
    }

    private static bool PreviewCandidateOcrMatches(
        IReadOnlyList<WorkflowOcrTextResult> ocrTexts,
        IReadOnlyList<string> acceptedTokens)
    {
        var accepted = acceptedTokens
            .Select(NormalizeLooseFieldOcrText)
            .Where(token => token.Length > 0)
            .ToList();

        return ocrTexts.Any(text => accepted.Any(token =>
            NormalizedOcrTextMatches(NormalizeLooseFieldOcrText(text.NormalizedText), token)));
    }

    private static string NormalizeLooseFieldOcrText(string value)
    {
        var normalized = NormalizeWorkflowOcrText(value);
        return new string(normalized
            .Where(static ch => char.IsLetterOrDigit(ch) || ch is >= '\u4e00' and <= '\u9fff')
            .ToArray());
    }

    private static IReadOnlyList<Stage1TagCatalogEntry> ResolveTagCatalogEntries(
        IReadOnlyList<string> tags,
        List<string> workflowLog)
    {
        var resolved = new List<(Stage1TagCatalogEntry Entry, int OriginalIndex)>();
        for (var index = 0; index < tags.Count; index++)
        {
            var tag = tags[index];
            var entry = ResolveTagCatalogEntry(tag)
                ?? throw new InvalidOperationException($"Unsupported Stage Tags value '{tag}'. Add it to TagCatalog before automated OCR matching.");
            LogWorkflow(workflowLog, 
                $"step=Tags action=ResolveTag toolValue=\"{entry.ToolValue}\" requested=\"{tag}\" " +
                $"canonical=\"{entry.CanonicalGameText}\" order={entry.DialogOrder} aliases=\"{string.Join("|", entry.OcrAliases)}\"");
            resolved.Add((entry, index));
        }

        return resolved
            .GroupBy(item => NormalizeWorkflowOcrText(item.Entry.ToolValue), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Entry.DialogOrder)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => item.Entry)
            .ToList();
    }

    private static Stage1TagCatalogEntry? ResolveTagCatalogEntry(string value)
    {
        var normalized = NormalizeWorkflowOcrText(value);
        if (normalized.Length == 0)
            return null;

        return TagCatalog.FirstOrDefault(entry =>
            string.Equals(NormalizeWorkflowOcrText(entry.ToolValue), normalized, StringComparison.Ordinal) ||
            string.Equals(NormalizeWorkflowOcrText(entry.CanonicalGameText), normalized, StringComparison.Ordinal) ||
            entry.OcrAliases.Any(alias => string.Equals(NormalizeWorkflowOcrText(alias), normalized, StringComparison.Ordinal)));
    }

    private static readonly IReadOnlyList<Stage1TagCatalogEntry> TagCatalog = CreateTagCatalog();

    private static IReadOnlyList<Stage1TagCatalogEntry> CreateTagCatalog()
    {
        var order = 0;
        return
        [
            Tag("Long-Term Play", "Long-Term Play", order++, "\u957f\u7ebf\u6e38\u73a9"),
            Tag("Casual Enjoyment", "Casual Enjoyment", order++, "\u8f7b\u91cf\u8da3\u5473"),
            Tag("Keyboard and Mouse", "Keyboard and Mouse", order++, "Keyboard & Mouse", "\u952e\u76d8\u9f20\u6807"),
            Tag("Touchscreen", "Touchscreen", order++, "Touch Screen", "\u89e6\u5c4f"),
            Tag("Controller", "Controller", order++, "Gamepad", "\u624b\u67c4"),
            Tag("Adventure", "Action Adventure", order++, "\u52a8\u4f5c\u5192\u9669"),
            Tag("Survival Challenge", "Survival Challenge", order++, "Desafio de supervivencia", "Desaf\u00edo de supervivencia", "\u751f\u5b58\u7ade\u6280"),
            Tag("First-Person Shooter", "First-Person Shooter", order++, "First Person Shooter", "\u7b2c\u4e00\u4eba\u79f0\u5c04\u51fb"),
            Tag("Management Simulation", "Management Simulation", order++, "\u6a21\u62df\u7ecf\u8425"),
            Tag("Competitive Multiplayer", "Competitive Multiplayer", order++, "\u591a\u4eba\u5728\u7ebf\u7ade\u6280"),
            Tag("Roguelike", "Roguelike", order++, "\u8089\u9e3d"),
            Tag("Third-Person Shooter", "Third-Person Shooter", order++, "Third Person Shooter", "\u7b2c\u4e09\u4eba\u79f0\u5c04\u51fb"),
            Tag("Asymmetric Battle Arena", "Asymmetric Battle Arena", order++, "\u975e\u5bf9\u79f0\u7ade\u6280"),
            Tag("Brawl-Style", "Brawl-Style", order++, "Brawl Style", "\u4e71\u6597"),
            Tag("Co-Op Battle", "Co-Op Battle", order++, "Co Op Battle", "\u5171\u6597"),
            Tag("Party", "Party", order++, "\u6d3e\u5bf9"),
            Tag("Collaboration", "Collaboration", order++, "\u5408\u4f5c"),
            Tag("Parkour", "Parkour", order++, "\u8dd1\u9177"),
            Tag("Exploration & Puzzle Solving", "Exploration & Puzzle Solving", order++, "Exploration and Puzzle Solving", "\u63a2\u7d22\u89e3\u8c1c", "探索解密"),
            Tag("Survival & Construction", "Survival & Construction", order++, "Survival and Construction", "\u751f\u5b58\u5efa\u9020"),
            Tag("Role-Playing", "Role-Playing", order++, "Role Playing", "\u89d2\u8272\u626e\u6f14"),
            Tag("Atmospheric", "Atmospheric", order++, "\u60c5\u666f\u89c2\u8d4f"),
            Tag("Strategy", "Strategy", order++, "\u6218\u672f\u7b56\u7565"),
            Tag("Tower Defense", "Tower Defense", order++, "\u5854\u9632"),
            Tag("Sports", "Sports", order++, "\u4f53\u80b2"),
            Tag("Racing", "Racing", order++, "\u7ade\u901f"),
            Tag("Tabletop", "Tabletop", order++, "\u684c\u6e38"),
            Tag("Idle", "Idle", order++, "\u653e\u7f6e"),
            Tag("Mystery & Suspense", "Mystery & Suspense", order++, "Mystery and Suspense", "\u60ac\u7591\u60ca\u609a"),
            Tag("Music & Rhythm", "Music & Rhythm", order++, "Music and Rhythm", "\u97f3\u4e50\u8282\u594f"),
            Tag("Casual & Puzzle", "Casual & Puzzle", order++, "Casual and Puzzle", "\u4f11\u95f2\u76ca\u667a"),
            Tag("First-Person", "First-Person", order++, "First Person", "\u7b2c\u4e00\u4eba\u79f0"),
            Tag("Third-Person", "Third-Person", order++, "Third Person", "\u7b2c\u4e09\u4eba\u79f0"),
            Tag("Bird's Eye View", "Bird's Eye View", order++, "Birds Eye View", "\u4fef\u89c6\u89c6\u89d2", "俯视角"),
            Tag("Fixed Camera", "Fixed Camera", order++, "\u56fa\u5b9a\u955c\u5934"),
            Tag("Over-the-Shoulder Shot", "Over-the-Shoulder Shot", order++, "Over the Shoulder Shot", "\u8d8a\u80a9\u89c6\u89d2"),
            Tag("Casual", "Casual", order++, "\u4f11\u95f2"),
            Tag("Moderate", "Moderate", order++, "\u4e2d\u7b49"),
            Tag("Hard", "Hard", order++, "\u56f0\u96be"),
            Tag("Hardcore", "Hardcore", order++, "\u786c\u6838"),
            Tag("Dynamic Difficulty", "Dynamic Difficulty", order++, "\u52a8\u6001\u96be\u5ea6")
        ];
    }

    private static Stage1TagCatalogEntry Tag(
        string toolValue,
        string canonicalGameText,
        int dialogOrder,
        params string[] aliases)
    {
        var allAliases = new[] { canonicalGameText }
            .Concat(aliases)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new Stage1TagCatalogEntry(toolValue, canonicalGameText, allAliases, dialogOrder);
    }

    private async Task<OcrTargetMatch> FindPrimaryButtonByOcrAsync(
        StablePageResult current,
        IReadOnlyList<string> acceptedTokens,
        CancellationToken cancellationToken,
        string? diagnosticScope = null)
    {
        var allOcrTexts = new List<WorkflowOcrTextResult>();
        var targets = FindTargets(current.Recognition, WorkflowTargetNames.PrimaryActionButton).ToList();
        var trace = diagnosticScope == null ? null : Guid.NewGuid().ToString("N");
        var timer = Stopwatch.StartNew();
        void Trace(string detail)
        {
            if (trace != null) LogWorkflow(_activeDiagnostics?.WorkflowLog as List<string>,
                $"step=ImageAssetButtonCheck scope={diagnosticScope} trace={trace} {detail}");
        }
        Trace($"action=Begin page={current.Recognition.Page} source=\"{current.Recognition.Source}\" " +
            $"frame={current.Capture.Bitmap.PixelWidth}x{current.Capture.Bitmap.PixelHeight} window=({current.Capture.Window}) " +
            $"candidateCount={targets.Count} acceptedTokens=[{string.Join("|", acceptedTokens)}] " +
            $"allPrimaryTargets=[{string.Join(";", current.Recognition.Targets.Where(t => t.Name == WorkflowTargetNames.PrimaryActionButton).Select(FormatTarget))}]");
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            var ocrTimer = Stopwatch.StartNew();
            IReadOnlyList<WorkflowOcrTextResult> ocrTexts;
            try { ocrTexts = await _workflowOcr.RecognizeTargetAsync(current.Capture, target, cancellationToken); }
            catch (Exception ex)
            {
                Trace($"action=OcrError candidate={index} target=({FormatTarget(target)}) elapsedMs={ocrTimer.ElapsedMilliseconds} error=\"{ex.GetType().Name}: {ex.Message}\"");
                throw;
            }
            allOcrTexts.AddRange(ocrTexts);
            var forbidden = OcrTextsContainForbiddenSubmitAction(ocrTexts);
            var matched = !forbidden && OcrTextsContainAny(ocrTexts, acceptedTokens);
            Trace($"action=Candidate candidate={index} target=({FormatTarget(target)}) " +
                $"requestedPixelBox=({(target.CenterX - target.Width / 2) * current.Capture.Bitmap.PixelWidth:F1}," +
                $"{(target.CenterY - target.Height / 2) * current.Capture.Bitmap.PixelHeight:F1}," +
                $"{target.Width * current.Capture.Bitmap.PixelWidth:F1},{target.Height * current.Capture.Bitmap.PixelHeight:F1}) " +
                $"ocr={System.Text.Json.JsonSerializer.Serialize(ocrTexts)} elapsedMs={ocrTimer.ElapsedMilliseconds} " +
                $"matched={matched} reason={(forbidden ? "forbidden_action" : matched ? "accepted_token" : ocrTexts.Count == 0 ? "ocr_empty" : "no_accepted_token")}");
            if (matched)
            {
                Trace($"action=End matched=True selectedCandidate={index} elapsedMs={timer.ElapsedMilliseconds}");
                return new OcrTargetMatch(target, allOcrTexts);
            }
        }
        Trace($"action=End matched=False reason={(targets.Count == 0 ? "no_candidate" : "no_ocr_match")} elapsedMs={timer.ElapsedMilliseconds}");
        if (trace != null)
        {
            // Save the actual rejected frame, not the later continuation screenshot.
            // Diagnostic I/O must not change workflow success/failure behavior.
            try
            {
                var path = SaveDiagnostic(current, "stage1-image-button-rejected-" + trace,
                    isFinalFailure: false, recoverable: true, phase: diagnosticScope, updateLatest: false);
                Trace($"action=SaveRejectedFrame diagnostic=\"{path}\"");
            }
            catch (Exception ex) { Trace($"action=DiagnosticWriteError error=\"{ex.Message}\""); }
        }
        return new OcrTargetMatch(null, allOcrTexts);
    }

    private static bool OcrTextsContainForbiddenSubmitAction(IReadOnlyList<WorkflowOcrTextResult> ocrTexts) =>
        OcrTextsContainAny(
            ocrTexts,
            ["POST", "PUBLISH", "REPLACE AND PUBLISH", "OVERWRITE AND PUBLISH", "覆盖并发布", "发布关卡", "\u53d1\u5e03", "SUBMITNOW", "SUBMIT NOW", "\u524d\u5f80\u63d0\u4ea4"]);

    private static bool OcrTextsContainAny(
        IReadOnlyList<WorkflowOcrTextResult> ocrTexts,
        IReadOnlyList<string> acceptedTokens)
    {
        var normalizedTokens = acceptedTokens
            .Select(NormalizeWorkflowOcrText)
            .Where(token => token.Length > 0)
            .ToList();
        if (normalizedTokens.Count == 0)
            return false;

        return ocrTexts.Any(text => normalizedTokens.Any(token =>
            NormalizedOcrTextMatches(text.NormalizedText, token)));
    }

    private static string NormalizeWorkflowOcrText(string value) =>
        RapidOcrWorkflowOcrService.NormalizePreviewText(value);

    private static string FormatRawOcrTexts(IReadOnlyList<WorkflowOcrTextResult> texts) =>
        texts.Count == 0
            ? "<none>"
            : string.Join("|", texts.Select(text => text.Text));

    private static string FormatNormalizedOcrTexts(IReadOnlyList<WorkflowOcrTextResult> texts) =>
        texts.Count == 0
            ? "<none>"
            : string.Join("|", texts.Select(text => text.NormalizedText));

    private static bool NormalizedOcrTextMatches(string observedText, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(observedText) || string.IsNullOrWhiteSpace(expectedToken))
            return false;

        if (string.Equals(observedText, expectedToken, StringComparison.Ordinal))
            return true;

        if (observedText.Length < 3 || expectedToken.Length < 3)
            return false;

        if (observedText.Contains(expectedToken, StringComparison.Ordinal))
        {
            var expectedCoverageOfObserved = expectedToken.Length / (double)observedText.Length;
            return expectedCoverageOfObserved >= 0.85;
        }

        var coverage = observedText.Length / (double)expectedToken.Length;
        return observedText.Length >= 6 &&
               coverage >= 0.75 &&
               expectedToken.Contains(observedText, StringComparison.Ordinal);
    }

    private static bool IsChoiceOptionVisuallySelected(
        GameCaptureResult capture,
        AutomationTarget target,
        out string evidence)
    {
        var bitmap = capture.Bitmap;
        if (bitmap.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            bitmap = converted;
        }

        var imageWidth = Math.Max(1, bitmap.PixelWidth);
        var imageHeight = Math.Max(1, bitmap.PixelHeight);
        var left = Math.Clamp(target.CenterX - target.Width / 2, 0, 1);
        var top = Math.Clamp(target.CenterY - target.Height / 2, 0, 1);
        var optionWidth = Math.Clamp(target.Width, 0, 1);
        var optionHeight = Math.Clamp(target.Height, 0, 1);

        var x = Math.Clamp((int)Math.Round((left + optionWidth * 0.02) * imageWidth), 0, imageWidth - 1);
        var y = Math.Clamp((int)Math.Round((top + optionHeight * 0.15) * imageHeight), 0, imageHeight - 1);
        var w = Math.Clamp((int)Math.Round(optionWidth * imageWidth * 0.14), 1, imageWidth - x);
        var h = Math.Clamp((int)Math.Round(optionHeight * imageHeight * 0.70), 1, imageHeight - y);

        var stride = w * 4;
        var pixels = new byte[stride * h];
        bitmap.CopyPixels(new Int32Rect(x, y, w, h), pixels, stride, 0);

        var greenPixels = 0;
        var brightPixels = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var b = pixels[index];
            var g = pixels[index + 1];
            var r = pixels[index + 2];
            if (g >= 135 && g > r + 35 && g > b + 15)
                greenPixels++;
            if (r >= 180 && g >= 180 && b >= 180)
                brightPixels++;
        }

        var selected = greenPixels >= Math.Max(4, (w * h) / 180);
        evidence = $"roi=({x},{y},{w},{h}) green={greenPixels} bright={brightPixels} selected={selected}";
        return selected;
    }

    private async Task ClickSpecificTargetAsync(
        StablePageResult current,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        if (current.Recognition.Page == GamePage.Unknown)
            throw new InvalidOperationException($"Stage1 refuses to click target {target.Name} on Unknown page.");

        _recordAction?.Invoke($"click:{target.Name}");
        await _input.ClickAsync(
            current.Capture.Window,
            Math.Clamp(target.CenterX, 0, 1),
            Math.Clamp(target.CenterY, 0, 1),
            cancellationToken);
    }

    private async Task ClickNormalizedAsync(
        GameWindowInfo window,
        double normalizedX,
        double normalizedY,
        CancellationToken cancellationToken)
    {
        _recordAction?.Invoke($"click:normalized:{normalizedX:F3},{normalizedY:F3}");
        await _input.ClickAsync(
            window,
            Math.Clamp(normalizedX, 0, 1),
            Math.Clamp(normalizedY, 0, 1),
            cancellationToken);
    }

    private static IReadOnlyList<AutomationTarget> FindTargets(
        PageRecognitionResult recognition,
        string targetName,
        double minimumConfidence = 0.3) =>
        recognition.Targets
            .Where(target => string.Equals(target.Name, targetName, StringComparison.Ordinal))
            .Where(target => target.Confidence >= minimumConfidence)
            .OrderByDescending(target => target.Confidence)
            .ToList();

    private static int CountTargets(
        PageRecognitionResult recognition,
        string targetName,
        double minimumConfidence = 0.3) =>
        recognition.Targets.Count(target =>
            string.Equals(target.Name, targetName, StringComparison.Ordinal) &&
            target.Confidence >= minimumConfidence);

    private static double CalculateIdentifierMatchScore(string normalizedText, string normalizedIdentifier)
    {
        if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(normalizedIdentifier))
            return 0;

        if (string.Equals(normalizedText, normalizedIdentifier, StringComparison.Ordinal))
            return 1.0;

        if (normalizedText.Contains(normalizedIdentifier, StringComparison.Ordinal))
            return 0.95;

        if (normalizedIdentifier.Length >= 6 && normalizedText.Length >= 6)
        {
            var compactText = normalizedText.Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal);
            var compactIdentifier = normalizedIdentifier.Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal);
            if (compactText.Contains(compactIdentifier, StringComparison.Ordinal))
                return 0.9;
        }

        if (normalizedText.Length >= 4 && normalizedIdentifier.Contains(normalizedText, StringComparison.Ordinal))
            return 0.75;

        var textDigits = ExtractAsciiDigits(normalizedText);
        var identifierDigits = ExtractAsciiDigits(normalizedIdentifier);
        if (textDigits.Length >= 8 && identifierDigits.Length >= 8)
        {
            var lengthDelta = Math.Abs(textDigits.Length - identifierDigits.Length);
            if (lengthDelta <= 3)
            {
                var distance = CalculateLevenshteinDistance(textDigits, identifierDigits);
                var similarity = 1.0 - distance / (double)Math.Max(textDigits.Length, identifierDigits.Length);
                if (similarity >= 0.82)
                    return Math.Min(0.9, similarity);
            }

            var subsequenceLength = CalculateLongestCommonSubsequenceLength(textDigits, identifierDigits);
            var subsequenceSimilarity = 2.0 * subsequenceLength / (textDigits.Length + identifierDigits.Length);
            if (subsequenceLength >= 8 && subsequenceSimilarity >= 0.80)
                return Math.Min(0.88, subsequenceSimilarity);
        }

        return 0;
    }

    private static string ExtractAsciiDigits(string value) =>
        string.Concat(value.Where(character => character is >= '0' and <= '9'));

    private static int CalculateLevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
            return right.Length;
        if (right.Length == 0)
            return left.Length;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
            previous[column] = column;

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static int CalculateLongestCommonSubsequenceLength(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return 0;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var row = 1; row <= left.Length; row++)
        {
            for (var column = 1; column <= right.Length; column++)
            {
                current[column] = left[row - 1] == right[column - 1]
                    ? previous[column - 1] + 1
                    : Math.Max(previous[column], current[column - 1]);
            }

            Array.Clear(previous);
            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private async Task<IReadOnlyList<LocalArchiveCardOcrMatch>> RecognizeCardRoisAsync(
        StablePageResult localArchive,
        IReadOnlyList<LocalArchiveCardCandidate> candidates,
        string runArchiveDisplayName,
        string runArchiveCode,
        string attemptName,
        CancellationToken cancellationToken,
        bool retryNameBand = false)
    {
        var matches = new List<LocalArchiveCardOcrMatch>();
        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            var roi = CreateCardRoiCapture(localArchive.Capture, candidate, retryNameBand);
            var roiScreenshotPath = SaveCardRoiScreenshot(roi.Capture,
                retryNameBand ? attemptName + "-name-retry" : attemptName, candidate.Source, candidate.Index);
            var rawTexts = await _cardTextRecognizer.RecognizeCardsAsync(roi.Capture, cancellationToken);
            var roiTexts = ArchiveOcrTextLayout.CardLines(rawTexts.Select(t => MapRoiResultToOriginal(t, roi.Roi)),
                candidate.NameRegion ?? ArchiveOcrTextLayout.Box(candidate.CardBox), localArchive.Capture.Bitmap.PixelWidth, localArchive.Capture.Bitmap.PixelHeight)
                .Select(result =>
                {
                    var mapped = result;
                    var score = RapidOcrLocalArchiveCardTextRecognizer.CalculateMatchScore(
                        mapped.Text,
                        runArchiveDisplayName,
                        runArchiveCode);
                    return mapped with
                    {
                        MatchedTargetName = score > 0 ? runArchiveDisplayName : null,
                        MatchScore = score
                    };
                })
                .ToList();

            var combinedText = string.Join(" | ", roiTexts.Select(result => result.Text));
            var bestScore = roiTexts.Count == 0 ? 0 : roiTexts.Max(result => result.MatchScore);
            LogWorkflow(null, $"step={attemptName} action=CardOcr candidate={candidate.Source}/{candidate.Index} " +
                $"nameBandRetry={retryNameBand} text=\"{combinedText}\" score={bestScore:F2}");

            matches.Add(new LocalArchiveCardOcrMatch(
                candidate,
                roiScreenshotPath,
                combinedText,
                RapidOcrLocalArchiveCardTextRecognizer.NormalizeForMatch(combinedText),
                bestScore,
                roiTexts));
        }

        // Retry the name band on the same frame before scrolling a potentially visible card away.
        // Keep the existing run-code matching rules; a missing character is never guessed.
        if (!retryNameBand && matches.Count > 0 && matches.All(match => match.MatchScore < 0.7))
        {
            LogWorkflow(null, $"step={attemptName} action=RetryCardNameBandBeforeScroll candidates={matches.Count}");
            var retried = await RecognizeCardRoisAsync(localArchive, candidates, runArchiveDisplayName,
                runArchiveCode, attemptName, cancellationToken, retryNameBand: true);
            return matches.Concat(retried).ToList();
        }

        return matches;
    }

    private CardRoiCapture CreateCardRoiCapture(GameCaptureResult source, LocalArchiveCardCandidate candidate,
        bool nameBandOnly = false)
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
        if (nameBandOnly)
            rect.Height = Math.Max(1, (int)Math.Round(rect.Height * 0.64));
        var cropped = new CroppedBitmap(bitmap, rect);
        cropped.Freeze();
        BitmapSource roiBitmap = cropped;

        var roiScale = GetRoiScale();


        var roi = new NormalizedPixelRoi(x, y, rect.Width, rect.Height, width, height, roiScale);
        var capture = new GameCaptureResult(
            source.Window,
            roiBitmap,
            source.CaptureMethod + " / local_archive_card ROI",
            source.GdiQuality,
            source.PrintWindowQuality,
            source.DxgiQuality,
            source.Warning,
            source.Strategy,
            source.CandidateQualities,
            source.IsLikelyBlackFrame,
            source.FallbackToGdi);
        return new CardRoiCapture(capture, roi);
    }

    private CardRoiCapture CreateBoxRoiCapture(GameCaptureResult source, NormalizedBox box, string captureMethodSuffix)
    {
        var bitmap = CreateFrozenBitmapCopy(source.Bitmap);
        var width = Math.Max(1, bitmap.PixelWidth);
        var height = Math.Max(1, bitmap.PixelHeight);
        var x = Math.Clamp((int)Math.Floor((box.CenterX - box.Width / 2) * width), 0, width - 1);
        var y = Math.Clamp((int)Math.Floor((box.CenterY - box.Height / 2) * height), 0, height - 1);
        var x2 = Math.Clamp((int)Math.Ceiling((box.CenterX + box.Width / 2) * width), x + 1, width);
        var y2 = Math.Clamp((int)Math.Ceiling((box.CenterY + box.Height / 2) * height), y + 1, height);
        var rect = new Int32Rect(x, y, x2 - x, y2 - y);
        var cropped = new CroppedBitmap(bitmap, rect);
        cropped.Freeze();
        BitmapSource roiBitmap = cropped;

        var roiScale = GetRoiScale();


        var roi = new NormalizedPixelRoi(x, y, rect.Width, rect.Height, width, height, roiScale);
        var capture = new GameCaptureResult(
            source.Window,
            roiBitmap,
            source.CaptureMethod + " / " + captureMethodSuffix,
            source.GdiQuality,
            source.PrintWindowQuality,
            source.DxgiQuality,
            source.Warning,
            source.Strategy,
            source.CandidateQualities,
            source.IsLikelyBlackFrame,
            source.FallbackToGdi);
        return new CardRoiCapture(capture, roi);
    }

    private static int GetRoiScale() => 1;

    private static BitmapSource CreateFrozenBitmapCopy(BitmapSource source)
    {
        var copy = new WriteableBitmap(source);
        copy.Freeze();
        return copy;
    }

    private string? SaveCardRoiScreenshot(GameCaptureResult roiCapture, string attemptName, string source, int index)
    {
        var diagnostics = _activeDiagnostics;
        if (diagnostics == null)
            return null;

        Directory.CreateDirectory(diagnostics.DiagnosticsDirectory);
        var path = Path.Combine(
            diagnostics.DiagnosticsDirectory,
            $"stage1-local-archive-card-roi-{diagnostics.RunId}-{attemptName}-{source}-{index:00}-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
        GameWindowCaptureService.SavePng(roiCapture.Bitmap, path);
        return path;
    }

    private static LocalArchiveCardTextResult MapRoiResultToOriginal(
        LocalArchiveCardTextResult result,
        NormalizedPixelRoi roi)
    {
        var box = result.NormalizedBox;
        var centerX = (roi.X + box.CenterX * roi.Width) / roi.SourceWidth;
        var centerY = (roi.Y + box.CenterY * roi.Height) / roi.SourceHeight;
        var width = box.Width * roi.Width / roi.SourceWidth;
        var height = box.Height * roi.Height / roi.SourceHeight;
        return result with
        {
            NormalizedBox = new NormalizedBox(
                Math.Clamp(centerX, 0, 1),
                Math.Clamp(centerY, 0, 1),
                Math.Clamp(width, 0, 1),
                Math.Clamp(height, 0, 1))
        };
    }

    private static AutomationTarget? FindMoreButtonForCard(
        PageRecognitionResult recognition,
        LocalArchiveCardCandidate candidate)
    {
        var card = candidate.CardBox;
        var actionRoi = OpenCvLocalArchiveMoreButtonLocator.GetLocalArchiveMoreButtonSearchRoi(candidate);
        var candidates = FindTargets(recognition, WorkflowTargetNames.LocalArchiveMoreButton)
            .Where(button => IsInsideCardLeftBottomActionArea(button, candidate))
            .OrderBy(button => DistanceSquared(button.CenterX, button.CenterY, actionRoi.CenterX, actionRoi.CenterY))
            .ThenByDescending(button => button.Confidence)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0];
    }

    private static (double X, double Y, string Source) GetLocalArchiveCardTextHoverPoint(LocalArchiveCardOcrMatch match)
    {
        var bestTextBox = match.OcrResults
            .Where(result => result.MatchScore > 0)
            .OrderByDescending(result => result.MatchScore)
            .ThenByDescending(result => result.Confidence)
            .Select(result => result.NormalizedBox)
            .FirstOrDefault();
        if (bestTextBox != null)
            return (bestTextBox.CenterX, bestTextBox.CenterY, "ocr_best_match_text_box");

        var textBoxes = match.OcrResults
            .Where(result => !string.IsNullOrWhiteSpace(result.Text))
            .Select(result => result.NormalizedBox)
            .ToList();
        if (textBoxes.Count > 0)
        {
            var left = textBoxes.Min(box => box.CenterX - box.Width / 2);
            var top = textBoxes.Min(box => box.CenterY - box.Height / 2);
            var right = textBoxes.Max(box => box.CenterX + box.Width / 2);
            var bottom = textBoxes.Max(box => box.CenterY + box.Height / 2);
            return (
                Math.Clamp((left + right) / 2, 0, 1),
                Math.Clamp((top + bottom) / 2, 0, 1),
                "ocr_text_union");
        }

        if (match.Candidate.NameRegion is { } nameRegion)
            return (nameRegion.CenterX, nameRegion.CenterY, "candidate_name_region");

        return (match.Candidate.CardBox.CenterX, match.Candidate.CardBox.CenterY, "candidate_card_center");
    }

    private static bool IsInsideCardLeftBottomActionArea(AutomationTarget button, LocalArchiveCardCandidate candidate)
    {
        var roi = OpenCvLocalArchiveMoreButtonLocator.GetLocalArchiveMoreButtonSearchRoi(candidate);
        const double padding = 0.012;
        var left = roi.CenterX - roi.Width / 2 - padding;
        var right = roi.CenterX + roi.Width / 2 + padding;
        var top = roi.CenterY - roi.Height / 2 - padding;
        var bottom = roi.CenterY + roi.Height / 2 + padding;

        return button.CenterX >= left &&
               button.CenterX <= right &&
               button.CenterY >= top &&
               button.CenterY <= bottom;
    }

    private static string FormatNormalizedBox(NormalizedBox box) =>
        $"cx={box.CenterX:F3} cy={box.CenterY:F3} w={box.Width:F3} h={box.Height:F3}";

    private static string BuildMoreButtonSelectionFailureReason(
        IReadOnlyList<AutomationTarget> buttons,
        AutomationTarget card)
    {
        if (buttons.Count == 0)
            return "No local_archive_more_button targets were detected after hovering the OCR-confirmed card.";

        return "Detected local_archive_more_button targets did not fall inside the OCR-confirmed card action area. " +
               $"targetCard=({FormatTarget(card)}), buttons=[{string.Join("; ", buttons.Select(FormatTarget))}]";
    }

    private static string FormatTarget(AutomationTarget target) =>
        $"{target.Name} conf={target.Confidence:F2} cx={target.CenterX:F3} cy={target.CenterY:F3} w={target.Width:F3} h={target.Height:F3}";

    private static AutomationTarget? FindLoginStartGameButton(PageRecognitionResult recognition) =>
        recognition.Targets
            .Where(target =>
                string.Equals(target.Name, WorkflowTargetNames.LoginStartGameButton, StringComparison.Ordinal) &&
                target.Confidence >= 0.3 &&
                target.Width is >= 0.08 and <= 0.45 &&
                target.Height is >= 0.04 and <= 0.25 &&
                target.CenterX is >= 0.25 and <= 0.75 &&
                target.CenterY is >= 0.42 and <= 0.62)
            .OrderByDescending(target => target.Confidence)
            .FirstOrDefault();

    private static bool HasTarget(PageRecognitionResult recognition, string targetName, double minimumConfidence) =>
        recognition.Targets.Any(target =>
            string.Equals(target.Name, targetName, StringComparison.Ordinal) &&
            target.Confidence >= minimumConfidence);

    private static string FormatOptionalTarget(AutomationTarget? target) =>
        target == null ? "<not-detected>" : FormatTarget(target);

    private static string FormatTargets(IReadOnlyList<AutomationTarget> targets) =>
        targets.Count == 0
            ? "<none>"
            : string.Join("; ", targets
                .OrderByDescending(target => target.Confidence)
                .Take(20)
                .Select(FormatTarget));

    private static string FormatOcrTexts(IReadOnlyList<WorkflowOcrTextResult> texts) =>
        texts.Count == 0
            ? "<none>"
            : string.Join(" | ", texts
                .Take(12)
                .Select(text => $"{text.Text}->{text.NormalizedText}"));

    private static string CreateOcrChangeSignature(IReadOnlyList<WorkflowOcrTextResult> texts) =>
        string.Join("|", texts
            .Select(text => text.NormalizedText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(text => text, StringComparer.Ordinal));

    private static long GetFileLengthOrMinusOne(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return -1;

        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static bool BoxCenterInside(NormalizedBox box, AutomationTarget target, double padding) =>
        box.CenterX >= target.CenterX - target.Width / 2 - padding &&
        box.CenterX <= target.CenterX + target.Width / 2 + padding &&
        box.CenterY >= target.CenterY - target.Height / 2 - padding &&
        box.CenterY <= target.CenterY + target.Height / 2 + padding;

    private static bool BoxCenterInside(AutomationTarget box, AutomationTarget target, double padding) =>
        box.CenterX >= target.CenterX - target.Width / 2 - padding &&
        box.CenterX <= target.CenterX + target.Width / 2 + padding &&
        box.CenterY >= target.CenterY - target.Height / 2 - padding &&
        box.CenterY <= target.CenterY + target.Height / 2 + padding;

    private static bool BoxesOverlap(AutomationTarget left, NormalizedBox right, double padding)
    {
        var leftX1 = left.CenterX - left.Width / 2 - padding;
        var leftY1 = left.CenterY - left.Height / 2 - padding;
        var leftX2 = left.CenterX + left.Width / 2 + padding;
        var leftY2 = left.CenterY + left.Height / 2 + padding;
        var rightX1 = right.CenterX - right.Width / 2;
        var rightY1 = right.CenterY - right.Height / 2;
        var rightX2 = right.CenterX + right.Width / 2;
        var rightY2 = right.CenterY + right.Height / 2;
        return leftX1 <= rightX2 && leftX2 >= rightX1 && leftY1 <= rightY2 && leftY2 >= rightY1;
    }

    private static List<LocalArchiveCardOcrMatch> DeduplicateCardMatches(
        IReadOnlyList<LocalArchiveCardOcrMatch> matches)
    {
        var deduplicated = new List<LocalArchiveCardOcrMatch>();
        foreach (var match in matches.OrderByDescending(match => match.MatchScore))
        {
            if (deduplicated.Any(existing => IsSameCard(existing.Candidate.CardBox, match.Candidate.CardBox)))
                continue;

            deduplicated.Add(match);
        }

        return deduplicated;
    }

    private static bool IsSameCard(AutomationTarget first, AutomationTarget second)
    {
        var distance = Math.Sqrt(DistanceSquared(first.CenterX, first.CenterY, second.CenterX, second.CenterY));
        if (distance < 0.05 && CalculateIoU(first, second) > 0.3)
            return true;

        return BoxCenterInside(first, second, padding: 0) || BoxCenterInside(second, first, padding: 0);
    }

    private static double CalculateIoU(AutomationTarget first, AutomationTarget second)
    {
        var firstLeft = first.CenterX - first.Width / 2;
        var firstTop = first.CenterY - first.Height / 2;
        var firstRight = first.CenterX + first.Width / 2;
        var firstBottom = first.CenterY + first.Height / 2;
        var secondLeft = second.CenterX - second.Width / 2;
        var secondTop = second.CenterY - second.Height / 2;
        var secondRight = second.CenterX + second.Width / 2;
        var secondBottom = second.CenterY + second.Height / 2;

        var overlapWidth = Math.Max(0, Math.Min(firstRight, secondRight) - Math.Max(firstLeft, secondLeft));
        var overlapHeight = Math.Max(0, Math.Min(firstBottom, secondBottom) - Math.Max(firstTop, secondTop));
        var overlap = overlapWidth * overlapHeight;
        if (overlap <= 0)
            return 0;

        var union = first.Width * first.Height + second.Width * second.Height - overlap;
        return union <= 0 ? 0 : overlap / union;
    }

    private static double DistanceSquared(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return dx * dx + dy * dy;
    }

    private string? SavePreviewInfoDiagnostic(
        StablePageResult? result,
        string prefix,
        UploadRequest request,
        string step,
        string expectedAction,
        IReadOnlyList<WorkflowOcrTextResult> ocrCandidates,
        IReadOnlyList<string> workflowLog,
        string failureReason,
        bool isFinalFailure = true,
        bool recoverable = false,
        string? phase = null,
        bool updateLatest = true)
    {
        if (result == null)
            return null;

        var diagnostics = _activeDiagnostics ?? Stage1RunDiagnostics.Create();
        Directory.CreateDirectory(diagnostics.DiagnosticsDirectory);

        var baseName = $"{prefix}-{diagnostics.RunId}-{DateTime.Now:yyyyMMdd-HHmmssfff}-{result.Recognition.Page}";
        var pngPath = Path.Combine(diagnostics.DiagnosticsDirectory, baseName + ".png");
        var jsonPath = Path.Combine(diagnostics.DiagnosticsDirectory, baseName + ".json");
        GameWindowCaptureService.SavePng(result.Capture.Bitmap, pngPath);

        var report = new Stage1PreviewInfoDiagnosticReport(
            DateTimeOffset.Now,
            diagnostics.RunId,
            step,
            isFinalFailure,
            recoverable,
            phase,
            expectedAction,
            failureReason,
            AppContext.BaseDirectory,
            diagnostics.DiagnosticsDirectory,
            request.Archive.Id,
            request.Archive.LevelName,
            request.IsUpdate,
            request.IsUpdate,
            _workflowOcr.EngineInfo,
            result.Recognition.Page,
            result.Recognition.Confidence,
            result.Recognition.Source,
            result.Capture.Strategy,
            result.Capture.CaptureMethod,
            result.Capture.Warning,
            SafeGetForegroundDiagnostic(result.Capture.Window),
            result.Recognition.Targets
                .OrderByDescending(target => target.Confidence)
                .Select(ToDiagnosticTarget)
                .ToList(),
            ocrCandidates,
            workflowLog.ToList(),
            pngPath);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, options));

        if (updateLatest)
        {
            diagnostics.LatestPngPath = pngPath;
            diagnostics.LatestJsonPath = jsonPath;
        }
        return pngPath;
    }

    private string? SaveMultilanguageDiagnostic(
        StablePageResult? result,
        string prefix,
        UploadRequest request,
        Stage1MultilanguageContext context,
        IReadOnlyList<string> workflowLog,
        string failureReason)
    {
        if (result == null)
            return null;

        var diagnostics = _activeDiagnostics ?? Stage1RunDiagnostics.Create();
        Directory.CreateDirectory(diagnostics.DiagnosticsDirectory);

        var baseName = $"{prefix}-{diagnostics.RunId}-{DateTime.Now:yyyyMMdd-HHmmssfff}-{result.Recognition.Page}";
        var pngPath = Path.Combine(diagnostics.DiagnosticsDirectory, baseName + ".png");
        var jsonPath = Path.Combine(diagnostics.DiagnosticsDirectory, baseName + ".json");
        GameWindowCaptureService.SavePng(result.Capture.Bitmap, pngPath);

        var report = new Stage1MultilanguageDiagnosticReport(
            DateTimeOffset.Now,
            diagnostics.RunId,
            prefix,
            failureReason,
            AppContext.BaseDirectory,
            diagnostics.DiagnosticsDirectory,
            request.Archive.Id,
            request.Archive.LevelName,
            request.IsUpdate,
            context.ClientLanguage?.ToString() ?? "<unknown>",
            context.DesiredLanguages,
            context.DefaultLanguage,
            context.SelectedLanguagesBefore,
            context.MissingLanguages,
            context.SelectedLanguagesAfter,
            context.SkippedDefaultLanguages,
            context.FillTargetLanguages,
            context.AddLanguageLayout,
            context.LanguageSelectionDiagnostics,
            context.TableLanguageOrder,
            context.ComputedVisibleRows,
            context.TableGeometry,
            context.EarlyAccessColumnEnabled,
            context.DetectedTableHeaders,
            context.TableTextEditIcons,
            context.TableRowClusters,
            context.TableColumnClusters,
            context.CellEdits,
            context.PageContextEvidence,
            context.AddLanguageDialogContextEvidence,
            _workflowOcr.EngineInfo,
            context.OcrTexts,
            result.Recognition.Page,
            result.Recognition.Confidence,
            result.Recognition.Source,
            result.Capture.Strategy,
            result.Capture.CaptureMethod,
            result.Capture.Warning,
            SafeGetForegroundDiagnostic(result.Capture.Window),
            result.Recognition.Targets
                .OrderByDescending(target => target.Confidence)
                .Select(ToDiagnosticTarget)
                .ToList(),
            workflowLog.ToList(),
            pngPath);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, options));

        diagnostics.LatestPngPath = pngPath;
        diagnostics.LatestJsonPath = jsonPath;
        return pngPath;
    }

    private string? SaveAddLanguageClickDiagnostic(
        StablePageResult? result,
        UploadRequest request,
        Stage1MultilanguageContext context,
        IReadOnlyList<string> workflowLog,
        Stage1AddLanguageClickDiagnostic clickDiagnostic)
    {
        if (result == null)
            return null;

        var diagnostics = _activeDiagnostics ?? Stage1RunDiagnostics.Create();
        Directory.CreateDirectory(diagnostics.DiagnosticsDirectory);

        const string prefix = "stage1-multilanguage-add-language-click-failed";
        var baseName = $"{prefix}-{diagnostics.RunId}-{DateTime.Now:yyyyMMdd-HHmmssfff}-{result.Recognition.Page}";
        var pngPath = Path.Combine(diagnostics.DiagnosticsDirectory, baseName + ".png");
        var jsonPath = Path.Combine(diagnostics.DiagnosticsDirectory, baseName + ".json");
        GameWindowCaptureService.SavePng(result.Capture.Bitmap, pngPath);

        var report = new
        {
            CreatedAt = DateTimeOffset.Now,
            diagnostics.RunId,
            Prefix = prefix,
            clickDiagnostic.ClientLanguage,
            clickDiagnostic.AttemptCount,
            clickDiagnostic.InitialAddButton,
            clickDiagnostic.LastAddButton,
            clickDiagnostic.LastRawPage,
            clickDiagnostic.LastContextualPage,
            LastRecognitionTargets = result.Recognition.Targets
                .OrderByDescending(target => target.Confidence)
                .Select(ToDiagnosticTarget)
                .ToList(),
            clickDiagnostic.ForegroundBeforeClick,
            clickDiagnostic.ForegroundAfterClick,
            WorkflowLog = workflowLog.ToList(),
            clickDiagnostic.FailureReason,
            AppBaseDirectory = AppContext.BaseDirectory,
            diagnostics.DiagnosticsDirectory,
            ArchiveId = request.Archive.Id,
            ArchiveName = request.Archive.LevelName,
            request.IsUpdate,
            OcrEngineInfo = _workflowOcr.EngineInfo,
            Context = new
            {
                ClientLanguage = context.ClientLanguage?.ToString() ?? "<unknown>",
                context.DesiredLanguages,
                context.SelectedLanguagesBefore,
                context.MissingLanguages,
                context.PageContextEvidence,
                context.AddLanguageDialogContextEvidence
            },
            LastRecognizedPage = result.Recognition.Page,
            result.Recognition.Confidence,
            result.Recognition.Source,
            CaptureStrategy = result.Capture.Strategy,
            result.Capture.CaptureMethod,
            result.Capture.Warning,
            ForegroundWindowDiagnostic = SafeGetForegroundDiagnostic(result.Capture.Window),
            ScreenshotPath = pngPath
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, options));

        diagnostics.LatestPngPath = pngPath;
        diagnostics.LatestJsonPath = jsonPath;
        return pngPath;
    }

    private string? SaveLocalArchiveCardDiagnostic(
        StablePageResult? result,
        string prefix,
        string runArchiveDisplayName,
        string runArchiveCode,
        string attemptName,
        IReadOnlyList<LocalArchiveCardOcrMatch> cardOcrMatches,
        LocalArchiveCardOcrMatch? selectedCard,
        bool detectedLocalArchiveCard,
        bool detectedLocalArchiveMoreButton,
        bool usedOcrConfirmedCardMatching,
        double? hoverOrClickX,
        double? hoverOrClickY,
        string failureReason,
        bool clearedHover = false,
        double? clearHoverX = null,
        double? clearHoverY = null,
        int ignoredPreExistingMoreButtons = 0,
        string? moreButtonSelectionReason = null,
        AutomationTarget? selectedMoreButton = null,
        IReadOnlyList<AutomationTarget>? moreButtonsBeforeHover = null,
        IReadOnlyList<AutomationTarget>? moreButtonsAfterHover = null,
        bool usedCardGeometryFallbackClick = false,
        double? fallbackClickX = null,
        double? fallbackClickY = null,
        bool yoloMoreButtonMatched = false,
        IReadOnlyList<AutomationTarget>? rejectedMoreButtonCandidates = null,
        bool openCvMoreButtonAttempted = false,
        bool openCvMoreButtonMatched = false,
        LocalArchiveMoreButtonLocationResult? openCvMoreButtonResult = null)
    {
        if (result == null)
            return null;

        var diagnostics = _activeDiagnostics ?? Stage1RunDiagnostics.Create();
        Directory.CreateDirectory(diagnostics.DiagnosticsDirectory);

        var baseName = $"{prefix}-{diagnostics.RunId}-{DateTime.Now:yyyyMMdd-HHmmssfff}-{result.Recognition.Page}";
        var pngPath = Path.Combine(diagnostics.DiagnosticsDirectory, baseName + ".png");
        var jsonPath = Path.Combine(diagnostics.DiagnosticsDirectory, baseName + ".json");
        GameWindowCaptureService.SavePng(result.Capture.Bitmap, pngPath);

        var foreground = _activation.GetForegroundWindowInfo(result.Capture.Window).ToDiagnosticString();
        var report = new Stage1LocalArchiveCardDiagnosticReport(
            DateTimeOffset.Now,
            diagnostics.RunId,
            attemptName,
            runArchiveDisplayName,
            runArchiveCode,
            _cardTextRecognizer.EngineInfo,
            GetOcrModelDirectory(),
            GetRoiScale(),
            AppContext.BaseDirectory,
            diagnostics.DiagnosticsDirectory,
            result.Recognition.Page,
            result.Recognition.Confidence,
            result.Recognition.Source,
            result.Capture.Strategy,
            result.Capture.CaptureMethod,
            result.Capture.Warning,
            result.Capture.CandidateQualities ?? [],
            result.Capture.IsLikelyBlackFrame,
            result.Capture.FallbackToGdi,
            foreground,
            cardOcrMatches.Count == 0 ? "<none>" : string.Join("+", cardOcrMatches.Select(match => match.Candidate.Source).Distinct(StringComparer.Ordinal)),
            cardOcrMatches.Count,
            cardOcrMatches.Any(match => match.Candidate.IsSynthetic),
            cardOcrMatches.Select(match => ToCandidateDiagnostic(match.Candidate)).ToList(),
            detectedLocalArchiveCard,
            detectedLocalArchiveMoreButton,
            usedOcrConfirmedCardMatching,
            hoverOrClickX,
            hoverOrClickY,
            clearedHover,
            clearHoverX,
            clearHoverY,
            ignoredPreExistingMoreButtons,
            moreButtonSelectionReason,
            selectedMoreButton == null ? null : ToDiagnosticTarget(selectedMoreButton),
            (moreButtonsBeforeHover ?? []).Select(ToDiagnosticTarget).ToList(),
            (moreButtonsAfterHover ?? []).Select(ToDiagnosticTarget).ToList(),
            usedCardGeometryFallbackClick,
            fallbackClickX,
            fallbackClickY,
            yoloMoreButtonMatched,
            (rejectedMoreButtonCandidates ?? []).Select(ToDiagnosticTarget).ToList(),
            openCvMoreButtonAttempted,
            openCvMoreButtonMatched,
            openCvMoreButtonResult?.Confidence,
            openCvMoreButtonResult?.SearchRoi,
            openCvMoreButtonResult?.Target?.CenterX,
            openCvMoreButtonResult?.Target?.CenterY,
            openCvMoreButtonResult?.RejectedReason,
            failureReason,
            cardOcrMatches
                .Select(match => new LocalArchiveCardOcrMatchDiagnostic(
                    ToCandidateDiagnostic(match.Candidate),
                    match.RoiScreenshotPath,
                    match.CombinedText,
                    match.NormalizedText,
                    match.MatchScore,
                    match.OcrResults))
                .ToList(),
            selectedCard == null ? null : ToCandidateDiagnostic(selectedCard.Candidate),
            FindTargets(result.Recognition, WorkflowTargetNames.LocalArchiveMoreButton).Select(ToDiagnosticTarget).ToList(),
            result.Recognition.Targets
                .OrderByDescending(target => target.Confidence)
                .Select(ToDiagnosticTarget)
                .ToList(),
            pngPath);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, options));

        diagnostics.LatestPngPath = pngPath;
        diagnostics.LatestJsonPath = jsonPath;
        return pngPath;
    }

    private static Stage1UploadDiagnosticTarget ToDiagnosticTarget(AutomationTarget target) =>
        new(
            target.Name,
            target.Confidence,
            target.CenterX,
            target.CenterY,
            target.Width,
            target.Height);

    private static LocalArchiveCardCandidateDiagnostic ToCandidateDiagnostic(LocalArchiveCardCandidate candidate) =>
        new(
            ToDiagnosticTarget(candidate.CardBox),
            candidate.Source,
            candidate.Index,
            candidate.NameRegion,
            candidate.IsSynthetic);

    private string? GetOcrModelDirectory() =>
        _cardTextRecognizer is RapidOcrLocalArchiveCardTextRecognizer rapid
            ? rapid.ModelDirectory
            : null;

    private string? SaveDiagnostic(
        StablePageResult? result,
        string prefix,
        bool isFinalFailure = true,
        bool recoverable = false,
        string? phase = null,
        bool updateLatest = true)
    {
        if (result == null)
            return null;

        var diagnostics = _activeDiagnostics ?? Stage1RunDiagnostics.Create();
        Directory.CreateDirectory(diagnostics.DiagnosticsDirectory);

        var baseName = $"{prefix}-{diagnostics.RunId}-{DateTime.Now:yyyyMMdd-HHmmssfff}-{result.Recognition.Page}";
        var pngPath = Path.Combine(diagnostics.DiagnosticsDirectory, baseName + ".png");
        var jsonPath = Path.Combine(diagnostics.DiagnosticsDirectory, baseName + ".json");
        GameWindowCaptureService.SavePng(result.Capture.Bitmap, pngPath);

        var foreground = _activation.GetForegroundWindowInfo(result.Capture.Window).ToDiagnosticString();
        var report = new Stage1UploadDiagnosticReport(
            DateTimeOffset.Now,
            diagnostics.RunId,
            prefix,
            isFinalFailure,
            recoverable,
            phase,
            AppContext.BaseDirectory,
            diagnostics.DiagnosticsDirectory,
            diagnostics.ProcessId,
            diagnostics.IsElevated,
            result.Recognition.Page,
            result.Recognition.Confidence,
            result.Recognition.Source,
            result.Capture.Strategy,
            result.Capture.CaptureMethod,
            result.Capture.Warning,
            result.Capture.CandidateQualities ?? [],
            result.Capture.IsLikelyBlackFrame,
            result.Capture.FallbackToGdi,
            foreground,
            result.Recognition.Targets
                .OrderByDescending(target => target.Confidence)
                .Take(20)
                .Select(target => new Stage1UploadDiagnosticTarget(
                    target.Name,
                    target.Confidence,
                    target.CenterX,
                    target.CenterY,
                    target.Width,
                    target.Height))
                .ToList(),
            diagnostics.WorkflowLog.ToList(),
            pngPath);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, options));

        if (updateLatest)
        {
            diagnostics.LatestPngPath = pngPath;
            diagnostics.LatestJsonPath = jsonPath;
        }
        return pngPath;
    }

    private static string FormatStage1FailureMessage(
        Stage1RunDiagnostics diagnostics,
        UploadServer server,
        UploadRunArchive? runArchive,
        string error,
        IReadOnlyCollection<string> workflowLog)
    {
        var runArchiveText = runArchive == null ? "<not-created>" : runArchive.RunArchiveDisplayName;
        return
            $"Stage1 upload failed. RunId={diagnostics.RunId}, server={server}, runArchive={runArchiveText}, " +
            $"error={error}, LatestDiagnosticPng={diagnostics.LatestPngPath ?? "<none>"}, " +
            $"LatestDiagnosticJson={diagnostics.LatestJsonPath ?? "<none>"}, " +
            $"AppContext.BaseDirectory={diagnostics.AppBaseDirectory}, DiagnosticsDirectory={diagnostics.DiagnosticsDirectory}. " +
            FormatWorkflowLog(workflowLog);
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string FormatPages(IEnumerable<GamePage> pages) =>
        string.Join(", ", pages.OrderBy(page => page.ToString(), StringComparer.Ordinal));

    private static IReadOnlySet<GamePage> Pages(params GamePage[] pages) =>
        new HashSet<GamePage>(pages);

    private static IReadOnlySet<GamePage> GetImmediateUploadResultPages(bool allowUploadLocationDialog) =>
        allowUploadLocationDialog
            ? Pages(GamePage.UploadLocationDialog, GamePage.UploadFailed, GamePage.UploadedArchive)
            : Pages(GamePage.UploadFailed, GamePage.UploadedArchive);

    private static string FormatWorkflowLog(IReadOnlyCollection<string> workflowLog) =>
        workflowLog.Count == 0
            ? string.Empty
            : $"Workflow log: {string.Join(" | ", workflowLog)}";

    private static string NormalizeFileNameSegment(string value)
    {
        var sanitized = new string(value
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "step" : sanitized.ToLowerInvariant();
    }

    private string SafeGetForegroundDiagnostic(GameWindowInfo? window)
    {
        if (window == null)
            return "<unavailable: missing game window>";

        try
        {
            return _activation.GetForegroundWindowInfo(window).ToDiagnosticString();
        }
        catch (Exception ex)
        {
            return $"<unavailable: {ex.GetType().Name}: {ex.Message}>";
        }
    }

    private sealed class Stage1RunDiagnostics
    {
        private Stage1RunDiagnostics(
            string runId,
            string appBaseDirectory,
            string diagnosticsDirectory,
            int processId,
            bool isElevated)
        {
            RunId = runId;
            AppBaseDirectory = appBaseDirectory;
            DiagnosticsDirectory = diagnosticsDirectory;
            ProcessId = processId;
            IsElevated = isElevated;
        }

        public string RunId { get; }
        public string AppBaseDirectory { get; }
        public string DiagnosticsDirectory { get; }
        public int ProcessId { get; }
        public bool IsElevated { get; }
        public string? LatestPngPath { get; set; }
        public string? LatestJsonPath { get; set; }
        public IReadOnlyCollection<string> WorkflowLog { get; set; } = [];

        public static Stage1RunDiagnostics Create()
        {
            var runId = $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}"[..28];
            var appBaseDirectory = AppContext.BaseDirectory;
            var diagnosticsDirectory = ApplicationPaths.DiagnosticsDirectory;
            return new Stage1RunDiagnostics(
                runId,
                appBaseDirectory,
                diagnosticsDirectory,
                Environment.ProcessId,
                IsProcessElevated());
        }
    }

    private sealed record Stage1UploadDiagnosticReport(
        DateTimeOffset CreatedAt,
        string RunId,
        string Prefix,
        bool IsFinalFailure,
        bool Recoverable,
        string? Phase,
        string AppBaseDirectory,
        string DiagnosticsDirectory,
        int ProcessId,
        bool IsElevated,
        GamePage LastRecognizedPage,
        double Confidence,
        string Source,
        GameCaptureStrategy CaptureStrategy,
        string CaptureMethod,
        string? CaptureWarning,
        IReadOnlyList<GameCaptureCandidateQuality> CaptureCandidates,
        bool IsLikelyBlackFrame,
        bool FallbackToGdi,
        string ForegroundWindowDiagnostic,
        IReadOnlyList<Stage1UploadDiagnosticTarget> TopTargets,
        IReadOnlyList<string> WorkflowLog,
        string ScreenshotPath);

    private sealed record Stage1UploadDiagnosticTarget(
        string Name,
        double Confidence,
        double CenterX,
        double CenterY,
        double Width,
        double Height);

    private sealed record Stage1PreviewInfoDiagnosticReport(
        DateTimeOffset CreatedAt,
        string RunId,
        string Step,
        bool IsFinalFailure,
        bool Recoverable,
        string? Phase,
        string ExpectedAction,
        string FailureReason,
        string AppBaseDirectory,
        string DiagnosticsDirectory,
        Guid ArchiveId,
        string ArchiveName,
        bool IsUpdate,
        bool SkippedTags,
        string OcrEngineInfo,
        GamePage LastRecognizedPage,
        double Confidence,
        string Source,
        GameCaptureStrategy CaptureStrategy,
        string CaptureMethod,
        string? CaptureWarning,
        string ForegroundWindowDiagnostic,
        IReadOnlyList<Stage1UploadDiagnosticTarget> Targets,
        IReadOnlyList<WorkflowOcrTextResult> OcrCandidateTexts,
        IReadOnlyList<string> WorkflowLog,
        string ScreenshotPath);

    private sealed record Stage1MultilanguageDiagnosticReport(
        DateTimeOffset CreatedAt,
        string RunId,
        string Prefix,
        string FailureReason,
        string AppBaseDirectory,
        string DiagnosticsDirectory,
        Guid ArchiveId,
        string ArchiveName,
        bool IsUpdate,
        string ClientLanguage,
        IReadOnlyList<string> DesiredLanguages,
        string DefaultLanguage,
        IReadOnlyList<string> SelectedLanguagesBefore,
        IReadOnlyList<string> MissingLanguages,
        IReadOnlyList<string> SelectedLanguagesAfter,
        IReadOnlyList<string> SkippedDefaultLanguages,
        IReadOnlyList<string> FillTargetLanguages,
        IReadOnlyList<string> AddLanguageLayout,
        IReadOnlyList<Stage1LanguageSelectionDiagnostic> LanguageSelectionDiagnostics,
        IReadOnlyList<string> TableLanguageOrder,
        IReadOnlyList<Stage1MultilanguageComputedRow> ComputedVisibleRows,
        Stage1MultilanguageTableGeometry? TableGeometry,
        bool EarlyAccessColumnEnabled,
        IReadOnlyList<WorkflowOcrTextResult> DetectedTableHeaders,
        IReadOnlyList<Stage1MultilanguageTableIconDiagnostic> TableTextEditIcons,
        IReadOnlyList<Stage1MultilanguageTableCluster> TableRowClusters,
        IReadOnlyList<Stage1MultilanguageTableCluster> TableColumnClusters,
        IReadOnlyList<Stage1MultilanguageCellEditDiagnostic> CellEdits,
        IReadOnlyList<Stage1MultilanguagePageContextEvidence> PageContextEvidence,
        IReadOnlyList<Stage1AddLanguageDialogContextEvidence> AddLanguageDialogContextEvidence,
        string OcrEngineInfo,
        IReadOnlyList<WorkflowOcrTextResult> OcrTexts,
        GamePage LastRecognizedPage,
        double Confidence,
        string Source,
        GameCaptureStrategy CaptureStrategy,
        string CaptureMethod,
        string? CaptureWarning,
        string ForegroundWindowDiagnostic,
        IReadOnlyList<Stage1UploadDiagnosticTarget> Targets,
        IReadOnlyList<string> WorkflowLog,
        string ScreenshotPath);

    private sealed record RunArchiveCardReadyResult(
        StablePageResult LocalArchive,
        LocalArchiveCardCandidate Candidate,
        AutomationTarget? MoreButton,
        bool OpenCvMoreButtonMatched,
        LocalArchiveCardOcrMatch OcrMatch,
        IReadOnlyList<LocalArchiveCardOcrMatch> CardOcrMatches,
        IReadOnlyList<AutomationTarget> MoreButtonsBeforeHover,
        IReadOnlyList<AutomationTarget> MoreButtonsAfterHover,
        int IgnoredPreExistingMoreButtons,
        string? MoreButtonSelectionReason,
        bool DetectedLocalArchiveCard,
        bool OpenCvMoreButtonAttempted,
        LocalArchiveMoreButtonLocationResult? OpenCvMoreButtonResult);

    private sealed record SandboxReadyRefocusResult(
        StablePageResult Page,
        SandboxWindowWaitResult WindowResult,
        string ForegroundBeforeRefocus,
        string ForegroundAfterRefocus);

    private sealed record SandboxCooldownResult(
        StablePageResult Page,
        bool SawSandboxWindow);

    private sealed record ReturnPageAfterEscResult(
        StablePageResult? Page,
        bool TimedOut,
        GamePage LastPage,
        StablePageResult? LastResult,
        string? LastDecisionReason);

    private sealed record OcrTargetMatch(
        AutomationTarget? Target,
        IReadOnlyList<WorkflowOcrTextResult> OcrTexts);

    private sealed record LocalArchiveCardOcrMatch(
        LocalArchiveCardCandidate Candidate,
        string? RoiScreenshotPath,
        string CombinedText,
        string NormalizedText,
        double MatchScore,
        IReadOnlyList<LocalArchiveCardTextResult> OcrResults);

    private sealed record CardRoiCapture(
        GameCaptureResult Capture,
        NormalizedPixelRoi Roi);

    private sealed record NormalizedPixelRoi(
        int X,
        int Y,
        int Width,
        int Height,
        int SourceWidth,
        int SourceHeight,
        int RoiScale);

    private sealed record LocalArchiveCardOcrMatchDiagnostic(
        LocalArchiveCardCandidateDiagnostic Candidate,
        string? RoiScreenshotPath,
        string CombinedText,
        string NormalizedText,
        double MatchScore,
        IReadOnlyList<LocalArchiveCardTextResult> OcrResults);

    private sealed record LocalArchiveCardCandidateDiagnostic(
        Stage1UploadDiagnosticTarget CardBox,
        string Source,
        int Index,
        NormalizedBox? NameRegion,
        bool IsSynthetic);

    private sealed record Stage1LocalArchiveCardDiagnosticReport(
        DateTimeOffset CreatedAt,
        string RunId,
        string AttemptName,
        string RunArchiveDisplayName,
        string RunArchiveCode,
        string OcrEngineInfo,
        string? ModelDirectory,
        int RoiScale,
        string AppBaseDirectory,
        string DiagnosticsDirectory,
        GamePage LastRecognizedPage,
        double Confidence,
        string Source,
        GameCaptureStrategy CaptureStrategy,
        string CaptureMethod,
        string? CaptureWarning,
        IReadOnlyList<GameCaptureCandidateQuality> CaptureCandidates,
        bool IsLikelyBlackFrame,
        bool FallbackToGdi,
        string ForegroundWindowDiagnostic,
        string CandidateProviderUsed,
        int CandidateCount,
        bool UsedLayoutFallback,
        IReadOnlyList<LocalArchiveCardCandidateDiagnostic> Candidates,
        bool DetectedLocalArchiveCard,
        bool DetectedLocalArchiveMoreButton,
        bool UsedOcrConfirmedCardMatching,
        double? HoverOrClickX,
        double? HoverOrClickY,
        bool ClearedHover,
        double? ClearHoverX,
        double? ClearHoverY,
        int IgnoredPreExistingMoreButtons,
        string? MoreButtonSelectionReason,
        Stage1UploadDiagnosticTarget? SelectedMoreButton,
        IReadOnlyList<Stage1UploadDiagnosticTarget> MoreButtonsBeforeHover,
        IReadOnlyList<Stage1UploadDiagnosticTarget> MoreButtonsAfterHover,
        bool UsedCardGeometryFallbackClick,
        double? FallbackClickX,
        double? FallbackClickY,
        bool YoloMoreButtonMatched,
        IReadOnlyList<Stage1UploadDiagnosticTarget> RejectedMoreButtonCandidates,
        bool OpenCvMoreButtonAttempted,
        bool OpenCvMoreButtonMatched,
        double? OpenCvMoreButtonConfidence,
        NormalizedBox? OpenCvSearchRoi,
        double? OpenCvClickX,
        double? OpenCvClickY,
        string? OpenCvRejectedReason,
        string FailureReason,
        IReadOnlyList<LocalArchiveCardOcrMatchDiagnostic> CardOcrResults,
        LocalArchiveCardCandidateDiagnostic? SelectedCandidate,
        IReadOnlyList<Stage1UploadDiagnosticTarget> MoreButtonCandidates,
        IReadOnlyList<Stage1UploadDiagnosticTarget> AvailableTargets,
        string ScreenshotPath);
}










