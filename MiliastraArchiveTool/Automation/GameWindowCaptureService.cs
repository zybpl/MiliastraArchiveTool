using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiliastraArchiveTool.Automation;

public sealed record GameWindowInfo(
    nint Handle,
    int ProcessId,
    string Title,
    int ClientWidth,
    int ClientHeight,
    int ClientScreenLeft,
    int ClientScreenTop,
    uint Dpi,
    bool IsFullscreenLike);

public sealed record GameWindowCandidateInfo(
    nint Handle,
    int ProcessId,
    string Title,
    bool IsVisible,
    bool IsIconic,
    int ClientWidth,
    int ClientHeight,
    int WindowWidth,
    int WindowHeight,
    long ClientArea);

public sealed class GameWindowCaptureService : IGameWindowFinder
{
    private const string GameProcessName = "GenshinImpact";
    private const string ExpectedWindowTitle = "原神";
    private const int SourceCopy = 0x00CC0020;
    private const int RestoreWindow = 9;
    private const uint MonitorDefaultToNearest = 2;

    public GameWindowInfo FindGameWindow()
    {
        var processes = Process.GetProcessesByName(GameProcessName);
        try
        {
            if (processes.Length == 0)
                throw new InvalidOperationException("GenshinImpact.exe process was not found.");

            var candidates = new List<(nint Handle, int ProcessId, string Title)>();
            foreach (var process in processes)
            {
                var processId = process.Id;
                EnumWindows((handle, _) =>
                {
                    GetWindowThreadProcessId(handle, out var windowProcessId);
                    if (windowProcessId != processId || !IsWindowVisible(handle))
                        return true;

                    candidates.Add((handle, processId, GetTitle(handle)));
                    return true;
                }, nint.Zero);
            }

            var candidate = candidates
                .OrderByDescending(item => string.Equals(item.Title, ExpectedWindowTitle, StringComparison.Ordinal))
                .ThenByDescending(item => GetClientArea(item.Handle))
                .FirstOrDefault();
            if (candidate.Handle == nint.Zero)
                throw new InvalidOperationException("Game process was found, but no visible main window was found.");
            RestoreIfMinimized(candidate.Handle);
            if (!GetClientRect(candidate.Handle, out var clientRect))
                throw new InvalidOperationException("Unable to read the game client area size.");

            var width = clientRect.Right - clientRect.Left;
            var height = clientRect.Bottom - clientRect.Top;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("The game client area size is invalid.");

            var clientOrigin = new NativePoint();
            if (!ClientToScreen(candidate.Handle, ref clientOrigin))
                throw new InvalidOperationException("Unable to read the game client area screen position.");

            return new GameWindowInfo(
                candidate.Handle,
                candidate.ProcessId,
                candidate.Title,
                width,
                height,
                clientOrigin.X,
                clientOrigin.Y,
                GetDpiForWindow(candidate.Handle),
                IsFullscreenWindow(candidate.Handle));
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public IReadOnlyList<GameWindowCandidateInfo> FindGameWindowCandidates()
    {
        var processes = Process.GetProcessesByName(GameProcessName);
        try
        {
            var candidates = new List<GameWindowCandidateInfo>();
            foreach (var process in processes)
            {
                var processId = process.Id;
                EnumWindows((handle, _) =>
                {
                    GetWindowThreadProcessId(handle, out var windowProcessId);
                    if (windowProcessId != processId)
                        return true;

                    var isVisible = IsWindowVisible(handle);
                    var isIconic = IsIconic(handle);
                    var title = GetTitle(handle);
                    var clientWidth = 0;
                    var clientHeight = 0;
                    if (GetClientRect(handle, out var clientRect))
                    {
                        clientWidth = Math.Max(0, clientRect.Right - clientRect.Left);
                        clientHeight = Math.Max(0, clientRect.Bottom - clientRect.Top);
                    }

                    var windowWidth = 0;
                    var windowHeight = 0;
                    if (GetWindowRect(handle, out var windowRect))
                    {
                        windowWidth = Math.Max(0, windowRect.Right - windowRect.Left);
                        windowHeight = Math.Max(0, windowRect.Bottom - windowRect.Top);
                    }

                    candidates.Add(new GameWindowCandidateInfo(
                        handle,
                        processId,
                        title,
                        isVisible,
                        isIconic,
                        clientWidth,
                        clientHeight,
                        windowWidth,
                        windowHeight,
                        (long)clientWidth * clientHeight));
                    return true;
                }, nint.Zero);
            }

            return candidates
                .OrderByDescending(candidate => candidate.IsVisible)
                .ThenByDescending(candidate => string.Equals(candidate.Title, ExpectedWindowTitle, StringComparison.Ordinal))
                .ThenByDescending(candidate => candidate.ClientArea)
                .ToList();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public BitmapSource CaptureClientArea(GameWindowInfo window) =>
        CaptureClientAreaCore(window, usePrintWindow: false);

    public BitmapSource CaptureClientAreaWithPrintWindow(GameWindowInfo window) =>
        CaptureClientAreaCore(window, usePrintWindow: true);

    public BitmapSource CaptureClientAreaFromScreen(GameWindowInfo window)
    {
        EnsureCapturable(window);

        var sourceDc = GetDC(nint.Zero);
        if (sourceDc == nint.Zero)
            throw new InvalidOperationException("Unable to acquire the desktop device context.");

        nint memoryDc = nint.Zero;
        nint bitmapHandle = nint.Zero;
        nint previousObject = nint.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(sourceDc);
            bitmapHandle = CreateCompatibleBitmap(sourceDc, window.ClientWidth, window.ClientHeight);
            if (memoryDc == nint.Zero || bitmapHandle == nint.Zero)
                throw new InvalidOperationException("Unable to create the desktop capture buffer.");

            previousObject = SelectObject(memoryDc, bitmapHandle);
            if (!BitBlt(
                    memoryDc,
                    0,
                    0,
                    window.ClientWidth,
                    window.ClientHeight,
                    sourceDc,
                    window.ClientScreenLeft,
                    window.ClientScreenTop,
                    SourceCopy))
            {
                throw new InvalidOperationException("Unable to copy the game client area from the desktop.");
            }

            return CreateFrozenBitmap(bitmapHandle);
        }
        finally
        {
            if (previousObject != nint.Zero && memoryDc != nint.Zero)
                SelectObject(memoryDc, previousObject);
            if (bitmapHandle != nint.Zero)
                DeleteObject(bitmapHandle);
            if (memoryDc != nint.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(nint.Zero, sourceDc);
        }
    }

    public BitmapSource CaptureWindowFromScreen(GameWindowInfo window)
    {
        EnsureCapturable(window);
        if (!GetWindowRect(window.Handle, out var windowRect))
            throw new InvalidOperationException("Unable to read the game window bounds.");

        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("The game window size is invalid.");

        var sourceDc = GetDC(nint.Zero);
        if (sourceDc == nint.Zero)
            throw new InvalidOperationException("Unable to acquire the desktop device context.");

        nint memoryDc = nint.Zero;
        nint bitmapHandle = nint.Zero;
        nint previousObject = nint.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(sourceDc);
            bitmapHandle = CreateCompatibleBitmap(sourceDc, width, height);
            if (memoryDc == nint.Zero || bitmapHandle == nint.Zero)
                throw new InvalidOperationException("Unable to create the window capture buffer.");

            previousObject = SelectObject(memoryDc, bitmapHandle);
            if (!BitBlt(memoryDc, 0, 0, width, height, sourceDc, windowRect.Left, windowRect.Top, SourceCopy))
                throw new InvalidOperationException("Unable to copy the whole game window from the desktop.");

            return CreateFrozenBitmap(bitmapHandle);
        }
        finally
        {
            if (previousObject != nint.Zero && memoryDc != nint.Zero)
                SelectObject(memoryDc, previousObject);
            if (bitmapHandle != nint.Zero)
                DeleteObject(bitmapHandle);
            if (memoryDc != nint.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(nint.Zero, sourceDc);
        }
    }

    public static void SavePng(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static bool IsLikelyBlackFrame(BitmapSource bitmap) =>
        CalculateVisualQuality(bitmap) < 1.5;

    public static double CalculateVisualQuality(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var sampleStepX = Math.Max(1, converted.PixelWidth / 160);
        var sampleStepY = Math.Max(1, converted.PixelHeight / 90);
        var sampled = 0;
        var luminanceSum = 0.0;
        var luminanceSquareSum = 0.0;
        var edgeSum = 0.0;
        var nonDark = 0;

        for (var y = 0; y < converted.PixelHeight; y += sampleStepY)
        {
            var previousLuminance = -1.0;
            for (var x = 0; x < converted.PixelWidth; x += sampleStepX)
            {
                var offset = y * stride + x * 4;
                var luminance = pixels[offset] * 0.0722 + pixels[offset + 1] * 0.7152 + pixels[offset + 2] * 0.2126;
                luminanceSum += luminance;
                luminanceSquareSum += luminance * luminance;
                if (luminance > 12)
                    nonDark++;
                if (previousLuminance >= 0)
                    edgeSum += Math.Abs(luminance - previousLuminance);
                previousLuminance = luminance;
                sampled++;
            }
        }

        if (sampled == 0)
            return 0;

        var mean = luminanceSum / sampled;
        var variance = Math.Max(0, luminanceSquareSum / sampled - mean * mean);
        var standardDeviation = Math.Sqrt(variance);
        var averageEdge = edgeSum / sampled;
        var nonDarkRatio = (double)nonDark / sampled;
        return standardDeviation + averageEdge * 0.5 + nonDarkRatio * 20;
    }

    private static BitmapSource CaptureClientAreaCore(GameWindowInfo window, bool usePrintWindow)
    {
        EnsureCapturable(window);

        var sourceDc = GetDC(window.Handle);
        if (sourceDc == nint.Zero)
            throw new InvalidOperationException("Unable to acquire the game window device context.");

        nint memoryDc = nint.Zero;
        nint bitmapHandle = nint.Zero;
        nint previousObject = nint.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(sourceDc);
            bitmapHandle = CreateCompatibleBitmap(sourceDc, window.ClientWidth, window.ClientHeight);
            if (memoryDc == nint.Zero || bitmapHandle == nint.Zero)
                throw new InvalidOperationException("Unable to create the capture buffer.");

            previousObject = SelectObject(memoryDc, bitmapHandle);
            var captured = usePrintWindow
                ? PrintWindow(window.Handle, memoryDc, 3)
                : BitBlt(memoryDc, 0, 0, window.ClientWidth, window.ClientHeight, sourceDc, 0, 0, SourceCopy);

            if (!captured)
                throw new InvalidOperationException(usePrintWindow
                    ? "PrintWindow failed to capture the game window."
                    : "Unable to copy the game window client area.");

            return CreateFrozenBitmap(bitmapHandle);
        }
        finally
        {
            if (previousObject != nint.Zero && memoryDc != nint.Zero)
                SelectObject(memoryDc, previousObject);
            if (bitmapHandle != nint.Zero)
                DeleteObject(bitmapHandle);
            if (memoryDc != nint.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(window.Handle, sourceDc);
        }
    }

    private static void EnsureCapturable(GameWindowInfo window)
    {
        if (!IsWindow(window.Handle))
            throw new InvalidOperationException("The game window has closed.");
        if (IsIconic(window.Handle))
            throw new InvalidOperationException("The game window is minimized and cannot be captured.");
    }

    private static BitmapSource CreateFrozenBitmap(nint bitmapHandle)
    {
        var bitmap = Imaging.CreateBitmapSourceFromHBitmap(
            bitmapHandle,
            nint.Zero,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        bitmap.Freeze();
        return bitmap;
    }

    private static long GetClientArea(nint handle) =>
        GetClientRect(handle, out var rect) ? (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top) : 0;

    private static void RestoreIfMinimized(nint handle)
    {
        if (!IsIconic(handle))
            return;

        ShowWindowAsync(handle, RestoreWindow);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (!IsIconic(handle))
                return;
            Thread.Sleep(50);
        }
    }

    private static string GetTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        var builder = new StringBuilder(Math.Max(length + 1, 2));
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool IsFullscreenWindow(nint handle)
    {
        if (!GetWindowRect(handle, out var windowRect))
            return false;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info))
            return false;
        return windowRect.Left <= info.Monitor.Left &&
               windowRect.Top <= info.Monitor.Top &&
               windowRect.Right >= info.Monitor.Right &&
               windowRect.Bottom >= info.Monitor.Bottom;
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(nint windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint handle, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint handle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint handle, nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint graphicObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint graphicObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        nint destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        int operation);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint windowHandle, nint destinationDeviceContext, uint flags);
}
