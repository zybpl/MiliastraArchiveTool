using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MiliastraArchiveTool.Views;

public partial class ResourcePreviewWindow : Window
{
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".avi" };

    public ResourcePreviewWindow(string resourcePath)
    {
        InitializeComponent();
        DataContext = App.Localization;
        PathText.Text = resourcePath;
        Title = $"{App.Localization["ResourcePreviewTitle"]} - {Path.GetFileName(resourcePath)}";

        if (VideoExtensions.Contains(Path.GetExtension(resourcePath)))
            LoadVideo(resourcePath);
        else
            LoadImage(resourcePath);
    }

    private void LoadImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        ImagePreview.Source = bitmap;
        ImagePreview.Visibility = Visibility.Visible;
    }

    private void LoadVideo(string path)
    {
        VideoPreview.Source = new Uri(path, UriKind.Absolute);
        VideoPreview.Visibility = Visibility.Visible;
        VideoControls.Visibility = Visibility.Visible;
        VideoPreview.MediaOpened += (_, _) => VideoPreview.Play();
    }

    private void Play_OnClick(object sender, RoutedEventArgs e) => VideoPreview.Play();
    private void Pause_OnClick(object sender, RoutedEventArgs e) => VideoPreview.Pause();
    private void Stop_OnClick(object sender, RoutedEventArgs e) => VideoPreview.Stop();

    private void VideoPreview_OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        MessageBox.Show(
            $"无法播放该视频：{e.ErrorException.Message}",
            "视频预览失败",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    protected override void OnClosed(EventArgs e)
    {
        VideoPreview.Stop();
        VideoPreview.Close();
        base.OnClosed(e);
    }
}
