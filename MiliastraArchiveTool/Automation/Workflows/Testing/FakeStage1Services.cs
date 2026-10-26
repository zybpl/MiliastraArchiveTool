using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation;
using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Automation.Workflows.Testing;

public sealed class FakeStage1SnapshotProvider : IGameSnapshotProvider
{
    private static readonly GameWindowInfo Window = new(
        new nint(1),
        1,
        "DryRun",
        1280,
        720,
        0,
        0,
        96,
        false);

    private readonly BitmapSource _bitmap;

    public FakeStage1SnapshotProvider()
    {
        _bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[] { 0, 0, 0, 255 },
            4);
        _bitmap.Freeze();
    }

    public Task<GameCaptureResult> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new GameCaptureResult(Window, _bitmap, "DryRun", 100, null, null, null));
    }
}

public sealed class ScriptedPageRecognizer : IPageRecognizer
{
    private readonly IReadOnlyList<ScriptedPageFrame> _frames;
    private int _index;

    public ScriptedPageRecognizer(IEnumerable<ScriptedPageFrame> frames)
    {
        _frames = frames.ToList();
        if (_frames.Count == 0)
            throw new ArgumentException("At least one scripted page frame is required.", nameof(frames));
    }

    public Task<PageRecognitionResult> RecognizeAsync(
        BitmapSource bitmap,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frame = _frames[Math.Min(_index, _frames.Count - 1)];
        _index++;
        return Task.FromResult(new PageRecognitionResult(
            frame.Page,
            frame.Confidence,
            frame.Source,
            frame.Targets));
    }
}

public sealed class RecordingGameInputAutomationService : IGameInputAutomationService
{
    private readonly Action<string> _record;
    private readonly Dictionary<string, int> _clickFailuresByCoordinate;

    public RecordingGameInputAutomationService(
        Action<string> record,
        IReadOnlyDictionary<string, int>? clickFailuresByCoordinate = null)
    {
        _record = record;
        _clickFailuresByCoordinate = new Dictionary<string, int>(
            clickFailuresByCoordinate ?? new Dictionary<string, int>(),
            StringComparer.Ordinal);
    }

    public Task ClickAsync(GameWindowInfo window, double normalizedX, double normalizedY, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var coordinate = $"{normalizedX:F3},{normalizedY:F3}";
        _record($"input_click:{coordinate}");
        if (_clickFailuresByCoordinate.TryGetValue(coordinate, out var remaining) && remaining > 0)
        {
            _clickFailuresByCoordinate[coordinate] = remaining - 1;
            throw new InvalidOperationException($"Fake click failure at {coordinate}.");
        }

        return Task.CompletedTask;
    }

    public Task MoveAsync(GameWindowInfo window, double normalizedX, double normalizedY, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _record($"input_move:{normalizedX:F3},{normalizedY:F3}");
        return Task.CompletedTask;
    }

    public Task PressKeyAsync(
        GameWindowInfo window,
        GameVirtualKey key,
        CancellationToken cancellationToken,
        bool allowPostMessageFallback = true,
        bool forcePostMessageFallback = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _record($"input_key:{key}");
        return Task.CompletedTask;
    }

    public Task ScrollAsync(GameWindowInfo window, double normalizedX, double normalizedY, int wheelDelta, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _record($"input_scroll:{normalizedX:F3},{normalizedY:F3},{wheelDelta}");
        return Task.CompletedTask;
    }

    public Task ReplaceTextAsync(GameWindowInfo window, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _record($"input_replace_text:{text}");
        return Task.CompletedTask;
    }
}

public sealed class FakeWindowsFileDialogAutomationService : IWindowsFileDialogAutomationService
{
    private readonly Action<string> _record;
    private readonly bool _shouldFail;
    private readonly string? _failPathContains;

    public FakeWindowsFileDialogAutomationService(Action<string> record, bool shouldFail = false, string? failPathContains = null)
    {
        _record = record;
        _shouldFail = shouldFail;
        _failPathContains = failPathContains;
    }

