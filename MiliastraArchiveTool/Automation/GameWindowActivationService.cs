using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MiliastraArchiveTool.Automation;

public sealed class GameWindowActivationService : IGameWindowActivationService
{
    private const int RestoreWindow = 9;

    public void RestoreAndActivate(GameWindowInfo window)
    {
        if (!TryRestoreAndActivate(window, out var diagnostic))
            throw new InvalidOperationException($"Unable to activate the game window. {diagnostic}");
    }

    public bool TryRestoreAndActivate(GameWindowInfo window, out string diagnostic)
    {
        if (!IsWindow(window.Handle))
        {
            diagnostic = $"The game window has closed. targetHandle=0x{window.Handle.ToInt64():X}, targetProcessId={window.ProcessId}";
            return false;
        }

        if (IsIconic(window.Handle) && !ShowWindowAsync(window.Handle, RestoreWindow))
        {
            diagnostic = $"Unable to restore the minimized game window. {GetForegroundWindowInfo(window).ToDiagnosticString()}";
            return false;
        }

        if (IsGameProcessInForeground(window))
        {
            diagnostic = GetForegroundWindowInfo(window).ToDiagnosticString();
            return true;
        }

        BringWindowToTop(window.Handle);
        TryAttachAndActivate(window.Handle);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (IsGameProcessInForeground(window))
            {
                diagnostic = GetForegroundWindowInfo(window).ToDiagnosticString();
                return true;
            }
            Thread.Sleep(50);
        }

        diagnostic = GetForegroundWindowInfo(window).ToDiagnosticString();
        return false;
    }

    public bool IsForeground(GameWindowInfo window) => IsGameProcessInForeground(window);

    public ForegroundWindowInfo GetForegroundWindowInfo(GameWindowInfo target)
    {
        var foregroundWindow = GetForegroundWindow();
        var foregroundProcessId = 0;
        string? foregroundProcessName = null;
        if (foregroundWindow != nint.Zero)
        {
            GetWindowThreadProcessId(foregroundWindow, out foregroundProcessId);
            if (foregroundProcessId > 0)
            {
                try
                {
                    using var process = Process.GetProcessById(foregroundProcessId);
                    foregroundProcessName = process.ProcessName;
                }
                catch
                {
                    foregroundProcessName = null;
                }
            }
        }

        return new ForegroundWindowInfo(
            foregroundWindow,
            foregroundProcessId,
            foregroundProcessName,
            foregroundWindow == nint.Zero ? string.Empty : GetTitle(foregroundWindow),
            target.Handle,
            target.ProcessId,
            foregroundWindow == target.Handle,
            foregroundProcessId == target.ProcessId);
    }

    public string? GetForegroundProcessName()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == nint.Zero)
            return null;

        GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId);
        if (foregroundProcessId <= 0)
            return null;

        try
        {
            using var process = Process.GetProcessById(foregroundProcessId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public bool IsProcessInForeground(IEnumerable<string> processNames)
    {
        var foregroundProcessName = GetForegroundProcessName();
        if (string.IsNullOrWhiteSpace(foregroundProcessName))
            return false;

        return processNames.Any(name => string.Equals(foregroundProcessName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGameProcessInForeground(GameWindowInfo window)
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == nint.Zero)
            return false;
        if (foregroundWindow == window.Handle)
            return true;

        GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId);
        return foregroundProcessId == window.ProcessId;
    }

    private static void TryAttachAndActivate(nint targetWindow)
    {
        var foregroundWindow = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = foregroundWindow == nint.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, nint.Zero);
        var attached = foregroundThread != 0 &&
                       foregroundThread != currentThread &&
                       AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            SetForegroundWindow(targetWindow);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private static string GetTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        var builder = new StringBuilder(Math.Max(length + 1, 2));
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(nint windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int count);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, nint processId);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out int processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint sourceThreadId, uint targetThreadId, bool attach);
}
