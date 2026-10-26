using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Automation.Workflows.Diagnostics;
using MiliastraArchiveTool.Automation.Workflows;
using MiliastraArchiveTool.Infrastructure;
using MiliastraArchiveTool.Localization;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;
using MiliastraArchiveTool.Views;

#pragma warning disable CS0162

namespace MiliastraArchiveTool.ViewModels;

public enum UploadMode
{
    NewArchive,
    UpdateExistingArchive
}

public enum UpdateVersionType
{
    Minor,
    Major
}

public sealed class MainViewModel : ObservableObject
{
    private const int MaximumExtraImages = 4;
    private readonly ArchiveStorageService _storage = new();
    private CancellationTokenSource? _uploadCancellation;
    private UploadContinuation? _uploadContinuation;
    private string _continuationMessage = string.Empty;
    public string ContinuationMessage { get => _continuationMessage; private set => SetProperty(ref _continuationMessage, value); }
    private UploadStatusOverlayWindow? _uploadStatusOverlay;
    private LevelArchive? _selectedArchive;
    private UploadTask? _selectedUploadTask;
    private bool _isUsServer;
    private bool _isAsiaServer;
    private bool _isEuropeServer;
    private bool _isHkTwServer;
    private bool _useCurrentLoginServer;
    private bool _isUploading;
    private bool _isStage1PreflightRunning;
    private bool _isOpenMainMenuProbeRunning;
    private bool _useExperimentalStage1Upload;
    private bool _stage1PreflightReadyForStart;
    private string _stage1PreflightRawPage = string.Empty;
    private string _stage1PreflightGatedPage = string.Empty;
    private string _stage1PreflightMessage = string.Empty;
    private string _stage1PreflightScreenshotPath = string.Empty;
    private string _stage1PreflightReportPath = string.Empty;
    private string _stage1PreflightSummaryPath = string.Empty;
    private string _stage1PreflightErrorMessage = string.Empty;
    private bool _openMainMenuProbeSuccess;
    private string _openMainMenuProbeWinningStrategy = string.Empty;
    private string _openMainMenuProbeWinningFrameIndex = string.Empty;
    private bool _openMainMenuProbeHasQianxingRealmEntry;
    private bool _openMainMenuProbeHasWorldPaimonIcon;
    private string _openMainMenuProbeReportPath = string.Empty;
    private string _openMainMenuProbeSummaryPath = string.Empty;
    private string _openMainMenuProbeErrorMessage = string.Empty;
    private bool _isEnforcingUploadSelection;
    private UploadMode _uploadMode = UploadMode.NewArchive;
    private UpdateVersionType _updateVersionType = UpdateVersionType.Minor;
    private string _existingArchiveIdentifier = string.Empty;