    public Task SelectFileAsync(string filePath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _record($"file_dialog_select:{filePath}");
        if (_shouldFail &&
            (string.IsNullOrWhiteSpace(_failPathContains) ||
                filePath.Contains(_failPathContains, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Fake file dialog failure.");
        }

        return Task.CompletedTask;
    }

    public Task SelectFilesAsync(IReadOnlyList<string> filePaths, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _record($"file_dialog_select_many:{string.Join("|", filePaths)}");
        if (_shouldFail &&
            (string.IsNullOrWhiteSpace(_failPathContains) ||
                filePaths.Any(path => path.Contains(_failPathContains, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Fake file dialog failure.");
        }

        return Task.CompletedTask;
    }
}

public sealed class FakeGameWindowActivationService : IGameWindowActivationService
{
    private readonly Action<string> _record;

    public FakeGameWindowActivationService(Action<string> record)
    {
        _record = record;
    }

    public void RestoreAndActivate(GameWindowInfo window) =>
        _record("activation_restore");

    public ForegroundWindowInfo GetForegroundWindowInfo(GameWindowInfo target) =>
        new(
            target.Handle,
            target.ProcessId,
            "DryRun",
            target.Title,
            target.Handle,
            target.ProcessId,
            true,
            true);

    public bool TryRestoreAndActivate(GameWindowInfo window, out string diagnostic)
    {
        RestoreAndActivate(window);
        diagnostic = GetForegroundWindowInfo(window).ToDiagnosticString();
        return true;
    }
}

public sealed class FakeGameWindowFinder : IGameWindowFinder
{
    public GameWindowInfo FindGameWindow() => new(
        new nint(1),
        1,
        "DryRun",
        1280,
        720,
        0,
        0,
        96,
        false);
}

public sealed class FakeSandboxProcessWatcher : ISandboxProcessWatcher
{
    private readonly bool _isStable;
    private readonly Queue<bool> _runningSequence;
    private readonly bool _windowFound;
    private readonly SandboxWindowInfo _windowInfo;
    private readonly Action<string> _record;

    public FakeSandboxProcessWatcher(
        bool isStable,
        Action<string> record,
        IEnumerable<bool>? runningSequence = null,
        bool windowFound = true,
        SandboxWindowInfo? windowInfo = null)
    {
        _isStable = isStable;
        _runningSequence = new Queue<bool>(runningSequence ?? []);
        _windowFound = windowFound;
        _windowInfo = windowInfo ?? new SandboxWindowInfo(
            "BeyondEditor",
            2,
            new nint(2),
            "DryRun Sandbox",
            true,
            false,
            1280,
            720);
        _record = record;
    }

    public bool IsSandboxRunning()
    {
        var isRunning = _runningSequence.Count > 0
            ? _runningSequence.Dequeue()
            : _isStable;
        _record($"sandbox_is_running:{isRunning}");
        return isRunning;
    }

    public Task<SandboxProcessWaitResult> WaitForStableAsync(
        TimeSpan stableDuration,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _record($"sandbox_wait_stable:{stableDuration.TotalSeconds:F1}s");
        return Task.FromResult(new SandboxProcessWaitResult(_isStable, _isStable, _isStable ? stableDuration : TimeSpan.Zero));
    }

    public SandboxWindowInfo? FindSandboxWindow() => _windowFound ? _windowInfo : null;

    public Task<SandboxWindowWaitResult> WaitForWindowAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _record($"sandbox_wait_window:{timeout.TotalSeconds:F1}s:{_windowFound}");
        return Task.FromResult(new SandboxWindowWaitResult(
            _windowFound,
            _isStable,
            _windowFound,
            TimeSpan.FromMilliseconds(650),
            _windowFound ? _windowInfo : null,
            _windowFound
                ? "fake sandbox window found"
                : "fake sandbox window missing"));
    }
}

public sealed class FakeUploadRunArchiveService : IUploadRunArchiveService
{
    private readonly string _runArchivePath;
    private readonly Action<string> _record;

    public FakeUploadRunArchiveService(string runArchivePath, Action<string> record)
    {
        _runArchivePath = runArchivePath;
        _record = record;
    }

    public Task<UploadRunArchive> CreateAsync(
        LevelArchive archive,
        UploadServer server,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(_runArchivePath)!);
        if (!File.Exists(_runArchivePath))
            File.WriteAllText(_runArchivePath, "dry-run-gil");

        _record($"run_archive_create:{Path.GetFileNameWithoutExtension(_runArchivePath)}");
        return Task.FromResult(new UploadRunArchive(
            archive,
            server,
            _runArchivePath,
            _runArchivePath,
            Path.GetFileName(_runArchivePath),
            "DRY1",
            Path.GetFileNameWithoutExtension(_runArchivePath),
            DateTimeOffset.Now));
    }
}

public sealed class FakeLocalArchiveCardTextRecognizer : ILocalArchiveCardTextRecognizer
{
    private readonly IReadOnlyList<LocalArchiveCardTextResult> _results;
    private readonly bool _useDefaultSequence;
    private int _callIndex;

    public FakeLocalArchiveCardTextRecognizer(IEnumerable<LocalArchiveCardTextResult>? results = null)
    {
        if (results == null)
        {
            _useDefaultSequence = true;
            _results = [];
        }
        else
        {
            _results = results.ToList();
        }
    }

    public string EngineInfo => "FakeLocalArchiveCardTextRecognizer";

    public Task<IReadOnlyList<LocalArchiveCardTextResult>> RecognizeCardsAsync(
        GameCaptureResult capture,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_useDefaultSequence)
        {
            var defaultResult = new LocalArchiveCardTextResult(
                "dry-run-gil_DRY1",
                new NormalizedBox(0.45, 0.45, 0.10, 0.04),
                1.0);
            _callIndex++;
            return Task.FromResult((IReadOnlyList<LocalArchiveCardTextResult>)[defaultResult]);
        }

        if (_results.Count <= 1)
            return Task.FromResult(_results);

        var result = _results[Math.Min(_callIndex, _results.Count - 1)];
        _callIndex++;
        return Task.FromResult((IReadOnlyList<LocalArchiveCardTextResult>)[result]);
    }
}

public sealed class FakeLocalArchiveMoreButtonLocator : ILocalArchiveMoreButtonLocator
{
    private readonly Queue<LocalArchiveMoreButtonLocationResult> _results;
    private readonly Action<string> _record;

    public FakeLocalArchiveMoreButtonLocator(
        IEnumerable<LocalArchiveMoreButtonLocationResult>? results,
        Action<string> record)
    {
        _results = new Queue<LocalArchiveMoreButtonLocationResult>(results ?? []);
        _record = record;
    }

    public LocalArchiveMoreButtonLocationResult Locate(
        GameCaptureResult capture,
        LocalArchiveCardCandidate candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _results.Count > 0
            ? _results.Dequeue()
            : new LocalArchiveMoreButtonLocationResult(
                false,
                null,
                0,
                OpenCvLocalArchiveMoreButtonLocator.GetLocalArchiveMoreButtonSearchRoi(candidate),
                "Fake OpenCV locator has no scripted result.");
        _record($"opencv_more_button:{result.Matched}");
        _record($"opencv_more_button:{result.Matched}:{result.Confidence:F2}");
        return result;
    }
}

public sealed class FakeUpdateArchiveButtonLocator : IUpdateArchiveButtonLocator
{
    private readonly Queue<UpdateArchiveButtonLocationResult> _results;
    private readonly Action<string> _record;

    public FakeUpdateArchiveButtonLocator(
        IEnumerable<UpdateArchiveButtonLocationResult>? results,
        Action<string> record)
    {
        _results = new Queue<UpdateArchiveButtonLocationResult>(results ?? []);
        _record = record;
    }

    public UpdateArchiveButtonLocationResult Locate(
        GameCaptureResult capture,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _results.Count > 0
            ? _results.Dequeue()
            : new UpdateArchiveButtonLocationResult(
                false,
                null,
                0,
                new NormalizedBox(0.855, 0.915, 0.285, 0.165),
                "Fake OpenCV update button locator has no scripted result.");
        _record($"opencv_update_button:{result.Matched}");
        _record($"opencv_update_button:{result.Matched}:{result.Confidence:F2}");
        return result;
    }
}

public sealed class FakeUpdateStartUploadButtonLocator : IUpdateStartUploadButtonLocator
{
    private readonly Queue<UpdateStartUploadButtonLocationResult> _results;
    private readonly Action<string> _record;

    public FakeUpdateStartUploadButtonLocator(
        IEnumerable<UpdateStartUploadButtonLocationResult>? results,
        Action<string> record)
    {
        _results = new Queue<UpdateStartUploadButtonLocationResult>(results ?? []);
        _record = record;
    }

    public UpdateStartUploadButtonLocationResult Locate(
        GameCaptureResult capture,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _results.Count > 0
            ? _results.Dequeue()
            : new UpdateStartUploadButtonLocationResult(
                false,
                null,
                0,
                new NormalizedBox(0.575, 0.600, 0.250, 0.120),
                "Fake OpenCV update start upload button locator has no scripted result.");
        _record($"opencv_update_start_upload_button:{result.Matched}");
        _record($"opencv_update_start_upload_button:{result.Matched}:{result.Confidence:F2}");
        return result;
    }
}

public sealed class FakeUploadedArchiveLaterButtonLocator : IUploadedArchiveLaterButtonLocator
{
    private readonly Queue<UploadedArchiveLaterButtonLocationResult> _results;
    private readonly Action<string> _record;

    public FakeUploadedArchiveLaterButtonLocator(
        IEnumerable<UploadedArchiveLaterButtonLocationResult>? results,
        Action<string> record)
    {
        _results = new Queue<UploadedArchiveLaterButtonLocationResult>(results ?? []);
        _record = record;
    }

    public UploadedArchiveLaterButtonLocationResult Locate(
        GameCaptureResult capture,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _results.Count > 0
            ? _results.Dequeue()
            : new UploadedArchiveLaterButtonLocationResult(
                false,
                null,
                0,
                new NormalizedBox(0.435, 0.575, 0.245, 0.155),
                "Fake OpenCV uploaded archive later button locator has no scripted result.");
        _record($"opencv_uploaded_archive_later_button:{result.Matched}");
        _record($"opencv_uploaded_archive_later_button:{result.Matched}:{result.Confidence:F2}");
        return result;
    }
}

public sealed class FakeInfoPanelIconLocator : IInfoPanelIconLocator
{
    private readonly Queue<InfoPanelIconLocationResult> _results;
    private readonly Action<string> _record;

    public FakeInfoPanelIconLocator(
        IEnumerable<InfoPanelIconLocationResult>? results,
        Action<string> record)
    {
        _results = new Queue<InfoPanelIconLocationResult>(results ?? []);
        _record = record;
    }

    public InfoPanelIconLocationResult Locate(
        GameCaptureResult capture,
        AutomationTarget row,
        string targetName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _results.Count > 0
            ? _results.Dequeue()
            : new InfoPanelIconLocationResult(
                false,
                null,
                0,
                OpenCvInfoPanelIconLocator.GetInfoPanelIconSearchRoi(row),
                "Fake OpenCV info panel icon locator has no scripted result.");
        _record($"opencv_info_panel_icon:{result.Matched}");
        _record($"opencv_info_panel_icon:{result.Matched}:{result.Confidence:F2}");
        return result;
    }
}

public sealed class FakeWorkflowOcrService : IWorkflowOcrService
{
    private readonly Queue<IReadOnlyList<WorkflowOcrTextResult>> _results;

    public FakeWorkflowOcrService(IEnumerable<IReadOnlyList<WorkflowOcrTextResult>>? results = null)
    {
        _results = new Queue<IReadOnlyList<WorkflowOcrTextResult>>(results ?? []);
    }

    public string EngineInfo => "FakeWorkflowOcrService";

    public Task<IReadOnlyList<WorkflowOcrTextResult>> RecognizeTargetAsync(
        GameCaptureResult capture,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NextOrDefault(target));
    }

    public Task<IReadOnlyList<WorkflowOcrTextResult>> RecognizeBoxAsync(
        GameCaptureResult capture,
        NormalizedBox box,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NextOrDefault(new AutomationTarget(sourceDescription, box.CenterX, box.CenterY, box.Width, box.Height, 1)));
    }

