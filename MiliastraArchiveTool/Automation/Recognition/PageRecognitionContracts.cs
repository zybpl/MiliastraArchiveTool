using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Automation;

namespace MiliastraArchiveTool.Automation.Recognition;

public sealed record AutomationTarget(
    string Name,
    double CenterX,
    double CenterY,
    double Width,
    double Height,
    double Confidence);

public sealed record PageRecognitionResult(
    GamePage Page,
    double Confidence,
    string Source,
    IReadOnlyList<AutomationTarget> Targets);

public interface IPageRecognizer
{
    Task<PageRecognitionResult> RecognizeAsync(
        BitmapSource bitmap,
        CancellationToken cancellationToken);
}

public interface ITargetLocator
{
    AutomationTarget? FindTarget(
        PageRecognitionResult recognition,
        string targetName,
        double minimumConfidence = 0.3);
}

public interface IGameSnapshotProvider
{
    Task<GameCaptureResult> CaptureAsync(CancellationToken cancellationToken);
}

public sealed record StablePageResult(
    GameCaptureResult Capture,
    PageRecognitionResult Recognition);
