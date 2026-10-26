using System.IO;
using MiliastraArchiveTool.Infrastructure;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MiliastraArchiveTool.Automation;

public sealed class WindowsFilePickerDriver : IFilePickerDriver
{
    private readonly List<object> _candidates = new();
    private readonly HashSet<string> _candidateKeys = new();
    private readonly Dictionary<nint, (AutomationElement Input, AutomationElement Button)> _verifiedControls = new();
    public void ResetDiagnostics() { _candidates.Clear(); _candidateKeys.Clear(); _verifiedControls.Clear(); }
    public IReadOnlyList<object> GetCandidateDiagnostics() => _candidates.ToArray();

    private static readonly string[] FileNames = ["File name:", "File name", "文件名(N):", "文件名:", "文件名", "檔案名稱(N):", "檔案名稱:"];
    private static readonly string[] OpenNames = ["Open", "Open(O)", "打开", "打开(O)", "開啟", "開啟(O)"];

    public FilePickerWindow? Find(GameWindowInfo owner)
    {
        var matches = new List<FilePickerWindow>();
        var processes = FilePickerProcessTree.Read();
        foreach (var handle in Windows())
        {
            if (!IsWindowVisible(handle)) continue;
            var pid = ProcessId(handle);
            var className = ClassName(handle);
            var related = FilePickerProcessTree.BelongsToGame(pid, owner.ProcessId, processes);
            if (className != "#32770" && !related) continue;
            void Record(string reason)
            {
                processes.TryGetValue(pid, out var process);
                processes.TryGetValue(process?.ParentId ?? 0, out var parentProcess);
                var ownerHandle = GetWindow(handle, 4);
                var details = new { Handle = handle.ToInt64(), ProcessId = pid, ProcessName = process?.Name,
                    ParentProcessId = process?.ParentId, ParentProcessName = parentProcess?.Name, ExpectedGameProcessId = owner.ProcessId,
                    OwnerHandle = ownerHandle.ToInt64(), OwnerProcessId = ProcessId(ownerHandle),
                    ClassName = className, Title = Title(handle), RelatedToGame = related, Reason = reason };
                var key = System.Text.Json.JsonSerializer.Serialize(details);
                if (_candidateKeys.Count < 256 && _candidateKeys.Add(key))
                {
                    _candidates.Add(details);
                    WorkflowPerformanceTrace.Event("FileDialog", "WindowCandidate", details);
                }
            }
            if (!related) { Record("Rejected: process is not the game or a verified descendant"); continue; }
            if (className != "#32770") { Record("Rejected: not a standard dialog class"); continue; }
            try
            {
                var (input, button) = Controls(handle);
                matches.Add(new(handle, pid, Title(handle), input.Current.AutomationId, button.Current.Name));
                Record(pid == owner.ProcessId ? "Accepted: game process and verified controls" : "Accepted: game descendant and verified controls");
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
            {
                Record("Rejected controls: " + ex.Message);
            }
        }
        // Never choose arbitrarily between multiple file pickers belonging to the same game.
        return matches.Count == 1 ? matches[0] : null;
    }

    public bool IsOpen(FilePickerWindow window) => IsWindowVisible(window.Handle) &&
        ProcessId(window.Handle) == window.ProcessId && ClassName(window.Handle) == "#32770";

    public string? ReadError(FilePickerWindow window)
    {
        foreach (var handle in Windows())
        {
            if (handle == window.Handle || !IsWindowVisible(handle) || ProcessId(handle) != window.ProcessId || !OwnedBy(handle, window.Handle)) continue;
            try
            {
                var root = AutomationElement.FromHandle(handle);
                var texts = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                    .Cast<AutomationElement>().Select(e => e.Current.Name).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
                if (texts.Length > 0) return $"{Title(handle)}: {string.Join(" | ", texts)}";
            }
            catch (ElementNotAvailableException) { }
        }
        return null;
    }

    public string ReadPath(FilePickerWindow window)
    {
        RequireOpen(window);
        var (input, _) = Controls(window.Handle);
        return ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
    }

    public string? ReadDirectory(FilePickerWindow window)
    {
        RequireOpen(window);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        EnumChildWindows(window.Handle, (child, _) =>
        {
            if (IsWindowVisible(child) && ClassName(child).Equals("ToolbarWindow32", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var name = AutomationElement.FromHandle(child).Current.Name;
                    names.Add(name);
                    // The shell address breadcrumb exposes its full path as its accessible name.
                    var match = System.Text.RegularExpressions.Regex.Match(name, @"(?:[A-Za-z]:[\\/]|\\\\)[^\r\n]*$");
                    if (match.Success && Path.IsPathFullyQualified(match.Value))
                        paths.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(match.Value)));
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException) { }
            }
            return true;
        }, 0);
        WorkflowPerformanceTrace.Event("FileDialog", "DirectoryObservation", new { Names = names, Paths = paths });
        return paths.Count == 1 ? paths.Single() : null;
    }

    public void FillPath(FilePickerWindow window, string value, bool keyboardFallback)
    {
        RequireOpen(window);
        var (input, _) = Controls(window.Handle);
        if (!keyboardFallback)
        {
            var pattern = (ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern);
            if (pattern.Current.IsReadOnly) throw new InvalidOperationException("Filename input is read-only.");
            pattern.SetValue(value);
            return;
        }
        Activate(window.Handle);
        input.SetFocus();
        if (!System.Windows.Automation.Automation.Compare(AutomationElement.FocusedElement, input))
            throw new InvalidOperationException("Filename input did not receive keyboard focus.");
        var dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("Clipboard fallback requires the application dispatcher.");
        IDataObject? previous = dispatcher.Invoke(Clipboard.GetDataObject);
        try
        {
            dispatcher.Invoke(() => Clipboard.SetText(value));
            if (GetForegroundWindow() != window.Handle || !System.Windows.Automation.Automation.Compare(AutomationElement.FocusedElement, input))
                throw new InvalidOperationException("Filename focus changed before paste.");
            Shortcut(0x11, 0x41); // Ctrl+A only after verifying the exact edit control.
            Shortcut(0x11, 0x56);
            Thread.Sleep(150);
        }
        finally
        {
            dispatcher.Invoke(() => { if (previous != null) Clipboard.SetDataObject(previous, true); else Clipboard.Clear(); });
        }
    }

    public void Open(FilePickerWindow window)
    {
        RequireOpen(window);
        var (_, button) = Controls(window.Handle);
        if (!button.Current.IsEnabled) throw new InvalidOperationException("Open button is disabled.");
        if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
        {
            // Post to the real standard IDOK control to avoid a provider blocking in a modal error loop.
            var native = (nint)button.Current.NativeWindowHandle;
            if (native != 0 && GetDlgCtrlID(native) == 1)
                PostMessage(window.Handle, 0x0111, (nint)1, native);
            else ((InvokePattern)pattern).Invoke();
            return;
        }
        Activate(window.Handle);
        var rect = button.Current.BoundingRectangle;
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) throw new InvalidOperationException("Open button has no visible bounds.");
        SetCursorPos((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2));
        mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0);
    }

    private (AutomationElement Input, AutomationElement Button) Controls(nint handle)
    {
        if (_verifiedControls.TryGetValue(handle, out var cached))
        {
            try
            {
                var pid = ProcessId(handle);
                if (IsWindowVisible(handle) && cached.Input.Current.ProcessId == pid && cached.Button.Current.ProcessId == pid)
                    return cached;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { }
            _verifiedControls.Remove(handle);
        }
        using var timing = WorkflowPerformanceTrace.Measure("FileDialog.ScanControls", new { Handle = handle.ToInt64() });
        var result = ScanControls(handle);
        _verifiedControls[handle] = result;
        return result;
    }

    private static (AutomationElement Input, AutomationElement Button) ScanControls(nint handle)
    {
        // Common file pickers expose native filename (1148) and IDOK controls.
        // Walk HWNDs locally before asking a remote UIA provider for its whole tree.
        var children = new List<nint>();
        EnumChildWindows(handle, (child, _) => { children.Add(child); return true; }, 0);
        var open = GetDlgItem(handle, 1);
        var filenameHosts = children.Where(h => GetDlgCtrlID(h) == 1148 && IsWindowVisible(h)).ToList();
        var edits = children.Where(h => ClassName(h).Equals("Edit", StringComparison.OrdinalIgnoreCase) &&
            IsWindowVisible(h) && filenameHosts.Any(host => h == host || IsChild(host, h))).ToList();
        var hasList = children.Any(h => IsWindowVisible(h) && ClassName(h) is "DirectUIHWND" or "SysListView32");
        if (edits.Count == 1 && hasList && open != 0 && IsChild(handle, open) && IsWindowVisible(open) &&
            ClassName(open).Equals("Button", StringComparison.OrdinalIgnoreCase) &&
            OpenNames.Contains(Title(open).Replace("&", ""), StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var input = AutomationElement.FromHandle(edits[0]);
                var button = AutomationElement.FromHandle(open);
                if (input.Current.ProcessId == ProcessId(handle) && input.Current.IsEnabled && !input.Current.IsOffscreen &&
                    input.TryGetCurrentPattern(ValuePattern.Pattern, out _) && button.Current.ProcessId == ProcessId(handle))
                {
                    WorkflowPerformanceTrace.Event("FileDialog", "NativeControlsVerified", new { Handle = handle.ToInt64() });
                    return (input, button);
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
            { WorkflowPerformanceTrace.Event("FileDialog", "NativeControlsFallback", ex.Message); }
        }
        WorkflowPerformanceTrace.Event("FileDialog", "FullTreeFallback", new { FilenameEdits = edits.Count, HasList = hasList });
        return ScanUiaControls(handle);
    }

    private static (AutomationElement Input, AutomationElement Button) ScanUiaControls(nint handle)
    {
        var root = AutomationElement.FromHandle(handle);
        var edits = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        var inputs = edits.Cast<AutomationElement>().Where(e =>
        {
            if (!e.Current.IsEnabled || e.Current.IsOffscreen || !e.TryGetCurrentPattern(ValuePattern.Pattern, out _)) return false;
            var parent = TreeWalker.ControlViewWalker.GetParent(e);
            return e.Current.AutomationId == "1148" || parent?.Current.AutomationId is "1148" or "FileNameControlHost" ||
                FileNames.Contains(e.Current.Name.Replace("&", ""), StringComparer.OrdinalIgnoreCase);
        }).ToList();
        var buttons = root.FindAll(TreeScope.Descendants, new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton)))
            .Cast<AutomationElement>().Where(e => !e.Current.IsOffscreen &&
                (e.Current.AutomationId == "1" || OpenNames.Contains(e.Current.Name.Replace("&", ""), StringComparer.OrdinalIgnoreCase))).ToList();
        // Standard Open dialogs can expose IDOK as a split button, or omit it from
        // the UIA descendant walk. Resolve the actual visible child control first.
        var nativeOpen = GetDlgItem(handle, 1);
        if (nativeOpen != 0 && IsChild(handle, nativeOpen) && IsWindowVisible(nativeOpen) &&
            ClassName(nativeOpen).Equals("Button", StringComparison.OrdinalIgnoreCase) &&
            OpenNames.Contains(Title(nativeOpen).Replace("&", ""), StringComparer.OrdinalIgnoreCase))
            buttons = [AutomationElement.FromHandle(nativeOpen)];
        var list = root.FindFirst(TreeScope.Descendants, new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid)));
        if (inputs.Count != 1 || buttons.Count != 1 || list == null)
            throw new InvalidOperationException($"Not an unambiguous file picker: filenameInputs={inputs.Count}, openButtons={buttons.Count}, fileList={list != null}. " +
                $"NativeOpen=0x{nativeOpen:X}, nativeTitle={Title(nativeOpen)}, nativeClass={ClassName(nativeOpen)}, nativeVisible={IsWindowVisible(nativeOpen)}.");
        return (inputs[0], buttons[0]);
    }

    private static void RequireOpen(FilePickerWindow window)
    {
        if (!IsWindowVisible(window.Handle) || ProcessId(window.Handle) != window.ProcessId || ClassName(window.Handle) != "#32770")
            throw new InvalidOperationException("The verified file picker is no longer open.");
    }
    private static void Activate(nint window)
    {
        SetForegroundWindow(window);
        // Native lookup may finish before Windows completes foreground activation.
        // Wait briefly for this exact window, rather than burning three immediate retries.
        for (var attempt = 0; attempt < 10 && GetForegroundWindow() != window && IsWindowVisible(window); attempt++)
            Thread.Sleep(50);
        if (GetForegroundWindow() != window) throw new InvalidOperationException("Could not activate the verified file picker.");
    }
    private static void Shortcut(byte modifier, byte key)
    {
        keybd_event(modifier, 0, 0, 0); keybd_event(key, 0, 0, 0);
        keybd_event(key, 0, 2, 0); keybd_event(modifier, 0, 2, 0);
    }
    private static bool OwnedBy(nint child, nint owner)
    {
        for (var i = 0; child != 0 && i < 16; i++) { child = GetWindow(child, 4); if (child == owner) return true; }
        return false;
    }
    private static List<nint> Windows()
    {
        var result = new List<nint>(); EnumWindows((h, _) => { result.Add(h); return true; }, 0); return result;
    }
    private static int ProcessId(nint handle) { GetWindowThreadProcessId(handle, out var pid); return (int)pid; }
    private static string Title(nint handle) { var text = new StringBuilder(512); GetWindowText(handle, text, text.Capacity); return text.ToString(); }
    private static string ClassName(nint handle) { var text = new StringBuilder(256); GetClassName(handle, text, text.Capacity); return text.ToString(); }

    public void SaveScreenshot(string path)
    {
        // Capture the desktop, including system dialogs, before the caller restores game focus.
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77), width = GetSystemMetrics(78), height = GetSystemMetrics(79);
        var screen = GetDC(0); var memory = CreateCompatibleDC(screen); var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, x, y, 0x40CC0020)) throw new InvalidOperationException("Desktop capture failed.");
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(path); encoder.Save(output);
        }
        finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(0, screen); }
    }
    private delegate bool EnumWindow(nint h, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint h, uint cmd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint h, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint h, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(nint h);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint h, int id);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint h, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint GetDC(nint h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint h, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dst, int x, int y, int width, int height, nint src, int sx, int sy, uint operation);
}
