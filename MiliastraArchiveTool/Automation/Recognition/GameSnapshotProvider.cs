using MiliastraArchiveTool.Infrastructure;
using MiliastraArchiveTool.Automation;

namespace MiliastraArchiveTool.Automation.Recognition;

public sealed class GameSnapshotProvider : IGameSnapshotProvider
{
    private readonly BestGameScreenCaptureService _captureService;
    private readonly GameCaptureStrategy _strategy;

    public GameSnapshotProvider(
        BestGameScreenCaptureService? captureService = null,
        GameCaptureStrategy strategy = GameCaptureStrategy.DefaultBest)
    {
        _captureService = captureService ?? new BestGameScreenCaptureService();
        _strategy = strategy;
    }

    public GameSnapshotProvider(GameCaptureStrategy strategy)
        : this(null, strategy)
    {
    }

    public async Task<GameCaptureResult> CaptureAsync(CancellationToken cancellationToken)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("Capture", new { Strategy = _strategy.ToString() });
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Task.Run(() => _captureService.Capture(_strategy), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
