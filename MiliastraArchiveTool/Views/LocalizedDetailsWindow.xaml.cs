using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.ViewModels;

namespace MiliastraArchiveTool.Views;

public partial class LocalizedDetailsWindow : Window
{
    public LocalizedDetailsWindow(LevelArchive archive)
    {
        InitializeComponent();
        DataContext = new LocalizedDetailsViewModel(this, archive);
    }

    private LocalizedDetailsViewModel ViewModel => (LocalizedDetailsViewModel)DataContext;

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void AddLanguage_OnClick(object sender, RoutedEventArgs e)
    {
        var availableLanguages = ViewModel.AvailableLanguages.ToList();
        if (availableLanguages.Count == 0)
            return;

        var preferredCode = App.Localization.CurrentLanguage == Localization.AppLocalization.ChineseSimplified
            ? "zh-Hans"
            : "en";
        var window = new AddLanguageWindow(availableLanguages, preferredCode)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            foreach (var language in window.SelectedLanguages)
                ViewModel.AddLanguage(language);
        }
    }

    private void RemoveLanguage_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LocalizedArchiveInfo entry })
            ViewModel.RemoveLanguage(entry);
    }

    private void EditLongText_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LocalizedArchiveInfo entry, Tag: string fieldName })
            return;

        var (title, maximum, currentText, setter) = fieldName switch
        {
            nameof(LocalizedArchiveInfo.GameDescription) => (
                $"{ViewModel.Loc["GameplayDetailsColumn"]}_{entry.LanguageName}",
                200,
                entry.GameDescription,
                new Action<string>(value => entry.GameDescription = value)),
            nameof(LocalizedArchiveInfo.LevelDescription) => (
                $"{ViewModel.Loc["StageDescriptionColumn"]}_{entry.LanguageName}",
                5000,
                entry.LevelDescription,
                value => entry.LevelDescription = value),
            nameof(LocalizedArchiveInfo.EarlyAccessDescription) => (
                $"{ViewModel.Loc["EarlyAccessDescriptionRequired"]}_{entry.LanguageName}",
                200,
                entry.EarlyAccessDescription,
                value => entry.EarlyAccessDescription = value),
            nameof(LocalizedArchiveInfo.VersionLog) => (
                $"{ViewModel.Loc["UpdateLogColumn"]}_{entry.LanguageName}",
                1000,
                entry.VersionLog,
                value => entry.VersionLog = value),
            _ => default
        };

        if (setter == null)
            return;

        var window = new LongTextEditWindow(title, currentText, maximum)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
            setter(window.EditedText);
    }
}
