namespace MiliastraArchiveTool.Automation;

public sealed record ForegroundWindowInfo(
    nint ForegroundHandle,
    int ForegroundProcessId,
    string? ForegroundProcessName,
    string ForegroundTitle,
    nint TargetGameHandle,
    int TargetProcessId,
    bool IsTargetHandleForeground,
    bool IsTargetProcessForeground)
{
    public string ToDiagnosticString() =>
        $"foregroundHandle=0x{ForegroundHandle.ToInt64():X}, " +
        $"foregroundProcessId={ForegroundProcessId}, " +
        $"foregroundProcessName={ForegroundProcessName ?? "<unknown>"}, " +
        $"foregroundTitle=\"{ForegroundTitle}\", " +
        $"targetHandle=0x{TargetGameHandle.ToInt64():X}, " +
        $"targetProcessId={TargetProcessId}, " +
        $"isTargetHandleForeground={IsTargetHandleForeground}, " +
        $"isTargetProcessForeground={IsTargetProcessForeground}";
}
