using System.IO;

namespace MiliastraArchiveTool.Infrastructure;

public static class ApplicationPaths
{
    private const string RuntimeDirectoryName = "runtime-data";
    private const string AppDirectoryName = "MiliastraArchiveTool";

    public static string WorkspaceRoot { get; } = ResolveWorkspaceRoot();

    public static string RuntimeRoot { get; } =
        Path.Combine(WorkspaceRoot, RuntimeDirectoryName, AppDirectoryName);

    public static string DiagnosticsDirectory { get; } =
        Path.Combine(RuntimeRoot, "logs", "diagnostics");

    public static string DatasetRoot { get; } =
        Path.Combine(WorkspaceRoot, "data", "千星奇域训练截图");

    public static string LegacyLocalAppDataRoot { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDirectoryName);

    public static void EnsureRuntimeDirectories()
    {
        Directory.CreateDirectory(RuntimeRoot);
        Directory.CreateDirectory(DiagnosticsDirectory);
    }

    private static string ResolveWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MiliastraArchiveTool.sln")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
