using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed record UploadRunArchive(
    LevelArchive SourceArchive,
    UploadServer Server,
    string OriginalArchivePath,
    string RunArchivePath,
    string RunArchiveFileName,
    string RunArchiveCode,
    string RunArchiveDisplayName,
    DateTimeOffset CreatedAt);
