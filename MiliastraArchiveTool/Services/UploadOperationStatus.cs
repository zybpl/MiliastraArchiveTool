namespace MiliastraArchiveTool.Services;

public sealed record UploadOperationStatus(UploadServer Server, string? ArchiveName, string Operation);
