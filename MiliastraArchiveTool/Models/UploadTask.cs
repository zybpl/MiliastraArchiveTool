using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Models;

public enum UploadTaskState
{
    Pending,
    Validating,
    Ready,
    WaitingForGame,
    Recognizing,
    Navigating,
    Filling,
    Submitting,
    Verifying,
    Paused,
    Interrupted,
    Succeeded,
    Failed,
    Cancelled
}

public sealed class UploadTask : ObservableObject
{
    private int _uploadIndex;
    private string _uploadTitle = string.Empty;
    private bool _isChecked;
    private UploadTaskState _state = UploadTaskState.Pending;
    private int _attemptCount;
    private string _durationText = string.Empty;
    private string _errorMessage = string.Empty;

    public int UploadIndex { get => _uploadIndex; set => SetProperty(ref _uploadIndex, value); }
    public string UploadTitle { get => _uploadTitle; set => SetProperty(ref _uploadTitle, value); }
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
    public UploadTaskState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
                OnPropertyChanged(nameof(UploadState));
        }
    }

    public string UploadState => App.Localization[$"UploadTaskState_{State}"];
    public int AttemptCount { get => _attemptCount; set => SetProperty(ref _attemptCount, value); }
    public string DurationText { get => _durationText; set => SetProperty(ref _durationText, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public LevelArchive SourceArchive { get; set; } = new();

    public void RefreshDisplayText() => OnPropertyChanged(nameof(UploadState));
}
