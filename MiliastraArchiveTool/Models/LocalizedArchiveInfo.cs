using System.Text.Json.Serialization;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Models;

public sealed class LocalizedArchiveInfo : ObservableObject
{
    private string _languageCode = string.Empty;
    private string _languageName = string.Empty;
    private bool _isDefault;
    private string _levelName = string.Empty;
    private string _gameDescription = string.Empty;
    private string _levelDescription = string.Empty;
    private string _earlyAccessDescription = string.Empty;
    private string _versionLog = string.Empty;

    public string LanguageCode
    {
        get => _languageCode;
        set
        {
            if (SetProperty(ref _languageCode, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string LanguageName
    {
        get => _languageName;
        set
        {
            if (SetProperty(ref _languageName, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (SetProperty(ref _isDefault, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(CanRemove));
            }
        }
    }

    public string LevelName { get => _levelName; set => SetProperty(ref _levelName, value); }
    public string GameDescription { get => _gameDescription; set => SetProperty(ref _gameDescription, value); }
    public string LevelDescription { get => _levelDescription; set => SetProperty(ref _levelDescription, value); }
    public string EarlyAccessDescription { get => _earlyAccessDescription; set => SetProperty(ref _earlyAccessDescription, value); }
    public string VersionLog { get => _versionLog; set => SetProperty(ref _versionLog, value); }

    [JsonIgnore]
    public string DisplayName => IsDefault ? $"{LanguageName} (Default)" : LanguageName;

    [JsonIgnore]
    public bool CanRemove => !IsDefault;

    public LocalizedArchiveInfo Clone() => new()
    {
        LanguageCode = LanguageCode,
        LanguageName = LanguageName,
        IsDefault = IsDefault,
        LevelName = LevelName,
        GameDescription = GameDescription,
        LevelDescription = LevelDescription,
        EarlyAccessDescription = EarlyAccessDescription,
        VersionLog = VersionLog
    };
}
