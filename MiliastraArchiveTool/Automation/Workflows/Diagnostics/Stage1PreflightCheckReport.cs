using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Automation;

namespace MiliastraArchiveTool.Automation.Workflows.Diagnostics;

public sealed record Stage1PreflightCheckReport(
    DateTimeOffset CreatedAt,
    Stage1PreflightModelInfo Model,
    Stage1PreflightWindowInfo? Window,
    IReadOnlyList<Stage1PreflightWindowCandidateInfo> WindowCandidates,
    Stage1PreflightCaptureInfo? Capture,
    string? ScreenshotPath,
    GamePage RawPage,
    GamePage GatedPage,
    double Confidence,
    string Source,
    bool ReadyForStage1CurrentServerStart,
    string Message,
    IReadOnlyList<Stage1PreflightTargetInfo> TopTargets,
    string? ErrorMessage)
{
    public bool RecoveryEscapeUsed { get; init; }
}

public sealed record Stage1PreflightModelInfo(
    string AssetDirectory,
    string OnnxModelPath,
    bool OnnxModelExists,
    string ClassesPath,
    int ClassCount,
    double DefaultThreshold);

public sealed record Stage1PreflightWindowInfo(
    string Title,
    string Handle,
    int ProcessId,
    int ClientWidth,
    int ClientHeight,
    int ClientScreenLeft,
    int ClientScreenTop,
    uint Dpi,
    bool IsFullscreenLike,
    bool IsMinimized);

public sealed record Stage1PreflightWindowCandidateInfo(
    string Handle,
    int ProcessId,
    string Title,
    bool IsVisible,
    bool IsMinimized,
    int ClientWidth,
    int ClientHeight,
    int WindowWidth,
    int WindowHeight,
    long ClientArea,
    bool IsSelected,
    string? FailureReason);

public sealed record Stage1PreflightCaptureInfo(
    GameCaptureStrategy CaptureStrategy,
    string CaptureMethod,
    double GdiQuality,
    double? PrintWindowQuality,
    double? DxgiQuality,
    IReadOnlyList<GameCaptureCandidateQuality> CaptureCandidates,
    double SelectedQuality,
    bool IsLikelyBlackFrame,
    bool FallbackToGdi,
    string? Warning);

public sealed record Stage1PreflightTargetInfo(
    string Name,
    double Confidence,
    double CenterX,
    double CenterY,
    double Width,
    double Height);
