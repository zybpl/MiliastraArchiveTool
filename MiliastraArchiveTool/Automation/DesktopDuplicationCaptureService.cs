using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace MiliastraArchiveTool.Automation;

public sealed record DesktopDuplicationFrame(
    BitmapSource Bitmap,
    int ScreenLeft,
    int ScreenTop);

public sealed class DesktopDuplicationCaptureService
{
    public DesktopDuplicationFrame CaptureMonitor(nint windowHandle)
    {
        var targetMonitor = MonitorFromWindow(windowHandle, 2);
        if (targetMonitor == nint.Zero)
            throw new InvalidOperationException("Unable to determine which monitor contains the game window.");

        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            var adapterResult = factory.EnumAdapters1(adapterIndex, out var adapter);
            if (adapterResult.Failure)
                break;

            using (adapter)
            {
                for (uint outputIndex = 0; ; outputIndex++)
                {
                    var outputResult = adapter.EnumOutputs(outputIndex, out var output);
                    if (outputResult.Failure)
                        break;

                    using (output)
                    {
                        if (output.Description.Monitor != targetMonitor)
                            continue;

                        var desktopCoordinates = output.Description.DesktopCoordinates;
                        return new DesktopDuplicationFrame(
                            CaptureOutput(adapter, output),
                            desktopCoordinates.Left,
                            desktopCoordinates.Top);
                    }
                }
            }
        }

        throw new InvalidOperationException("No DXGI output was found for the game window monitor.");
    }

    public static BitmapSource CropToClientArea(DesktopDuplicationFrame frame, GameWindowInfo window)
    {
        var left = window.ClientScreenLeft - frame.ScreenLeft;
        var top = window.ClientScreenTop - frame.ScreenTop;
        var crop = new System.Windows.Int32Rect(
            Math.Max(0, left),
            Math.Max(0, top),
            Math.Min(window.ClientWidth, frame.Bitmap.PixelWidth - Math.Max(0, left)),
            Math.Min(window.ClientHeight, frame.Bitmap.PixelHeight - Math.Max(0, top)));
        if (crop.Width <= 0 || crop.Height <= 0)
            throw new InvalidOperationException("The game client area is outside the captured monitor frame.");

        var cropped = new CroppedBitmap(frame.Bitmap, crop);
        cropped.Freeze();
        return cropped;
    }

    private static BitmapSource CaptureOutput(IDXGIAdapter1 adapter, IDXGIOutput output)
    {
        var createResult = D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            Array.Empty<FeatureLevel>(),
            out ID3D11Device device,
            out ID3D11DeviceContext context);
        createResult.CheckError();

        using (device)
        using (context)
        using (var output1 = output.QueryInterface<IDXGIOutput1>())
        using (var duplication = output1.DuplicateOutput(device))
        {
            IDXGIResource? desktopResource = null;
            var frameAcquired = false;
            try
            {
                var acquireResult = duplication.AcquireNextFrame(1500, out _, out desktopResource);
                acquireResult.CheckError();
                frameAcquired = true;

                using var sourceTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
                var sourceDescription = sourceTexture.Description;
                var stagingDescription = sourceDescription;
                stagingDescription.Usage = ResourceUsage.Staging;
                stagingDescription.BindFlags = BindFlags.None;
                stagingDescription.CPUAccessFlags = CpuAccessFlags.Read;
                stagingDescription.MiscFlags = ResourceOptionFlags.None;

                using var stagingTexture = device.CreateTexture2D(stagingDescription);
                context.CopyResource(stagingTexture, sourceTexture);
                var mapped = context.Map(stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var width = checked((int)sourceDescription.Width);
                    var height = checked((int)sourceDescription.Height);
                    var targetStride = width * 4;
                    var pixels = new byte[targetStride * height];
                    for (var row = 0; row < height; row++)
                    {
                        Marshal.Copy(
                            new IntPtr(mapped.DataPointer + (long)row * mapped.RowPitch),
                            pixels,
                            row * targetStride,
                            targetStride);
                    }

                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, targetStride);
                    bitmap.Freeze();
                    return bitmap;
                }
                finally
                {
                    context.Unmap(stagingTexture, 0);
                }
            }
            finally
            {
                desktopResource?.Dispose();
                if (frameAcquired)
                    duplication.ReleaseFrame();
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);
}
