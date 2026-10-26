using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Automation.Workflows;

public interface ISandboxProcessWatcher
{
    bool IsSandboxRunning();

    SandboxWindowInfo? FindSandboxWindow();

    Task<SandboxProcessWaitResult> WaitForStableAsync(
        TimeSpan stableDuration,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<SandboxWindowWaitResult> WaitForWindowAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed record SandboxWindowInfo(
    string ProcessName,
    int ProcessId,
    IntPtr WindowHandle,
    string WindowTitle,
    bool IsVisible,
    bool IsIconic,
    int WindowWidth,
    int WindowHeight);

public sealed record SandboxWindowWaitResult(
    bool Found,
    bool WasProcessSeen,
    bool WasWindowSeen,
    TimeSpan Elapsed,
    SandboxWindowInfo? Window,
    string Diagnostic);

public interface IUploadRunArchiveService
{
    Task<UploadRunArchive> CreateAsync(
        LevelArchive archive,
        UploadServer server,
        CancellationToken cancellationToken);
}

public interface IStage1WorkflowClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemStage1WorkflowClock : IStage1WorkflowClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
