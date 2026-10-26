using System.Windows;
using System.Windows.Input;
using MiliastraArchiveTool.ViewModels;

namespace MiliastraArchiveTool.Views;

public partial class TagSelectWindow : Window
{
    private readonly TagSelectViewModel _viewModel;

    public TagSelectWindow(string initialTags)
    {
        InitializeComponent();
        _viewModel = new TagSelectViewModel(this, initialTags);
        DataContext = _viewModel;
    }

    public string ResultTags => _viewModel.ResultTags;

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
