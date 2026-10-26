namespace MiliastraArchiveTool.Models;

public enum UploadLogLevel
{
    Information,
    Warning,
    Error
}

public sealed record UploadLogEntry(DateTimeOffset Timestamp, UploadLogLevel Level, string Message)
{
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string LevelText => App.Localization[$"UploadLogLevel_{Level}"];
    public string ForegroundBrush => Level switch
    {
        UploadLogLevel.Warning => "#B45309",
        UploadLogLevel.Error => "#DC2626",
        _ => "#111827"
    };

    public void RefreshDisplayText()
    {
    }
}
