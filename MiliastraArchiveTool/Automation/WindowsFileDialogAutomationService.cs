using MiliastraArchiveTool.Infrastructure;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace MiliastraArchiveTool.Automation;

public sealed record FilePickerWindow(nint Handle, int ProcessId, string Title, string InputId, string ButtonName);

public interface IFilePickerDriver
{
    void ResetDiagnostics() { }
    IReadOnlyList<object> GetCandidateDiagnostics() => Array.Empty<object>();
    FilePickerWindow? Find(GameWindowInfo owner);
    bool IsOpen(FilePickerWindow window);
    string? ReadError(FilePickerWindow window);
    string ReadPath(FilePickerWindow window);
    string? ReadDirectory(FilePickerWindow window) => null;
    void FillPath(FilePickerWindow window, string value, bool keyboardFallback);
    void Open(FilePickerWindow window);
    void SaveScreenshot(string path);
}

public sealed class WindowsFileDialogAutomationService : IWindowsFileDialogAutomationService
{
    public static TimeSpan SelectionTimeout { get; } = TimeSpan.FromSeconds(60);
    private readonly IFilePickerDriver _driver;
    private readonly string _diagnosticsDirectory;
    public WindowsFileDialogAutomationService(IFilePickerDriver? driver = null, string? diagnosticsDirectory = null)
    {
        _driver = driver ?? new WindowsFilePickerDriver();
        _diagnosticsDirectory = diagnosticsDirectory ?? ApplicationPaths.DiagnosticsDirectory;
    }

    public Task SelectFileAsync(string filePath, TimeSpan timeout, CancellationToken ct) =>
        SelectFilesAsync([filePath], timeout, ct);
    public Task SelectFilesAsync(IReadOnlyList<string> paths, TimeSpan timeout, CancellationToken ct) =>
        throw new InvalidOperationException("File selection requires the expected game window and process.");
    public Task SelectFileAsync(GameWindowInfo owner, string path, TimeSpan timeout, CancellationToken ct) =>
        SelectFilesAsync(owner, [path], timeout, ct);

    public Task SelectFilesAsync(GameWindowInfo owner, IReadOnlyList<string> paths, TimeSpan timeout, CancellationToken ct) =>
        Task.Run(() => RunAsync(owner, paths, timeout, ct), ct);

