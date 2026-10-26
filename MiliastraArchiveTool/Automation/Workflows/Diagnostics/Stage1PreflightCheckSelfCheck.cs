namespace MiliastraArchiveTool.Automation.Workflows.Diagnostics;

public static class Stage1PreflightCheckSelfCheck
{
    public static async Task<Stage1PreflightCheckReport> RunAsync(
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var check = new Stage1PreflightCheck();
        return await check.RunAsync(outputDirectory, cancellationToken);
    }
}
