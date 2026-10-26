using System.IO;
using System.Text.RegularExpressions;

namespace MiliastraArchiveTool.Infrastructure;

public static class DiagnosticLogRetention
{
    public static (int DeletedFiles, long FreedBytes) Clean(string? directory = null, long maxBytes = 256L * 1024 * 1024)
    {
        directory = Path.GetFullPath(directory ?? ApplicationPaths.DiagnosticsDirectory);
        if (!Directory.Exists(directory)) return (0, 0);
        // Only direct diagnostic artifacts; never recurse or follow directory links.
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return (0, 0);
        var files = new DirectoryInfo(directory).GetFiles()
            .Where(f => (f.Attributes & FileAttributes.ReparsePoint) == 0 &&
                f.Name.StartsWith("stage1-", StringComparison.OrdinalIgnoreCase) &&
                new[] { ".png", ".json", ".jsonl", ".md" }.Contains(f.Extension.ToLowerInvariant())).ToArray();
        var groups = files.GroupBy(f =>
        {
            var match = Regex.Match(f.Name, @"\d{8}-\d{9}-[a-f0-9]{9}");
            return match.Success ? match.Value : Path.GetFileNameWithoutExtension(f.Name);
        }).Select(g => new { Key = g.Key, Files = g.ToArray(), Latest = g.Max(f => f.LastWriteTimeUtc), Bytes = g.Sum(f => f.Length) })
            .OrderByDescending(g => g.Latest).ToArray();
        var protectedKeys = groups.Where(g => Regex.IsMatch(g.Key, @"^\d{8}-\d{9}-[a-f0-9]{9}$"))
            .Take(3).Select(g => g.Key).ToHashSet();
        long total = files.Sum(f => f.Length), freed = 0;
        int deleted = 0;
        var now = DateTime.UtcNow;
        foreach (var group in groups.Reverse())
        {
            if (protectedKeys.Contains(group.Key) || group.Latest > now.AddMinutes(-30)) continue;
            if (group.Latest >= now.AddDays(-7) && total <= maxBytes) continue;
            foreach (var file in group.Files)
            {
                try
                {
                    var bytes = file.Length;
                    file.Delete();
                    total -= bytes; freed += bytes; deleted++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return (deleted, freed);
    }

    public static void TryClean()
    {
        try { Clean(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Diagnostic cleanup failed: {ex.Message}"); }
    }
}