    private async Task RunAsync(GameWindowInfo owner, IReadOnlyList<string> paths, TimeSpan timeout, CancellationToken ct)
    {
        using var timing = WorkflowPerformanceTrace.Measure("FileDialog.SelectFiles", new { owner.ProcessId, Count = paths.Count, TimeoutMs = timeout.TotalMilliseconds });
        var watch = Stopwatch.StartNew();
        var events = new List<object>();
        var id = $"stage1-file-dialog-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
        FilePickerWindow? window = null;
        _driver.ResetDiagnostics();
        void Log(string step, object? details = null)
        {
            var entry = new { At = DateTimeOffset.Now, ElapsedMs = watch.ElapsedMilliseconds, Step = step, Details = details };
            events.Add(entry);
            WorkflowPerformanceTrace.Event("FileDialog", step, entry);
        }
        void Check()
        {
            ct.ThrowIfCancellationRequested();
            if (watch.Elapsed >= timeout) throw new TimeoutException("Windows file selection exceeded its total deadline.");
        }
        void CheckError()
        {
            var error = _driver.ReadError(window!);
            if (!string.IsNullOrWhiteSpace(error))
                throw new IOException($"Windows file picker reported: {error}");
        }
        try
        {
            Check();
            if (paths.Count == 0) throw new ArgumentException("At least one file must be selected.");
            var normalized = paths.Select(Path.GetFullPath).ToArray();
            foreach (var path in normalized)
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Log("FileReadable", new { Path = path, file.Length });
            }
            var value = normalized.Length == 1 ? normalized[0] : string.Join(" ", normalized.Select(p => $"\"{p}\""));
            var directories = normalized.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var selectionDirectory = normalized.Length > 1 && directories.Length == 1 ? directories[0] : null;
            if (selectionDirectory != null)
                value = string.Join(" ", normalized.Select(p => $"\"{Path.GetFileName(p)}\""));
            // Never send a value known to exceed the classic filename edit limit.
            if (normalized.Length > 1 && (value.Length > 259 || selectionDirectory?.Length >= 259))
                throw new IOException("File selection text is too long. Use a shorter directory or filenames; multi-file selection should use one directory.");
            Log("WaitingForWindow", new { owner.ProcessId, OwnerHandle = owner.Handle.ToInt64() });
            while (window == null)
            {
                Check();
                window = _driver.Find(owner);
                if (window == null) await Task.Delay(150, ct);
            }
            Log("WindowVerified", new { Handle = window.Handle.ToInt64(), window.ProcessId, window.Title, window.InputId, window.ButtonName });
            if (selectionDirectory != null)
            {
                var directoryReady = false;
                var folderValue = Path.TrimEndingDirectorySeparator(selectionDirectory) + Path.DirectorySeparatorChar;
                for (var attempt = 1; attempt <= 3 && !directoryReady; attempt++)
                {
                    Check(); CheckError();
                    if (!_driver.IsOpen(window)) throw new IOException("File picker closed before directory navigation completed.");
                    var actualDirectory = _driver.ReadDirectory(window);
                    directoryReady = DirectoriesMatch(actualDirectory, selectionDirectory);
                    Log("DirectoryReadback", new { attempt, Expected = selectionDirectory, Actual = actualDirectory, Matched = directoryReady });
                    if (directoryReady) break;
                    try
                    {
                        _driver.FillPath(window, folderValue, attempt > 1);
                        await Task.Delay(150, ct);
                        Check(); CheckError();
                        var folderReadback = _driver.ReadPath(window);
                        Log("DirectoryInputReadback", new { attempt, Expected = folderValue, Actual = folderReadback });
                        if (!PathsMatch(folderReadback, folderValue)) continue;
                        _driver.Open(window);
                        Log("DirectoryOpenInvoked", new { attempt });
                    }
                    catch (Exception ex) when (ex is not IOException && ex is not OperationCanceledException && ex is not TimeoutException)
                    { Log("DirectoryNavigationRetry", new { attempt, ex.Message }); }
                    var navigationUntil = watch.Elapsed + TimeSpan.FromSeconds(3);
                    while (watch.Elapsed < navigationUntil)
                    {
                        await Task.Delay(150, ct);
                        Check(); CheckError();
                        if (!_driver.IsOpen(window)) throw new IOException("File picker closed while navigating to the requested directory.");
                        actualDirectory = _driver.ReadDirectory(window);
                        if (DirectoriesMatch(actualDirectory, selectionDirectory))
                        {
                            directoryReady = true;
                            Log("DirectoryVerified", new { Expected = selectionDirectory, Actual = actualDirectory });
                            break;
                        }
                    }
                }
                if (!directoryReady) throw new IOException("File picker directory could not be confirmed; no files were submitted.");
            }
            for (var confirmAttempt = 1; confirmAttempt <= 3; confirmAttempt++)
            {
                Check();
                CheckError();
                if (!_driver.IsOpen(window)) throw new IOException("File picker closed before a verified Open action; import is unconfirmed.");
                if (selectionDirectory != null && !DirectoriesMatch(_driver.ReadDirectory(window), selectionDirectory))
                    throw new IOException("File picker directory changed before entering filenames.");
                bool filled = false;
                for (var fillAttempt = 1; fillAttempt <= 3; fillAttempt++)
                {
                    Check();
                    CheckError();
                    try
                    {
                        Log("FillPathStarted", new { confirmAttempt, fillAttempt });
                        using (WorkflowPerformanceTrace.Measure("FileDialog.FillPath"))
                            _driver.FillPath(window, value, fillAttempt > 1);
                        Log("FillPathCompleted", new { confirmAttempt, fillAttempt });
                        await Task.Delay(150, ct);
                        Check();
                        CheckError();
                        string readback;
                        using (WorkflowPerformanceTrace.Measure("FileDialog.ReadPath"))
                            readback = _driver.ReadPath(window);
                        filled = PathsMatch(readback, value);
                        Log("PathReadback", new { confirmAttempt, fillAttempt, Expected = value, Actual = readback, Matched = filled });
                        if (filled) break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not TimeoutException && ex is not IOException)
                    {
                        Log("FillRetry", new { confirmAttempt, fillAttempt, Error = ex.Message });
                    }
                }
                if (!filled) throw new IOException("File picker filename did not match the requested path after three input attempts.");
                Check();
                CheckError();
                if (selectionDirectory != null && !DirectoriesMatch(_driver.ReadDirectory(window), selectionDirectory))
                    throw new IOException("File picker directory changed before confirming filenames.");
                if (!_driver.IsOpen(window))
                    throw new IOException("File picker closed before a verified Open action; import is unconfirmed.");
                // An invocation can close the dialog even if its provider then reports an error.
                // Always observe closure before deciding to retry the same dialog.
                try { _driver.Open(window); Log("OpenInvoked", new { confirmAttempt }); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { Log("OpenInvocationError", new { confirmAttempt, Error = ex.Message }); }
                var until = watch.Elapsed + TimeSpan.FromMilliseconds(Math.Clamp(timeout.TotalMilliseconds / 4, 300, 5000));
                int closedFrames = 0;
                while (watch.Elapsed < until)
                {
                    Check();
                    CheckError();
                    closedFrames = !_driver.IsOpen(window) ? closedFrames + 1 : 0;
                    if (closedFrames >= 2)
                    {
                        Log("DialogClosed", new { confirmAttempt, Next = "Caller verifies the game import/upload result" });
                        return;
                    }
                    await Task.Delay(150, ct);
                }
                // Never reopen a closed picker or repeat the original game upload action.
                CheckError();
                if (!_driver.IsOpen(window))
                {
                    await Task.Delay(150, ct);
                    Check();
                    CheckError();
                    if (!_driver.IsOpen(window)) { Log("DialogClosed"); return; }
                }
                Log("ConfirmRetry", new { confirmAttempt, Actual = _driver.ReadPath(window) });
            }
            throw new TimeoutException("Windows file picker remained open after three verified selection attempts.");
        }
        catch (Exception ex)
        {
            Log("Failed", new { Type = ex.GetType().Name, ex.Message });
            try
            {
                Directory.CreateDirectory(_diagnosticsDirectory);
                _driver.SaveScreenshot(Path.Combine(_diagnosticsDirectory, id + ".png"));
            }
            catch (Exception captureError) { Log("ScreenshotFailed", captureError.Message); }
            throw;
        }
        finally
        {
            Log("WindowCandidates", _driver.GetCandidateDiagnostics());
            try
            {
                Directory.CreateDirectory(_diagnosticsDirectory);
                File.WriteAllText(Path.Combine(_diagnosticsDirectory, id + ".json"), JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { WorkflowPerformanceTrace.Event("FileDialog", "DiagnosticWriteFailed", ex.Message); }
        }
    }

    public static bool DirectoriesMatch(string? actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(actual) || !Path.IsPathFullyQualified(actual)) return false;
        try { return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static bool PathsMatch(string actual, string expected) =>
        string.Equals(actual.Trim().Trim('"'), expected.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
}
