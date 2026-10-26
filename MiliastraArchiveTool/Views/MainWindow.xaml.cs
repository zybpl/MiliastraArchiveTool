using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MiliastraArchiveTool.ViewModels;

namespace MiliastraArchiveTool.Views;

public partial class MainWindow : Window
{
    private const int HotKeyMessage = 0x0312;
    private const uint NoRepeatModifier = 0x4000;
    private const int StartUploadHotKeyId = 9101;
    private const int CancelUploadHotKeyId = 9102;
    private const int VirtualKeyF9 = 0x78;
    private const int VirtualKeyF10 = 0x79;

    private nint _windowHandle;
    private HwndSource? _windowSource;
    private bool _startUploadHotKeyRegistered;
    private bool _cancelUploadHotKeyRegistered;
    private readonly DispatcherTimer _hotKeyPollingTimer;
    private readonly Dictionary<int, bool> _keyDownStates = new()
    {
        [VirtualKeyF9] = false,
        [VirtualKeyF10] = false
    };

    public MainWindow()
    {
        InitializeComponent();
        if (UploadLogList.Items is INotifyCollectionChanged logItems)
            logItems.CollectionChanged += UploadLogItems_CollectionChanged;

        _hotKeyPollingTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(60),
            DispatcherPriority.Input,
            PollGlobalKeyStates,
            Dispatcher);
    }

    private void UploadLogItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (UploadLogList.Items.Count == 0)
            return;

        Dispatcher.BeginInvoke(
            () => UploadLogList.ScrollIntoView(UploadLogList.Items[^1]),
            DispatcherPriority.Background);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(ProcessWindowMessage);

        _startUploadHotKeyRegistered = RegisterGlobalHotKey(StartUploadHotKeyId, VirtualKeyF9);
        _cancelUploadHotKeyRegistered = RegisterGlobalHotKey(CancelUploadHotKeyId, VirtualKeyF10);
        _hotKeyPollingTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotKeyPollingTimer.Stop();

        if (_windowHandle != nint.Zero)
        {
            if (_startUploadHotKeyRegistered)
                UnregisterHotKey(_windowHandle, StartUploadHotKeyId);

            if (_cancelUploadHotKeyRegistered)
                UnregisterHotKey(_windowHandle, CancelUploadHotKeyId);
        }

        _windowSource?.RemoveHook(ProcessWindowMessage);
        base.OnClosed(e);
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private nint ProcessWindowMessage(
        nint handle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message != HotKeyMessage)
            return nint.Zero;

        handled = true;
        ExecuteHotKeyCommand(wordParameter.ToInt32());
        return nint.Zero;
    }

    private void PollGlobalKeyStates(object? sender, EventArgs e)
    {
        if (!_startUploadHotKeyRegistered)
            PollKey(VirtualKeyF9, StartUploadHotKeyId);

        if (!_cancelUploadHotKeyRegistered)
            PollKey(VirtualKeyF10, CancelUploadHotKeyId);
    }

    private void PollKey(int virtualKey, int hotKeyId)
    {
        var isDown = (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        var wasDown = _keyDownStates[virtualKey];
        _keyDownStates[virtualKey] = isDown;

        if (isDown && !wasDown)
            ExecuteHotKeyCommand(hotKeyId);
    }

    private void ExecuteHotKeyCommand(int hotKeyId)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var command = hotKeyId switch
        {
            StartUploadHotKeyId => viewModel.StartUploadCommand,
            CancelUploadHotKeyId => viewModel.CancelUploadCommand,
            _ => null
        };

        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }

    private bool RegisterGlobalHotKey(int id, uint virtualKey)
    {
        return RegisterHotKey(_windowHandle, id, NoRepeatModifier, virtualKey);
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(
        nint handle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint handle, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
