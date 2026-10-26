using System.IO;
using MiliastraArchiveTool.Automation;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Automation.Recognition;

public sealed class StablePageWaiter
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(650);

    private readonly IGameSnapshotProvider _snapshotProvider;
    private readonly IPageRecognizer _recognizer;
    private readonly TimeSpan _pollInterval;

    public StablePageWaiter(
        IGameSnapshotProvider snapshotProvider,
        IPageRecognizer recognizer,
        TimeSpan? pollInterval = null)
    {
        _snapshotProvider = snapshotProvider;
        _recognizer = recognizer;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        if (_pollInterval < TimeSpan.FromMilliseconds(500) ||
            _pollInterval > TimeSpan.FromMilliseconds(1000))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "Poll interval must be between 500ms and 1000ms.");
        }
    }

    public async Task<StablePageResult> WaitForAnyAsync(
        IReadOnlyCollection<GamePage> expectedPages,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int requiredConsecutiveMatches = 2,
        double minimumConfidence = 0.45,
        bool throwOnBlocking = true)
    {
        if (expectedPages.Count == 0)
            throw new ArgumentException("Expected pages cannot be empty.", nameof(expectedPages));
        if (requiredConsecutiveMatches <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveMatches));

        var expected = expectedPages.ToHashSet();
        var deadline = DateTimeOffset.UtcNow + timeout;
        StablePageResult? lastResult = null;
        GamePage? stableCandidate = null;
        var consecutiveMatches = 0;
        var blockingStreak = new PageObservationStreak();

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RecognizeOnceAsync(cancellationToken);
            lastResult = result;
            var recognition = result.Recognition;

            if (blockingStreak.Observe(recognition.Page, throwOnBlocking && recognition.Page.IsBlocking()))
                throw CreateBlockingException(recognition);

            if (expected.Contains(recognition.Page) &&
                recognition.Confidence >= minimumConfidence)
            {
                if (stableCandidate == recognition.Page)
                {
                    consecutiveMatches++;
                }
                else
                {
                    stableCandidate = recognition.Page;
                    consecutiveMatches = 1;
                }

                if (consecutiveMatches >= Math.Max(2, requiredConsecutiveMatches))
                    return result;
            }
            else
            {
                stableCandidate = null;
                consecutiveMatches = 0;
            }

            await DelayUntilNextPollAsync(deadline, cancellationToken);
        }

        var diagnosticPath = SaveTimeoutDiagnostic(lastResult);
        throw CreateTimeoutException(expected, lastResult, diagnosticPath);
    }

    public async Task<StablePageResult> WaitForActionableAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int requiredConsecutiveMatches = 2,
        double minimumConfidence = 0.45)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        StablePageResult? lastResult = null;
        GamePage? stableCandidate = null;
        var consecutiveMatches = 0;
        var blockingStreak = new PageObservationStreak();

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RecognizeOnceAsync(cancellationToken);
            lastResult = result;
            var recognition = result.Recognition;

            if (blockingStreak.Observe(recognition.Page, recognition.Page.IsBlocking()))
                throw CreateBlockingException(recognition);

            if (recognition.Page.IsActionable() &&
                recognition.Confidence >= minimumConfidence)
            {
                if (stableCandidate == recognition.Page)
                {
                    consecutiveMatches++;
                }
                else
                {
                    stableCandidate = recognition.Page;
                    consecutiveMatches = 1;
                }

                if (consecutiveMatches >= Math.Max(2, requiredConsecutiveMatches))
                    return result;
            }
            else
            {
                stableCandidate = null;
                consecutiveMatches = 0;
            }

            await DelayUntilNextPollAsync(deadline, cancellationToken);
        }

        var diagnosticPath = SaveTimeoutDiagnostic(lastResult);
        throw CreateTimeoutException(["<any actionable page>"], lastResult, diagnosticPath);
    }

    public async Task<StablePageResult> RecognizeOnceAsync(CancellationToken cancellationToken)
    {
        var capture = await _snapshotProvider.CaptureAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var recognition = await _recognizer.RecognizeAsync(capture.Bitmap, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new StablePageResult(capture, recognition);
    }

    private async Task DelayUntilNextPollAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return;

        await Task.Delay(remaining < _pollInterval ? remaining : _pollInterval, cancellationToken);
    }

    private static InvalidOperationException CreateBlockingException(PageRecognitionResult recognition) =>
        new(
            $"Blocking page detected: {recognition.Page}, confidence={recognition.Confidence:F2}, source={recognition.Source}.");

    private static TimeoutException CreateTimeoutException(
        IEnumerable<GamePage> expectedPages,
        StablePageResult? lastResult,
        string? diagnosticPath)
    {
        var lastRecognition = lastResult?.Recognition;
        var expectedText = string.Join(", ", expectedPages);
        var lastPage = lastRecognition?.Page.ToString() ?? "<none>";
        var lastConfidence = lastRecognition?.Confidence.ToString("F2") ?? "<none>";
        var pathText = diagnosticPath ?? "<not saved>";

        return new TimeoutException(
            $"Timed out waiting for pages [{expectedText}]. Last page={lastPage}, confidence={lastConfidence}, diagnostic={pathText}.");
    }

    private static TimeoutException CreateTimeoutException(
        IEnumerable<string> expectedPages,
        StablePageResult? lastResult,
        string? diagnosticPath)
    {
        var lastRecognition = lastResult?.Recognition;
        var expectedText = string.Join(", ", expectedPages);
        var lastPage = lastRecognition?.Page.ToString() ?? "<none>";
        var lastConfidence = lastRecognition?.Confidence.ToString("F2") ?? "<none>";
        var pathText = diagnosticPath ?? "<not saved>";

        return new TimeoutException(
            $"Timed out waiting for pages [{expectedText}]. Last page={lastPage}, confidence={lastConfidence}, diagnostic={pathText}.");
    }

    private static string? SaveTimeoutDiagnostic(StablePageResult? lastResult)
    {
        if (lastResult == null)
            return null;

        var directory = ApplicationPaths.DiagnosticsDirectory;
        var lastPage = lastResult.Recognition.Page;
        var fileName = $"page-timeout-{DateTime.Now:yyyyMMdd-HHmmssfff}-{lastPage}.png";
        var path = Path.Combine(directory, fileName);
        GameWindowCaptureService.SavePng(lastResult.Capture.Bitmap, path);
        return path;
    }
}

