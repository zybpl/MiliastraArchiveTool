using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using MiliastraArchiveTool.Infrastructure;
using MiliastraArchiveTool.Localization;
using MiliastraArchiveTool.Models;

namespace MiliastraArchiveTool.ViewModels;

public sealed record ArchiveLanguage(string Code, string Name);

public sealed class LocalizedDetailsViewModel : ObservableObject
{
    public string DefaultLanguageCode => _archive.DefaultLanguage == "zh-CN" ? "zh-Hans" : "en";
    public static IReadOnlyList<ArchiveLanguage> DefaultLanguages { get; } = [new("en", "English"), new("zh-CN", "简体中文")];
    private readonly Window _ownerWindow;
    private readonly LevelArchive _archive;
    private LocalizedArchiveInfo? _selectedEntry;

    public LocalizedDetailsViewModel(Window ownerWindow, LevelArchive archive)
    {
        _ownerWindow = ownerWindow;
        _archive = archive;
        var existingDefaultLanguageEntry = archive.LocalizedInfos.FirstOrDefault(entry =>
            string.Equals(entry.LanguageCode, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase));
        Entries.Add(CreateDefaultEntry(existingDefaultLanguageEntry));


        foreach (var entry in archive.LocalizedInfos.Where(entry =>
                     !entry.IsDefault &&
                     !string.Equals(entry.LanguageCode, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase)))
            Entries.Add(entry.Clone());

        // Preserve the old format, which stored English separately and Chinese
        // in the main fields. Ordinary new archives store no separate default entry.
        if (DefaultLanguageCode == "en" && existingDefaultLanguageEntry != null &&
            Entries.All(entry => entry.LanguageCode != "zh-Hans"))
        {
            Entries.Add(new LocalizedArchiveInfo
            {
                LanguageCode = "zh-Hans", LanguageName = "简体中文",
                LevelName = archive.LevelName, GameDescription = archive.GameDescription,
                LevelDescription = archive.LevelDescription,
                EarlyAccessDescription = archive.EarlyAccessDescription, VersionLog = archive.VersionLog
            });
        }

        SelectedEntry = Entries.FirstOrDefault();
        RemoveLanguageCommand = new RelayCommand(RemoveSelectedLanguage, () => SelectedEntry is { IsDefault: false });
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    public static IReadOnlyList<ArchiveLanguage> AllLanguages { get; } =
    [
        new("zh-Hans", "简体中文"),
        new("zh-Hant", "繁体中文"),
        new("en", "English"),
        new("ko", "한국어"),
        new("ja", "日本語"),
        new("es", "Español"),
        new("fr", "Français"),
        new("ru", "Русский"),
        new("th", "ไทย"),
        new("vi", "Tiếng Việt"),
        new("de", "Deutsch"),
        new("id", "Indonesia"),
        new("it", "Italiano"),
        new("pt", "Português"),
        new("tr", "Türkçe")
    ];

    public ObservableCollection<LocalizedArchiveInfo> Entries { get; } = new();
    public AppLocalization Loc => App.Localization;

    public IEnumerable<ArchiveLanguage> AvailableLanguages =>
        AllLanguages.Where(language => Entries.All(entry =>
            !string.Equals(entry.LanguageCode, language.Code, StringComparison.OrdinalIgnoreCase)));

    public LocalizedArchiveInfo? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsEarlyAccess => _archive.IsEarlyAccess;
    public ICommand RemoveLanguageCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    private LocalizedArchiveInfo CreateDefaultEntry(LocalizedArchiveInfo? existingDefaultLanguageEntry) => new()
    {
        LanguageCode = DefaultLanguageCode,
        LanguageName = _archive.DefaultLanguage == "zh-CN" ? "简体中文" : "English",
        IsDefault = true,
        LevelName = existingDefaultLanguageEntry?.LevelName ?? _archive.LevelName,
        GameDescription = existingDefaultLanguageEntry?.GameDescription ?? _archive.GameDescription,
        LevelDescription = existingDefaultLanguageEntry?.LevelDescription ?? _archive.LevelDescription,
        EarlyAccessDescription = existingDefaultLanguageEntry?.EarlyAccessDescription ?? _archive.EarlyAccessDescription,
        VersionLog = existingDefaultLanguageEntry?.VersionLog ?? _archive.VersionLog
    };

    public void AddLanguage(ArchiveLanguage language)
    {
        var entry = new LocalizedArchiveInfo
        {
            LanguageCode = language.Code,
            LanguageName = language.Name
        };
        Entries.Add(entry);
        SelectedEntry = entry;
        OnPropertyChanged(nameof(AvailableLanguages));
    }

    public void RemoveLanguage(LocalizedArchiveInfo? entry)
    {
        if (entry == null)
            return;

        SelectedEntry = entry;
        RemoveSelectedLanguage();
    }

    private void RemoveSelectedLanguage()
    {
        if (SelectedEntry is not { IsDefault: false })
            return;

        var index = Entries.IndexOf(SelectedEntry);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries[Math.Clamp(index - 1, 0, Entries.Count - 1)];
        OnPropertyChanged(nameof(AvailableLanguages));
    }

    private void Confirm()
    {
        var errors = Validate();
        errors.AddRange(ValidatePreviewByteLimits());
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, errors), "多语言配置校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var defaultEntry = Entries.First(entry => entry.IsDefault);
        _archive.LevelName = defaultEntry.LevelName.Trim();
        _archive.GameDescription = defaultEntry.GameDescription.Trim();
        _archive.LevelDescription = defaultEntry.LevelDescription.Trim();
        _archive.EarlyAccessDescription = IsEarlyAccess ? defaultEntry.EarlyAccessDescription.Trim() : string.Empty;
        _archive.VersionLog = defaultEntry.VersionLog.Trim();

        _archive.LocalizedInfos.Clear();
        foreach (var entry in Entries.Where(entry => !entry.IsDefault))
        {
            var clone = entry.Clone();
            clone.LevelName = clone.LevelName.Trim();
            clone.GameDescription = clone.GameDescription.Trim();
            clone.LevelDescription = clone.LevelDescription.Trim();
            clone.EarlyAccessDescription = IsEarlyAccess ? clone.EarlyAccessDescription.Trim() : string.Empty;
            clone.VersionLog = clone.VersionLog.Trim();
            _archive.LocalizedInfos.Add(clone);
        }

        _ownerWindow.DialogResult = true;
    }

