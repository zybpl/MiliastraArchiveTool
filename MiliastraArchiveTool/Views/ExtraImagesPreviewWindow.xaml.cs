using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MiliastraArchiveTool.Views;

public partial class ExtraImagesPreviewWindow : Window
{
    private readonly ObservableCollection<ExtraImagePreviewItem> _items = new();
    private readonly Func<string, bool> _deleteImage;
    private int _failedCount;

    public ExtraImagesPreviewWindow(
        IEnumerable<(string OriginalPath, string FullPath)> imagePaths,
        Func<string, bool> deleteImage)
    {
        InitializeComponent();
        DataContext = App.Localization;
        _deleteImage = deleteImage;

        foreach (var item in imagePaths.Where(item => File.Exists(item.FullPath)))
        {
            try
            {
                _items.Add(new ExtraImagePreviewItem(
                    item.OriginalPath,
                    item.FullPath,
                    LoadBitmap(item.FullPath, 160)));
            }
            catch
            {
                _failedCount++;
            }
        }

        ImageList.ItemsSource = _items;
        RefreshCount();
        if (_items.Count > 0)
            ImageList.SelectedIndex = 0;
    }

    private void ImageList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageList.SelectedItem is not ExtraImagePreviewItem item)
        {
            LargePreview.Source = null;
            EmptyTip.Text = App.Localization["NoPreviewImage"];
            EmptyTip.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            LargePreview.Source = LoadBitmap(item.FullPath, 1600);
            EmptyTip.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            LargePreview.Source = null;
            EmptyTip.Text = string.Format(App.Localization["ImageLoadFailed"], ex.Message);
            EmptyTip.Visibility = Visibility.Visible;
        }
    }

    private void ImageList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedPreview();

    private void PreviewSelected_OnClick(object sender, RoutedEventArgs e) => OpenSelectedPreview();

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DeleteSelected_OnClick(object sender, RoutedEventArgs e)
    {
        if (ImageList.SelectedItem is not ExtraImagePreviewItem item)
        {
            MessageBox.Show(
                App.Localization["SelectImageBeforeDelete"],
                App.Localization["DeleteSelectedImage"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var removeTip = Path.IsPathRooted(item.OriginalPath)
            ? App.Localization["RemoveExternalImageTip"]
            : App.Localization["RemoveManagedImageTip"];

        if (MessageBox.Show(
                string.Format(App.Localization["ConfirmDeleteImageMessage"], item.FileName, Environment.NewLine, removeTip),
                App.Localization["ConfirmDeleteImageTitle"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var oldIndex = ImageList.SelectedIndex;
        if (!_deleteImage(item.OriginalPath))
            return;

        _items.Remove(item);
        LargePreview.Source = null;
        RefreshCount();

        if (_items.Count > 0)
            ImageList.SelectedIndex = Math.Min(oldIndex, _items.Count - 1);
        else
            EmptyTip.Visibility = Visibility.Visible;
    }

    private void OpenSelectedPreview()
    {
        if (ImageList.SelectedItem is not ExtraImagePreviewItem item)
            return;

        new ResourcePreviewWindow(item.FullPath)
        {
            Owner = this
        }.ShowDialog();
    }

    private static BitmapImage LoadBitmap(string path, int decodeWidth)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void RefreshCount()
    {
        CountText.Text = _failedCount == 0
            ? string.Format(App.Localization["ExtraImageCount"], _items.Count)
            : string.Format(App.Localization["ExtraImageCountWithFailures"], _items.Count, _failedCount);
    }

    private sealed record ExtraImagePreviewItem(
        string OriginalPath,
        string FullPath,
        BitmapImage Thumbnail)
    {
        public string FileName => Path.GetFileName(FullPath);
    }
}
