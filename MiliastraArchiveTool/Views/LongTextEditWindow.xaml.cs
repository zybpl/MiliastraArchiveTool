using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MiliastraArchiveTool.Localization;

namespace MiliastraArchiveTool.Views;

public partial class LongTextEditWindow : Window, INotifyPropertyChanged
{
    private string _editedText;

    public LongTextEditWindow(string titleText, string initialText, int maximumLength)
    {
        TitleText = titleText;
        _editedText = initialText;
        MaximumLength = maximumLength;
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLocalization Loc => App.Localization;
    public string TitleText { get; }
    public int MaximumLength { get; }

    public string EditedText
    {
        get => _editedText;
        set
        {
            if (_editedText == value)
                return;

            _editedText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CountText));
        }
    }

    public string CountText => $"{EditedText.Length}/{MaximumLength}";

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
