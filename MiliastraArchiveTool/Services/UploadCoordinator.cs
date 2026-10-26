using System.Diagnostics;
using MiliastraArchiveTool.Models;

namespace MiliastraArchiveTool.Services;

[Flags]
public enum UploadServer
{
    None = 0,
    Us = 1,
    Asia = 2,
    Europe = 4,
    HkTw = 8
}

public sealed record UploadRequest(
    LevelArchive Archive,
    UploadServer Server,
    bool IsUpdate,
    string ExistingArchiveIdentifier,
    bool IsMajorVersionUpdate);

public sealed record UploadExecutionResult(bool IsSuccess, string Message)
{
    public static UploadExecutionResult Success(string message = "Upload completed.") => new(true, message);
    public static UploadExecutionResult Failure(string message) => new(false, message);
}

public interface IUploadExecutor
{
    Task<UploadExecutionResult> ExecuteAsync(
        UploadRequest request,
        IProgress<UploadTaskState> progress,
        CancellationToken cancellationToken);
}

public interface IUploadServerLifecycleExecutor
{
    Task BeginServerAsync(
        UploadServer server,
        bool isFirstServer,
        Action<UploadLogLevel, string> log,
        CancellationToken cancellationToken);

    Task EndServerAsync(
        UploadServer server,
        bool hasMoreServers,
        Action<UploadLogLevel, string> log,
        CancellationToken cancellationToken);
}

public sealed class UploadStartupException(string title, string message) : Exception(message)
{
    public string Title { get; } = title;
}

public sealed class SimulatedUploadExecutor : IUploadExecutor
{
    public async Task<UploadExecutionResult> ExecuteAsync(
        UploadRequest request,
        IProgress<UploadTaskState> progress,
        CancellationToken cancellationToken)
    {
        await ReportAfterDelayAsync(UploadTaskState.WaitingForGame, cancellationToken);
        await ReportAfterDelayAsync(UploadTaskState.Recognizing, cancellationToken);
        await ReportAfterDelayAsync(UploadTaskState.Navigating, cancellationToken);
        await ReportAfterDelayAsync(UploadTaskState.Filling, cancellationToken);
        await ReportAfterDelayAsync(UploadTaskState.Submitting, cancellationToken);
        await ReportAfterDelayAsync(UploadTaskState.Verifying, cancellationToken);

        var mode = request.IsUpdate ? "update" : "new archive";
        return UploadExecutionResult.Success($"Simulated {mode} upload completed.");

        async Task ReportAfterDelayAsync(UploadTaskState state, CancellationToken token)
        {
            progress.Report(state);
            await Task.Delay(350, token);
        }
    }
}

public sealed class UploadCoordinator
{
    private readonly IUploadExecutor _executor;
    private readonly TimeSpan _attemptTimeout;
    private readonly int _maximumAttempts;
    private readonly UploadContinuation? _continuation;

    public UploadCoordinator(
        IUploadExecutor executor,
        TimeSpan? attemptTimeout = null,
        int maximumAttempts = 2,
        UploadContinuation? continuation = null)
    {
        _executor = executor;
        _attemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(30);
        _maximumAttempts = Math.Max(1, maximumAttempts);
        _continuation = continuation;
    }

    public async Task RunAsync(
        IReadOnlyList<UploadTask> tasks,
        IReadOnlyList<UploadServer> servers,
        Func<LevelArchive, IReadOnlyList<string>> validate,
        Func<UploadServer, string> formatServerName,
        Action<UploadLogLevel, string> log,
        bool isUpdate,
        string existingArchiveIdentifier,
        bool isMajorVersionUpdate,
        Func<CancellationToken, Task> runPreflight,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runPreflight);
        foreach (var task in tasks)
        {
            task.State = UploadTaskState.Pending;
            task.AttemptCount = 0;
            task.ErrorMessage = string.Empty;
            task.DurationText = string.Empty;
        }

