using System.Windows;
using System.Windows.Input;
using MiliastraArchiveTool.Infrastructure;
using MiliastraArchiveTool.Localization;

namespace MiliastraArchiveTool.ViewModels;

public sealed class TagSelectViewModel : ObservableObject
{
    private readonly Window _ownerWindow;
    private string _selectedPlayType = string.Empty;
    private string _selectedGameplay = string.Empty;
    private bool _deviceKeyboard;
    private bool _deviceTouch;
    private bool _deviceGamepad;
    private readonly bool[] _extraSelections = new bool[10];

    public TagSelectViewModel(Window ownerWindow, string initialTags)
    {
        _ownerWindow = ownerWindow;
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
        Load(initialTags);
    }

    public static IReadOnlyList<string> PlayTypes { get; } = ["长线游玩", "轻量趣味"];
    public static IReadOnlyList<string> GameplayTags { get; } =
    [
        "动作冒险", "生存竞技", "第一人称射击", "模拟经营", "多人在线竞技", "肉鸽",
        "第三人称射击", "非对称竞技", "乱斗", "共斗", "派对", "合作", "跑酷",
        "探索解谜", "生存建造", "角色扮演", "情景观赏", "战术策略", "塔防",
        "体育", "竞速", "桌游", "放置", "悬疑惊悚", "音乐节奏", "休闲益智"
    ];

    public static IReadOnlyList<string> ExtraTags { get; } =
    [
        "第一人称", "第三人称", "俯视角", "固定镜头", "越肩视角",
        "休闲", "中等", "困难", "硬核", "动态难度"
    ];

    public string SelectedPlayType { get => _selectedPlayType; set => SetProperty(ref _selectedPlayType, value); }
    public AppLocalization Loc => App.Localization;
    public string SelectedGameplay { get => _selectedGameplay; set => SetProperty(ref _selectedGameplay, value); }
    public bool DeviceKeyboard { get => _deviceKeyboard; set => SetProperty(ref _deviceKeyboard, value); }
    public bool DeviceTouch { get => _deviceTouch; set => SetProperty(ref _deviceTouch, value); }
    public bool DeviceGamepad { get => _deviceGamepad; set => SetProperty(ref _deviceGamepad, value); }
    public string ExtraTip => string.Format(Loc["TagExtraTip"], ExtraSelectedCount);
    public string ResultTags { get; private set; } = string.Empty;
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public bool Extra1 { get => GetExtra(0); set => SetExtra(0, value); }
    public bool Extra2 { get => GetExtra(1); set => SetExtra(1, value); }
    public bool Extra3 { get => GetExtra(2); set => SetExtra(2, value); }
    public bool Extra4 { get => GetExtra(3); set => SetExtra(3, value); }
    public bool Extra5 { get => GetExtra(4); set => SetExtra(4, value); }
    public bool Extra6 { get => GetExtra(5); set => SetExtra(5, value); }
    public bool Extra7 { get => GetExtra(6); set => SetExtra(6, value); }
    public bool Extra8 { get => GetExtra(7); set => SetExtra(7, value); }
    public bool Extra9 { get => GetExtra(8); set => SetExtra(8, value); }
    public bool Extra10 { get => GetExtra(9); set => SetExtra(9, value); }

    private int ExtraSelectedCount => _extraSelections.Count(value => value);

    private bool GetExtra(int index) => _extraSelections[index];

    private void SetExtra(int index, bool value)
    {
        if (_extraSelections[index] == value)
            return;

        _extraSelections[index] = value;
        OnPropertyChanged($"Extra{index + 1}");
        OnPropertyChanged(nameof(ExtraTip));
    }

    private void Load(string tagText)
    {
        var tags = tagText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        SelectedPlayType = PlayTypes.FirstOrDefault(tags.Contains) ?? string.Empty;
        SelectedGameplay = GameplayTags.FirstOrDefault(tags.Contains) ?? string.Empty;
        DeviceKeyboard = tags.Contains("键盘鼠标");
        DeviceTouch = tags.Contains("触屏");
        DeviceGamepad = tags.Contains("手柄");

        for (var index = 0; index < ExtraTags.Count; index++)
            SetExtra(index, tags.Contains(ExtraTags[index]));
    }

    private void Confirm()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SelectedPlayType))
            errors.Add(Loc["TagErrorPlayType"]);
        if (!DeviceKeyboard && !DeviceTouch && !DeviceGamepad)
            errors.Add(Loc["TagErrorDevice"]);
        if (string.IsNullOrWhiteSpace(SelectedGameplay))
            errors.Add(Loc["TagErrorGameplay"]);
        if (ExtraSelectedCount > 6)
            errors.Add(Loc["TagErrorExtraLimit"]);

        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, errors), Loc["TagValidationTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tags = new List<string> { SelectedPlayType };
        if (DeviceKeyboard) tags.Add("键盘鼠标");
        if (DeviceTouch) tags.Add("触屏");
        if (DeviceGamepad) tags.Add("手柄");
        tags.Add(SelectedGameplay);
        tags.AddRange(ExtraTags.Where((_, index) => _extraSelections[index]));
        ResultTags = string.Join(",", tags);
        _ownerWindow.DialogResult = true;
    }

    private void Cancel() => _ownerWindow.DialogResult = false;
}
