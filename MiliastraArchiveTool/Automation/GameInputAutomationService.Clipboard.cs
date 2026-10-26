using System.Runtime.InteropServices;
using System.Windows;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Automation;

public sealed partial class GameInputAutomationService
{
    private static readonly SemaphoreSlim ClipboardGate = new(1, 1);

    // Isolated from the normal text and file-dialog paths. Never fall back to
    // character injection after a failed paste: that can silently corrupt CJK.
    public async Task ReplaceMultilanguageTextAsync(GameWindowInfo window, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var timing = WorkflowPerformanceTrace.Measure("Input.ReplaceMultilanguageText",
            new { Characters = text.Length, Method = "UnicodeClipboard" });
        await ClipboardGate.WaitAsync(ct);
        DataObject? previous = null;
        uint ownedSequence = 0;
        var ownsClipboard = false;
        try
        {
            EnsureForeground();
            await OnClipboardThreadAsync(() =>
            {
                var old = Clipboard.GetDataObject();
                if (old != null)
                {
                    // Materialize formats before replacing the clipboard.
                    previous = new DataObject();
                    foreach (var format in old.GetFormats(false))
                        previous.SetData(format, old.GetData(format, false), false);
                }
            }, ct);
            await OnClipboardThreadAsync(() =>
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                ownedSequence = GetClipboardSequenceNumber();
                ownsClipboard = true;
            }, ct);
            await OnClipboardThreadAsync(() =>
            {
                if (Clipboard.GetText(TextDataFormat.UnicodeText) != text)
                    throw new InvalidOperationException("Unicode clipboard text verification failed.");
            }, ct);
            ct.ThrowIfCancellationRequested();
            EnsureForeground();
            SendCtrlAOrThrow();
            await Task.Delay(120, ct);
            EnsureForeground();
            if (GetClipboardSequenceNumber() != ownedSequence)
                throw new InvalidOperationException("Clipboard changed before paste. Keep the current editor open and retry.");
            var inputs = new[]
            {
                CreateVirtualKeyInput(GameVirtualKey.Control, false),
                CreateVirtualKeyInput(GameVirtualKey.V, false),
                CreateVirtualKeyInput(GameVirtualKey.V, true),
                CreateVirtualKeyInput(GameVirtualKey.Control, true)
            };
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
                throw new InvalidOperationException("Unable to send Ctrl+V for multilingual text.");
            // Leave text available long enough for the game to consume paste,
            // including if cancellation arrives immediately after the keypress.
            await Task.Delay(300, CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            EnsureForeground();
        }
        finally
        {
            try
            {
                if (ownsClipboard)
                    await OnClipboardThreadAsync(() =>
                    {
                        if (GetClipboardSequenceNumber() != ownedSequence) return;
                        if (previous != null) Clipboard.SetDataObject(previous, true);
                        else Clipboard.Clear();
                    }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                WorkflowPerformanceTrace.Event("Input.ClipboardRestore", "Warning", new { Error = ex.Message });
            }
            finally { ClipboardGate.Release(); }
        }

        void EnsureForeground()
        {
            _activation.RestoreAndActivate(window);
            var foreground = _activation.GetForegroundWindowInfo(window);
            if (!foreground.IsTargetHandleForeground && !foreground.IsTargetProcessForeground)
                throw new InvalidOperationException($"Game window is not foreground for clipboard input. {foreground.ToDiagnosticString()}");
        }
    }

    private static async Task OnClipboardThreadAsync(Action action, CancellationToken ct)
    {
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("Unicode clipboard input requires the application dispatcher.");
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { await dispatcher.InvokeAsync(action); return; }
            catch (COMException) when (attempt < 5) { await Task.Delay(100, ct); }
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