        // No server lifecycle or upload action may run until the entire batch
        // validates and the fresh preflight succeeds.
        try
        {
            var failures = new List<string>();
            foreach (var task in tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                task.State = UploadTaskState.Validating;
                IReadOnlyList<string> errors;
                try
                {
                    errors = validate(task.SourceArchive);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors = [$"校验时发生异常：{ex.Message}"];
                }

                if (errors.Count > 0)
                {
                    task.State = UploadTaskState.Failed;
                    task.ErrorMessage = string.Join(Environment.NewLine, errors);
                    failures.Add($"【{task.UploadTitle}】{Environment.NewLine}{task.ErrorMessage}");
                    log(UploadLogLevel.Error, $"{task.UploadTitle}：校验失败 - {task.ErrorMessage}");
                }
                else
                {
                    task.State = UploadTaskState.Ready;
                    log(UploadLogLevel.Information, $"{task.UploadTitle}：校验通过");
                }
            }

            if (failures.Count > 0)
                throw new UploadStartupException("存档校验失败",
                    "以下存档未通过校验，本次任务已中止。请修正后重新开始。" +
                    Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine + Environment.NewLine, failures));

            cancellationToken.ThrowIfCancellationRequested();
            await runPreflight(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            foreach (var task in tasks.Where(task => task.State != UploadTaskState.Failed))
            {
                task.State = UploadTaskState.Cancelled;
                task.ErrorMessage = "启动检查未完成或未通过，整个批次已中止。";
            }
            throw;
        }

