namespace MiliastraArchiveTool.Services;

public sealed class ArchiveValidationException : Exception
{
    public ArchiveValidationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
