using MiliastraArchiveTool.Infrastructure;
using System.IO;
using System.Text;
using MiliastraArchiveTool.Models;

namespace MiliastraArchiveTool.Services;

public static class ArchiveValidator
{
    private static readonly HashSet<string> PreviewImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };
    private static readonly HashSet<string> JpgOnlyExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg" };
    private static readonly HashSet<string> PngOnlyExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png" };
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".3gp",
            ".avi",
            ".flv",
            ".mkv",
            ".mov",
            ".mp4",
            ".mpeg",
            ".mpg",
            ".webm",
            ".wmv"
        };

    private const long ThreeMb = 3L * 1024 * 1024;
    private const long FiveMb = 5L * 1024 * 1024;
    private const long MaximumVideoBytes = 300L * 1024 * 1024;
    private const int MaximumExtraImages = 4;
    private const int GameplayDetailsBytes = 400;
    private const int EarlyAccessDescriptionBytes = 400;
    private const int StageDescriptionBytes = 10000;

    public static IReadOnlyList<string> ValidateForSave(LevelArchive archive, Func<string, string?> resolvePath)
    {
        var errors = new List<string>();
        if (archive.DefaultLanguage is not "en" and not "zh-CN")
            errors.Add("存档默认语言必须为 English 或简体中文。");

        if (string.IsNullOrWhiteSpace(archive.LevelName))
            errors.Add("Stage Name cannot be empty.");
        else if (archive.LevelName.Trim().Length > 100)
            errors.Add("Stage Name cannot exceed 100 characters.");

        ValidatePreviewTextLengths(
            archive.GameDescription,
            archive.LevelDescription,
            archive.EarlyAccessDescription,
            "Default language",
            errors);
        ValidateLength(archive.VersionLog, 1000, "Update Log", errors);

        foreach (var localizedInfo in archive.LocalizedInfos)
        {
            var labelPrefix = string.IsNullOrWhiteSpace(localizedInfo.LanguageName)
                ? localizedInfo.LanguageCode
                : localizedInfo.LanguageName;
            ValidateLength(localizedInfo.LevelName, 100, $"{labelPrefix} Stage Name", errors);
            ValidatePreviewTextLengths(
                localizedInfo.GameDescription,
                localizedInfo.LevelDescription,
                localizedInfo.EarlyAccessDescription,
                labelPrefix,
                errors);
            ValidateLength(localizedInfo.VersionLog, 1000, $"{labelPrefix} Update Log", errors);
        }

        var archivePath = !string.IsNullOrWhiteSpace(archive.PendingArchiveSourcePath)
            ? archive.PendingArchiveSourcePath
            : resolvePath(archive.ArchiveRelativePath);
        ValidateFile(archivePath, [".gil"], "GIL archive", true, errors);

        ValidateResource(archive.LevelCoverPath, PreviewImageExtensions, "Stage Cover Image", FiveMb, resolvePath, errors);
        ValidateResource(archive.MainImagePath, JpgOnlyExtensions, "Loading Screen Image", ThreeMb, resolvePath, errors);
        ValidateResource(archive.AchievementIconPath, JpgOnlyExtensions, "Ultimate Achievement Icon", FiveMb, resolvePath, errors);
        ValidateResource(archive.VideoPath, VideoExtensions, "Showcase Video", MaximumVideoBytes, resolvePath, errors);

        if (archive.ExtraImagePaths.Count > MaximumExtraImages)
            errors.Add($"More Showcase Images cannot exceed {MaximumExtraImages} files.");

        for (var index = 0; index < archive.ExtraImagePaths.Count; index++)
            ValidateResource(
                archive.ExtraImagePaths[index],
                PreviewImageExtensions,
                $"More Showcase Images #{index + 1}",
                FiveMb,
                resolvePath,
                errors);

        return errors;
    }

    public static IReadOnlyList<string> ValidateForUpload(LevelArchive archive, Func<string, string?> resolvePath)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("ArchiveValidation", new { archive.LevelName });
        var errors = ValidateForSave(archive, resolvePath).ToList();
        if (string.IsNullOrWhiteSpace(archive.GameDescription))
            errors.Add("Gameplay Details must be filled before upload.");
        if (string.IsNullOrWhiteSpace(archive.LevelDescription))
            errors.Add("Stage Description must be filled before upload.");
        if (archive.IsEarlyAccess && string.IsNullOrWhiteSpace(archive.EarlyAccessDescription))
            errors.Add("Early Access Version Description must be filled before upload.");
        if (string.IsNullOrWhiteSpace(archive.Tags))
            errors.Add("Stage Tags must be selected before upload.");
        if (string.IsNullOrWhiteSpace(archive.LevelCoverPath))
            errors.Add("Stage Cover Image must be selected before upload.");
        return errors;
    }

    private static void ValidatePreviewTextLengths(
        string gameDescription,
        string levelDescription,
        string earlyAccessDescription,
        string labelPrefix,
        ICollection<string> errors)
    {
        ValidateUtf8ByteLength(gameDescription, GameplayDetailsBytes, $"{labelPrefix} Gameplay Details", errors);
        ValidateUtf8ByteLength(levelDescription, StageDescriptionBytes, $"{labelPrefix} Stage Description", errors);
        ValidateUtf8ByteLength(earlyAccessDescription, EarlyAccessDescriptionBytes, $"{labelPrefix} Early Access Version Description", errors);
    }

    private static void ValidateResource(
        string path,
        IReadOnlySet<string> extensions,
        string label,
        long maximumBytes,
        Func<string, string?> resolvePath,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var resolvedPath = Path.IsPathRooted(path) ? path : resolvePath(path);
        ValidateFile(resolvedPath, extensions, label, false, errors);
        if (!string.IsNullOrWhiteSpace(resolvedPath) &&
            File.Exists(resolvedPath) &&
            new FileInfo(resolvedPath).Length > maximumBytes)
        {
            errors.Add($"{label} cannot exceed {FormatMegabytes(maximumBytes)} MB.");
        }
    }

    private static void ValidateFile(
        string? path,
        IEnumerable<string> extensions,
        string label,
        bool required,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (required)
                errors.Add($"Please select {label}.");
            return;
        }

        if (!File.Exists(path))
        {
            errors.Add($"{label} does not exist: {path}");
            return;
        }

        var allowedExtensions = extensions.ToList();
        var actualExtension = Path.GetExtension(path);
        if (!allowedExtensions.Contains(actualExtension, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(
                $"{label} must use {FormatExtensions(allowedExtensions)} format. Current extension: {actualExtension}");
        }
    }

    private static void ValidateLength(string value, int maximum, string label, ICollection<string> errors)
    {
        if ((value?.Length ?? 0) > maximum)
            errors.Add($"{label} cannot exceed {maximum} characters.");
    }

    private static void ValidateUtf8ByteLength(string value, int maximumBytes, string label, ICollection<string> errors)
    {
        if (Encoding.UTF8.GetByteCount(value ?? string.Empty) > maximumBytes)
            errors.Add($"{label} cannot exceed {maximumBytes} UTF-8 bytes.");
    }

    private static string FormatExtensions(IEnumerable<string> extensions) =>
        string.Join("/", extensions.Select(extension => extension.TrimStart('.').ToUpperInvariant()));

    private static long FormatMegabytes(long bytes) => bytes / 1024 / 1024;
}
