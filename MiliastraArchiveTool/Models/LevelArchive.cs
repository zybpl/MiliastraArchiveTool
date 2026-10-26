using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json.Serialization;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Models;

public sealed class LevelArchive : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private int _index;
    private string _levelName = string.Empty;
    private string _defaultLanguage = "en";
    private string _archiveFileName = string.Empty;
    private string _archiveRelativePath = string.Empty;
    private string _gameDescription = string.Empty;
    private string _levelDescription = string.Empty;
    private bool _isEarlyAccess;
    private string _earlyAccessDescription = string.Empty;
    private string _tags = string.Empty;
    private string _versionLog = string.Empty;
    private string _levelCoverPath = string.Empty;
    private string _mainImagePath = string.Empty;
    private string _achievementIconPath = string.Empty;
    private string _luxuryGiftIconPath = string.Empty;
    private string _colorfulGiftIconPath = string.Empty;
    private string _questGiftIconPath = string.Empty;
    private string _videoPath = string.Empty;
    private ObservableCollection<LocalizedArchiveInfo> _localizedInfos = new();
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime _updatedAt = DateTime.UtcNow;

    public LevelArchive()
    {
        _localizedInfos.CollectionChanged += OnLocalizedInfosChanged;
    }

    public Guid Id { get => _id; set => SetProperty(ref _id, value); }
    public int Index { get => _index; set => SetProperty(ref _index, value); }

    public string LevelName
    {
        get => _levelName;
        set
        {
            if (SetProperty(ref _levelName, value))
                SyncArchiveFileName();
        }
    }

    public string DefaultLanguage { get => _defaultLanguage; set => SetProperty(ref _defaultLanguage, value); }

    public string ArchiveFileName { get => _archiveFileName; set => SetProperty(ref _archiveFileName, value); }
    public string ArchiveRelativePath { get => _archiveRelativePath; set => SetProperty(ref _archiveRelativePath, value); }

    [JsonIgnore]
    public string PendingArchiveSourcePath { get; set; } = string.Empty;

    public string GameDescription { get => _gameDescription; set => SetProperty(ref _gameDescription, value); }
    public string LevelDescription { get => _levelDescription; set => SetProperty(ref _levelDescription, value); }
    public bool IsEarlyAccess { get => _isEarlyAccess; set => SetProperty(ref _isEarlyAccess, value); }
    public string EarlyAccessDescription { get => _earlyAccessDescription; set => SetProperty(ref _earlyAccessDescription, value); }
    public string Tags
    {
        get => _tags;
        set
        {
            if (SetProperty(ref _tags, value))
                OnPropertyChanged(nameof(LocalizedTags));
        }
    }
    public string VersionLog { get => _versionLog; set => SetProperty(ref _versionLog, value); }
    public string LevelCoverPath { get => _levelCoverPath; set => SetProperty(ref _levelCoverPath, value); }
    public string MainImagePath { get => _mainImagePath; set => SetProperty(ref _mainImagePath, value); }
    public string AchievementIconPath { get => _achievementIconPath; set => SetProperty(ref _achievementIconPath, value); }
    private bool? _submitLuxuryGiftIcon;
    public bool SubmitLuxuryGiftIcon { get => _submitLuxuryGiftIcon ?? !string.IsNullOrWhiteSpace(LuxuryGiftIconPath); set => SetProperty(ref _submitLuxuryGiftIcon, (bool?)value); }
    public string LuxuryGiftIconPath { get => _luxuryGiftIconPath; set => SetProperty(ref _luxuryGiftIconPath, value); }
    private bool? _submitColorfulGiftIcon;
    public bool SubmitColorfulGiftIcon { get => _submitColorfulGiftIcon ?? !string.IsNullOrWhiteSpace(ColorfulGiftIconPath); set => SetProperty(ref _submitColorfulGiftIcon, (bool?)value); }
    public string ColorfulGiftIconPath { get => _colorfulGiftIconPath; set => SetProperty(ref _colorfulGiftIconPath, value); }
    private bool? _submitQuestGiftIcon;
    public bool SubmitQuestGiftIcon { get => _submitQuestGiftIcon ?? !string.IsNullOrWhiteSpace(QuestGiftIconPath); set => SetProperty(ref _submitQuestGiftIcon, (bool?)value); }
    public string QuestGiftIconPath { get => _questGiftIconPath; set => SetProperty(ref _questGiftIconPath, value); }
    public ObservableCollection<string> ExtraImagePaths { get; set; } = new();
    public string VideoPath { get => _videoPath; set => SetProperty(ref _videoPath, value); }

    public ObservableCollection<LocalizedArchiveInfo> LocalizedInfos
    {
        get => _localizedInfos;
        set
        {
            if (ReferenceEquals(_localizedInfos, value))
                return;

            _localizedInfos.CollectionChanged -= OnLocalizedInfosChanged;
            _localizedInfos = value ?? new ObservableCollection<LocalizedArchiveInfo>();
            _localizedInfos.CollectionChanged += OnLocalizedInfosChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MultiLanguageSummary));
        }
    }

    [JsonIgnore]
    public string MultiLanguageSummary => LocalizedInfos.Count == 0
        ? App.Localization["DefaultLanguage"]
        : string.Format(App.Localization["AdditionalLanguages"], LocalizedInfos.Count);

    [JsonIgnore]
    public string LocalizedTags => string.Join(
        ",",
        Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(LocalizeTag));

    public DateTime CreatedAt { get => _createdAt; set => SetProperty(ref _createdAt, value); }
    public DateTime UpdatedAt { get => _updatedAt; set => SetProperty(ref _updatedAt, value); }

    public void RefreshLocalizedSummary()
    {
        OnPropertyChanged(nameof(MultiLanguageSummary));
        OnPropertyChanged(nameof(LocalizedTags));
    }

    public void SyncArchiveFileName()
    {
        ArchiveFileName = GetArchiveFileName(LevelName);
    }

    public static string GetArchiveFileName(string levelName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string((levelName ?? string.Empty)
            .Trim()
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "archive";

        if (sanitized.Length > 120)
            sanitized = sanitized[..120].TrimEnd('.', ' ');

        var reservedNames = new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        if (reservedNames.Contains(sanitized, StringComparer.OrdinalIgnoreCase))
            sanitized = "_" + sanitized;

        return sanitized + ".gil";
    }

    private void OnLocalizedInfosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(MultiLanguageSummary));
    }

    private static string LocalizeTag(string tag)
    {
        return TagResourceKeys.TryGetValue(tag, out var resourceKey)
            ? App.Localization[resourceKey]
            : tag;
    }

    private static readonly IReadOnlyDictionary<string, string> TagResourceKeys = new Dictionary<string, string>
    {
        ["长线游玩"] = "Tag_LongTermPlay",
        ["轻量趣味"] = "Tag_CasualEnjoyment",
        ["键盘鼠标"] = "KeyboardMouse",
        ["触屏"] = "TouchScreen",
        ["手柄"] = "Gamepad",
        ["动作冒险"] = "Tag_ActionAdventure",
        ["生存竞技"] = "Tag_SurvivalChallenge",
        ["第一人称射击"] = "Tag_FirstPersonShooter",
        ["模拟经营"] = "Tag_ManagementSimulation",
        ["多人在线竞技"] = "Tag_CompetitiveMultiplayer",
        ["肉鸽"] = "Tag_Roguelike",
        ["第三人称射击"] = "Tag_ThirdPersonShooter",
        ["非对称竞技"] = "Tag_AsymmetricBattleArena",
        ["乱斗"] = "Tag_BrawlStyle",
        ["共斗"] = "Tag_CoOpBattle",
        ["派对"] = "Tag_Party",
        ["合作"] = "Tag_Collaboration",
        ["跑酷"] = "Tag_Parkour",
        ["探索解谜"] = "Tag_ExplorationPuzzleSolving",
        ["生存建造"] = "Tag_SurvivalConstruction",
        ["角色扮演"] = "Tag_RolePlaying",
        ["情景观赏"] = "Tag_Atmospheric",
        ["战术策略"] = "Tag_Strategy",
        ["塔防"] = "Tag_TowerDefense",
        ["体育"] = "Tag_Sports",
        ["竞速"] = "Tag_Racing",
        ["桌游"] = "Tag_Tabletop",
        ["放置"] = "Tag_Idle",
        ["悬疑惊悚"] = "Tag_MysterySuspense",
        ["音乐节奏"] = "Tag_MusicRhythm",
        ["休闲益智"] = "Tag_CasualPuzzle",
        ["第一人称"] = "Tag_FirstPerson",
        ["第三人称"] = "Tag_ThirdPerson",
        ["俯视角"] = "Tag_BirdsEyeView",
        ["固定镜头"] = "Tag_FixedCamera",
        ["越肩视角"] = "Tag_OverShoulderShot",
        ["休闲"] = "Tag_Casual",
        ["中等"] = "Tag_Moderate",
        ["困难"] = "Tag_Hard",
        ["硬核"] = "Tag_Hardcore",
        ["动态难度"] = "Tag_DynamicDifficulty"
    };
}
