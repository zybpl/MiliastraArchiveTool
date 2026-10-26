namespace MiliastraArchiveTool.Automation.Recognition.Yolo.Evaluation;

public sealed record PageRecognitionBatchReport(
    string DatasetRoot,
    string OutputDirectory,
    DateTimeOffset CreatedAt,
    int MaxImagesPerCategory,
    int TotalImages,
    int TotalMatched,
    int TotalActionableMatched,
    int TotalExpectedUnknownMatched,
    int TotalActionableUnknown,
    int TotalActionableMismatched,
    int TotalUnexpectedRecognized,
    int TotalErrors,
    IReadOnlyList<PageRecognitionCategorySummary> Categories,
    IReadOnlyList<PageRecognitionBatchItem> Items,
    IReadOnlyList<string> SkippedCategories);

public sealed record PageRecognitionBatchItem(
    string Category,
    string ImagePath,
    GamePage? ExpectedPage,
    GamePage RecognizedPage,
    double Confidence,
    bool Matched,
    bool ExpectedUnknown,
    bool UnexpectedRecognized,
    int TargetCount,
    IReadOnlyList<PageRecognitionTopTarget> TopTargets,
    string? ErrorMessage);

public sealed record PageRecognitionCategorySummary(
    string Category,
    GamePage? ExpectedPage,
    bool ExpectedUnknown,
    int Total,
    int Matched,
    int Unknown,
    int Mismatched,
    int Errors,
    int UnexpectedRecognized,
    double Accuracy,
    IReadOnlyList<string> TopMismatches);

public sealed record PageRecognitionTopTarget(
    string Name,
    double Confidence,
    double CenterX,
    double CenterY,
    double Width,
    double Height);