    private List<string> Validate()
    {
        var errors = new List<string>();
        foreach (var entry in Entries)
        {
            var prefix = entry.LanguageName;
            if (string.IsNullOrWhiteSpace(entry.LevelName))
                errors.Add($"{prefix}：关卡名称不能为空。");
            if (string.IsNullOrWhiteSpace(entry.LevelDescription))
                errors.Add($"{prefix}：关卡描述不能为空。");
            if (IsEarlyAccess && string.IsNullOrWhiteSpace(entry.EarlyAccessDescription))
                errors.Add($"{prefix}：先行体验说明不能为空。");
            ValidateLength(entry.LevelName, 100, $"{prefix} 关卡名称", errors);
            ValidateLength(entry.GameDescription, 200, $"{prefix} 玩法说明", errors);
            ValidateLength(entry.LevelDescription, 5000, $"{prefix} 关卡描述", errors);
            ValidateLength(entry.EarlyAccessDescription, 200, $"{prefix} 先行体验说明", errors);
            ValidateLength(entry.VersionLog, 1000, $"{prefix} 版本更新记录", errors);
        }

        return errors;
    }

    private List<string> ValidatePreviewByteLimits()
    {
        var errors = new List<string>();
        foreach (var entry in Entries)
        {
            var prefix = string.IsNullOrWhiteSpace(entry.LanguageName) ? entry.LanguageCode : entry.LanguageName;
            ValidateUtf8ByteLength(entry.GameDescription, 400, $"{prefix} Gameplay Details", errors);
            ValidateUtf8ByteLength(entry.LevelDescription, 10000, $"{prefix} Stage Description", errors);
            ValidateUtf8ByteLength(entry.EarlyAccessDescription, 400, $"{prefix} Early Access Version Description", errors);
        }

        return errors;
    }

    private static void ValidateLength(string value, int maximum, string label, ICollection<string> errors)
    {
        if (value.Length > maximum)
            errors.Add($"{label}不能超过 {maximum} 个字符。");
    }

    private static void ValidateUtf8ByteLength(string value, int maximumBytes, string label, ICollection<string> errors)
    {
        if (Encoding.UTF8.GetByteCount(value ?? string.Empty) > maximumBytes)
            errors.Add($"{label} cannot exceed {maximumBytes} UTF-8 bytes.");
    }

    private void Cancel() => _ownerWindow.DialogResult = false;
}