        var lifecycleExecutor = _executor as IUploadServerLifecycleExecutor;
        for (var serverIndex = 0; serverIndex < servers.Count; serverIndex++)
        {
            var server = servers[serverIndex];
            var serverName = formatServerName(server);
            var hasMoreServers = serverIndex < servers.Count - 1;
            var stopAfterTaskFailure = false;
            if (cancellationToken.IsCancellationRequested)
            {
                MarkRemainingCancelled(tasks, serverName, null, log);
                break;
            }

            log(UploadLogLevel.Information, $"开始处理服务器：{serverName}");

            if (lifecycleExecutor != null)
            {
                try
                {
                    await lifecycleExecutor.BeginServerAsync(
                        server,
                        serverIndex == 0,
                        log,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    MarkRemainingCancelled(tasks, serverName, null, log);
                    break;
                }
                catch (Exception ex)
                {
                    log(UploadLogLevel.Error, $"{serverName}: server setup failed - {ex.GetType().Name}: {ex.Message}");
                    MarkRemainingCancelled(tasks, serverName, null, log);
                    break;
                }
            }

            foreach (var task in tasks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    MarkRemainingCancelled(tasks, serverName, task, log);
                    break;
                }

                task.State = UploadTaskState.Ready;
                task.ErrorMessage = string.Empty;
                task.DurationText = string.Empty;

                await ExecuteTaskAsync(
                    task,
                    server,
                    serverName,
                    log,
                    isUpdate,
                    existingArchiveIdentifier,
                    isMajorVersionUpdate,
                    cancellationToken);

                if (lifecycleExecutor != null && task.State == UploadTaskState.Failed)
                {
                    log(UploadLogLevel.Warning, $"{serverName} / {task.UploadTitle}: Stage1 task failed; stopping before server teardown to preserve the game state for diagnostics.");
                    stopAfterTaskFailure = true;
                    break;
                }
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            if (stopAfterTaskFailure)
                break;

            if (lifecycleExecutor != null)
            {
                try
                {
                    await lifecycleExecutor.EndServerAsync(
                        server,
                        hasMoreServers,
                        log,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    MarkRemainingCancelled(tasks, serverName, null, log);
                    break;
                }
                catch (Exception ex)
                {
                    log(UploadLogLevel.Error, $"{serverName}: server switch/logout failed - {ex.GetType().Name}: {ex.Message}");
                    break;
                }
            }
        }
    }

    private async Task ExecuteTaskAsync(
        UploadTask task,
        UploadServer server,
        string serverName,
        Action<UploadLogLevel, string> log,
        bool isUpdate,
        string existingArchiveIdentifier,
        bool isMajorVersionUpdate,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var suspendedAtStart = _continuation?.SuspendedTime ?? TimeSpan.Zero;
        TimeSpan ActiveElapsed() => stopwatch.Elapsed - ((_continuation?.SuspendedTime ?? TimeSpan.Zero) - suspendedAtStart);
        try
        {
            for (var attempt = 1; attempt <= _maximumAttempts; attempt++)
            {
                task.AttemptCount = attempt;
                task.ErrorMessage = string.Empty;
                task.DurationText = FormatDuration(ActiveElapsed());
                log(UploadLogLevel.Information, $"{serverName} / {task.UploadTitle}：第 {attempt}/{_maximumAttempts} 次尝试（attemptTimeout={_attemptTimeout.TotalSeconds:0}s, maximumAttempts={_maximumAttempts}）");

                using var timeoutSource = new CancellationTokenSource();
                using var timeoutMonitorStop = new CancellationTokenSource();
                var timeoutMonitor = MonitorActiveTimeoutAsync(timeoutSource, timeoutMonitorStop.Token);
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);

                var progress = new Progress<UploadTaskState>(state =>
                {
                    task.State = state;
                    task.DurationText = FormatDuration(ActiveElapsed());
                    log(UploadLogLevel.Information, $"{serverName} / {task.UploadTitle}：{task.UploadState}");
                });

                try
                {
                    var result = await _executor.ExecuteAsync(
                        new UploadRequest(
                            task.SourceArchive,
                            server,
                            isUpdate,
                            existingArchiveIdentifier,
                            isMajorVersionUpdate),
                        progress,
                        linkedSource.Token);

                    task.DurationText = FormatDuration(ActiveElapsed());
                    if (result.IsSuccess)
                    {
                        task.State = UploadTaskState.Succeeded;
                        task.ErrorMessage = string.Empty;
                        log(UploadLogLevel.Information, $"{serverName} / {task.UploadTitle}：{result.Message}");
                        return;
                    }

                    task.ErrorMessage = result.Message;
                    log(UploadLogLevel.Warning, $"{serverName} / {task.UploadTitle}：第 {attempt} 次尝试失败 - {result.Message}");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    task.State = UploadTaskState.Cancelled;
                    task.ErrorMessage = "Cancelled by user.";
                    task.DurationText = FormatDuration(ActiveElapsed());
                    log(UploadLogLevel.Warning, $"{serverName} / {task.UploadTitle}：已取消");
                    return;
                }
                catch (OperationCanceledException)
                {
                    task.ErrorMessage = $"Attempt timed out after {_attemptTimeout.TotalSeconds:0} seconds.";
                    task.DurationText = FormatDuration(ActiveElapsed());
                    log(UploadLogLevel.Warning, $"{serverName} / {task.UploadTitle}：第 {attempt} 次尝试达到总时限（{_attemptTimeout.TotalMinutes:0.#} 分钟），自动取消；elapsedSeconds={ActiveElapsed().TotalSeconds:0.0}");
                }
                catch (Exception ex)
                {
                    task.ErrorMessage = ex.Message;
                    task.DurationText = FormatDuration(ActiveElapsed());
                    log(UploadLogLevel.Error, $"{serverName} / {task.UploadTitle}：第 {attempt} 次尝试异常 {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    timeoutMonitorStop.Cancel();
                    await timeoutMonitor;
                }
            }

            task.State = UploadTaskState.Failed;
            log(UploadLogLevel.Error, $"{serverName} / {task.UploadTitle}：达到最大尝试次数后失败");
        }
        finally
        {
            stopwatch.Stop();
            task.DurationText = FormatDuration(ActiveElapsed());
        }
    }

    private async Task MonitorActiveTimeoutAsync(CancellationTokenSource timeout, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        var excludedAtStart = _continuation?.SuspendedTime ?? TimeSpan.Zero;
        try
        {
            while (clock.Elapsed - ((_continuation?.SuspendedTime ?? TimeSpan.Zero) - excludedAtStart) < _attemptTimeout)
                await Task.Delay(100, stop);
            timeout.Cancel();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private static void MarkRemainingCancelled(
        IReadOnlyList<UploadTask> tasks,
        string serverName,
        UploadTask? firstRemaining,
        Action<UploadLogLevel, string> log)
    {
        var startIndex = firstRemaining == null ? 0 : tasks.IndexOf(firstRemaining);
        if (startIndex < 0)
            return;

        for (var index = startIndex; index < tasks.Count; index++)
        {
            tasks[index].State = UploadTaskState.Cancelled;
            tasks[index].ErrorMessage = "Batch cancelled.";
            log(UploadLogLevel.Warning, $"{serverName} / {tasks[index].UploadTitle}：已标记为取消");
        }
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
}

internal static class UploadTaskReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value))
                return index;
        }

        return -1;
    }
}
