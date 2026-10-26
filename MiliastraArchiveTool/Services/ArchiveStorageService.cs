using System.Text.Json;
using System.IO;
using MiliastraArchiveTool.Infrastructure;
using MiliastraArchiveTool.Models;

namespace MiliastraArchiveTool.Services;

public sealed class ArchiveStorageService
{
    private const int CurrentSchemaVersion = 1;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ArchiveStorageService(string? rootPath = null)
    {
        RootPath = rootPath ?? ApplicationPaths.RuntimeRoot;
        if (rootPath == null)
            MigrateLegacyRootIfNeeded(RootPath, ApplicationPaths.LegacyLocalAppDataRoot);

        ArchivesPath = Path.Combine(RootPath, "archives");
    }

    public string RootPath { get; }
    public string ArchivesPath { get; }

    public IReadOnlyList<LevelArchive> LoadAll()
    {
        Directory.CreateDirectory(ArchivesPath);
        var archives = new List<LevelArchive>();

        foreach (var metadataPath in Directory.EnumerateFiles(ArchivesPath, "metadata.json", SearchOption.AllDirectories))
        {
            try
            {
                var document = JsonSerializer.Deserialize<ArchiveDocument>(File.ReadAllText(metadataPath), _jsonOptions);
                if (document?.Archive == null)
                    continue;

                if (document.Archive.Id == Guid.Empty)
                    document.Archive.Id = Guid.NewGuid();

                document.Archive.SyncArchiveFileName();
                archives.Add(document.Archive);
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        return archives.OrderBy(item => item.Index).ThenBy(item => item.CreatedAt).ToList();
    }

    public IReadOnlyList<string> ValidateForSave(LevelArchive archive) =>
        ArchiveValidator.ValidateForSave(archive, path => ResolveManagedPath(archive, path));

    public IReadOnlyList<string> ValidateForUpload(LevelArchive archive) =>
        ArchiveValidator.ValidateForUpload(archive, path => ResolveManagedPath(archive, path));

    public void Save(LevelArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var errors = ValidateForSave(archive);
        if (errors.Count > 0)
            throw new ArchiveValidationException(errors);

        if (archive.Id == Guid.Empty)
            archive.Id = Guid.NewGuid();

        var archiveDirectory = GetArchiveDirectory(archive.Id);
        Directory.CreateDirectory(archiveDirectory);

        ImportArchiveFile(archive, archiveDirectory);
        ImportResources(archive, archiveDirectory);

        archive.UpdatedAt = DateTime.UtcNow;
        if (archive.CreatedAt == default)
            archive.CreatedAt = archive.UpdatedAt;

        var document = new ArchiveDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Archive = archive
        };

        var metadataPath = Path.Combine(archiveDirectory, "metadata.json");
        var temporaryPath = metadataPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, _jsonOptions));
        File.Move(temporaryPath, metadataPath, true);

