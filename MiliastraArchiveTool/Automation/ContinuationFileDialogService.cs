using System.IO;
using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Automation;

/// <summary>Retries selection in the verified, still-open chooser; never clicks the game upload button again.</summary>
public sealed class ContinuationFileDialogService(IWindowsFileDialogAutomationService inner,
    UploadContinuation continuation, IFilePickerDriver? inspection = null) : IWindowsFileDialogAutomationService
{
    private readonly IFilePickerDriver _inspection = inspection ?? new WindowsFilePickerDriver();
    public Task SelectFileAsync(string path, TimeSpan timeout, CancellationToken ct) => throw new InvalidOperationException("Game owner is required.");
    public Task SelectFilesAsync(IReadOnlyList<string> paths, TimeSpan timeout, CancellationToken ct) => throw new InvalidOperationException("Game owner is required.");
    public Task SelectFileAsync(GameWindowInfo owner, string path, TimeSpan timeout, CancellationToken ct) => SelectFilesAsync(owner, [path], timeout, ct);
    public async Task SelectFilesAsync(GameWindowInfo owner, IReadOnlyList<string> paths, TimeSpan timeout, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { await inner.SelectFilesAsync(owner, paths, timeout, ct); return; }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
            {
                FilePickerWindow? expected = null;
                try { expected = await Task.Run(() => { _inspection.ResetDiagnostics(); return _inspection.Find(owner); }, ct); }
                catch (OperationCanceledException) { throw; }
                catch { }
                var window = expected;
                await continuation.WaitAsync("File selection: " + string.Join(", ", paths.Select(Path.GetFileName)), ex.Message,
                    token => Task.Run<string?>(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (window == null) return "The original file window is unavailable; selection outcome is uncertain. Terminate instead of uploading twice.";
                        _inspection.ResetDiagnostics();
                        var current = _inspection.Find(owner);
                        if (current?.Handle != window.Handle || current.ProcessId != window.ProcessId || !_inspection.IsOpen(window))
                            return "Keep the original file selection window open. A different or closed window cannot be resumed.";
                        var error = _inspection.ReadError(current);
                        if (!string.IsNullOrWhiteSpace(error)) return "Dismiss the file selection error first: " + error;
                        foreach (var path in paths) { using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
                        return null;
                    }, token), ct);
            }
        }
    }
}