    public MainViewModel()
    {
        App.Localization.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == "Item[]")
            {
                foreach (var archive in LevelArchiveList)
                    archive.RefreshLocalizedSummary();
                foreach (var task in UploadTaskList)
                    task.RefreshDisplayText();
            }
        };

        AddArchiveCommand = new RelayCommand(AddArchive, () => !IsUploading);
        DeleteArchiveCommand = new RelayCommand(DeleteSelectedArchive, () => !IsUploading && SelectedArchive != null);
        SaveArchiveCommand = new RelayCommand(SaveSelectedArchive, () => !IsUploading && SelectedArchive != null);
        SelectResourceCommand = new RelayCommand(SelectResource, _ => !IsUploading && SelectedArchive != null);
        PreviewResourceCommand = new RelayCommand(PreviewResource, _ => SelectedArchive != null);
        DeleteResourceCommand = new RelayCommand(DeleteResource, _ => !IsUploading && SelectedArchive != null);
        OpenLocalizedDetailsCommand = new RelayCommand(OpenLocalizedDetails, () => !IsUploading && SelectedArchive != null);
        OpenTagSelectorCommand = new RelayCommand(OpenTagSelector, () => !IsUploading && SelectedArchive != null);
        OpenExtraImagesCommand = new RelayCommand(OpenExtraImages, () => !IsUploading && SelectedArchive != null);
        RefreshUploadListCommand = new RelayCommand(RebuildUploadTasks, () => !IsUploading);
        SelectAllUploadsCommand = new RelayCommand(SelectAllUploads, () => !IsUploading && UploadTaskList.Count > 0);
        ClearUploadSelectionCommand = new RelayCommand(ClearUploadSelection, () => !IsUploading && UploadTaskList.Count > 0);
        StartUploadCommand = new AsyncRelayCommand(StartUploadBatchAsync, () => !IsUploading && !IsStage1PreflightRunning && !IsOpenMainMenuProbeRunning);
        CancelUploadCommand = new RelayCommand(CancelUpload, () => IsUploading);
        PauseUploadCommand = new RelayCommand(() => _uploadContinuation?.RequestPause(),
            () => IsUploading && _uploadContinuation?.State == UploadContinuationState.Running);
        ContinueUploadCommand = new RelayCommand(() => _uploadContinuation?.Continue(),
            () => IsUploading && _uploadContinuation?.State is UploadContinuationState.Paused or UploadContinuationState.Interrupted);
        RunStage1PreflightCommand = new AsyncRelayCommand(RunStage1PreflightAsync, () => !IsUploading && !IsStage1PreflightRunning && !IsOpenMainMenuProbeRunning);
        RunOpenMainMenuProbeCommand = new AsyncRelayCommand(RunOpenMainMenuProbeAsync, () => !IsUploading && !IsOpenMainMenuProbeRunning && !IsStage1PreflightRunning);
        OpenDiagnosticsDirectoryCommand = new RelayCommand(OpenDiagnosticsDirectory);

        LoadArchives();
    }

    public AppLocalization Loc => App.Localization;
    public ObservableCollection<LevelArchive> LevelArchiveList { get; } = new();
    public ObservableCollection<UploadTask> UploadTaskList { get; } = new();
    public ObservableCollection<UploadLogEntry> UploadLogEntries { get; } = new();

    public LevelArchive? SelectedArchive
    {
        get => _selectedArchive;
        set
        {
            if (SetProperty(ref _selectedArchive, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public UploadTask? SelectedUploadTask { get => _selectedUploadTask; set => SetProperty(ref _selectedUploadTask, value); }
    public bool IsUsServer
    {
        get => _isUsServer;
        set
        {
            if (SetProperty(ref _isUsServer, value))
                NotifyExperimentalStage1UploadStateChanged();
        }
    }

    public bool IsAsiaServer
    {
        get => _isAsiaServer;
        set
        {
            if (SetProperty(ref _isAsiaServer, value))
                NotifyExperimentalStage1UploadStateChanged();
        }
    }

    public bool IsEuropeServer
    {
        get => _isEuropeServer;
        set
        {
            if (SetProperty(ref _isEuropeServer, value))
                NotifyExperimentalStage1UploadStateChanged();
        }
    }

    public bool IsHkTwServer
    {
        get => _isHkTwServer;
        set
        {
            if (SetProperty(ref _isHkTwServer, value))
                NotifyExperimentalStage1UploadStateChanged();
        }
    }

    public bool UseCurrentLoginServer
    {
        get => _useCurrentLoginServer;
        set
        {
            if (!SetProperty(ref _useCurrentLoginServer, value))
                return;

            if (value)
                ClearTargetServers();

            OnPropertyChanged(nameof(CanSelectExplicitTargetServers));
            NotifyExperimentalStage1UploadStateChanged();
        }
    }
    public bool IsNewArchiveUpload
    {
        get => UploadMode == UploadMode.NewArchive;
        set
        {
            if (value)
                UploadMode = UploadMode.NewArchive;
        }
    }

    public bool IsUpdateExistingArchive
    {
        get => UploadMode == UploadMode.UpdateExistingArchive;
        set
        {
            if (value)
                UploadMode = UploadMode.UpdateExistingArchive;
        }
    }

    public string SelectedUploadModeKey
    {
        get => UploadMode == UploadMode.NewArchive ? nameof(UploadMode.NewArchive) : nameof(UploadMode.UpdateExistingArchive);
        set
        {
            UploadMode = value == nameof(UploadMode.UpdateExistingArchive)
                ? UploadMode.UpdateExistingArchive
                : UploadMode.NewArchive;
            OnPropertyChanged();
        }
    }

    public UploadMode UploadMode
    {
        get => _uploadMode;
        private set
        {
            if (SetProperty(ref _uploadMode, value))
            {
                OnPropertyChanged(nameof(IsNewArchiveUpload));
                OnPropertyChanged(nameof(IsUpdateExistingArchive));
                OnPropertyChanged(nameof(SelectedUploadModeKey));
                OnPropertyChanged(nameof(IsUpdateModeEnabled));
                NotifyExperimentalStage1UploadStateChanged();
                if (value == UploadMode.UpdateExistingArchive)
                    EnforceSingleUpdateSelection();
            }
        }
    }

    public bool IsMinorVersionUpdate
    {
        get => UpdateVersionType == UpdateVersionType.Minor;
        set
        {
            if (value)
                UpdateVersionType = UpdateVersionType.Minor;
        }
    }

    public bool IsMajorVersionUpdate
    {
        get => UpdateVersionType == UpdateVersionType.Major;
        set
        {
            if (value)
                UpdateVersionType = UpdateVersionType.Major;
        }
    }

    public string SelectedUpdateVersionTypeKey
    {
        get => UpdateVersionType == UpdateVersionType.Minor ? nameof(UpdateVersionType.Minor) : nameof(UpdateVersionType.Major);
        set
        {
            UpdateVersionType = value == nameof(UpdateVersionType.Major)
                ? UpdateVersionType.Major
                : UpdateVersionType.Minor;
            OnPropertyChanged();
        }
    }

    public UpdateVersionType UpdateVersionType
    {
        get => _updateVersionType;
        private set
        {
            if (SetProperty(ref _updateVersionType, value))
            {
                OnPropertyChanged(nameof(IsMinorVersionUpdate));
                OnPropertyChanged(nameof(IsMajorVersionUpdate));
                OnPropertyChanged(nameof(SelectedUpdateVersionTypeKey));
            }
        }
    }

    public string ExistingArchiveIdentifier
    {
        get => _existingArchiveIdentifier;
        set => SetProperty(ref _existingArchiveIdentifier, value);
    }

    public bool IsUpdateModeEnabled => UploadMode == UploadMode.UpdateExistingArchive;

    public bool IsUploading
    {
        get => _isUploading;
        private set
        {
            if (SetProperty(ref _isUploading, value))
            {
                OnPropertyChanged(nameof(CanEditDuringUpload));
                OnPropertyChanged(nameof(CanSelectExplicitTargetServers));
                NotifyExperimentalStage1UploadStateChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanEditDuringUpload => !IsUploading;
    public bool CanSelectExplicitTargetServers => CanEditDuringUpload && !UseCurrentLoginServer;
    public string DiagnosticsDirectory => ApplicationPaths.DiagnosticsDirectory;

    public bool UseExperimentalStage1Upload
    {
        get => _useExperimentalStage1Upload;
        set
        {
            if (SetProperty(ref _useExperimentalStage1Upload, value))
                NotifyExperimentalStage1UploadStateChanged();
        }
    }

    public bool CanUseExperimentalStage1Upload => GetExperimentalStage1UploadBlockingReasons().Count == 0;

    public string ExperimentalStage1UploadStatus
    {
        get
        {
            var reasons = GetExperimentalStage1UploadBlockingReasons();
            return reasons.Count == 0
                ? "Stage1 自动上传可用：仅当前服务器、新关卡、最近预检为支持的起点；单次最多约 10 分钟，失败后不会自动重试。"
                : "Stage1 自动上传不可用：" + string.Join("；", reasons);
        }
    }

    public bool IsStage1PreflightRunning
    {
        get => _isStage1PreflightRunning;
        private set
        {
            if (SetProperty(ref _isStage1PreflightRunning, value))
            {
                OnPropertyChanged(nameof(Stage1PreflightButtonText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string Stage1PreflightButtonText => IsStage1PreflightRunning ? "Stage1预检中..." : "Stage1预检";

    public bool IsOpenMainMenuProbeRunning
    {
        get => _isOpenMainMenuProbeRunning;
        private set
        {
            if (SetProperty(ref _isOpenMainMenuProbeRunning, value))
            {
                OnPropertyChanged(nameof(OpenMainMenuProbeButtonText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string OpenMainMenuProbeButtonText => IsOpenMainMenuProbeRunning ? "OpenMainMenu Probe运行中..." : "OpenMainMenu Probe";

    public bool OpenMainMenuProbeSuccess
    {
        get => _openMainMenuProbeSuccess;
        private set => SetProperty(ref _openMainMenuProbeSuccess, value);
    }

    public string OpenMainMenuProbeWinningStrategy
    {
        get => _openMainMenuProbeWinningStrategy;
        private set => SetProperty(ref _openMainMenuProbeWinningStrategy, value);
    }

    public string OpenMainMenuProbeWinningFrameIndex
    {
        get => _openMainMenuProbeWinningFrameIndex;
        private set => SetProperty(ref _openMainMenuProbeWinningFrameIndex, value);
    }

    public bool OpenMainMenuProbeHasQianxingRealmEntry
    {
        get => _openMainMenuProbeHasQianxingRealmEntry;
        private set => SetProperty(ref _openMainMenuProbeHasQianxingRealmEntry, value);
    }

    public bool OpenMainMenuProbeHasWorldPaimonIcon
    {
        get => _openMainMenuProbeHasWorldPaimonIcon;
        private set => SetProperty(ref _openMainMenuProbeHasWorldPaimonIcon, value);
    }

    public string OpenMainMenuProbeReportPath
    {
        get => _openMainMenuProbeReportPath;
        private set => SetProperty(ref _openMainMenuProbeReportPath, value);
    }

    public string OpenMainMenuProbeSummaryPath
    {
        get => _openMainMenuProbeSummaryPath;
        private set => SetProperty(ref _openMainMenuProbeSummaryPath, value);
    }

    public string OpenMainMenuProbeErrorMessage
    {
        get => _openMainMenuProbeErrorMessage;
        private set => SetProperty(ref _openMainMenuProbeErrorMessage, value);
    }

    public bool Stage1PreflightReadyForStart
    {
        get => _stage1PreflightReadyForStart;
        private set
        {
            if (SetProperty(ref _stage1PreflightReadyForStart, value))
                NotifyExperimentalStage1UploadStateChanged();
        }
    }

    public string Stage1PreflightRawPage
    {
        get => _stage1PreflightRawPage;
        private set
        {
            if (SetProperty(ref _stage1PreflightRawPage, value))
                NotifyExperimentalStage1UploadStateChanged();
        }
    }

    public string Stage1PreflightGatedPage
    {
        get => _stage1PreflightGatedPage;
        private set
        {
            if (SetProperty(ref _stage1PreflightGatedPage, value))
                NotifyExperimentalStage1UploadStateChanged();
        }
    }

    public string Stage1PreflightMessage
    {
        get => _stage1PreflightMessage;
        private set => SetProperty(ref _stage1PreflightMessage, value);
    }

    public string Stage1PreflightScreenshotPath
    {
        get => _stage1PreflightScreenshotPath;
        private set => SetProperty(ref _stage1PreflightScreenshotPath, value);
    }

    public string Stage1PreflightReportPath
    {
        get => _stage1PreflightReportPath;
        private set => SetProperty(ref _stage1PreflightReportPath, value);
    }

    public string Stage1PreflightSummaryPath
    {
        get => _stage1PreflightSummaryPath;
        private set => SetProperty(ref _stage1PreflightSummaryPath, value);
    }

    public string Stage1PreflightErrorMessage
    {
        get => _stage1PreflightErrorMessage;
        private set
        {
            if (SetProperty(ref _stage1PreflightErrorMessage, value))
                OnPropertyChanged(nameof(HasStage1PreflightError));
        }
    }

    public bool HasStage1PreflightError => !string.IsNullOrWhiteSpace(Stage1PreflightErrorMessage);

    public ICommand AddArchiveCommand { get; }
    public ICommand DeleteArchiveCommand { get; }
    public ICommand SaveArchiveCommand { get; }
    public ICommand SelectResourceCommand { get; }
    public ICommand PreviewResourceCommand { get; }
    public ICommand DeleteResourceCommand { get; }
    public ICommand OpenLocalizedDetailsCommand { get; }
    public ICommand OpenTagSelectorCommand { get; }
    public ICommand OpenExtraImagesCommand { get; }
    public ICommand RefreshUploadListCommand { get; }
    public ICommand SelectAllUploadsCommand { get; }
    public ICommand ClearUploadSelectionCommand { get; }
    public ICommand StartUploadCommand { get; }
    public ICommand CancelUploadCommand { get; }
    public ICommand PauseUploadCommand { get; }
    public ICommand ContinueUploadCommand { get; }
    public ICommand RunStage1PreflightCommand { get; }
    public ICommand RunOpenMainMenuProbeCommand { get; }
    public ICommand OpenDiagnosticsDirectoryCommand { get; }

    private void LoadArchives()
    {
        foreach (var archive in _storage.LoadAll())
            LevelArchiveList.Add(archive);

        ReindexArchives();
        RebuildUploadTasks();
        SelectedArchive = LevelArchiveList.FirstOrDefault();
        AppendLog(UploadLogLevel.Information, string.Format(Loc["Loaded"], LevelArchiveList.Count));
    }

    private void AddArchive()
    {
        var archive = new LevelArchive
        {
            Index = LevelArchiveList.Count + 1,
            DefaultLanguage = Loc.CurrentLanguage == "zh-CN" ? "zh-CN" : "en",
            LevelName = Loc["NewLevel"]
        };
        LevelArchiveList.Add(archive);
        SelectedArchive = archive;
        RebuildUploadTasks();
    }

    private void DeleteSelectedArchive()
    {
        if (SelectedArchive == null)
            return;

        var archive = SelectedArchive;
        var result = MessageBox.Show(
            string.Format(Loc["ConfirmDeleteArchive"], archive.LevelName),
            Loc["ConfirmDelete"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            _storage.Delete(archive);
            var oldIndex = LevelArchiveList.IndexOf(archive);
            LevelArchiveList.Remove(archive);
            ReindexArchives();
            RebuildUploadTasks();
            SelectedArchive = LevelArchiveList.Count == 0 ? null : LevelArchiveList[Math.Min(oldIndex, LevelArchiveList.Count - 1)];
            AppendLog(UploadLogLevel.Information, string.Format(Loc["Deleted"], archive.LevelName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(
                ex.Message,
                Loc["ValidationFailed"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveSelectedArchive()
    {
        if (SelectedArchive == null)
            return;

        try
        {
            _storage.Save(SelectedArchive);
            RebuildUploadTasks();
            AppendLog(UploadLogLevel.Information, string.Format(Loc["Saved"], SelectedArchive.LevelName));
        }
        catch (ArchiveValidationException ex)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, ex.Errors.Select(error => "- " + error)),
                Loc["ValidationFailed"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, Loc["ValidationFailed"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectResource(object? parameter)
    {
        if (SelectedArchive == null)
            return;

        var resource = parameter?.ToString() ?? string.Empty;
        var dialog = new OpenFileDialog();

        switch (resource)
        {
            case "Archive":
                dialog.Filter = "GIL archive (*.gil)|*.gil";
                break;
            case "MainImage":
                dialog.Filter = "JPG image (*.jpg)|*.jpg";
                break;
            case "AchievementIcon":
                dialog.Filter = "JPG image (*.jpg)|*.jpg";
                break;
            case "LuxuryGiftIcon":
            case "ColorfulGiftIcon":
            case "QuestGiftIcon":
                dialog.Filter = "PNG image (*.png)|*.png";
                break;
            case "Video":
                dialog.Filter = "Video files|*.3gp;*.avi;*.flv;*.mkv;*.mov;*.mp4;*.mpeg;*.mpg;*.webm;*.wmv";
                break;
            case "ExtraImages":
                dialog.Filter = "Image files|*.png;*.jpg;*.jpeg";
                dialog.Multiselect = true;
                break;
            default:
                dialog.Filter = "Image files|*.png;*.jpg;*.jpeg";
                break;
        }

        if (dialog.ShowDialog() != true)
            return;

        switch (resource)
        {
            case "Archive":
                SelectedArchive.PendingArchiveSourcePath = dialog.FileName;
                SelectedArchive.SyncArchiveFileName();
                break;
            case "LevelCover":
                SelectedArchive.LevelCoverPath = dialog.FileName;
                break;
            case "MainImage":
                SelectedArchive.MainImagePath = dialog.FileName;
                break;
            case "AchievementIcon":
                SelectedArchive.AchievementIconPath = dialog.FileName;
                break;
            case "Video":
                SelectedArchive.VideoPath = dialog.FileName;
                break;
            case "ExtraImages":
                foreach (var path in dialog.FileNames)
                {
                    if (SelectedArchive.ExtraImagePaths.Count >= MaximumExtraImages)
                        break;
                    if (!SelectedArchive.ExtraImagePaths.Contains(path))
                        SelectedArchive.ExtraImagePaths.Add(path);
                }
                break;
        }

        AppendLog(UploadLogLevel.Information, string.Format(Loc["SelectFileLog"], dialog.FileName));
    }

    private void PreviewResource(object? parameter)
    {
        if (SelectedArchive == null)
            return;

        var path = GetResourcePath(SelectedArchive, parameter?.ToString());
        OpenResource(path);
    }

    private void DeleteResource(object? parameter)
    {
        if (SelectedArchive == null)
            return;

        switch (parameter?.ToString())
        {
            case "LevelCover":
                SelectedArchive.LevelCoverPath = string.Empty;
                break;
            case "MainImage":
                SelectedArchive.MainImagePath = string.Empty;
                break;
            case "AchievementIcon":
                SelectedArchive.AchievementIconPath = string.Empty;
                break;
            case "Video":
                SelectedArchive.VideoPath = string.Empty;
                break;
        }
    }

    private void OpenLocalizedDetails()
    {
        if (SelectedArchive == null)
            return;

        var window = new LocalizedDetailsWindow(SelectedArchive)
        {
            Owner = Application.Current.MainWindow
        };
        if (window.ShowDialog() == true)
        {
            SelectedArchive.RefreshLocalizedSummary();
            OnPropertyChanged(nameof(SelectedArchive));
        }
    }

    private void OpenTagSelector()
    {
        if (SelectedArchive == null)
            return;

        var window = new TagSelectWindow(SelectedArchive.Tags)
        {
            Owner = Application.Current.MainWindow
        };
        if (window.ShowDialog() == true)
            SelectedArchive.Tags = window.ResultTags;
    }

    private void OpenExtraImages()
    {
        if (SelectedArchive == null)
            return;

        var imagePaths = SelectedArchive.ExtraImagePaths
            .Select(path => (
                OriginalPath: path,
                FullPath: _storage.ResolveResourcePath(SelectedArchive, path)))
            .Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
            .Select(item => (item.OriginalPath, item.FullPath!))
            .ToList();

        if (imagePaths.Count == 0)
        {
            MessageBox.Show(
                Loc["NoExtraImages"],
                Loc["ExtraImages"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        new ExtraImagesPreviewWindow(
            imagePaths,
            originalPath =>
            {
                if (!SelectedArchive.ExtraImagePaths.Remove(originalPath))
                    return false;

                AppendLog(UploadLogLevel.Information, $"已移除更多展示图片：{Path.GetFileName(originalPath)}");
                return true;
            })
        {
            Owner = Application.Current.MainWindow
        }.ShowDialog();
    }

    private void SelectAllUploads()
    {
        if (IsUpdateExistingArchive)
        {
            ClearUploadSelection();
            var firstTask = UploadTaskList.FirstOrDefault();
            if (firstTask != null)
                firstTask.IsChecked = true;
            AppendLog(UploadLogLevel.Warning, Loc["UpdateModeSingleSelectionApplied"]);
            return;
        }

        foreach (var task in UploadTaskList)
            task.IsChecked = true;
    }

    private void ClearUploadSelection()
    {
        foreach (var task in UploadTaskList)
            task.IsChecked = false;
    }

    private async Task StartUploadBatchAsync()
    {
        var blockingReasons = GetStage1BatchUploadBlockingReasons();
        if (blockingReasons.Count > 0)
        {
            ShowUploadStartupError("无法开始上传", string.Join(Environment.NewLine, blockingReasons));
            return;
        }

        var servers = GetSelectedServerList();
        var selectedTasks = UploadTaskList.Where(task => task.IsChecked).ToList();
        if (selectedTasks.Count == 0)
        {
            ShowUploadStartupError("无法开始上传", Loc["NoUploadTaskSelected"]);
            return;
        }

        var isUpdate = IsUpdateExistingArchive;
        var identifier = ExistingArchiveIdentifier.Trim();
        var isMajor = UpdateVersionType == UpdateVersionType.Major;
        var explicitServerMode = !UseCurrentLoginServer;
        if (isUpdate && selectedTasks.Count != 1)
        {
            ShowUploadStartupError("无法开始上传", Loc["UpdateModeRequiresSingleTask"]);
            return;
        }
        if (isUpdate && string.IsNullOrWhiteSpace(identifier))
        {
            ShowUploadStartupError("无法开始上传", Loc["ExistingArchiveIdentifierRequired"]);
            return;
        }

        using var performanceTrace = new WorkflowPerformanceTrace();
        AppendLog(UploadLogLevel.Information, $"Performance trace: {performanceTrace.FilePath}");
        _uploadCancellation?.Dispose();
        _uploadCancellation = new CancellationTokenSource();
        IsUploading = true;
        var startupCompleted = false;

        void LogWithOverlay(UploadLogLevel level, string message)
        {
            AppendLog(level, message);
            if (startupCompleted)
                UpdateUploadStatusOverlay(selectedTasks, servers, level, message);
        }

        try
        {
            var continuation = new UploadContinuation();
            UploadOperationStatus? latestOperation = null;
            UploadTask? heldTask = null;
            var heldState = UploadTaskState.Ready;
            _uploadContinuation = continuation;
            ContinuationMessage = string.Empty;
            continuation.Changed += status => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_uploadContinuation != continuation) return;
                ContinuationMessage = $"{status.State}: {status.Step}\n{status.Message}";
                if (status.State is UploadContinuationState.Paused or UploadContinuationState.Interrupted)
                {
                    if (heldTask == null)
                    {
                        heldTask = selectedTasks.FirstOrDefault(t => t.State is UploadTaskState.WaitingForGame or UploadTaskState.Recognizing or UploadTaskState.Navigating or UploadTaskState.Filling or UploadTaskState.Submitting or UploadTaskState.Verifying);
                        if (heldTask != null) heldState = heldTask.State;
                    }
                    if (heldTask != null) heldTask.State = status.State == UploadContinuationState.Paused ? UploadTaskState.Paused : UploadTaskState.Interrupted;
                }
                else if (status.State == UploadContinuationState.Running && heldTask != null)
                {
                    heldTask.State = heldState;
                    heldTask = null;
                }
                AppendLog(status.State == UploadContinuationState.Interrupted ? UploadLogLevel.Warning : UploadLogLevel.Information, ContinuationMessage);
                _uploadStatusOverlay?.UpdateOperation(new UploadOperationStatus(latestOperation?.Server ?? servers.FirstOrDefault(), latestOperation?.ArchiveName,
                    $"{status.State}: {latestOperation?.Operation ?? "Upload"}. " +
                    (status.State == UploadContinuationState.Interrupted ? "Check the task window, then Continue or Terminate." : "")));
                CommandManager.InvalidateRequerySuggested();
                if (status.State == UploadContinuationState.Interrupted &&
                    (status.Message.StartsWith("预览信息无法正常提交：", StringComparison.Ordinal) ||
                     status.Message.StartsWith("图片资源上传失败：", StringComparison.Ordinal)))
                {
                    MessageBox.Show(Application.Current.MainWindow,
                        $"{status.Message}{Environment.NewLine}{Environment.NewLine}" +
                        $"任务已中断。关闭此提示不会自动继续提交，请检查后选择继续或终止任务。{Environment.NewLine}" +
                        $"诊断目录：{DiagnosticsDirectory}",
                        status.Message.StartsWith("图片资源上传失败：", StringComparison.Ordinal) ? "图片资源上传失败" : "预览信息提交失败",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            using var executor = new Stage1GameUploadExecutor(_storage,
                new GameSnapshotProvider(MiliastraArchiveTool.Automation.GameCaptureStrategy.LiveGameplay),
                new MiliastraArchiveTool.Automation.Recognition.Yolo.Stage1GatedPageRecognizer(),
                fileDialog: new MiliastraArchiveTool.Automation.ContinuationFileDialogService(
                    new MiliastraArchiveTool.Automation.WindowsFileDialogAutomationService(), continuation),
                ownsRecognizer: true) { Continuation = continuation };
            executor.OperationChanged += status => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                latestOperation = status;
                _uploadStatusOverlay?.UpdateOperation(status);
            });
            var uploadCoordinator = new UploadCoordinator(executor,
                attemptTimeout: TimeSpan.FromMinutes(30), maximumAttempts: 1, continuation: continuation);
            AppendLog(UploadLogLevel.Information, "Upload timeout policy: 30 active minutes per save/server attempt; paused/interrupted time is excluded. Individual step timeouts remain enabled.");
            AppendLog(UploadLogLevel.Information, string.Format(Loc["UploadValidationStart"], selectedTasks.Count));

            await uploadCoordinator.RunAsync(
                selectedTasks,
                servers,
                _storage.ValidateForUpload,
                FormatUploadServerName,
                LogWithOverlay,
                isUpdate,
                identifier,
                isMajor,
                async cancellationToken =>
                {
                    AppendLog(UploadLogLevel.Information, "全部已选存档校验通过，开始启动预检。");
                    var report = await RunStage1PreflightCoreAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var reason = report == null
                        ? Stage1PreflightErrorMessage
                        : Stage1PreflightCheck.GetStartupBlockingReason(report, explicitServerMode);
                    Stage1PreflightReadyForStart = report != null && reason == null;
                    Stage1PreflightMessage = Stage1PreflightReadyForStart ? "启动预检通过。" : "启动预检未通过。";
                    if (reason != null)
                    {
                        Stage1PreflightErrorMessage = reason;
                    }
                    if (report == null || reason != null)
                        throw new UploadStartupException("启动预检失败",
                            $"本次任务已中止，请处理以下问题后重新开始：{Environment.NewLine}{reason}" +
                            $"{Environment.NewLine}{Environment.NewLine}诊断目录：{DiagnosticsDirectory}");

                    Stage1PreflightReadyForStart = false;
                    Stage1PreflightMessage = "正在检查游戏语言与存档默认语言。";
                    AppendLog(UploadLogLevel.Information, Stage1PreflightMessage);
                    var gameLanguage = await new ArchiveGameLanguageCheck().RunAsync(selectedTasks.Select(t => t.SourceArchive).ToList(),
                        report!.GatedPage, cancellationToken, report.RecoveryEscapeUsed);
                    Stage1PreflightReadyForStart = true;
                    Stage1PreflightMessage = "启动预检及存档默认语言检查通过。";
                    AppendLog(UploadLogLevel.Information, $"游戏语言：{gameLanguage}；全部已选存档默认语言一致。");
                    startupCompleted = true;
                    AppendLog(UploadLogLevel.Information, "存档校验与启动预检均通过，开始上传任务。");
                    AppendLog(UploadLogLevel.Information, isUpdate
                        ? string.Format(Loc["UploadModeUpdateSummary"], identifier, Loc[$"UpdateVersionType_{UpdateVersionType}"])
                        : Loc["UploadModeNewSummary"]);
                    ShowUploadStatusOverlay(selectedTasks, servers);
                },
                _uploadCancellation.Token);
        }
        catch (UploadStartupException ex)
        {
            Stage1PreflightReadyForStart = false;
            Stage1PreflightMessage = ex.Title;
            Stage1PreflightErrorMessage = ex.Message;
            ShowUploadStartupError(ex.Title, ex.Message);
            return;
        }
        catch (OperationCanceledException) when (_uploadCancellation.IsCancellationRequested)
        {
            foreach (var task in selectedTasks.Where(task => task.State is not UploadTaskState.Succeeded and not UploadTaskState.Failed))
            {
                task.State = UploadTaskState.Cancelled;
                task.ErrorMessage = "已取消。";
            }
            AppendLog(UploadLogLevel.Warning, "启动检查或上传任务已取消。");
            if (!startupCompleted)
                return;
        }
        catch (Exception ex)
        {
            foreach (var task in selectedTasks.Where(task =>
                         task.State is not UploadTaskState.Succeeded and not UploadTaskState.Cancelled))
            {
                task.State = UploadTaskState.Failed;
                task.ErrorMessage = ex.Message;
            }
            if (!startupCompleted)
            {
                ShowUploadStartupError("启动检查失败", $"本次任务已中止：{Environment.NewLine}{ex.Message}");
                return;
            }
            AppendLog(UploadLogLevel.Error, string.Format(Loc["UploadBatchUnexpectedError"], ex.Message));
            UpdateUploadStatusOverlay(selectedTasks, servers, UploadLogLevel.Error, "Batch error. Check the main window log.");
        }
        finally
        {
            _uploadContinuation?.Stop();
            _uploadContinuation = null;
            ContinuationMessage = string.Empty;
            IsUploading = false;
            _uploadCancellation.Dispose();
            _uploadCancellation = null;
        }

        var succeeded = selectedTasks.Count(task => task.State == UploadTaskState.Succeeded);
        var failed = selectedTasks.Count(task => task.State == UploadTaskState.Failed);
        var cancelled = selectedTasks.Count(task => task.State == UploadTaskState.Cancelled);
        AppendLog(UploadLogLevel.Information, string.Format(Loc["UploadBatchFinished"], succeeded, failed, cancelled));
        UpdateUploadStatusOverlay(selectedTasks, servers,
            failed > 0 ? UploadLogLevel.Error : cancelled > 0 ? UploadLogLevel.Warning : UploadLogLevel.Information,
            $"Batch finished: OK {succeeded}, failed {failed}, cancelled {cancelled}.");
        HideUploadStatusOverlayAfterDelay(TimeSpan.FromSeconds(failed > 0 ? 12 : 6));
    }

    private void ShowUploadStartupError(string title, string message)
    {
        AppendLog(UploadLogLevel.Error, message);
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
    private void CancelUpload()
    {
        if (_uploadCancellation == null || _uploadCancellation.IsCancellationRequested)
            return;

        AppendLog(UploadLogLevel.Warning, Loc["UploadCancelling"]);
        _uploadCancellation.Cancel();
        _uploadContinuation?.Stop();
        UpdateUploadStatusOverlay(
            UploadTaskList.Where(task => task.IsChecked).ToList(),
            GetSelectedServerList(),
            UploadLogLevel.Warning,
            "Cancelling...");
    }

    private void ShowUploadStatusOverlay(IReadOnlyList<UploadTask> tasks, IReadOnlyList<UploadServer> servers)
    {
        void ShowCore()
        {
            try
            {
                _uploadStatusOverlay?.Close();
                _uploadStatusOverlay = new UploadStatusOverlayWindow();
                _uploadStatusOverlay.PlaceNearGameWindow();
                _uploadStatusOverlay.UpdateStatus(tasks, servers, IsUpdateExistingArchive, "Batch started.");
                AppendLog(
                    UploadLogLevel.Information,
                    $"Upload status HUD {(_uploadStatusOverlay.IsVisible ? "visible outside game" : "hidden: no safe space")}.");
            }
            catch (Exception ex)
            {
                AppendLog(UploadLogLevel.Warning, $"Upload status HUD failed to open: {ex.Message}");
            }
        }

        if (Application.Current.Dispatcher.CheckAccess())
            ShowCore();
        else
            Application.Current.Dispatcher.Invoke(ShowCore);
    }

    private void UpdateUploadStatusOverlay(
        IReadOnlyList<UploadTask> tasks,
        IReadOnlyList<UploadServer> servers,
        UploadLogLevel level,
        string message)
    {
        var briefMessage = CreateOverlayBriefMessage(level, message);
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _uploadStatusOverlay?.UpdateStatus(tasks, servers, IsUpdateExistingArchive, briefMessage);
            }
            catch
            {
                // The HUD is best-effort; upload automation must not depend on it.
            }
        });
    }

    private async void HideUploadStatusOverlayAfterDelay(TimeSpan delay)
    {
        var overlay = _uploadStatusOverlay;
        if (overlay == null)
            return;

        try
        {
            await Task.Delay(delay);
            if (!ReferenceEquals(_uploadStatusOverlay, overlay))
                return;

            overlay.Close();
            _uploadStatusOverlay = null;
        }
        catch
        {
            _uploadStatusOverlay = null;
        }
    }

    private static string CreateOverlayBriefMessage(UploadLogLevel level, string message)
    {
        if (level == UploadLogLevel.Error)
            return "Error. Check the main window log.";
        if (level == UploadLogLevel.Warning)
            return message.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                ? "Cancelling..."
                : "Waiting or retrying current step.";

        if (string.IsNullOrWhiteSpace(message))
            return "Running.";

        var normalized = message.ReplaceLineEndings(" ");
        if (normalized.Contains("Batch started", StringComparison.OrdinalIgnoreCase))
            return "Batch started.";
        if (normalized.Contains("Batch finished", StringComparison.OrdinalIgnoreCase))
            return "Batch finished.";
        if (normalized.Contains("succeeded", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("success", StringComparison.OrdinalIgnoreCase))
            return "Step completed.";
        if (normalized.Contains("retry", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("wait", StringComparison.OrdinalIgnoreCase))
            return "Waiting or retrying current step.";

        return "Running.";
    }

    private async Task RunStage1PreflightAsync()
    {
        await RunStage1PreflightCoreAsync(CancellationToken.None);
    }

    private async Task<Stage1PreflightCheckReport?> RunStage1PreflightCoreAsync(CancellationToken cancellationToken)
    {
        var outputDirectory = ApplicationPaths.DiagnosticsDirectory;
        var reportPath = Path.Combine(outputDirectory, "stage1-preflight-report.json");
        var summaryPath = Path.Combine(outputDirectory, "stage1-preflight-summary.md");

        IsStage1PreflightRunning = true;
        Stage1PreflightReadyForStart = false;
        Stage1PreflightRawPage = string.Empty;
        Stage1PreflightGatedPage = string.Empty;
        Stage1PreflightMessage = "Stage1预检运行中...";
        Stage1PreflightScreenshotPath = string.Empty;
        Stage1PreflightReportPath = reportPath;
        Stage1PreflightSummaryPath = summaryPath;
        Stage1PreflightErrorMessage = string.Empty;
        AppendLog(UploadLogLevel.Information, "Stage1预检开始：检测并激活游戏窗口，然后进行截图和页面识别诊断。");

        try
        {
            var report = await Stage1PreflightCheckSelfCheck.RunAsync(outputDirectory, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            Stage1PreflightReadyForStart = report.ReadyForStage1CurrentServerStart;
            Stage1PreflightRawPage = report.RawPage.ToString();
            Stage1PreflightGatedPage = report.GatedPage.ToString();
            Stage1PreflightMessage = report.Message;
            Stage1PreflightScreenshotPath = report.ScreenshotPath ?? string.Empty;
            Stage1PreflightReportPath = reportPath;
            Stage1PreflightSummaryPath = summaryPath;
            Stage1PreflightErrorMessage = report.ErrorMessage ?? string.Empty;

            var logLevel = string.IsNullOrWhiteSpace(report.ErrorMessage)
                ? UploadLogLevel.Information
                : UploadLogLevel.Warning;
            AppendLog(logLevel, $"Stage1预检完成：{report.Message}");
            if (!string.IsNullOrWhiteSpace(report.ErrorMessage))
                AppendLog(UploadLogLevel.Warning, $"Stage1预检错误：{report.ErrorMessage}");
            return report;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Stage1PreflightMessage = "Stage1预检已取消。";
            throw;
        }
        catch (Exception ex)
        {
            Stage1PreflightMessage = "Stage1预检失败。";
            Stage1PreflightErrorMessage = ex.Message;
            AppendLog(UploadLogLevel.Error, $"Stage1预检失败：{ex.Message}");
            return null;
        }
        finally
        {
            IsStage1PreflightRunning = false;
        }
    }

    private async Task RunOpenMainMenuProbeAsync()
    {
        var outputDirectory = ApplicationPaths.DiagnosticsDirectory;
        var reportPath = Path.Combine(outputDirectory, "stage1-input-focus-probe-report.json");
        var summaryPath = Path.Combine(outputDirectory, "stage1-input-focus-probe-summary.md");

        IsOpenMainMenuProbeRunning = true;
        OpenMainMenuProbeSuccess = false;
        OpenMainMenuProbeWinningStrategy = string.Empty;
        OpenMainMenuProbeWinningFrameIndex = string.Empty;
        OpenMainMenuProbeHasQianxingRealmEntry = false;
        OpenMainMenuProbeHasWorldPaimonIcon = false;
        OpenMainMenuProbeReportPath = reportPath;
        OpenMainMenuProbeSummaryPath = summaryPath;
        OpenMainMenuProbeErrorMessage = string.Empty;
        AppendLog(UploadLogLevel.Information, "OpenMainMenu Probe 开始：将在正式应用进程内测试 ESC 与派蒙图标 fallback。");

        try
        {
            var report = await Stage1InputFocusSelfCheck.RunOpenMainMenuProbeAsync(outputDirectory);
            OpenMainMenuProbeSuccess = report.Success;
            OpenMainMenuProbeWinningStrategy = report.WinningStrategy;
            OpenMainMenuProbeWinningFrameIndex = report.WinningFrameIndex?.ToString() ?? "<none>";
            OpenMainMenuProbeHasQianxingRealmEntry = report.HasAnyQianxingRealmEntry;
            OpenMainMenuProbeHasWorldPaimonIcon = report.HasAnyWorldPaimonIcon;
            OpenMainMenuProbeReportPath = report.ReportPath ?? reportPath;
            OpenMainMenuProbeSummaryPath = report.SummaryPath ?? summaryPath;
            OpenMainMenuProbeErrorMessage = report.ErrorMessage ?? string.Empty;

            var logLevel = report.Success ? UploadLogLevel.Information : UploadLogLevel.Warning;
            AppendLog(logLevel, $"OpenMainMenu Probe 完成：success={report.Success}, strategy={report.WinningStrategy}, frame={OpenMainMenuProbeWinningFrameIndex}");
            if (!string.IsNullOrWhiteSpace(report.ErrorMessage))
                AppendLog(UploadLogLevel.Warning, $"OpenMainMenu Probe 诊断：{report.ErrorMessage}");
        }
        catch (Exception ex)
        {
            OpenMainMenuProbeWinningStrategy = "None";
            OpenMainMenuProbeErrorMessage = ex.Message;
            AppendLog(UploadLogLevel.Error, $"OpenMainMenu Probe 失败：{ex.Message}");
        }
        finally
        {
            IsOpenMainMenuProbeRunning = false;
        }
    }

    private void OpenDiagnosticsDirectory()
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DiagnosticsDirectory,
                UseShellExecute = true
            });
            AppendLog(UploadLogLevel.Information, $"已打开诊断目录：{DiagnosticsDirectory}");
        }
        catch (Exception ex)
        {
            AppendLog(UploadLogLevel.Error, $"打开诊断目录失败：{ex.Message}");
        }
    }

    private IReadOnlyList<string> GetStage1BatchUploadBlockingReasons()
    {
        var reasons = new List<string>();

        if (IsUploading)
            reasons.Add("An upload task is already running");
        if (IsStage1PreflightRunning || IsOpenMainMenuProbeRunning)
            reasons.Add("当前正在运行诊断检查，请等待检查结束后再启动。");
        if (!UseCurrentLoginServer && GetSelectedServerList().Count == 0)
            reasons.Add("Select at least one target server, or choose Current");

        return reasons;
    }

    private IReadOnlyList<string> GetExperimentalStage1UploadBlockingReasons()
    {
        var reasons = new List<string>();
        var explicitServerMode = !UseCurrentLoginServer;

        if (IsUploading)
            reasons.Add("An upload task is already running");
        if (explicitServerMode && GetSelectedServerList().Count == 0)
            reasons.Add("Select at least one explicit target server");
        if (!Stage1PreflightReadyForStart)
            reasons.Add("Last Stage1 preflight is not Ready");
        if (!TryParseGamePage(Stage1PreflightRawPage, out var rawPage) ||
            !IsSupportedExperimentalStage1StartPage(rawPage, explicitServerMode))
            reasons.Add("Last preflight RawPage is not a supported Stage1 start page for the selected server mode");
        if (!TryParseGamePage(Stage1PreflightGatedPage, out var gatedPage) ||
            !IsSupportedExperimentalStage1StartPage(gatedPage, explicitServerMode))
            reasons.Add("Last preflight GatedPage is not a supported Stage1 start page for the selected server mode");

        return reasons;

        if (IsUploading)
            reasons.Add("当前已有上传任务正在运行");
        if (!UseCurrentLoginServer)
            reasons.Add("必须勾选“当前已登录服务器”");
        if (IsUsServer || IsAsiaServer || IsEuropeServer || IsHkTwServer)
            reasons.Add("不能同时勾选 America / Asia / Europe / TW HK MO");
        if (UploadMode != UploadMode.NewArchive)
            reasons.Add("只支持新关卡上传，不支持更新已有存档");
        if (!Stage1PreflightReadyForStart)
            reasons.Add("最近一次 Stage1 预检未 Ready");
        if (!TryParseGamePage(Stage1PreflightRawPage, out rawPage) || !IsSupportedExperimentalStage1StartPage(rawPage, explicitServerMode))
            reasons.Add("最近一次预检 RawPage 不是支持的 Stage1 起点");
        if (!TryParseGamePage(Stage1PreflightGatedPage, out gatedPage) || !IsSupportedExperimentalStage1StartPage(gatedPage, explicitServerMode))
            reasons.Add("最近一次预检 GatedPage 不是支持的 Stage1 起点");

        return reasons;
    }

    private static bool TryParseGamePage(string value, out GamePage page) =>
        Enum.TryParse(value, ignoreCase: false, out page);

    private static bool IsSupportedExperimentalStage1StartPage(GamePage page, bool explicitServerMode) =>
        Stage1PreflightCheck.IsSupportedStartPage(page, explicitServerMode);

    private void NotifyExperimentalStage1UploadStateChanged()
    {
        OnPropertyChanged(nameof(CanUseExperimentalStage1Upload));
        OnPropertyChanged(nameof(ExperimentalStage1UploadStatus));
        CommandManager.InvalidateRequerySuggested();
    }

    private IReadOnlyList<UploadServer> GetSelectedServerList()
    {
        if (UseCurrentLoginServer)
            return [UploadServer.None];

        var servers = new List<UploadServer>();
        if (IsUsServer)
            servers.Add(UploadServer.Us);
        if (IsAsiaServer)
            servers.Add(UploadServer.Asia);
        if (IsEuropeServer)
            servers.Add(UploadServer.Europe);
        if (IsHkTwServer)
            servers.Add(UploadServer.HkTw);
        return servers;
    }

    private void ClearTargetServers()
    {
        IsUsServer = false;
        IsAsiaServer = false;
        IsEuropeServer = false;
        IsHkTwServer = false;
    }

    private string FormatUploadServerName(UploadServer server) =>
        server switch
        {
            UploadServer.None => Loc["CurrentServer"],
            UploadServer.Us => Loc["America"],
            UploadServer.Asia => Loc["Asia"],
            UploadServer.Europe => Loc["Europe"],
            UploadServer.HkTw => Loc["HkTw"],
            _ => server.ToString()
        };

    private void UploadTask_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isEnforcingUploadSelection ||
            e.PropertyName != nameof(UploadTask.IsChecked) ||
            !IsUpdateExistingArchive ||
            sender is not UploadTask changedTask ||
            !changedTask.IsChecked)
            return;

        EnforceSingleUpdateSelection(changedTask);
    }

    private void OpenResource(string path)
    {
        if (SelectedArchive == null || string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show(Loc["NoArchiveSelected"]);
            return;
        }

        var resolvedPath = _storage.ResolveResourcePath(SelectedArchive, path);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            MessageBox.Show(
                string.Format(Loc["ResourceMissing"], resolvedPath ?? path),
                Loc["Preview"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        new ResourcePreviewWindow(resolvedPath)
        {
            Owner = Application.Current.MainWindow
        }.ShowDialog();
    }

    private static string GetResourcePath(LevelArchive archive, string? resource) => resource switch
    {
        "LevelCover" => archive.LevelCoverPath,
        "MainImage" => archive.MainImagePath,
        "AchievementIcon" => archive.AchievementIconPath,
        "Video" => archive.VideoPath,
        _ => string.Empty
    };

    private void ReindexArchives()
    {
        for (var index = 0; index < LevelArchiveList.Count; index++)
            LevelArchiveList[index].Index = index + 1;
    }

    private void RebuildUploadTasks()
    {
        var checkedArchiveIds = UploadTaskList.Where(task => task.IsChecked)
            .Select(task => task.SourceArchive.Id)
            .ToHashSet();
        foreach (var task in UploadTaskList)
            task.PropertyChanged -= UploadTask_PropertyChanged;

        UploadTaskList.Clear();

        foreach (var archive in LevelArchiveList.Where(item => !string.IsNullOrWhiteSpace(item.ArchiveRelativePath)))
        {
            var task = new UploadTask
            {
                UploadIndex = UploadTaskList.Count + 1,
                UploadTitle = archive.LevelName,
                State = UploadTaskState.Pending,
                IsChecked = checkedArchiveIds.Contains(archive.Id),
                SourceArchive = archive
            };
            task.PropertyChanged += UploadTask_PropertyChanged;
            UploadTaskList.Add(task);
        }

        if (IsUpdateExistingArchive)
            EnforceSingleUpdateSelection();
    }

    private void EnforceSingleUpdateSelection(UploadTask? preferredTask = null)
    {
        if (_isEnforcingUploadSelection)
            return;

        try
        {
            _isEnforcingUploadSelection = true;
            var selectedTasks = UploadTaskList.Where(task => task.IsChecked).ToList();
            var taskToKeep = preferredTask?.IsChecked == true
                ? preferredTask
                : selectedTasks.FirstOrDefault();

            foreach (var task in UploadTaskList)
                task.IsChecked = ReferenceEquals(task, taskToKeep);

            if (selectedTasks.Count > 1)
                AppendLog(UploadLogLevel.Warning, Loc["UpdateModeSingleSelectionApplied"]);
        }
        finally
        {
            _isEnforcingUploadSelection = false;
        }
    }

    private void AppendLog(UploadLogLevel level, string message)
    {
        WorkflowPerformanceTrace.Event("ApplicationLog", level.ToString(), message);
        UploadLogEntries.Add(new UploadLogEntry(DateTimeOffset.Now, level, message));
        while (UploadLogEntries.Count > 500)
            UploadLogEntries.RemoveAt(0);
    }
}


