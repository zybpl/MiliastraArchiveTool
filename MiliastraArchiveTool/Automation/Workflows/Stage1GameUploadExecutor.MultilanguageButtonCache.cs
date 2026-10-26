using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed partial class Stage1GameUploadExecutor
{
    private (AutomationTarget Target, string Pixels, int Width, int Height, nint Window)? _multilanguageConfirm;

    private static string ButtonPixels(BitmapSource bitmap, AutomationTarget target)
    {
        var x = Math.Clamp((int)((target.CenterX-target.Width/2)*bitmap.PixelWidth),0,bitmap.PixelWidth-1);
        var y = Math.Clamp((int)((target.CenterY-target.Height/2)*bitmap.PixelHeight),0,bitmap.PixelHeight-1);
        var rect = new Int32Rect(x,y,Math.Clamp((int)(target.Width*bitmap.PixelWidth),1,bitmap.PixelWidth-x),
            Math.Clamp((int)(target.Height*bitmap.PixelHeight),1,bitmap.PixelHeight-y));
        var gray = new FormatConvertedBitmap(new CroppedBitmap(bitmap,rect),PixelFormats.Gray8,null,0);
        var pixels = new byte[gray.PixelWidth*gray.PixelHeight]; gray.CopyPixels(pixels,gray.PixelWidth,0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }

    private async Task<OcrTargetMatch> FindMultilanguageConfirmAsync(StablePageResult current,
        List<string> logs, CancellationToken ct)
    {
        var bitmap = current.Capture.Bitmap;
        if (current.Recognition.Page == GamePage.TextEditDialog && _multilanguageConfirm is { } cached &&
            cached.Width == bitmap.PixelWidth && cached.Height == bitmap.PixelHeight && cached.Window == current.Capture.Window.Handle &&
            current.Recognition.Targets.Any(t => t.Name == WorkflowTargetNames.PrimaryActionButton && t.Confidence >= .35 &&
                Math.Abs(t.CenterX-cached.Target.CenterX)<.01 && Math.Abs(t.CenterY-cached.Target.CenterY)<.01) &&
            ButtonPixels(bitmap,cached.Target) == cached.Pixels)
        {
            LogWorkflow(logs,"step=Multilanguage action=ConfirmButton source=verified_pixels ocrSkipped=True");
            return new OcrTargetMatch(cached.Target, []);
        }
        _multilanguageConfirm = null;
        var found = await FindPrimaryButtonByOcrAsync(current, ["OK","CONFIRM","确定","确认","SUBMIT","提交"], ct);
        if (found.Target != null && current.Recognition.Page == GamePage.TextEditDialog)
            _multilanguageConfirm = (found.Target,ButtonPixels(bitmap,found.Target),bitmap.PixelWidth,bitmap.PixelHeight,current.Capture.Window.Handle);
        LogWorkflow(logs,"step=Multilanguage action=ConfirmButton source=ocr");
        return found;
    }
}
