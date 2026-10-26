using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

internal static class ImageAssetTabVisualState
{
    public static bool IsSelected(BitmapSource source, AutomationTarget target)
    {
        var bitmap = source.Format == PixelFormats.Bgra32 ? source :
            new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var x = Math.Clamp((int)((target.CenterX - target.Width * .4) * bitmap.PixelWidth), 0, bitmap.PixelWidth - 1);
        var y = Math.Clamp((int)((target.CenterY - target.Height * .3) * bitmap.PixelHeight), 0, bitmap.PixelHeight - 1);
        var w = Math.Clamp((int)(target.Width * .8 * bitmap.PixelWidth), 1, bitmap.PixelWidth - x);
        var h = Math.Clamp((int)(target.Height * .6 * bitmap.PixelHeight), 1, bitmap.PixelHeight - y);
        var pixels = new byte[w * h * 4];
        bitmap.CopyPixels(new Int32Rect(x, y, w, h), pixels, w * 4, 0);
        var dark = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] < 180 && pixels[i + 1] < 160 && pixels[i + 2] < 160) dark++;
        // Selected tabs have a dark blue background; unselected tabs are white.
        return dark / (double)(w * h) > .6;
    }
}