        CleanupUnreferencedResources(archive, archiveDirectory);
    }

    public void Delete(LevelArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var archiveDirectory = GetArchiveDirectory(archive.Id);
        if (!Directory.Exists(archiveDirectory))
            return;

        var archivesRoot = Path.GetFullPath(ArchivesPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetRoot = Path.GetFullPath(archiveDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!targetRoot.StartsWith(archivesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to delete archive outside managed storage: {targetRoot}");

        ClearReadOnlyAttributes(archiveDirectory);
        Directory.Delete(archiveDirectory, true);
    }

    public string? ResolveResourcePath(LevelArchive archive, string path) => ResolveManagedPath(archive, path);

    public string? GetManagedArchivePath(LevelArchive archive) =>
        ResolveManagedPath(archive, archive.ArchiveRelativePath);

    private void ImportArchiveFile(LevelArchive archive, string archiveDirectory)
    {
        var oldManagedPath = ResolveManagedPath(archive, archive.ArchiveRelativePath);
        var sourcePath = !string.IsNullOrWhiteSpace(archive.PendingArchiveSourcePath)
            ? archive.PendingArchiveSourcePath
            : oldManagedPath;

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return;

        archive.SyncArchiveFileName();
        var relativePath = archive.ArchiveFileName;
        var destinationPath = Path.Combine(archiveDirectory, relativePath);
        CopyIfDifferent(sourcePath, destinationPath);

        archive.ArchiveRelativePath = relativePath;
        archive.PendingArchiveSourcePath = string.Empty;
    }

    private void ImportResources(LevelArchive archive, string archiveDirectory)
    {
        archive.LevelCoverPath = ImportOptionalResource(archive.LevelCoverPath, archiveDirectory, "images", "cover");
        archive.MainImagePath = ImportOptionalResource(archive.MainImagePath, archiveDirectory, "images", "main");
        archive.AchievementIconPath = ImportOptionalResource(archive.AchievementIconPath, archiveDirectory, "images", "icon-achievement");
        archive.VideoPath = ImportOptionalResource(archive.VideoPath, archiveDirectory, "video", "showcase");

        var importedExtraImages = new List<string>();
        for (var index = 0; index < archive.ExtraImagePaths.Count; index++)
            importedExtraImages.Add(ImportOptionalResource(archive.ExtraImagePaths[index], archiveDirectory, Path.Combine("images", "extra"), $"extra-{index + 1:D2}"));

        archive.ExtraImagePaths.Clear();
        foreach (var path in importedExtraImages.Where(path => !string.IsNullOrWhiteSpace(path)))
            archive.ExtraImagePaths.Add(path);
    }

    private static string ImportOptionalResource(string sourceOrRelativePath, string archiveDirectory, string relativeDirectory, string baseName)
    {
        if (string.IsNullOrWhiteSpace(sourceOrRelativePath) || !Path.IsPathRooted(sourceOrRelativePath))
            return sourceOrRelativePath;

        var extension = Path.GetExtension(sourceOrRelativePath).ToLowerInvariant();
        var relativePath = Path.Combine(relativeDirectory, baseName + extension);
        CopyIfDifferent(sourceOrRelativePath, Path.Combine(archiveDirectory, relativePath));
        return relativePath;
    }

    private string? ResolveManagedPath(LevelArchive archive, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;
        if (Path.IsPathRooted(relativePath))
            return relativePath;
        return Path.Combine(GetArchiveDirectory(archive.Id), relativePath);
    }

    private static void CopyIfDifferent(string sourcePath, string destinationPath)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var destinationFullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);
        File.Copy(sourceFullPath, destinationFullPath, true);
    }

    private static void CleanupUnreferencedResources(LevelArchive archive, string archiveDirectory)
    {
        var referencedPaths = new[]
            {
                archive.LevelCoverPath,
                archive.MainImagePath,
                archive.AchievementIconPath,
                archive.LuxuryGiftIconPath,
                archive.ColorfulGiftIconPath,
                archive.QuestGiftIconPath,
                archive.VideoPath
            }
            .Concat(archive.ExtraImagePaths)
            .Where(path => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path))
            .Select(path => Path.GetFullPath(Path.Combine(archiveDirectory, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relativeDirectory in new[] { "images", "video" })
        {
            var directory = Path.Combine(archiveDirectory, relativeDirectory);
            if (!Directory.Exists(directory))
                continue;

            foreach (var filePath in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!referencedPaths.Contains(Path.GetFullPath(filePath)))
                    File.Delete(filePath);
            }
        }
    }

    private static void ClearReadOnlyAttributes(string directory)
    {
        foreach (var filePath in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(filePath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(directoryPath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(directoryPath, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private string GetArchiveDirectory(Guid archiveId)
    {
        if (archiveId == Guid.Empty)
            throw new InvalidOperationException("存档缺少有效 ID。");

        return Path.Combine(ArchivesPath, archiveId.ToString("N"));
    }

    private static void MigrateLegacyRootIfNeeded(string newRootPath, string legacyRootPath)
    {
        try
        {
            var newFullPath = Path.GetFullPath(newRootPath);
            var legacyFullPath = Path.GetFullPath(legacyRootPath);
            if (string.Equals(newFullPath, legacyFullPath, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(legacyFullPath))
            {
                return;
            }

            Directory.CreateDirectory(newFullPath);
            CopyDirectoryIfMissing(legacyFullPath, newFullPath);
            TryDeleteDirectoryIfEmptyOrMigrated(legacyFullPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void CopyDirectoryIfMissing(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativeDirectory = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativeDirectory));
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativeFile = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destinationFile = Path.Combine(destinationDirectory, relativeFile);
            if (File.Exists(destinationFile))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, false);
        }
    }

    private static void TryDeleteDirectoryIfEmptyOrMigrated(string legacyRootPath)
    {
        try
        {
            Directory.Delete(legacyRootPath, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class ArchiveDocument
    {
        public int SchemaVersion { get; set; }
        public LevelArchive? Archive { get; set; }
    }
}
