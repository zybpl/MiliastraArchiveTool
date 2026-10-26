using System.Diagnostics;

namespace MiliastraArchiveTool.Services;

public enum UploadContinuationState { Running, PauseRequested, Paused, Interrupted, Checking, Stopped }
public sealed record UploadContinuationStatus(UploadContinuationState State, string Step, string Message);

/// <summary>One in-memory batch. Keeps the awaiting workflow alive; never replays the batch.</summary>
public sealed class UploadContinuation
{
    private readonly object _sync = new();
    private TaskCompletionSource<bool>? _signal;
    private UploadContinuationState _state;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private TimeSpan _excluded;
    private TimeSpan? _suspendedAt;
    public event Action<UploadContinuationStatus>? Changed;
    public UploadContinuationState State { get { lock (_sync) return _state; } }
    public TimeSpan SuspendedTime { get { lock (_sync) return _excluded + (_suspendedAt is { } at ? _elapsed.Elapsed - at : TimeSpan.Zero); } }
    public TimeSpan ActiveElapsed => _elapsed.Elapsed - SuspendedTime;

    public void RequestPause()
    {
        lock (_sync) { if (_state != UploadContinuationState.Running) return; _state = UploadContinuationState.PauseRequested; }
        Publish(new(State, "", "Pausing after the current safe operation"));
    }

    public void Continue()
    {
        TaskCompletionSource<bool>? signal;
        lock (_sync)
        {
            if (_state is not (UploadContinuationState.Paused or UploadContinuationState.Interrupted)) return;
            _state = UploadContinuationState.Checking;
            signal = _signal;
        }
        Publish(new(State, "", "Checking the current page before continuing"));
        signal?.TrySetResult(true);
    }

    public void Stop()
    {
        TaskCompletionSource<bool>? signal;
        lock (_sync) { _state = UploadContinuationState.Stopped; signal = _signal; }
        signal?.TrySetCanceled();
    }

    public async Task WaitAsync(string step, string? error, Func<CancellationToken, Task<string?>> validate, CancellationToken ct)
    {
        if (error == null && State != UploadContinuationState.PauseRequested) return;
        lock (_sync) _suspendedAt ??= _elapsed.Elapsed;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                Task signal;
                lock (_sync)
                {
                    if (_state == UploadContinuationState.Stopped) throw new OperationCanceledException(ct);
                    _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _state = error == null ? UploadContinuationState.Paused : UploadContinuationState.Interrupted;
                    signal = _signal.Task;
                }
                Publish(new(State, step, error ?? "Paused at a safe boundary"));
                await signal.WaitAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                try { error = await validate(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { error = "Resume check failed: " + ex.Message; }
                if (error != null) continue;
                lock (_sync)
                {
                    if (_state == UploadContinuationState.Stopped) throw new OperationCanceledException(ct);
                    _state = UploadContinuationState.Running;
                }
                Publish(new(State, step, "Resume checks passed; continuing this step"));
                return;
            }
        }
        finally
        {
            lock (_sync)
            {
                if (_suspendedAt is { } at) _excluded += _elapsed.Elapsed - at;
                _suspendedAt = null;
                _signal = null;
            }
        }
    }

    private void Publish(UploadContinuationStatus status)
    {
        try { Changed?.Invoke(status); } catch { /* UI must not break the awaiting workflow. */ }
    }
}
