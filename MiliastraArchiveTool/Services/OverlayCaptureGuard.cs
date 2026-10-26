using System.Runtime.InteropServices;
using System.Windows;

namespace MiliastraArchiveTool.Services;

/// <summary>Hide overlays before capture; restore only after all nested captures finish.</summary>
public static class OverlayCaptureGuard
{
    private static int _depth;
    public static bool IsCapturing => Volatile.Read(ref _depth) > 0;
    public static event Action? VisibilityChanged;

    public static IDisposable Begin()
    {
        OnUi(() => { Interlocked.Increment(ref _depth); VisibilityChanged?.Invoke(); });
        // Wait for the compositor to remove the hidden HWND before GDI/DXGI capture.
        DwmFlush();
        return new Scope();
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else if (!dispatcher.HasShutdownStarted) dispatcher.Invoke(action);
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OnUi(() => { Interlocked.Decrement(ref _depth); VisibilityChanged?.Invoke(); });
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
