namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public sealed record Stage1ContextPageDecision(
    bool IsAccepted,
    bool IsExpected,
    bool IsUnexpectedSafePage,
    bool IsUnknown,
    bool IsBlocking,
    GamePage RecognizedPage,
    string Reason,
    PageRecognitionResult Recognition);
