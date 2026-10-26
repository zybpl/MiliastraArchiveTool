using System.IO;
using System.Security.Cryptography;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed class UploadRunArchiveService : IUploadRunArchiveService
{
    private const int MaxDisplayNameLength = 96;
    private const int MaxFileNameWithoutExtensionLength = 120;
    private const int MaximumCreateAttempts = 20;
    // Avoid OCR-confusing characters in game cards: 0/O/Q, 1/I/L, J/brackets, and S/5.
    private const string RandomCodeCharacters = "ABCDEFGHKMNPRTUVWXYZ2346789";
    private readonly ArchiveStorageService _storage;

    public UploadRunArchiveService(ArchiveStorageService storage)
    {
        _storage = storage;
    }

    public async Task<UploadRunArchive> CreateAsync(
        LevelArchive archive,
        UploadServer server,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        cancellationToken.ThrowIfCancellationRequested();

        var originalArchivePath = _storage.GetManagedArchivePath(archive);
        if (string.IsNullOrWhiteSpace(originalArchivePath) || !File.Exists(originalArchivePath))
            throw new FileNotFoundException(
                $"Managed source .gil archive file was not found. LevelName='{archive.LevelName}', Server={server}, OriginalArchivePath='{originalArchivePath ?? "<null>"}'.",
                originalArchivePath);

        var createdAt = DateTimeOffset.Now;
        var serverName = server.ToString();
        var displayBase = SanitizeDisplayName(archive.LevelName);
        Exception? lastException = null;
        string? lastRunArchiveDisplayName = null;
        string? lastRunArchivePath = null;

        for (var attempt = 1; attempt <= MaximumCreateAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var code = CreateRandomCode();
            var runArchiveDisplayName = CreateRunArchiveDisplayName(displayBase, code);
            var directoryName = SanitizeFileName($"{createdAt:yyyyMMdd-HHmmss}-{serverName}-{code}", maxLength: 96);
            var runDirectory = Path.Combine(_storage.RootPath, "upload-runs", directoryName);
            var runArchiveFileName = SanitizeFileName(runArchiveDisplayName, MaxFileNameWithoutExtensionLength) + ".gil";
            var runArchivePath = Path.Combine(runDirectory, runArchiveFileName);
            lastRunArchiveDisplayName = runArchiveDisplayName;
            lastRunArchivePath = runArchivePath;

            if (Directory.Exists(runDirectory) || File.Exists(runArchivePath))
                continue;

            try
            {
                Directory.CreateDirectory(runDirectory);

                await using (var source = File.Open(originalArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var destination = new FileStream(
                                 runArchivePath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }

                return new UploadRunArchive(
                    archive,
                    server,
                    originalArchivePath,
                    runArchivePath,
                    runArchiveFileName,
                    code,
                    runArchiveDisplayName,
                    createdAt);
            }
            catch (IOException ex)
            {
                lastException = ex;
            }
        }

        throw new IOException(
            $"Failed to create upload run archive after {MaximumCreateAttempts} attempts. " +
            $"LevelName='{archive.LevelName}', RunArchiveDisplayName='{lastRunArchiveDisplayName ?? "<none>"}', " +
            $"Server={server}, OriginalArchivePath='{originalArchivePath}', RunArchivePath='{lastRunArchivePath ?? "<none>"}', CreatedAt={createdAt:O}.",
            lastException);
    }

    private static string SanitizeDisplayName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string((value ?? string.Empty)
            .Trim()
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .TrimEnd('.', ' ');

        return TrimToLength(sanitized, MaxDisplayNameLength);
    }

    private static string CreateRunArchiveDisplayName(string sanitizedLevelName, string code)
    {
        var suffix = "_" + code;
        var baseMaxLength = Math.Max(1, MaxDisplayNameLength - suffix.Length);
        var baseName = string.IsNullOrWhiteSpace(sanitizedLevelName)
            ? "archive"
            : sanitizedLevelName;
        baseName = TrimToLength(baseName, baseMaxLength).TrimEnd('.', ' ', '_');
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "archive";

        return baseName + suffix;
    }

    private static string CreateRandomCode()
    {
        Span<char> code = stackalloc char[4];
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);

        for (var index = 0; index < code.Length; index++)
            code[index] = RandomCodeCharacters[bytes[index] % RandomCodeCharacters.Length];

        return new string(code);
    }

    private static string SanitizeFileName(string value, int maxLength)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string((value ?? string.Empty)
            .Trim()
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .TrimEnd('.', ' ');

        sanitized = TrimToLength(sanitized, maxLength).TrimEnd('.', ' ');
        if (IsReservedDeviceName(sanitized))
            sanitized = "_" + sanitized;
        return sanitized;
    }

    private static string TrimToLength(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength].TrimEnd('.', ' ', '_');
    }

    private static bool IsReservedDeviceName(string value)
    {
        var name = Path.GetFileNameWithoutExtension(value);
        var reservedNames = new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        return reservedNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
