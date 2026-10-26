using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Automation.Recognition;

namespace MiliastraArchiveTool.Automation.Workflows.Testing;

public sealed record ScriptedPageFrame(
    GamePage Page,
    IReadOnlyList<AutomationTarget> Targets,
    double Confidence = 0.9,
    string Source = "ScriptedPageRecognizer");

public sealed record Stage1GameUploadExecutorDryRunScenarioResult(
    string ScenarioName,
    bool ResultSuccess,
    string ResultMessage,
    IReadOnlyList<string> RecordedActions,
    IReadOnlyList<UploadTaskState> ProgressStates,
    bool Passed,
    IReadOnlyList<string> AssertionFailures);
