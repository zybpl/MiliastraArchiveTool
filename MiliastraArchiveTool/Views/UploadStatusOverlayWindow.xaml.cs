using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MiliastraArchiveTool.Automation;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Views;

public partial class UploadStatusOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private readonly DispatcherTimer _placementTimer;
    private bool _closed;
    private readonly Func<Rect> _getGameBounds;
    private readonly Func<IReadOnlyList<Rect>> _getWorkAreas;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    public UploadStatusOverlayWindow() : this(() =>
    {
        var game = new GameWindowCaptureService().FindGameWindow();
        return new Rect(game.ClientScreenLeft, game.ClientScreenTop, game.ClientWidth, game.ClientHeight);
    }, GetWorkAreas) { }

    internal UploadStatusOverlayWindow(Func<Rect> getGameBounds, Func<IReadOnlyList<Rect>> getWorkAreas)
    {
        _getGameBounds = getGameBounds;
        _getWorkAreas = getWorkAreas;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        _placementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _placementTimer.Tick += (_, _) => PlaceNearGameWindow();
        _placementTimer.Start();
        OverlayCaptureGuard.VisibilityChanged += PlaceNearGameWindow;
        Closed += (_, _) =>
        {
            _closed = true;
            _placementTimer.Stop();
            OverlayCaptureGuard.VisibilityChanged -= PlaceNearGameWindow;
        };
    }

    public void PlaceNearGameWindow()
    {
        if (_closed) return;
        if (OverlayCaptureGuard.IsCapturing) { Hide(); return; }
        try
        {
            var gameRect = _getGameBounds();
            var handle = new WindowInteropHelper(this).EnsureHandle();
            var areas = _getWorkAreas();
            // Moving across monitors can change DPI and native size. Recheck before Show.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (!GetWindowRect(handle, out var bounds)) { Hide(); return; }
                var placement = HudPlacement.Find(gameRect, areas, bounds.ToRect().Size);
                if (placement == null) { Hide(); return; }
                if (IsVisible && bounds.ToRect() != placement.Value) Hide();
                if (!SetWindowPos(handle, HwndTopmost, (int)placement.Value.X, (int)placement.Value.Y,
                    0, 0, SwpNoSize | SwpNoActivate)) { Hide(); return; }
                if (GetWindowRect(handle, out var actual) && areas.Any(a => a.Contains(actual.ToRect()))
                    && !actual.ToRect().IntersectsWith(gameRect))
                {
                    if (!IsVisible) Show();
                    EnsureTopmostNoActivate();
                    return;
                }
            }
        }
        catch
        {
            // Unknown game/monitor geometry must never fall back to covering the game.
        }
        Hide();
    }

    public void EnsureTopmostNoActivate()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero || !IsVisible || _closed)
            return;

        Topmost = true;
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private static IReadOnlyList<Rect> GetWorkAreas()
    {
        var areas = new List<Rect>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref NativeRect rect, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info)) areas.Add(info.Work.ToRect());
            return true;
        }, 0);
        return areas;
    }

    private UploadOperationStatus? _operation;

    public void UpdateOperation(UploadOperationStatus status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateOperation(status));
            return;
        }
        _operation = status;
        ElapsedText.Text = FormatElapsed(DateTimeOffset.Now - _startedAt);
        RenderOperation();
    }

    private void RenderOperation()
    {
        ModeText.Text = "Server: " + (_operation == null ? "Preparing server" : FormatServers([_operation.Server]));
        TaskText.Text = "Save: " + (string.IsNullOrWhiteSpace(_operation?.ArchiveName) ? "No save in progress" : _operation.ArchiveName);
        PhaseText.Text = "Action: " + (_operation?.Operation ?? "Preparing upload");
    }

    public void UpdateStatus(
        IReadOnlyList<UploadTask> tasks,
        IReadOnlyList<UploadServer> servers,
        bool isUpdate,
        string? lastBriefMessage = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateStatus(tasks, servers, isUpdate, lastBriefMessage));
            return;
        }

        var total = Math.Max(1, tasks.Count);
        var succeeded = tasks.Count(task => task.State == UploadTaskState.Succeeded);
        var failed = tasks.Count(task => task.State == UploadTaskState.Failed);
        var cancelled = tasks.Count(task => task.State == UploadTaskState.Cancelled);
        var finished = succeeded + failed + cancelled;
        var active = FindActiveTask(tasks);


        RenderOperation();
        if (lastBriefMessage == "Error. Check the main window log.")
            PhaseText.Text = "Failed: " + (_operation?.Operation ?? "Upload task") + " (see the main log)";
        else if (lastBriefMessage == "Cancelling...")
            PhaseText.Text = "Action: Stopping the current task";
        else if (lastBriefMessage == "Batch finished.")
            PhaseText.Text = failed > 0 ? "Action: Stopped after an upload failure" : cancelled > 0 ? "Action: Stopped" : "Action: All uploads completed";
        SummaryText.Text = $"{(isUpdate ? "Update existing save" : "Upload new save")} · {tasks.Count} saves selected";
        ElapsedText.Text = FormatElapsed(DateTimeOffset.Now - _startedAt);
        OverallProgress.IsIndeterminate = lastBriefMessage is not "Batch finished." and not "Error. Check the main window log.";
        OverallProgress.Value = lastBriefMessage == "Batch finished." ? 100 : 0;

        ApplyVisualState(active, failed, cancelled, finished >= total);
        EnsureTopmostNoActivate();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow);
        EnsureTopmostNoActivate();
    }

    private static UploadTask? FindActiveTask(IReadOnlyList<UploadTask> tasks) =>
        tasks.FirstOrDefault(task => task.State is
            UploadTaskState.Validating or
            UploadTaskState.Ready or
            UploadTaskState.WaitingForGame or
            UploadTaskState.Recognizing or
            UploadTaskState.Navigating or
            UploadTaskState.Filling or
            UploadTaskState.Submitting or
            UploadTaskState.Verifying)
        ?? tasks.FirstOrDefault(task => task.State == UploadTaskState.Pending)
        ?? tasks.LastOrDefault();

    private static string FormatServers(IReadOnlyList<UploadServer> servers) =>
        servers.Count == 0
            ? "No server"
            : string.Join(", ", servers.Select(server => server switch
            {
                UploadServer.None => "Current login server",
                UploadServer.Us => "America",
                UploadServer.Asia => "Asia",
                UploadServer.Europe => "Europe",
                UploadServer.HkTw => "TW/HK/MO",
                _ => server.ToString()
            }));

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"m\:ss");

    private void ApplyVisualState(UploadTask? active, int failed, int cancelled, bool complete)
    {
        var color = active?.State switch
        {
            UploadTaskState.Failed => Color.FromRgb(248, 113, 113),
            UploadTaskState.Cancelled => Color.FromRgb(251, 191, 36),
            UploadTaskState.Succeeded => Color.FromRgb(52, 211, 153),
            UploadTaskState.Submitting => Color.FromRgb(96, 165, 250),
            UploadTaskState.Verifying => Color.FromRgb(129, 140, 248),
            _ => Color.FromRgb(96, 165, 250)
        };

        if (failed > 0)
            color = Color.FromRgb(248, 113, 113);
        else if (cancelled > 0)
            color = Color.FromRgb(251, 191, 36);
        else if (complete)
            color = Color.FromRgb(52, 211, 153);

        StateDot.Fill = new SolidColorBrush(color);
        RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(140, color.R, color.G, color.B));
    }

    private static int IndexOf<T>(IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value))
                return index;
        }

        return -1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Math.Max(0, Right - Left), Math.Max(0, Bottom - Top));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    private delegate bool MonitorEnum(nint monitor, nint dc, ref NativeRect rect, nint data);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnum callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint handle, int index, int newLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        nint handle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

