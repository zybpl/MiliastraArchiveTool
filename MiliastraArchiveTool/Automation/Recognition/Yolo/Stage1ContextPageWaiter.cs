using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiliastraArchiveTool.Automation;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed class Stage1ContextPageWaiter : IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(650);

    private readonly IGameSnapshotProvider _snapshotProvider;
    private readonly IPageRecognizer _recognizer;
    private readonly Stage1ContextPageGuard _guard;
    private readonly TimeSpan _pollInterval;
    private readonly bool _ownsRecognizer;

    private bool _disposed;

    public Stage1ContextPageWaiter()
        : this(new GameSnapshotProvider(), new Stage1GatedPageRecognizer(), ownsRecognizer: true)
    {
    }

    public Stage1ContextPageWaiter(
        IGameSnapshotProvider snapshotProvider,
        IPageRecognizer recognizer,
        Stage1ContextPageGuard? guard = null,
        TimeSpan? pollInterval = null,
        bool ownsRecognizer = false)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _guard = guard ?? new Stage1ContextPageGuard();
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _ownsRecognizer = ownsRecognizer;

        if (_pollInterval < TimeSpan.FromMilliseconds(500) ||
            _pollInterval > TimeSpan.FromMilliseconds(1000))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "Poll interval must be between 500ms and 1000ms.");
        }
    }

    public async Task<StablePageResult> WaitForExpectedAsync(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        TimeSpan timeout,
        int stableCount,
        CancellationToken cancellationToken,
        bool allowWaitingPages = true)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("WaitForPage", new { Step = stepName, Expected = expectedPages.Select(p => p.ToString()).ToArray(), TimeoutMs = timeout.TotalMilliseconds, RequiredStableCount = Math.Max(2, stableCount) });
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (expectedPages.Count == 0)
            throw new ArgumentException("Expected pages cannot be empty.", nameof(expectedPages));
        if (stableCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(stableCount));

        var deadline = DateTimeOffset.UtcNow + timeout;
        StablePageResult? lastResult = null;
        Stage1ContextPageDecision? lastDecision = null;
        GamePage? stableCandidate = null;
        var consecutiveMatches = 0;
        var blockingStreak = new PageObservationStreak();

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pollStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = await RecognizeOnceAsync(cancellationToken);
            lastResult = result;
            lastDecision = _guard.Decide(result.Recognition, expectedPages, stepName, allowWaitingPages);
            WorkflowPerformanceTrace.Event("PageObservation", "Observed", new { Step = stepName,
                Page = result.Recognition.Page.ToString(), result.Recognition.Confidence,
                lastDecision.IsAccepted, lastDecision.IsExpected, lastDecision.Reason,
                PreviousStableCount = consecutiveMatches, RequiredStableCount = Math.Max(2, stableCount) });

            if (blockingStreak.Observe(result.Recognition.Page, lastDecision.IsBlocking))
                throw CreateBlockingException(stepName, expectedPages, lastDecision);

            if (lastDecision.IsAccepted && lastDecision.IsExpected)
            {
                if (stableCandidate == lastDecision.RecognizedPage)
                {
                    consecutiveMatches++;
                }
                else
                {
                    stableCandidate = lastDecision.RecognizedPage;
                    consecutiveMatches = 1;
                }

                if (consecutiveMatches >= Math.Max(2, stableCount))
                    return result;
            }
            else
            {
                stableCandidate = null;
                consecutiveMatches = 0;
            }

            // Only accelerate confirming an expected page. Loading/unknown pages keep their existing cadence.
            var workTime = lastDecision.IsAccepted && lastDecision.IsExpected
                ? System.Diagnostics.Stopwatch.GetElapsedTime(pollStarted) : TimeSpan.Zero;
            await DelayUntilNextPollAsync(deadline, workTime, cancellationToken);
        }

        var diagnostic = SaveTimeoutDiagnostic(stepName, expectedPages, lastResult, lastDecision);
        throw CreateTimeoutException(stepName, expectedPages, lastResult, lastDecision, diagnostic);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_ownsRecognizer && _recognizer is IDisposable disposable)
            disposable.Dispose();

        _disposed = true;
    }

    private async Task<StablePageResult> RecognizeOnceAsync(CancellationToken cancellationToken)
    {
        var capture = await _snapshotProvider.CaptureAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var recognition = await _recognizer.RecognizeAsync(capture.Bitmap, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new StablePageResult(capture, recognition);
    }

    private async Task DelayUntilNextPollAsync(
        DateTimeOffset deadline,
        TimeSpan workTime,
        CancellationToken cancellationToken)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return;

        var interval = TimeSpan.FromMilliseconds(Math.Max(100, (_pollInterval - workTime).TotalMilliseconds));
        var delay = remaining < interval ? remaining : interval;
        using var timing = WorkflowPerformanceTrace.Measure("Delay", new { Caller = "PageWaiter.Poll", RequestedMs = delay.TotalMilliseconds, RecognitionMs = workTime.TotalMilliseconds });
        await Task.Delay(delay, cancellationToken);
    }

    private static InvalidOperationException CreateBlockingException(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        Stage1ContextPageDecision decision) =>
        new(
            $"Blocking page detected during step '{stepName}'. Expected [{FormatExpectedPages(expectedPages)}], " +
            $"recognized={decision.RecognizedPage}, confidence={decision.Recognition.Confidence:F2}, " +
            $"source={decision.Recognition.Source}. Reason: {decision.Reason}");

    private static TimeoutException CreateTimeoutException(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        StablePageResult? lastResult,
        Stage1ContextPageDecision? lastDecision,
        Stage1ContextTimeoutDiagnosticPaths diagnostic)
    {
        var lastRecognition = lastResult?.Recognition;
        var lastPage = lastRecognition?.Page.ToString() ?? "<none>";
        var lastConfidence = lastRecognition?.Confidence.ToString("F2") ?? "<none>";
        var lastReason = lastDecision?.Reason ?? "<none>";
        var pathText = diagnostic.ScreenshotPath ?? "<not saved>";
        var jsonText = diagnostic.JsonPath ?? "<not saved>";

        return new TimeoutException(
            $"Timed out during step '{stepName}' waiting for expected pages [{FormatExpectedPages(expectedPages)}]. " +
            $"Last page={lastPage}, confidence={lastConfidence}, lastDecisionReason={lastReason}, diagnostic={pathText}, diagnosticJson={jsonText}.");
    }

    private static Stage1ContextTimeoutDiagnosticPaths SaveTimeoutDiagnostic(
        string stepName,
        IReadOnlySet<GamePage> expectedPages,
        StablePageResult? lastResult,
        Stage1ContextPageDecision? lastDecision)
    {
        if (lastResult == null)
            return new Stage1ContextTimeoutDiagnosticPaths(null, null);

        var directory = ApplicationPaths.DiagnosticsDirectory;
        var lastPage = lastResult.Recognition.Page;
        var baseName = $"stage1-context-timeout-{DateTime.Now:yyyyMMdd-HHmmssfff}-{lastPage}";
        var screenshotPath = Path.Combine(directory, baseName + ".png");
        var jsonPath = Path.Combine(directory, baseName + ".json");
        GameWindowCaptureService.SavePng(lastResult.Capture.Bitmap, screenshotPath);

        var foreground = new GameWindowActivationService()
            .GetForegroundWindowInfo(lastResult.Capture.Window)
            .ToDiagnosticString();
        var report = new Stage1ContextTimeoutDiagnosticReport(
            DateTimeOffset.Now,
            stepName,
            expectedPages.Select(page => page.ToString()).Order(StringComparer.Ordinal).ToList(),
            lastResult.Recognition.Page,
            lastResult.Recognition.Confidence,
            lastDecision?.Reason,
            lastResult.Capture.Strategy,
            lastResult.Capture.CaptureMethod,
            lastResult.Capture.Warning,
            lastResult.Capture.CandidateQualities ?? [],
            lastResult.Capture.IsLikelyBlackFrame,
            lastResult.Capture.FallbackToGdi,
            foreground,
            lastResult.Recognition.Targets
                .OrderByDescending(target => target.Confidence)
                .Take(20)
                .Select(target => new Stage1ContextTimeoutTarget(
                    target.Name,
                    target.Confidence,
                    target.CenterX,
                    target.CenterY,
                    target.Width,
                    target.Height))
                .ToList(),
            screenshotPath);

        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, options));
        return new Stage1ContextTimeoutDiagnosticPaths(screenshotPath, jsonPath);
    }

    private static string FormatExpectedPages(IEnumerable<GamePage> expectedPages) =>
        string.Join(", ", expectedPages.OrderBy(page => page.ToString(), StringComparer.Ordinal));

    private sealed record Stage1ContextTimeoutDiagnosticPaths(
        string? ScreenshotPath,
        string? JsonPath);

    private sealed record Stage1ContextTimeoutDiagnosticReport(
        DateTimeOffset CreatedAt,
        string StepName,
        IReadOnlyList<string> ExpectedPages,
        GamePage LastRecognizedPage,
        double Confidence,
        string? LastDecisionReason,
        GameCaptureStrategy CaptureStrategy,
        string CaptureMethod,
        string? CaptureWarning,
        IReadOnlyList<GameCaptureCandidateQuality> CaptureCandidates,
        bool IsLikelyBlackFrame,
        bool FallbackToGdi,
        string ForegroundWindowDiagnostic,
        IReadOnlyList<Stage1ContextTimeoutTarget> TopTargets,
        string DiagnosticScreenshotPath);

    private sealed record Stage1ContextTimeoutTarget(
        string Name,
        double Confidence,
        double CenterX,
        double CenterY,
        double Width,
        double Height);
}