    private IReadOnlyList<WorkflowOcrTextResult> NextOrDefault(AutomationTarget target)
    {
        if (string.Equals(target.Name, WorkflowTargetNames.InfoPanelRow, StringComparison.Ordinal))
            return CreateSingleResult("Preview Info", target);

        if (_results.Count > 0)
            return _results.Dequeue();

        return CreateSingleResult(GetDefaultText(target.Name), target);
    }

    private static string GetDefaultText(string targetName) =>
        targetName switch
        {
            WorkflowTargetNames.MultilanguageAddButton => "Add Language",
            WorkflowTargetNames.LanguageConfirmButton => "OK",
            WorkflowTargetNames.PrimaryActionButton => "OK",
            "AddLanguageDialog/title" => "Add Language",
            _ => targetName
        };

    private static IReadOnlyList<WorkflowOcrTextResult> CreateSingleResult(string text, AutomationTarget target)
    {
        return
        [
            new WorkflowOcrTextResult(
                text,
                RapidOcrWorkflowOcrService.NormalizePreviewText(text),
                new NormalizedBox(target.CenterX, target.CenterY, target.Width, target.Height),
                1)
        ];
    }
}

public sealed class FakeStage1WorkflowClock : IStage1WorkflowClock
{
    private readonly TimeSpan _advancePerDelay;

    public FakeStage1WorkflowClock(TimeSpan? advancePerDelay = null)
    {
        _advancePerDelay = advancePerDelay ?? TimeSpan.FromSeconds(30);
        UtcNow = new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset UtcNow { get; private set; }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UtcNow += _advancePerDelay;
        return Task.CompletedTask;
    }
}
