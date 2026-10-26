using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed record SandboxProcessWaitResult(
    bool IsStable,
    bool WasSeen,
    TimeSpan StableDuration);

public sealed class SandboxProcessWatcher : ISandboxProcessWatcher
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(650);
    private const int MinimumReadyWindowWidth = 200;
    private const int MinimumReadyWindowHeight = 100;
    private readonly IReadOnlyList<string> _processNames;
    private readonly TimeSpan _pollInterval;

    public SandboxProcessWatcher(
        IEnumerable<string>? processNames = null,
        TimeSpan? pollInterval = null)
    {
        _processNames = (processNames ?? ["BeyondEditor"])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_processNames.Count == 0)
            throw new ArgumentException("At least one sandbox process name is required.", nameof(processNames));

        _pollInterval = pollInterval ?? DefaultPollInterval;
        if (_pollInterval < TimeSpan.FromMilliseconds(500) ||
            _pollInterval > TimeSpan.FromMilliseconds(1000))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "Poll interval must be between 500ms and 1000ms.");
        }
    }

    public bool IsSandboxRunning()
    {
        foreach (var processName in _processNames)
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Any(process => !process.HasExited))
                    return true;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        return false;
    }

    public SandboxWindowInfo? FindSandboxWindow()
    {
        var snapshot = CaptureWindowSnapshot();
        return snapshot.ReadyWindows
            .OrderByDescending(window => !string.IsNullOrWhiteSpace(window.WindowTitle))
            .ThenByDescending(window => window.WindowWidth * window.WindowHeight)
            .FirstOrDefault();
    }

    public async Task<SandboxProcessWaitResult> WaitForStableAsync(
        TimeSpan stableDuration,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (stableDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(stableDuration));

        var deadline = DateTimeOffset.UtcNow + timeout;
        DateTimeOffset? stableSince = null;
        var wasSeen = false;
        var longestStable = TimeSpan.Zero;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isRunning = IsSandboxRunning();
            var now = DateTimeOffset.UtcNow;

            if (isRunning)
            {
                wasSeen = true;
                stableSince ??= now;
                var currentStable = now - stableSince.Value;
                if (currentStable > longestStable)
                    longestStable = currentStable;
                if (currentStable >= stableDuration)
                    return new SandboxProcessWaitResult(true, wasSeen, currentStable);
            }
            else
            {
                stableSince = null;
            }

            var remaining = deadline - now;
            if (remaining <= TimeSpan.Zero)
                break;
            await Task.Delay(remaining < _pollInterval ? remaining : _pollInterval, cancellationToken);
        }

        return new SandboxProcessWaitResult(false, wasSeen, longestStable);
    }

    public async Task<SandboxWindowWaitResult> WaitForWindowAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var deadline = startedAt + timeout;
        SandboxWindowSnapshot? lastSnapshot = null;
        var wasProcessSeen = false;
        var wasWindowSeen = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastSnapshot = CaptureWindowSnapshot();
            wasProcessSeen = wasProcessSeen || lastSnapshot.ProcessCount > 0;
            wasWindowSeen = wasWindowSeen || lastSnapshot.CandidateWindows.Count > 0;

            var selected = lastSnapshot.ReadyWindows
                .OrderByDescending(window => !string.IsNullOrWhiteSpace(window.WindowTitle))
                .ThenByDescending(window => window.WindowWidth * window.WindowHeight)
                .FirstOrDefault();
            if (selected != null)
            {
                return new SandboxWindowWaitResult(
                    true,
                    wasProcessSeen,
                    wasWindowSeen,
                    DateTimeOffset.UtcNow - startedAt,
                    selected,
                    CreateWindowDiagnostic(lastSnapshot, selected, "ready visible top-level sandbox window found"));
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;
            await Task.Delay(remaining < _pollInterval ? remaining : _pollInterval, cancellationToken);
        }

        lastSnapshot ??= CaptureWindowSnapshot();
        return new SandboxWindowWaitResult(
            false,
            wasProcessSeen || lastSnapshot.ProcessCount > 0,
            wasWindowSeen || lastSnapshot.CandidateWindows.Count > 0,
            DateTimeOffset.UtcNow - startedAt,
            null,
            CreateWindowDiagnostic(lastSnapshot, null, "timeout waiting for visible top-level sandbox window"));
    }

    private SandboxWindowSnapshot CaptureWindowSnapshot()
    {
        var processInfos = new Dictionary<int, string>();
        foreach (var processName in _processNames)
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                foreach (var process in processes)
                {
                    if (!process.HasExited)
                        processInfos[process.Id] = process.ProcessName;
                }
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        var candidates = new List<SandboxWindowInfo>();
        if (processInfos.Count > 0)
        {
            EnumWindows((hwnd, lParam) =>
            {
                _ = GetWindowThreadProcessId(hwnd, out var processId);
                if (!processInfos.TryGetValue((int)processId, out var processName))
                    return true;

                var isVisible = IsWindowVisible(hwnd);
                var isIconic = IsIconic(hwnd);
                _ = GetWindowRect(hwnd, out var rect);
                var title = GetWindowTitle(hwnd);
                candidates.Add(new SandboxWindowInfo(
                    processName,
                    (int)processId,
                    hwnd,
                    title,
                    isVisible,
                    isIconic,
                    Math.Max(0, rect.Right - rect.Left),
                    Math.Max(0, rect.Bottom - rect.Top)));
                return true;
            }, IntPtr.Zero);
        }

        var ready = candidates
            .Where(window =>
                window.IsVisible &&
                !window.IsIconic &&
                window.WindowWidth > MinimumReadyWindowWidth &&
                window.WindowHeight > MinimumReadyWindowHeight)
            .ToList();

        return new SandboxWindowSnapshot(processInfos.Count, candidates, ready);
    }

    private static string CreateWindowDiagnostic(
        SandboxWindowSnapshot snapshot,
        SandboxWindowInfo? selected,
        string reason)
    {
        var builder = new StringBuilder();
        builder.Append($"reason={reason}; processCount={snapshot.ProcessCount}; candidateCount={snapshot.CandidateWindows.Count}; ");
        if (selected != null)
        {
            builder.Append(
                $"selectedHandle=0x{selected.WindowHandle.ToInt64():X}; selectedProcessId={selected.ProcessId}; " +
                $"selectedTitle=\"{selected.WindowTitle}\"; selectedSize={selected.WindowWidth}x{selected.WindowHeight}; ");
        }

        builder.Append("candidates=[");
        for (var i = 0; i < snapshot.CandidateWindows.Count; i++)
        {
            var candidate = snapshot.CandidateWindows[i];
            if (i > 0)
                builder.Append(" | ");
            builder.Append(
                $"handle=0x{candidate.WindowHandle.ToInt64():X},pid={candidate.ProcessId},name={candidate.ProcessName}," +
                $"visible={candidate.IsVisible},iconic={candidate.IsIconic},size={candidate.WindowWidth}x{candidate.WindowHeight}," +
                $"title=\"{candidate.WindowTitle}\"");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private sealed record SandboxWindowSnapshot(
        int ProcessCount,
        IReadOnlyList<SandboxWindowInfo> CandidateWindows,
        IReadOnlyList<SandboxWindowInfo> ReadyWindows);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RECT
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
