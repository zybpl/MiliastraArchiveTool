using System.Collections.ObjectModel;
using System.Windows;
using MiliastraArchiveTool.Localization;
using MiliastraArchiveTool.ViewModels;

namespace MiliastraArchiveTool.Views;

public partial class AddLanguageWindow : Window
{
    private readonly string _preferredLanguageCode;

    public AddLanguageWindow(IEnumerable<ArchiveLanguage> languages, string preferredLanguageCode)
    {
        _preferredLanguageCode = preferredLanguageCode;
        InitializeComponent();
        Languages = new ObservableCollection<ArchiveLanguage>(languages);
        DataContext = this;
        Loaded += OnLoaded;
    }

    public AppLocalization Loc => App.Localization;
    public ObservableCollection<ArchiveLanguage> Languages { get; }
    public IReadOnlyList<ArchiveLanguage> SelectedLanguages =>
        LanguageList.SelectedItems.OfType<ArchiveLanguage>().ToList();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var preferredLanguage = Languages.FirstOrDefault(language =>
                                    string.Equals(language.Code, _preferredLanguageCode, StringComparison.OrdinalIgnoreCase))
                                ?? Languages.FirstOrDefault();

        if (preferredLanguage != null)
            LanguageList.SelectedItems.Add(preferredLanguage);
    }

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedLanguages.Count == 0)
            return;

        DialogResult = true;
    }
}
