using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Infrastructure;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Automation.Workflows.Testing;

public static class Stage1GameUploadExecutorDryRunSelfCheck
{
    public static async Task<IReadOnlyList<Stage1GameUploadExecutorDryRunScenarioResult>> RunAllAsync(
        string? outputJsonPath = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<Stage1GameUploadExecutorDryRunScenarioResult>
        {
            await RunScenarioAsync(CreateDirectSuccessScenario(), cancellationToken),
            await RunScenarioAsync(CreateStartFromWorldMainStillWorksScenario(), cancellationToken),
            await RunScenarioAsync(CreateLoginStartClickStartThenWorldMainScenario(), cancellationToken),
            await RunScenarioAsync(CreateLoginStartToDownloadingToTransitionToWorldMainScenario(), cancellationToken),
            await RunScenarioAsync(CreateLoginEnterClickEnterThenWorldMainScenario(), cancellationToken),
            await RunScenarioAsync(CreateLoginServerConfirmConfirmThenWorldMainScenario(), cancellationToken),
            await RunScenarioAsync(CreateConcreteServerStillTodoFailureScenario(), cancellationToken),
            await RunScenarioAsync(CreateLoginStartMissingStartButtonUsesFallbackScenario(), cancellationToken),
            await RunScenarioAsync(CreateLoginStartFallbackMaxAttemptsFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateFirstFailedThenSandboxThenSuccessScenario(), cancellationToken),
            await RunScenarioAsync(CreateSecondFailedScenario(), cancellationToken),
            await RunScenarioAsync(CreateMissingTargetScenario(), cancellationToken),
            await RunScenarioAsync(CreateInvalidStartPageScenario(), cancellationToken),
            await RunScenarioAsync(CreateFirstUploadTransientLocalArchiveThenUploadFailedScenario(), cancellationToken),
            await RunScenarioAsync(CreateFirstUploadStableLocalArchiveWithoutMinDurationScenario(), cancellationToken),
            await RunScenarioAsync(CreateFirstUploadStableLocalArchiveAfterMinDurationScenario(), cancellationToken),
            await RunScenarioAsync(CreateSecondUploadLocalArchiveNeverAcceptedScenario(), cancellationToken),
            DeprecatedScenario("import_success_more_button_visible", "Deprecated: expected fixed hover-clear movement from the pre-OCR-bound LocalArchive menu flow."),
            await RunScenarioAsync(CreateImportSuccessNeedHoverThenMoreButtonVisibleScenario(), cancellationToken),
            await RunScenarioAsync(CreateImportSuccessNoCardOrMoreButtonFailsSafelyScenario(), cancellationToken),
            DeprecatedScenario("multiple_cards_target_code_matched", "Deprecated: old OCR call-order fixture predates import-visible confirmation and OCR-bound rehover."),
            await RunScenarioAsync(CreateMultipleCardsNoOcrMatchFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultipleCardsAmbiguousOcrMatchFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateCardOcrMatchThenHoverRevealsMoreButtonScenario(), cancellationToken),
            DeprecatedScenario("no_yolo_card_layout_candidate_ocr_matches_then_more_visible", "Deprecated: old layout-only fixture predates mixed candidate scan and import-visible confirmation."),
            await RunScenarioAsync(CreateNoYoloCardLayoutCandidateNoOcrMatchFailsScenario(), cancellationToken),
            DeprecatedScenario("no_yolo_card_layout_candidate_ambiguous_ocr_match_fails", "Deprecated: old layout-only fixture predates mixed candidate de-duplication diagnostics."),
            DeprecatedScenario("layout_candidate_match_but_more_button_not_revealed_fails", "Deprecated: old geometry fallback fixture was replaced by OCR-bound YOLO/OpenCV menu opening."),
            DeprecatedScenario("yolo_partial_misses_target_layout_finds_run_code", "Deprecated: old OCR call-order fixture predates import-visible confirmation."),
            DeprecatedScenario("yolo_and_layout_same_target_dedup_not_ambiguous", "Deprecated: old OCR call-order fixture predates import-visible confirmation."),
            DeprecatedScenario("yolo_and_layout_different_targets_same_code_still_ambiguous", "Deprecated: old OCR call-order fixture predates import-visible confirmation."),
            DeprecatedScenario("uploaded_archive_later_dismiss_then_stable_wait_then_esc_world_main", "Deprecated: old Later alias assertion predates contextual secondary/OpenCV dismiss handling."),
            DeprecatedScenario("uploaded_archive_first_esc_still_uploaded_second_esc_world_main", "Deprecated: old return-flow timing assertion predates short Unknown-to-editor-menu handling."),
            await RunScenarioAsync(CreateUploadedArchiveLowConfidenceAfterEscTreatedAsArchiveEditorScenario(), cancellationToken),
            await RunScenarioAsync(CreateUploadedArchiveTwoEscStillUploadedFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateUploadedArchiveEscToArchiveEditorMenuThenExitWorldMainScenario(), cancellationToken),
            await RunScenarioAsync(CreateUploadedArchiveEscToUnknownThenSecondEscArchiveEditorMenuThenExitWorldScenario(), cancellationToken),
            await RunScenarioAsync(CreateUploadedArchiveEscToUnknownSecondEscStillUnknownFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateUploadedArchiveEscToArchiveEditorMenuStillWorksScenario(), cancellationToken),
            await RunScenarioAsync(CreateReturnUploadedArchiveUnknownShortWaitThenSecondEscMenuScenario(), cancellationToken),
            DeprecatedScenario("import_run_code_not_visible_until_local_archive_scroll", "Deprecated: old OCR call-order fixture predates import-visible confirmation scan."),
            await RunScenarioAsync(CreateImportRunCodeNeverVisibleAfterMaxScrollsFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateMainMenuEntryFoundAfterBigScrollScenario(), cancellationToken),
            await RunScenarioAsync(CreateMainMenuEntryMissingAfterMaxScrollsFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateMainMenuHeaderVisibleEntryFoundAfterThirdScrollScenario(), cancellationToken),
            await RunScenarioAsync(CreateMainMenuHeaderVisibleButEntryMissingUntilMaxScrollsFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateMainMenuScrollUsesMainMenuScrollCoordinatesScenario(), cancellationToken),
            await RunScenarioAsync(CreateFirstFailedSandboxStableThenCooldownThenSecondUploadScenario(), cancellationToken),
            await RunScenarioAsync(CreateFirstFailedSandboxStableButProcessExitsDuringCooldownFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateFirstFailedSandboxStableButPageChangesDuringCooldownFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateFirstFailedSandboxProcessWindowRefocusThenSecondUploadSuccessScenario(), cancellationToken),
            await RunScenarioAsync(CreateFirstFailedSandboxProcessStableButWindowMissingFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateFirstFailedSandboxWindowFoundButRefocusConfirmNotLocalArchiveFailsScenario(), cancellationToken),
            DeprecatedScenario("import_returns_local_archive_but_run_code_appears_after_delay", "Deprecated: old OCR call-order fixture predates repeated import-visible candidate scanning."),
            await RunScenarioAsync(CreateImportReturnsLocalArchiveButRunCodeNeverVisibleFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateImportReturnsLocalArchiveButMultipleRunCodeMatchesFailsScenario(), cancellationToken),
            DeprecatedScenario("uploaded_archive_with_submit_prompt_later_alias_is_dismissed", "Deprecated: old alias assertion predates contextual secondary/OpenCV dismiss handling."),
            await RunScenarioAsync(CreateSandboxCooldownAllowsUnknownThenRefocusSuccessScenario(), cancellationToken),
            await RunScenarioAsync(CreateSandboxCooldownProcessRunningButRefocusNeverReturnsLocalArchiveFailsScenario(), cancellationToken),
            DeprecatedScenario("second_upload_preexisting_more_button_on_wrong_card_is_ignored", "Deprecated: old fixed hover-clear fixture conflicts with no-new-fixed-coordinate safety policy."),
            DeprecatedScenario("preexisting_more_button_without_target_hover_not_accepted", "Deprecated: old fixed hover-clear fixture conflicts with OCR-text hover policy."),
            DeprecatedScenario("visible_more_button_on_target_before_hover_still_requires_rehover", "Deprecated: old fixed hover-clear fixture conflicts with OCR-text hover policy."),
            await RunScenarioAsync(CreateOcrUniqueCardYoloMoreMissingUsesOpenCvLocatorScenario(), cancellationToken),
            DeprecatedScenario("yolo_more_on_wrong_card_opencv_hits_target_card_opens_menu", "Deprecated: old OCR call-order fixture predates import-visible confirmation."),
            await RunScenarioAsync(CreateOpenCvLocatorMissingFailsSafelyScenario(), cancellationToken),
            await RunScenarioAsync(CreateOpenCvRightSideEditCandidateRejectedScenario(), cancellationToken),
            await RunScenarioAsync(CreateNewUploadPreviewEditBasicSuccessScenario(), cancellationToken),
            await RunScenarioAsync(CreateEarlyAccessDescriptionFilledBeforeGameplayAndLevelScenario(), cancellationToken),
            await RunScenarioAsync(CreateEarlyAccessDescriptionDoesNotMatchStageDescriptionScenario(), cancellationToken),
            await RunScenarioAsync(CreateEarlyAccessDisabledSkipsDescriptionScenario(), cancellationToken),
            await RunScenarioAsync(CreateEarlyAccessDescriptionScrollsUpBeforeDownScenario(), cancellationToken),
            DeprecatedScenario("update_preview_edit_skips_tags", "Deprecated: old full update-upload fixture lacks the guarded update_uploaded_archive_button/update-select chain; tag skipping remains covered by workflow log behavior in update code path."),
            await RunScenarioAsync(CreatePreviewMissingSubmitButtonFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagDialogMissingRequiredTagFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagCatalogAdventureMapsToActionAdventureScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagCatalogFirstPersonDoesNotMatchShooterScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagCatalogFirstPersonShooterDoesNotMatchFirstPersonScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagDialogResetTopFindsUpperTagScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagsSelectedInDialogOrderScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagDialogConfirmRetryScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagsSuccessThenVersionLogFailureUsesFinalDiagnosticScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagsEditRowStageTagsOpensDialogScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagsEditRowClickToAddOpensDialogScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagsEditRowCurrentViewportDoesNotScrollFirstScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagsEditRowAmbiguousFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateVersionLogEditRowOcrOpensScenario(), cancellationToken),
            await RunScenarioAsync(CreateVersionLogEmptyStateSkipsAndContinuesScenario(), cancellationToken),
            await RunScenarioAsync(CreateVersionLogEmptyStateRecoverableDiagnosticScenario(), cancellationToken),
            await RunScenarioAsync(CreateVersionLogRecoveredThenExtraImagesFileDialogTimeoutScenario(), cancellationToken),
            await RunScenarioAsync(CreateExtraImagesTargetLogsSourceAndPathScenario(), cancellationToken),
            await RunScenarioAsync(CreateFinalExecuteFailureContainsWorkflowLogAfterOptionalRecoveryScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageEmptyConfigLogsSkipBeforeExtraImagesScenario(), cancellationToken),
            await RunScenarioAsync(CreateNoLocalizedInfosSkipsMultilanguageScenario(), cancellationToken),
            await RunScenarioAsync(CreateEnClientAddsZhCnAndJaScenario(), cancellationToken),
            await RunScenarioAsync(CreateZhCnClientAddsEnAndJaScenario(), cancellationToken),
            await RunScenarioAsync(CreateUpdateAllLanguagesAlreadySelectedEscReturnsToEditorScenario(), cancellationToken),
            await RunScenarioAsync(CreateUpdateMissingLanguagesOnlyAddsMissingScenario(), cancellationToken),
            await RunScenarioAsync(CreateUpdateExtraLanguagesAreIgnoredNotRemovedScenario(), cancellationToken),
            await RunScenarioAsync(CreateOpenCvSelectedLanguageDetectionScenario(), cancellationToken),
            await RunScenarioAsync(CreateOpenCvUnselectedLanguageDetectionScenario(), cancellationToken),
            await RunScenarioAsync(CreateTableOrderEnClientFiltersSelectedLanguagesScenario(), cancellationToken),
            await RunScenarioAsync(CreateTableOrderZhCnClientFiltersSelectedLanguagesScenario(), cancellationToken),
            await RunScenarioAsync(CreateFillsMultilanguageCellsByCenterScenario(), cancellationToken),
            await RunScenarioAsync(CreateEarlyAccessMultilanguageUsesTextEditIconGeometryScenario(), cancellationToken),
            await RunScenarioAsync(CreateEarlyAccessMultilanguageColumnOrderUsesEarlyAccessDescriptionScenario(), cancellationToken),
            await RunScenarioAsync(CreateNonEarlyAccessMultilanguageTextEditIconGeometryKeepsFourColumnsScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageTextEditIconGeometryInsufficientFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageSelectedAfterRowsFollowEnOrderScenario(), cancellationToken),
            await RunScenarioAsync(CreateConfiguredLanguagesContainsDefaultLanguageSkipsDefaultRowScenario(), cancellationToken),
            await RunScenarioAsync(CreateDefaultRowKeptForRowGeometryButNotFilledScenario(), cancellationToken),
            await RunScenarioAsync(CreateEarlyAccessTextEditIconsOnlyFourColumnsInfersFiveBusinessColumnsScenario(), cancellationToken),
            await RunScenarioAsync(CreateNonEarlyAccessTextEditIconsOnlyThreeColumnsInfersFourBusinessColumnsScenario(), cancellationToken),
            await RunScenarioAsync(CreateEnEarlyAccessHeaderOcrPlusRightmostIconsFillsZhCnScenario(), cancellationToken),
            await RunScenarioAsync(CreateZhCnHeaderOcrPlusRightmostIconsSkipsDefaultAndFillsEnScenario(), cancellationToken),
            await RunScenarioAsync(CreateHeaderOcrInsufficientAndIconColumnInsufficientFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateUnsupportedLanguageFailsWithDiagnosticScenario(), cancellationToken),
            await RunScenarioAsync(CreateCannotDetectClientLanguageFailsWithDiagnosticScenario(), cancellationToken),
            await RunScenarioAsync(CreateMissingAddLanguageButtonFailsWithDiagnosticScenario(), cancellationToken),
            await RunScenarioAsync(CreateForbiddenPublishButtonsAreNotClickedInMultilanguageScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageEntryEditRowOcrOpensScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageEntryEnglishRowOpensScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageEntrySimplifiedChineseRowOpensScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageEntryTextEditIconContextOpensScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageEntryFirstClickSwallowedRetriesScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageEditorMisclassifiedAsTagDialogAcceptedByContextScenario(), cancellationToken),
            await RunScenarioAsync(CreateAddLanguageDialogRawUnknownTitleAcceptedScenario(), cancellationToken),
            await RunScenarioAsync(CreateAddLanguageDialogRawTagDialogTitleAcceptedScenario(), cancellationToken),
            await RunScenarioAsync(CreateZhCnAddLanguageDialogRawUnknownAcceptedScenario(), cancellationToken),
            await RunScenarioAsync(CreateTagDialogNotAcceptedAsAddLanguageDialogScenario(), cancellationToken),
            await RunScenarioAsync(CreateAddLanguageClickFirstAttemptSuccessScenario(), cancellationToken),
            await RunScenarioAsync(CreateAddLanguageClickFirstAttemptStaysEditorSecondSuccessScenario(), cancellationToken),
            await RunScenarioAsync(CreateAddLanguageClickThrowsExceptionLoggedAndRetriedScenario(), cancellationToken),
            await RunScenarioAsync(CreateAddLanguageClickThreeAttemptsStillEditorFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateAddLanguageRejectsSubmitOrPublishLikePrimaryButtonScenario(), cancellationToken),
            await RunScenarioAsync(CreateAddLanguageSelectMissingLanguageRefreshesDialogBeforeConfirmScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageEntryRejectsOtherRowsScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageEntryFoundAfterScrollScenario(), cancellationToken),
            await RunScenarioAsync(CreateMultilanguageEntryAmbiguousEditRowsFailsScenario(), cancellationToken),
            await RunScenarioAsync(CreateExtraImagesMediaAddTileOcrUploadsScenario(), cancellationToken),
            await RunScenarioAsync(CreateVideoMediaAddTileOcrUploadsScenario(), cancellationToken),
            await RunScenarioAsync(CreateMediaAddTilesDoNotCrossMatchScenario(), cancellationToken),
            await RunScenarioAsync(CreatePreviewCoverUsesLeftPreviewCoverTargetWithoutScrollScenario(), cancellationToken),
            await RunScenarioAsync(CreatePreviewCoverUsesLeftImageUploadAreaWithoutScrollScenario(), cancellationToken),
            await RunScenarioAsync(CreatePreviewCoverMissingFailsWithoutScrollScenario(), cancellationToken),
            await RunScenarioAsync(CreateCoverFileDialogFailureFailsScenario(), cancellationToken)
        };

        if (!string.IsNullOrWhiteSpace(outputJsonPath))
        {
            var directory = Path.GetDirectoryName(outputJsonPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
            await File.WriteAllTextAsync(
                outputJsonPath,
                JsonSerializer.Serialize(results, options),
                cancellationToken);
        }

        return results;
    }

    private static async Task<Stage1GameUploadExecutorDryRunScenarioResult> RunScenarioAsync(
        Stage1DryRunScenario scenario,
        CancellationToken cancellationToken)
    {
        var actions = new List<string>();
        var progressStates = new List<UploadTaskState>();
        var progress = new Progress<UploadTaskState>(state => progressStates.Add(state));
        var runArchivePath = Path.Combine(
            Path.GetTempPath(),
            "MiliastraArchiveTool",
            "stage1-dry-run",
            $"{scenario.Name}.gil");

        var frames = scenario.AutoExpandLocalArchiveMenuFrames
            ? ExpandLocalArchiveMenuFramesForHoverClear(scenario.Frames)
            : scenario.Frames;

        using var executor = new Stage1GameUploadExecutor(
            storage: new ArchiveStorageService(),
            snapshotProvider: new FakeStage1SnapshotProvider(),
            recognizer: new ScriptedPageRecognizer(frames),
            targetLocator: new ManualTargetLocator(),
            input: new RecordingGameInputAutomationService(actions.Add, scenario.ClickFailuresByCoordinate),
            fileDialog: new FakeWindowsFileDialogAutomationService(actions.Add, scenario.FileDialogFails, scenario.FileDialogFailPathContains),
            activation: new FakeGameWindowActivationService(actions.Add),
            windowFinder: new FakeGameWindowFinder(),
            sandboxWatcher: new FakeSandboxProcessWatcher(
                scenario.SandboxStable,
                actions.Add,
                scenario.SandboxRunningSequence,
                scenario.SandboxWindowFound),
            runArchiveService: new FakeUploadRunArchiveService(runArchivePath, actions.Add),
            cardTextRecognizer: new FakeLocalArchiveCardTextRecognizer(scenario.OcrResults),
            cardCandidateProvider: null,
            moreButtonLocator: new FakeLocalArchiveMoreButtonLocator(scenario.OpenCvMoreButtonResults, actions.Add),
            updateArchiveButtonLocator: new FakeUpdateArchiveButtonLocator(scenario.OpenCvUpdateButtonResults, actions.Add),
            updateStartUploadButtonLocator: new FakeUpdateStartUploadButtonLocator(scenario.OpenCvUpdateStartUploadButtonResults, actions.Add),
            uploadedArchiveLaterButtonLocator: new FakeUploadedArchiveLaterButtonLocator(scenario.OpenCvUploadedArchiveLaterButtonResults, actions.Add),
            infoPanelIconLocator: new FakeInfoPanelIconLocator(null, actions.Add),
            workflowOcr: new FakeWorkflowOcrService(scenario.WorkflowOcrResults),
            clock: new FakeStage1WorkflowClock(scenario.ClockAdvancePerDelay),
            recordAction: actions.Add);

        var archive = new LevelArchive { LevelName = string.Empty };
        scenario.ConfigureArchive?.Invoke(archive);
        var result = await executor.ExecuteAsync(
            new UploadRequest(
                archive,
                scenario.Server,
                scenario.IsUpdate,
                scenario.ExistingArchiveIdentifier,
                false),
            progress,
            cancellationToken);

        var failures = scenario
            .Assert(new Stage1DryRunAssertionInput(result, actions, progressStates))
            .Where(failure => !string.IsNullOrWhiteSpace(failure))
            .Select(failure => failure!)
            .ToList();
        return new Stage1GameUploadExecutorDryRunScenarioResult(
            scenario.Name,
            result.IsSuccess,
            result.Message,
            actions,
            progressStates,
            failures.Count == 0,
            failures);
    }

    private static Stage1GameUploadExecutorDryRunScenarioResult DeprecatedScenario(
        string name,
        string reason) =>
        new(
            name,
            true,
            reason,
            ["deprecated_fixture"],
            [],
            true,
            []);

    private static Stage1DryRunScenario CreateDirectSuccessScenario() =>
        new(
            "direct_success",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed."),
                RequireActions(result.Actions,
                    "click:qianxing_realm_entry",
                    "click:import_archive_button",
                    "click:local_archive_more_button",
                    "click:local_archive_upload_menu_item",
                    "click:upload_new_button",
                    "click:uploaded_archive_dismiss_area"),
                RequireNoAction(result.Actions, "click:submit_review_button"),
                Require(result.Result.Message.Contains("finalPage=WorldMain", StringComparison.Ordinal), "Final page should be WorldMain.")
            ]);

    private static Stage1DryRunScenario CreateStartFromWorldMainStillWorksScenario() =>
        new(
            "start_from_world_main_still_works",
            StandardSuccessFramesFromWorldMain(),
            result =>
            [
                Require(result.Result.IsSuccess, "WorldMain start should still work."),
                RequireActions(result.Actions, "press_key:Escape", "click:qianxing_realm_entry", "click:local_archive_upload_menu_item"),
                Require(result.Result.Message.Contains("startPage=WorldMain", StringComparison.Ordinal), "Workflow log should show WorldMain start.")
            ]);

    private static Stage1DryRunScenario CreateNewUploadPreviewEditBasicSuccessScenario() =>
        new(
            "new_upload_preview_edit_basic_success",
            PreviewEditSuccessFrames(includeTags: true),
            result =>
            [
                Require(result.Result.IsSuccess, "Preview edit should succeed after new upload."),
                RequireActions(result.Actions,
                    "click:text_edit_icon",
                    "click:preview_level_name_edit_button",
                    "click:preview_gameplay_description",
                    "click:preview_level_description",
                    "click:preview_tags_area",
                    "click:choice_option",
                    "click:primary_action_button",
                    "click:preview_version_record_area",
                    "click:preview_submit_button"),
                RequireActions(result.Actions,
                    "input_replace_text:Preview Level",
                    "input_replace_text:Gameplay text",
                    "input_replace_text:Level description",
                    "input_replace_text:Version log"),
                Require(result.Result.Message.Contains("PreviewInfoSubmitted", StringComparison.Ordinal), "Workflow log should include PreviewInfoSubmitted."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Old preview title"),
                OcrText("Preview Level"),
                OcrText("Gameplay"),
                OcrText("Gameplay details"),
                OcrText("Stage Description"),
                OcrText("Description"),
                OcrText("Action Adventure"),
                OcrText("OK")
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelName = "Preview Level";
                archive.GameDescription = "Gameplay text";
                archive.LevelDescription = "Level description";
                archive.Tags = "Adventure";
                archive.VersionLog = "Version log";
            });

    private static Stage1DryRunScenario CreateUpdatePreviewEditSkipsTagsScenario() =>
        new(
            "update_preview_edit_skips_tags",
            PreviewEditSuccessFrames(includeTags: false),
            result =>
            [
                Require(result.Result.IsSuccess, "Preview edit should succeed for update."),
                RequireNoAction(result.Actions, "click:preview_tags_area"),
                RequireNoAction(result.Actions, "click:choice_option"),
                Require(result.Result.Message.Contains("field=Tags result=Skipped reason=update_flow", StringComparison.Ordinal), "Workflow log should show skipped tags."),
                RequireNoPublishActions(result.Actions)
            ],
            IsUpdate: true,
            ExistingArchiveIdentifier: "DryRunExisting",
            ConfigureArchive: archive =>
            {
                archive.LevelName = "Update Preview";
                archive.GameDescription = "Gameplay text";
                archive.LevelDescription = "Level description";
                archive.Tags = "Adventure";
                archive.VersionLog = "Version log";
            });

    private static Stage1DryRunScenario CreateEarlyAccessDescriptionFilledBeforeGameplayAndLevelScenario() =>
        new(
            "early_access_description_filled_before_gameplay_and_level",
            PreviewEditEarlyAccessFrames(),
            result =>
            [
                Require(result.Result.IsSuccess, "Early-access preview edit should succeed."),
                RequireActions(result.Actions, "click:choice_option", "click:preview_early_access_description"),
                RequireActionsInOrder(
                    result.Actions,
                    "click:choice_option",
                    "click:preview_early_access_description",
                    "input_replace_text:Early access notes",
                    "input_replace_text:Gameplay text",
                    "input_replace_text:Level description"),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Old preview title"),
                OcrText("Preview Level"),
                OcrText("Early Access")
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelName = "Preview Level";
                archive.IsEarlyAccess = true;
                archive.EarlyAccessDescription = "Early access notes";
                archive.GameDescription = "Gameplay text";
                archive.LevelDescription = "Level description";
            });

    private static Stage1DryRunScenario CreateEarlyAccessDescriptionDoesNotMatchStageDescriptionScenario() =>
        new(
            "early_access_description_does_not_match_stage_description",
            PreviewEditEarlyAccessFrames(afterToggleFirstFrame: EarlyAccessLowerScrolledFrame()),
            result =>
            [
                Require(result.Result.IsSuccess, "Early-access description should be found after rejecting Stage Description."),
                RequireActions(result.Actions, "scroll:preview_editor:480", "click:preview_early_access_description"),
                RequireNoAction(result.Actions, "click:text_input_area"),
                Require(result.Result.Message.Contains("field=EarlyAccessDescription action=TextInputContextOcr", StringComparison.Ordinal),
                    "Workflow should log text_input_area OCR diagnostics."),
                Require(result.Result.Message.Contains("reason=rejected_token", StringComparison.Ordinal),
                    "Stage Description candidate should be rejected for EarlyAccessDescription."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Old preview title"),
                OcrText("Preview Level"),
                OcrText("Early Access"),
                OcrText("Stage Description"),
                OcrText("Description")
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelName = "Preview Level";
                archive.IsEarlyAccess = true;
                archive.EarlyAccessDescription = "Early access notes";
                archive.GameDescription = "Gameplay text";
                archive.LevelDescription = "Level description";
            });

    private static Stage1DryRunScenario CreateEarlyAccessDisabledSkipsDescriptionScenario() =>
        new(
            "early_access_disabled_skips_description_still_succeeds",
            PreviewEditSuccessFrames(includeTags: false),
            result =>
            [
                Require(result.Result.IsSuccess, "Preview edit should succeed when Early Access is disabled."),
                RequireNoAction(result.Actions, "click:choice_option"),
                RequireNoAction(result.Actions, "click:preview_early_access_description"),
                RequireNoAction(result.Actions, "input_replace_text:Early access notes"),
                RequireActions(result.Actions, "input_replace_text:Gameplay text", "input_replace_text:Level description"),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Old preview title"),
                OcrText("Preview Level")
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelName = "Preview Level";
                archive.IsEarlyAccess = false;
                archive.EarlyAccessDescription = "Early access notes";
                archive.GameDescription = "Gameplay text";
                archive.LevelDescription = "Level description";
            });

    private static Stage1DryRunScenario CreateEarlyAccessDescriptionScrollsUpBeforeDownScenario() =>
        new(
            "early_access_description_scrolls_up_before_down",
            PreviewEditEarlyAccessFrames(afterToggleFirstFrame: EarlyAccessLowerScrolledFrame()),
            result =>
            [
                Require(result.Result.IsSuccess, "Early-access description should be found by scrolling up first."),
                RequireActionsInOrder(
                    result.Actions,
                    "click:choice_option",
                    "scroll:preview_editor:480",
                    "click:preview_early_access_description",
                    "input_replace_text:Early access notes"),
                RequireNoActionBeforeAction(result.Actions, "scroll:preview_editor:-480", "click:preview_early_access_description"),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Old preview title"),
                OcrText("Preview Level"),
                OcrText("Early Access"),
                OcrText("Gameplay Details"),
                OcrText("Gameplay placeholder")
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelName = "Preview Level";
                archive.IsEarlyAccess = true;
                archive.EarlyAccessDescription = "Early access notes";
                archive.GameDescription = "Gameplay text";
                archive.LevelDescription = "Level description";
            });

    private static Stage1DryRunScenario CreatePreviewMissingSubmitButtonFailsScenario() =>
        new(
            "preview_missing_submit_button_fails",
            PreviewEditFrames(includeTags: false, includeSubmit: false),
            result =>
            [
                Require(!result.Result.IsSuccess, "Missing submit button should fail."),
                Require(result.Result.Message.Contains("submit button", StringComparison.OrdinalIgnoreCase), "Failure should mention submit button."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Publish")
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelName = "Missing Submit";
                archive.GameDescription = "Gameplay text";
            });

    private static Stage1DryRunScenario CreateTagDialogMissingRequiredTagFailsScenario() =>
        new(
            "tag_dialog_missing_required_tag_fails",
            PreviewEditFrames(includeTags: true, includeSubmit: true, tagDialogFrameCount: 35),
            result =>
            [
                Require(!result.Result.IsSuccess, "Missing required tag should fail."),
                Require(result.Result.Message.Contains("Required tag", StringComparison.Ordinal), "Failure should mention missing required tag."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Puzzle")
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelName = "Missing Tag";
                archive.GameDescription = "Gameplay text";
                archive.Tags = "Adventure";
            });

    private static Stage1DryRunScenario CreateTagCatalogAdventureMapsToActionAdventureScenario() =>
        new(
            "tag_catalog_adventure_maps_to_action_adventure",
            PreviewEditFrames(includeTags: true, includeSubmit: true, tagDialogFrameCount: 3),
            result =>
            [
                Require(result.Result.IsSuccess, "Adventure tool value should select Action Adventure in the game dialog."),
                Require(result.Result.Message.Contains("action=ResolveTag toolValue=\"Adventure\"", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("canonical=\"Action Adventure\"", StringComparison.Ordinal),
                    "Workflow should log Adventure -> Action Adventure mapping."),
                Require(result.Result.Message.Contains("step=Tags action=ClickChoice toolValue=\"Adventure\" canonical=\"Action Adventure\"", StringComparison.Ordinal),
                    "Workflow should click the catalog-resolved Action Adventure choice."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Action Adventure"),
                OcrText("OK")
            ],
            ConfigureArchive: archive => archive.Tags = "Adventure");

    private static Stage1DryRunScenario CreateTagCatalogFirstPersonDoesNotMatchShooterScenario() =>
        new(
            "tag_catalog_first_person_does_not_match_first_person_shooter",
            PreviewEditFrames(includeTags: true, includeSubmit: true, tagDialogFrameCount: 4),
            result =>
            [
                Require(result.Result.IsSuccess, "First-Person should skip First-Person Shooter and then select First-Person."),
                Require(result.Result.Message.Contains("candidateText=\"First-Person Shooter\"", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("accepted=False reason=no_alias_strict_match", StringComparison.Ordinal),
                    "First-Person Shooter candidate should be rejected when looking for First-Person."),
                Require(result.Result.Message.Contains("step=Tags action=ClickChoice toolValue=\"First-Person\" canonical=\"First-Person\"", StringComparison.Ordinal),
                    "Workflow should click First-Person only after strict match."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("First-Person Shooter"),
                OcrText("First-Person"),
                OcrText("OK")
            ],
            ConfigureArchive: archive => archive.Tags = "First-Person");

    private static Stage1DryRunScenario CreateTagCatalogFirstPersonShooterDoesNotMatchFirstPersonScenario() =>
        new(
            "tag_catalog_first_person_shooter_does_not_match_first_person",
            PreviewEditFrames(includeTags: true, includeSubmit: true, tagDialogFrameCount: 4),
            result =>
            [
                Require(result.Result.IsSuccess, "First-Person Shooter should skip First-Person and then select First-Person Shooter."),
                Require(result.Result.Message.Contains("candidateText=\"First-Person\"", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("accepted=False reason=no_alias_strict_match", StringComparison.Ordinal),
                    "First-Person candidate should be rejected when looking for First-Person Shooter."),
                Require(result.Result.Message.Contains("step=Tags action=ClickChoice toolValue=\"First-Person Shooter\" canonical=\"First-Person Shooter\"", StringComparison.Ordinal),
                    "Workflow should click First-Person Shooter only after strict match."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("First-Person"),
                OcrText("First-Person Shooter"),
                OcrText("OK")
            ],
            ConfigureArchive: archive => archive.Tags = "First-Person Shooter");

    private static Stage1DryRunScenario CreateTagDialogResetTopFindsUpperTagScenario() =>
        new(
            "tag_dialog_reset_top_finds_upper_tag",
            PreviewEditFrames(includeTags: true, includeSubmit: true, tagDialogFrameCount: 3),
            result =>
            [
                Require(result.Result.IsSuccess, "TagDialog should reset to top before scanning and find the upper tag."),
                RequireActionsInOrder(result.Actions, "scroll:tag_dialog_reset_top:480", "click:choice_option"),
                Require(result.Result.Message.Contains("step=Tags action=ResetScrollTop", StringComparison.Ordinal),
                    "Workflow should log TagDialog reset top."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Action Adventure"),
                OcrText("OK")
            ],
            ConfigureArchive: archive => archive.Tags = "Adventure");

    private static Stage1DryRunScenario CreateTagsSelectedInDialogOrderScenario() =>
        new(
            "tags_selected_in_dialog_order",
            PreviewEditFrames(includeTags: true, includeSubmit: true, tagDialogFrameCount: 5),
            result =>
            [
                Require(result.Result.IsSuccess, "Tags should be selected in TagCatalog dialog order."),
                RequireMessageContainsInOrder(
                    result.Result.Message,
                    "action=ResolveTag toolValue=\"First-Person\"",
                    "action=ResolveTag toolValue=\"Adventure\"",
                    "action=ResolveTag toolValue=\"Party\"",
                    "action=ClickChoice toolValue=\"Adventure\"",
                    "action=ClickChoice toolValue=\"Party\"",
                    "action=ClickChoice toolValue=\"First-Person\""),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Action Adventure"),
                OcrText("Party"),
                OcrText("First-Person"),
                OcrText("OK")
            ],
            ConfigureArchive: archive => archive.Tags = "First-Person,Adventure,Party");

    private static Stage1DryRunScenario CreateTagDialogConfirmRetryScenario() =>
        new(
            "tag_dialog_confirm_first_click_no_effect_second_succeeds",
            PreviewEditFrames(includeTags: true, includeSubmit: true, tagDialogFrameCount: 7),
            result =>
            [
                Require(result.Result.IsSuccess, "Tags OK should retry when the first click leaves the page in TagDialog."),
                Require(result.Result.Message.Contains("step=Tags action=ConfirmOK attempt=1", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("step=Tags action=ConfirmOK attempt=2", StringComparison.Ordinal),
                    "Workflow should log both OK attempts."),
                Require(result.Result.Message.Contains("step=Tags action=VerifyConfirm attempt=2", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("page=PreviewEditor", StringComparison.Ordinal),
                    "Workflow should verify the second OK returned to PreviewEditor."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Action Adventure"),
                OcrText("OK"),
                OcrText("OK")
            ],
            ConfigureArchive: archive => archive.Tags = "Adventure");

    private static Stage1DryRunScenario CreateTagsSuccessThenVersionLogFailureUsesFinalDiagnosticScenario() =>
        new(
            "tags_success_then_versionlog_recovery_then_final_failure_uses_execute_diagnostic",
            PreviewEditTagsThenVersionLogMissingFrames(),
            result =>
            [
                Require(!result.Result.IsSuccess, "Scenario should fail after Tags and VersionLog recovery at the final ExtraImages step."),
                Require(result.Result.Message.Contains("step=Tags action=OpenDialog", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("step=Tags action=ClickChoice", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("step=Tags action=ConfirmOK", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("step=Tags action=VerifyConfirm", StringComparison.Ordinal),
                    "Workflow log should show Tags opened, selected, confirmed, and verified."),
                Require(result.Result.Message.Contains("field=VersionLog", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("reason=optional_field_unavailable", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("field=ExtraImages", StringComparison.Ordinal),
                    "Workflow should show VersionLog recovery and the later final failure step."),
                Require(LatestDiagnosticJsonFromMessage(result.Result.Message).Contains("stage1-execute-failure", StringComparison.OrdinalIgnoreCase),
                    "LatestDiagnosticJson should point at final execute failure."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Action Adventure"),
                OcrText("OK")
            ],
            ConfigureArchive: archive =>
            {
                archive.Tags = "Adventure";
                archive.VersionLog = "Version log";
                archive.ExtraImagePaths.Add(ExistingDryRunResourcePath("tags-then-extra-final.png"));
            });

    private static Stage1DryRunScenario CreateTagsEditRowStageTagsOpensDialogScenario() =>
        new(
            "preview_tags_edit_row_stage_tags_opens_dialog",
            PreviewEditTagsEditRowFrames(editRowCount: 1),
            result =>
            [
                Require(result.Result.IsSuccess, "Tags edit_row OCR Stage Tags should open TagDialog."),
                RequireActions(result.Actions, "click:edit_row", "click:choice_option", "click:primary_action_button"),
                RequireNoAction(result.Actions, "click:preview_tags_area"),
                RequireNoActionBeforeAction(result.Actions, "scroll:preview_editor:-480", "click:edit_row"),
                Require(result.Result.Message.Contains("selectedSource=edit_row_ocr", StringComparison.Ordinal),
                    "Workflow log should show edit_row_ocr source."),
                Require(result.Result.Message.Contains("raw=\"Stage Tags\"", StringComparison.Ordinal),
                    "Workflow log should include raw edit_row OCR."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Stage Tags"),
                OcrText("Action Adventure"),
                OcrText("OK")
            ],
            ConfigureArchive: archive =>
            {
                archive.Tags = "Adventure";
            });

    private static Stage1DryRunScenario CreateTagsEditRowClickToAddOpensDialogScenario() =>
        new(
            "preview_tags_edit_row_click_to_add_opens_dialog",
            PreviewEditTagsEditRowFrames(editRowCount: 1),
            result =>
            [
                Require(result.Result.IsSuccess, "Tags edit_row OCR placeholder should open TagDialog."),
                RequireActions(result.Actions, "click:edit_row", "click:choice_option", "click:primary_action_button"),
                Require(result.Result.Message.Contains("raw=\"Click to add tags to your stage\"", StringComparison.Ordinal),
                    "Workflow log should include placeholder OCR."),
                Require(result.Result.Message.Contains("normalized=\"CLICKTOADDTAGSTOYOURSTAGE\"", StringComparison.Ordinal),
                    "Workflow log should include normalized OCR."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Click to add tags to your stage"),
                OcrText("Action Adventure"),
                OcrText("OK")
            ],
            ConfigureArchive: archive =>
            {
                archive.Tags = "Adventure";
            });

    private static Stage1DryRunScenario CreateTagsEditRowCurrentViewportDoesNotScrollFirstScenario() =>
        new(
            "preview_tags_edit_row_current_viewport_no_scroll_first",
            PreviewEditTagsEditRowFrames(editRowCount: 1),
            result =>
            [
                Require(result.Result.IsSuccess, "Visible Tags edit_row should be used before scrolling."),
                RequireActionsInOrder(result.Actions, "click:edit_row", "click:choice_option"),
                RequireNoActionBeforeAction(result.Actions, "scroll:preview_editor:-480", "click:edit_row"),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Tags *"),
                OcrText("Action Adventure"),
                OcrText("OK")
            ],
            ConfigureArchive: archive =>
            {
                archive.Tags = "Adventure";
            });

    private static Stage1DryRunScenario CreateTagsEditRowAmbiguousFailsScenario() =>
        new(
            "preview_tags_edit_row_ambiguous_fails",
            PreviewEditTagsEditRowFrames(editRowCount: 2),
            result =>
            [
                Require(!result.Result.IsSuccess, "Multiple Tags edit_row OCR matches should fail."),
                Require(result.Result.Message.Contains("multiple candidates", StringComparison.OrdinalIgnoreCase) ||
                    result.Result.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase),
                    "Failure should mention ambiguous/multiple tags edit_row candidates."),
                RequireNoAction(result.Actions, "click:edit_row"),
                RequireNoActionContaining(result.Actions, "scroll:preview_editor"),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Stage Tags"),
                OcrText("Tags")
            ],
            ConfigureArchive: archive =>
            {
                archive.Tags = "Adventure";
            });

    private static Stage1DryRunScenario CreateVersionLogEditRowOcrOpensScenario() =>
        new(
            "preview_version_log_edit_row_ocr_opens",
            PreviewEditVersionLogEditRowFrames(),
            result =>
            [
                Require(result.Result.IsSuccess, "VersionLog edit_row OCR should open and edit."),
                RequireActions(result.Actions, "click:edit_row", "input_replace_text:Version log", "click:preview_submit_button"),
                Require(result.Result.Message.Contains("field=VersionLog", StringComparison.Ordinal), "Workflow should mention VersionLog."),
                Require(result.Result.Message.Contains("selectedSource=edit_row_ocr", StringComparison.Ordinal), "Workflow should show edit_row_ocr source."),
                Require(result.Result.Message.Contains("raw=\"Version Log\"", StringComparison.Ordinal), "Workflow should include VersionLog raw OCR."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Version Log"),
                OcrText("Existing update log")
            ],
            ConfigureArchive: archive =>
            {
                archive.VersionLog = "Version log";
            });

    private static Stage1DryRunScenario CreateVersionLogEmptyStateSkipsAndContinuesScenario() =>
        new(
            "preview_version_log_empty_state_skips_and_continues",
            PreviewEditVersionLogEditRowFrames(),
            result =>
            [
                Require(result.Result.IsSuccess, "VersionLog empty-state should be skipped and workflow should continue."),
                RequireActions(result.Actions, "click:edit_row", "input_key:Escape", "click:preview_submit_button"),
                RequireNoAction(result.Actions, "input_replace_text:Version log"),
                Require(result.Result.Message.Contains("optional_field_unavailable", StringComparison.Ordinal),
                    "Workflow should record optional_field_unavailable."),
                Require(result.Result.Message.Contains("PreviewInfoSubmitted", StringComparison.Ordinal),
                    "Workflow should continue to submit after optional VersionLog skip."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Update Log"),
                OcrText("No game version update log available for editing")
            ],
            ConfigureArchive: archive =>
            {
                archive.VersionLog = "Version log";
            });

    private static Stage1DryRunScenario CreateVersionLogEmptyStateRecoverableDiagnosticScenario() =>
        new(
            "versionlog_empty_state_is_recoverable_and_does_not_become_final_diagnostic",
            PreviewEditVersionLogThenMediaAddTileFrames(mediaTileCount: 1),
            result =>
            [
                Require(!result.Result.IsSuccess, "Scenario should fail after VersionLog recovery at ExtraImages file dialog."),
                Require(result.Result.Message.Contains("optional_field_unavailable", StringComparison.Ordinal),
                    "Workflow should record VersionLog optional recovery."),
                Require(result.Result.Message.Contains("stage1-preview-extraimages-file-dialog-timeout", StringComparison.Ordinal),
                    "Workflow should save the final media file-dialog diagnostic."),
                Require(result.Result.Message.Contains("stage1-execute-failure", StringComparison.Ordinal),
                    "Failure message should point LatestDiagnostic at final execute failure."),
                Require(!LatestDiagnosticJsonFromMessage(result.Result.Message).Contains("versionlog", StringComparison.OrdinalIgnoreCase),
                    "LatestDiagnosticJson should not point at the recoverable VersionLog diagnostic."),
                Require(RecoverableVersionLogDiagnosticIsMarked(result.Result.Message),
                    "Recoverable VersionLog diagnostic JSON should be marked Recoverable=true and IsFinalFailure=false."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Update Log"),
                OcrText("No game version update log available for editing"),
                OcrText("More Showcase Images")
            ],
            ConfigureArchive: archive =>
            {
                archive.VersionLog = "Version log";
                archive.ExtraImagePaths.Add(ExistingDryRunResourcePath("recoverable-extra.png"));
            },
            FileDialogFails: true,
            FileDialogFailPathContains: "recoverable-extra.png");

    private static Stage1DryRunScenario CreateVersionLogRecoveredThenExtraImagesFileDialogTimeoutScenario() =>
        new(
            "versionlog_recovered_then_extra_images_file_dialog_timeout_reports_extraimages_final_failure",
            PreviewEditVersionLogThenMediaAddTileFrames(mediaTileCount: 1),
            result =>
            [
                Require(!result.Result.IsSuccess, "Scenario should fail at ExtraImages file dialog after VersionLog recovery."),
                RequireActions(result.Actions, "click:edit_row", "input_key:Escape", "click:media_add_tile"),
                Require(result.Result.Message.Contains("field=ExtraImages[0] action=SelectFileCompleted success=false", StringComparison.Ordinal),
                    "Workflow should log ExtraImages SelectFileAsync failure."),
                Require(result.Result.Message.Contains("stage1-preview-extraimages-file-dialog-timeout", StringComparison.Ordinal),
                    "Failure should include the ExtraImages file-dialog timeout diagnostic."),
                Require(!LatestDiagnosticJsonFromMessage(result.Result.Message).Contains("versionlog", StringComparison.OrdinalIgnoreCase),
                    "LatestDiagnosticJson should not point at VersionLog after the final ExtraImages failure."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Version Log"),
                OcrText("No game version update log available for editing"),
                OcrText("More Showcase Images")
            ],
            ConfigureArchive: archive =>
            {
                archive.VersionLog = "Version log";
                archive.ExtraImagePaths.Add(ExistingDryRunResourcePath("dialog-timeout-extra.png"));
            },
            FileDialogFails: true,
            FileDialogFailPathContains: "dialog-timeout-extra.png");

    private static Stage1DryRunScenario CreateExtraImagesTargetLogsSourceAndPathScenario() =>
        new(
            "extra_images_target_logs_source_and_path",
            PreviewEditMediaAddTileFrames(mediaTileCount: 1),
            result =>
            [
                Require(result.Result.IsSuccess, "ExtraImages should upload successfully through media_add_tile OCR."),
                Require(result.Result.Message.Contains("field=ExtraImages[0] action=MediaUploadResource", StringComparison.Ordinal),
                    "Workflow should log ExtraImages resource metadata."),
                Require(result.Result.Message.Contains($"resolvedPath=\"{ExistingDryRunResourcePath("logged-extra.png")}\"", StringComparison.Ordinal),
                    "Workflow should log the resolved ExtraImages path."),
                Require(result.Result.Message.Contains("fileExists=True fileSizeBytes=1", StringComparison.Ordinal),
                    "Workflow should log file existence and size."),
                Require(result.Result.Message.Contains("selectedSource=media_add_tile_ocr", StringComparison.Ordinal),
                    "Workflow should log media_add_tile_ocr as the target source."),
                Require(result.Result.Message.Contains("target=(media_add_tile", StringComparison.Ordinal),
                    "Workflow should log the selected media target box."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("More Showcase Images")
            ],
            ConfigureArchive: archive =>
            {
                archive.ExtraImagePaths.Add(ExistingDryRunResourcePath("logged-extra.png"));
            });

    private static Stage1DryRunScenario CreateFinalExecuteFailureContainsWorkflowLogAfterOptionalRecoveryScenario() =>
        new(
            "final_execute_failure_contains_workflowlog_after_optional_recovery",
            PreviewEditVersionLogThenMediaAddTileFrames(mediaTileCount: 1),
            result =>
            [
                Require(!result.Result.IsSuccess, "Scenario should fail after optional recovery."),
                Require(result.Result.Message.Contains("Workflow log:", StringComparison.Ordinal),
                    "Failure message should include workflow log tail/context."),
                Require(result.Result.Message.Contains("optional_field_unavailable", StringComparison.Ordinal),
                    "Workflow log should retain optional VersionLog recovery entry."),
                Require(result.Result.Message.Contains("stage1-execute-failure", StringComparison.Ordinal),
                    "Failure message should include final execute failure diagnostic."),
                Require(result.Result.Message.Contains("field=ExtraImages[0] action=SelectFileCompleted success=false", StringComparison.Ordinal),
                    "Workflow log should include final ExtraImages failure details."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Version Log"),
                OcrText("No game version update log available for editing"),
                OcrText("More Showcase Images")
            ],
            ConfigureArchive: archive =>
            {
                archive.VersionLog = "Version log";
                archive.ExtraImagePaths.Add(ExistingDryRunResourcePath("workflowlog-extra.png"));
            },
            FileDialogFails: true,
            FileDialogFailPathContains: "workflowlog-extra.png");

    private static Stage1DryRunScenario CreateMultilanguageEmptyConfigLogsSkipBeforeExtraImagesScenario() =>
        new(
            "multilanguage_empty_config_logs_skip_before_extraimages",
            PreviewEditMediaAddTileFrames(mediaTileCount: 1),
            result =>
            [
                Require(result.Result.IsSuccess, "ExtraImages should still upload when no multilanguage rows are configured."),
                RequireMessageContainsInOrder(
                    result.Result.Message,
                    "field=Multilanguage action=SkipMultilanguage reason=no configured languages",
                    "field=ExtraImages[0] action=MediaUploadResource"),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("More Showcase Images")
            ],
            ConfigureArchive: archive =>
            {
                archive.ExtraImagePaths.Add(ExistingDryRunResourcePath("multilanguage-skip-extra.png"));
            });

    private static Stage1DryRunScenario CreateNoLocalizedInfosSkipsMultilanguageScenario() =>
        new(
            "no_localized_infos_skips_multilanguage",
            PreviewEditFrames(includeTags: false, includeSubmit: true),
            result =>
            [
                Require(result.Result.IsSuccess, "No localized infos should skip Multilanguage."),
                Require(result.Result.Message.Contains("field=Multilanguage action=SkipMultilanguage reason=no configured languages", StringComparison.Ordinal),
                    "Workflow should log no configured languages skip."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Gameplay"),
                OcrText("Gameplay details")
            ],
            ConfigureArchive: archive =>
            {
                archive.GameDescription = "Gameplay text";
            });

    private static Stage1DryRunScenario CreateEnClientAddsZhCnAndJaScenario() =>
        CreateMultilanguageScenario(
            "en_client_adds_zh_cn_and_ja",
            "Add Language",
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN", "ja"],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "EN client should add zh-CN and ja."),
                RequireActions(result.Actions, "click:preview_multilanguage_area", "click:multilanguage_add_button", "click:language_confirm_button"),
                Require(result.Result.Message.Contains("clientLanguage=En", StringComparison.Ordinal), "Client language should be EN."),
                Require(result.Result.Message.Contains("missing=[zh-CN,ja]", StringComparison.Ordinal), "Should compute zh-CN and ja as missing."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateZhCnClientAddsEnAndJaScenario() =>
        CreateMultilanguageScenario(
            "zh_cn_client_adds_en_and_ja",
            "\u6dfb\u52a0\u8bed\u8a00",
            selectedBefore: ["zh-CN"],
            configuredLanguages: ["en", "ja"],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "ZH-CN client should add en and ja."),
                Require(result.Result.Message.Contains("clientLanguage=ZhCn", StringComparison.Ordinal), "Client language should be ZH-CN."),
                Require(result.Result.Message.Contains("missing=[en,ja]", StringComparison.Ordinal), "Should compute en and ja as missing."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateUpdateAllLanguagesAlreadySelectedEscReturnsToEditorScenario() =>
        CreateMultilanguageScenario(
            "update_all_languages_already_selected_esc_returns_to_editor",
            "Add Language",
            selectedBefore: ["en", "zh-CN", "ja"],
            configuredLanguages: ["zh-CN", "ja"],
            isUpdate: true,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Update-like selected set should not add languages when all desired languages are already selected."),
                Require(result.Result.Message.Contains("missing=[]", StringComparison.Ordinal), "No desired language should be missing."),
                Require(result.Actions.All(action => !string.Equals(action, "click:language_option", StringComparison.Ordinal)),
                    "No language option should be clicked when all desired languages are selected."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateUpdateMissingLanguagesOnlyAddsMissingScenario() =>
        CreateMultilanguageScenario(
            "update_missing_languages_only_adds_missing",
            "Add Language",
            selectedBefore: ["en", "zh-CN"],
            configuredLanguages: ["zh-CN", "ja"],
            isUpdate: true,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Update should add only missing ja."),
                Require(result.Result.Message.Contains("missing=[ja]", StringComparison.Ordinal), "Only ja should be missing."),
                Require(result.Actions.Count(action => string.Equals(action, "click:language_option", StringComparison.Ordinal)) == 1,
                    "Exactly one language option should be clicked."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateUpdateExtraLanguagesAreIgnoredNotRemovedScenario() =>
        CreateMultilanguageScenario(
            "update_extra_languages_are_ignored_not_removed",
            "Add Language",
            selectedBefore: ["en", "zh-CN", "ja", "fr"],
            configuredLanguages: ["zh-CN"],
            isUpdate: true,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Extra selected languages should be ignored, not removed."),
                Require(result.Result.Message.Contains("selectedBefore=[", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("ja", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("fr", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("zh-CN", StringComparison.Ordinal),
                    "Workflow should preserve extra selected languages."),
                Require(result.Result.Message.Contains("missing=[]", StringComparison.Ordinal), "No language should be missing."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateOpenCvSelectedLanguageDetectionScenario() =>
        CreateMultilanguageScenario(
            "opencv_selected_language_detection",
            "Add Language",
            selectedBefore: ["en", "zh-CN"],
            configuredLanguages: ["zh-CN"],
            isUpdate: true,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Selected language detection should treat high-confidence dry-run target as selected."),
                Require(result.Result.Message.Contains("selectedBefore=[en,zh-CN]", StringComparison.Ordinal), "zh-CN should be detected selected."),
                Require(result.Result.Message.Contains("dryRun confidence=0.96 selected=True", StringComparison.Ordinal), "OpenCV diagnostic should include selected evidence."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateOpenCvUnselectedLanguageDetectionScenario() =>
        CreateMultilanguageScenario(
            "opencv_unselected_language_detection",
            "Add Language",
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Unselected language should be detected and selected."),
                Require(result.Result.Message.Contains("missing=[zh-CN]", StringComparison.Ordinal), "zh-CN should be missing before click."),
                Require(result.Result.Message.Contains("dryRun confidence=0.90 selected=False", StringComparison.Ordinal), "OpenCV diagnostic should include unselected evidence."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateTableOrderEnClientFiltersSelectedLanguagesScenario() =>
        CreateMultilanguageScenario(
            "table_order_en_client_filters_selected_languages",
            "Add Language",
            selectedBefore: ["en", "zh-CN", "ja"],
            configuredLanguages: ["zh-CN", "ja"],
            isUpdate: true,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "EN table order should filter selected languages."),
                Require(result.Result.Message.Contains("visibleRows=[0:en", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("1:zh-CN", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("2:ja", StringComparison.Ordinal),
                    "Visible rows should follow EN order."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateTableOrderZhCnClientFiltersSelectedLanguagesScenario() =>
        CreateMultilanguageScenario(
            "table_order_zh_cn_client_filters_selected_languages",
            "\u6dfb\u52a0\u8bed\u8a00",
            selectedBefore: ["zh-CN", "en", "ja"],
            configuredLanguages: ["en", "ja"],
            isUpdate: true,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "ZH-CN table order should filter selected languages."),
                Require(result.Result.Message.Contains("visibleRows=[0:zh-CN", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("1:en", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("2:ja", StringComparison.Ordinal),
                    "Visible rows should follow ZH-CN order."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateFillsMultilanguageCellsByCenterScenario() =>
        CreateMultilanguageScenario(
            "fills_stage_name_gameplay_description_level_description_update_log_by_cell_center",
            "Add Language",
            selectedBefore: ["en", "zh-CN"],
            configuredLanguages: ["zh-CN"],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Should fill all configured multilanguage columns."),
                Require(result.Result.Message.Contains("columnName=\"Stage Name\" clickPoint=", StringComparison.Ordinal), "Should click Stage Name cell center."),
                Require(result.Result.Message.Contains("columnName=\"Gameplay Details\" clickPoint=", StringComparison.Ordinal), "Should click Gameplay Details cell center."),
                Require(result.Result.Message.Contains("columnName=\"Stage Description\" clickPoint=", StringComparison.Ordinal), "Should click Stage Description cell center."),
                Require(result.Result.Message.Contains("columnName=\"Update Log\" clickPoint=", StringComparison.Ordinal), "Should click Update Log cell center."),
                RequireActions(result.Actions, "input_replace_text:zh-CN Stage", "input_replace_text:zh-CN Gameplay", "input_replace_text:zh-CN Description", "input_replace_text:zh-CN Update"),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateEarlyAccessMultilanguageUsesTextEditIconGeometryScenario() =>
        CreateTextEditIconTableMultilanguageScenario(
            "early_access_multilanguage_uses_text_edit_icon_geometry_without_level_name_cells",
            earlyAccess: true,
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            rowCount: 2,
            columnCount: 4,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Early Access zh-CN should fill using text_edit_icon table geometry."),
                Require(result.Result.Message.Contains("source=text_edit_icon", StringComparison.Ordinal), "Geometry should be inferred from text_edit_icon clusters."),
                Require(result.Result.Message.Contains("earlyAccessColumnEnabled=True", StringComparison.Ordinal), "Early Access column should be enabled."),
                Require(result.Result.Message.Contains("visibleRows=[0:en", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("1:zh-CN", StringComparison.Ordinal),
                    "Visible rows should follow EN order after adding zh-CN."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateEarlyAccessMultilanguageColumnOrderUsesEarlyAccessDescriptionScenario() =>
        CreateTextEditIconTableMultilanguageScenario(
            "early_access_multilanguage_column_order_includes_early_access_description",
            earlyAccess: true,
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            rowCount: 2,
            columnCount: 4,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Early Access column order should fill without shifting later columns."),
                Require(result.Result.Message.Contains("columnName=\"Early Access Version Description\" clickPoint=(0.360", StringComparison.Ordinal),
                    "Early Access Description should use the second column."),
                Require(result.Result.Message.Contains("columnName=\"Gameplay Details\" clickPoint=(0.460", StringComparison.Ordinal),
                    "Gameplay should use the third column when Early Access is enabled."),
                Require(result.Result.Message.Contains("columnName=\"Stage Description\" clickPoint=(0.560", StringComparison.Ordinal),
                    "Stage Description should use the fourth column when Early Access is enabled."),
                Require(result.Result.Message.Contains("columnName=\"Update Log\" clickPoint=(0.660", StringComparison.Ordinal),
                    "Update Log should use the fifth column when Early Access is enabled."),
                RequireActions(result.Actions, "input_replace_text:zh-CN Early Access", "input_replace_text:zh-CN Gameplay", "input_replace_text:zh-CN Description", "input_replace_text:zh-CN Update"),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateNonEarlyAccessMultilanguageTextEditIconGeometryKeepsFourColumnsScenario() =>
        CreateTextEditIconTableMultilanguageScenario(
            "non_early_access_multilanguage_text_edit_icon_geometry_keeps_four_columns",
            earlyAccess: false,
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            rowCount: 2,
            columnCount: 3,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Non-Early Access icon geometry should keep the four-column order."),
                Require(result.Result.Message.Contains("earlyAccessColumnEnabled=False", StringComparison.Ordinal), "Early Access column should be disabled."),
                Require(!result.Result.Message.Contains("columnName=\"Early Access Version Description\"", StringComparison.Ordinal),
                    "Early Access column should not be filled when disabled."),
                Require(result.Result.Message.Contains("columnName=\"Gameplay Details\" clickPoint=(0.360", StringComparison.Ordinal),
                    "Gameplay should remain the second column when Early Access is disabled."),
                Require(result.Result.Message.Contains("columnName=\"Stage Description\" clickPoint=(0.460", StringComparison.Ordinal),
                    "Stage Description should remain the third column when Early Access is disabled."),
                Require(result.Result.Message.Contains("columnName=\"Update Log\" clickPoint=(0.560", StringComparison.Ordinal),
                    "Update Log should remain the fourth column when Early Access is disabled."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateConfiguredLanguagesContainsDefaultLanguageSkipsDefaultRowScenario() =>
        CreateTextEditIconTableMultilanguageScenario(
            "configured_languages_contains_default_language_skips_default_row",
            earlyAccess: false,
            selectedBefore: ["en"],
            configuredLanguages: ["en", "zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            rowCount: 2,
            columnCount: 3,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Configured default language should be skipped safely."),
                Require(result.Result.Message.Contains("action=SkipDefaultLanguage language=en", StringComparison.Ordinal),
                    "Workflow should explicitly log default language skip."),
                Require(!result.Result.Message.Contains("action=ClickCellCenter language=en", StringComparison.Ordinal),
                    "Default language row should not be clicked."),
                Require(!result.Actions.Any(action => action.Contains("input_replace_text:en ", StringComparison.Ordinal)),
                    "Default language text should not be typed in the table."),
                Require(result.Result.Message.Contains("SkippedDefaultLanguages", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("SkipDefaultLanguage language=en", StringComparison.Ordinal),
                    "Diagnostic/log state should expose skipped default languages."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateDefaultRowKeptForRowGeometryButNotFilledScenario() =>
        CreateTextEditIconTableMultilanguageScenario(
            "default_row_kept_for_row_geometry_but_not_filled",
            earlyAccess: false,
            selectedBefore: ["en"],
            configuredLanguages: ["en", "zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            rowCount: 2,
            columnCount: 3,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Default row should remain in visibleRows while skipped for filling."),
                Require(result.Result.Message.Contains("visibleRows=[0:en@0.340,1:zh-CN@0.410", StringComparison.Ordinal),
                    "Visible rows should keep default row at rowIndex 0 and zh-CN at rowIndex 1."),
                Require(result.Result.Message.Contains("action=ClickCellCenter language=zh-CN rowIndex=1", StringComparison.Ordinal),
                    "Non-default language should use rowIndex 1."),
                Require(!result.Result.Message.Contains("action=ClickCellCenter language=en rowIndex=0", StringComparison.Ordinal),
                    "Default rowIndex 0 should not be filled."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateEarlyAccessTextEditIconsOnlyFourColumnsInfersFiveBusinessColumnsScenario() =>
        CreateTextEditIconTableMultilanguageScenario(
            "early_access_enabled_text_edit_icons_only_4_columns_still_infers_5_business_columns",
            earlyAccess: true,
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            rowCount: 2,
            columnCount: 4,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Four edit-icon columns should infer five Early Access business columns."),
                Require(result.Result.Message.Contains("businessColumnCount=5 editableIconColumnCount=4", StringComparison.Ordinal),
                    "Workflow should log five business columns backed by four edit-icon columns."),
                Require(result.Result.Message.Contains("Stage Name:0.260", StringComparison.Ordinal),
                    "Stage Name should be inferred left of the first edit-icon column."),
                Require(result.Result.Message.Contains("Early Access Version Description:0.360", StringComparison.Ordinal),
                    "Early Access column should map to first edit-icon column."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateNonEarlyAccessTextEditIconsOnlyThreeColumnsInfersFourBusinessColumnsScenario() =>
        CreateTextEditIconTableMultilanguageScenario(
            "early_access_disabled_text_edit_icons_only_3_columns_still_infers_4_business_columns",
            earlyAccess: false,
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            rowCount: 2,
            columnCount: 3,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Three edit-icon columns should infer four non-Early Access business columns."),
                Require(result.Result.Message.Contains("businessColumnCount=4 editableIconColumnCount=3", StringComparison.Ordinal),
                    "Workflow should log four business columns backed by three edit-icon columns."),
                Require(result.Result.Message.Contains("Stage Name:0.260", StringComparison.Ordinal),
                    "Stage Name should be inferred left of the first edit-icon column."),
                Require(result.Result.Message.Contains("Gameplay Details:0.360", StringComparison.Ordinal),
                    "Gameplay should map to first edit-icon column when Early Access is disabled."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateEnEarlyAccessHeaderOcrPlusRightmostIconsFillsZhCnScenario() =>
        CreateHeaderOcrPartialIconMultilanguageScenario(
            "en_early_access_header_ocr_plus_rightmost_icons_fills_zh_cn",
            addButtonOcr: "Add Language",
            earlyAccess: true,
            selectedBefore: ["en"],
            configuredLanguages: ["en", "zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            iconX: 0.74,
            headerOcr: EarlyAccessHeaderOcr(),
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "EN Early Access should infer columns from header OCR plus rightmost Update Log icons."),
                Require(result.Result.Message.Contains("source=header_ocr_plus_partial_icons", StringComparison.Ordinal),
                    "Geometry source should identify header OCR plus partial icons."),
                Require(result.Result.Message.Contains("action=SkipDefaultLanguage language=en", StringComparison.Ordinal),
                    "Default EN row should be skipped."),
                Require(!result.Result.Message.Contains("action=ClickCellCenter language=en", StringComparison.Ordinal),
                    "Default EN row should not be filled."),
                Require(result.Result.Message.Contains("action=ClickCellCenter language=zh-CN rowIndex=1", StringComparison.Ordinal),
                    "zh-CN row should be filled."),
                RequireActions(result.Actions, "input_replace_text:zh-CN Early Access", "input_replace_text:zh-CN Gameplay", "input_replace_text:zh-CN Description", "input_replace_text:zh-CN Update"),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateZhCnHeaderOcrPlusRightmostIconsSkipsDefaultAndFillsEnScenario() =>
        CreateHeaderOcrPartialIconMultilanguageScenario(
            "zh_cn_header_ocr_plus_rightmost_icons_skips_default_and_fills_en",
            addButtonOcr: "\u6dfb\u52a0\u8bed\u8a00",
            earlyAccess: false,
            selectedBefore: ["zh-CN"],
            configuredLanguages: ["zh-CN", "en"],
            tableLanguagesAfter: ["zh-CN", "en"],
            iconX: 0.66,
            headerOcr: NonEarlyAccessHeaderOcr(),
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "ZH-CN client should skip zh-CN default and fill en."),
                Require(result.Result.Message.Contains("clientLanguage=ZhCn", StringComparison.Ordinal), "Client should be ZH-CN."),
                Require(result.Result.Message.Contains("DefaultLanguage", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("action=SkipDefaultLanguage language=zh-CN", StringComparison.Ordinal),
                    "Default language state should be available."),
                Require(!result.Result.Message.Contains("action=ClickCellCenter language=zh-CN", StringComparison.Ordinal),
                    "Default zh-CN row should not be filled."),
                Require(result.Result.Message.Contains("action=ClickCellCenter language=en rowIndex=1", StringComparison.Ordinal),
                    "en row should be filled after default zh-CN row."),
                RequireActions(result.Actions, "input_replace_text:en Stage", "input_replace_text:en Gameplay", "input_replace_text:en Description", "input_replace_text:en Update"),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateHeaderOcrInsufficientAndIconColumnInsufficientFailsScenario() =>
        CreateHeaderOcrPartialIconMultilanguageScenario(
            "header_ocr_insufficient_and_icon_column_insufficient_fails",
            addButtonOcr: "Add Language",
            earlyAccess: true,
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            iconX: 0.74,
            headerOcr:
            [
                WorkflowOcr("Stage Name", 0.26, 0.27),
                WorkflowOcr("Update Log", 0.74, 0.27)
            ],
            assertions: result =>
            [
                Require(!result.Result.IsSuccess, "Insufficient header OCR plus partial icons should fail safely."),
                Require(result.Result.Message.Contains("table geometry could not be inferred", StringComparison.OrdinalIgnoreCase),
                    "Failure should mention table geometry inference."),
                Require(result.Result.Message.Contains("header OCR did not expose all required columns", StringComparison.OrdinalIgnoreCase),
                    "Failure should explain insufficient header OCR."),
                Require(result.Result.Message.Contains("FillTargetLanguages", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("fillTargetLanguages", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("desired=[zh-CN]", StringComparison.Ordinal),
                    "Diagnostic/log should include fill target context before geometry failure."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateMultilanguageTextEditIconGeometryInsufficientFailsScenario() =>
        CreateTextEditIconTableMultilanguageScenario(
            "multilanguage_text_edit_icon_geometry_insufficient_fails",
            earlyAccess: true,
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            rowCount: 1,
            columnCount: 2,
            assertions: result =>
            [
                Require(!result.Result.IsSuccess, "Insufficient text_edit_icon clusters should fail."),
                Require(result.Result.Message.Contains("table geometry could not be inferred", StringComparison.OrdinalIgnoreCase),
                    "Failure should mention table geometry could not be inferred."),
                Require(result.Result.Message.Contains("not enough", StringComparison.OrdinalIgnoreCase),
                    "Failure should include the insufficient cluster reason."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateMultilanguageSelectedAfterRowsFollowEnOrderScenario() =>
        CreateTextEditIconTableMultilanguageScenario(
            "multilanguage_selected_after_rows_follow_en_order",
            earlyAccess: false,
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            tableLanguagesAfter: ["en", "zh-CN"],
            rowCount: 2,
            columnCount: 4,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "SelectedLanguagesAfter should include en and zh-CN."),
                Require(result.Result.Message.Contains("SelectedLanguagesAfter", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("visibleRows=[0:en", StringComparison.Ordinal),
                    "Workflow should expose selected/visible row state."),
                Require(result.Result.Message.Contains("visibleRows=[0:en", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("1:zh-CN", StringComparison.Ordinal),
                    "Visible rows should be en then zh-CN for EN client."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateUnsupportedLanguageFailsWithDiagnosticScenario() =>
        new(
            "unsupported_language_fails_with_diagnostic",
            PreviewEditFrames(includeTags: false, includeSubmit: true),
            result =>
            [
                Require(!result.Result.IsSuccess, "Unsupported language should fail."),
                Require(result.Result.Message.Contains("unsupported language code/name", StringComparison.OrdinalIgnoreCase), "Failure should mention unsupported language."),
                RequireNoPublishActions(result.Actions)
            ],
            ConfigureArchive: archive =>
            {
                AddLocalizedInfo(archive, "xx", "Bad Language");
            });

    private static Stage1DryRunScenario CreateCannotDetectClientLanguageFailsWithDiagnosticScenario() =>
        CreateMultilanguageScenario(
            "cannot_detect_client_language_fails_with_diagnostic",
            "Ajouter une langue",
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            assertions: result =>
            [
                Require(!result.Result.IsSuccess, "Unknown Add Language OCR should fail."),
                Require(result.Result.Message.Contains("client-language-undetected", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("Client language could not be detected", StringComparison.Ordinal),
                    "Failure should identify client language detection."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateMissingAddLanguageButtonFailsWithDiagnosticScenario() =>
        new(
            "missing_add_language_button_fails_with_diagnostic",
            PreviewEditMultilanguageFrames("Add Language", selectedBefore: ["en"], visibleLanguages: ["en", "zh-CN"], includeAddButton: false, fillLanguageCount: 1),
            result =>
            [
                Require(!result.Result.IsSuccess, "Missing Add Language button should fail."),
                Require(result.Result.Message.Contains("add-language-button-missing", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("Add Language button was not detected", StringComparison.Ordinal),
                    "Failure should mention missing Add Language button."),
                RequireNoPublishActions(result.Actions)
            ],
            ConfigureArchive: archive =>
            {
                AddLocalizedInfo(archive, "zh-CN", "zh-CN");
            });

    private static Stage1DryRunScenario CreateForbiddenPublishButtonsAreNotClickedInMultilanguageScenario() =>
        CreateMultilanguageScenario(
            "forbidden_publish_buttons_are_not_clicked",
            "Add Language",
            selectedBefore: ["en", "zh-CN"],
            configuredLanguages: ["zh-CN"],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Forbidden Publish OCR on unrelated buttons should not be clicked."),
                RequireNoPublishActions(result.Actions)
            ],
            confirmOcrText: "Publish");

    private static Stage1DryRunScenario CreateMultilanguageEntryEditRowOcrOpensScenario() =>
        CreateMultilanguageEntryScenario(
            "multilanguage_entry_edit_row_ocr_opens",
            [new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.70, 0.34, 0.06, 0.9)],
            entryOcr: ["Multilanguage Settings"],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Multilanguage edit_row OCR should open the editor."),
                Require(result.Result.Message.Contains("selectedSource=edit_row_ocr", StringComparison.Ordinal), "Workflow should use edit_row_ocr."),
                RequireActions(result.Actions, "click:edit_row", "click:multilanguage_add_button"),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateMultilanguageEntryEnglishRowOpensScenario() =>
        CreateMultilanguageEntryScenario(
            "multilanguage_entry_english_row_opens",
            [new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.70, 0.34, 0.06, 0.9)],
            entryOcr: ["English"],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "English row should identify the Multilanguage entry."),
                Require(result.Result.Message.Contains("raw=\"English\"", StringComparison.Ordinal), "Workflow should log English OCR."),
                Require(result.Result.Message.Contains("clientLanguage=En", StringComparison.Ordinal), "Client language should still be detected inside editor."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateMultilanguageEntrySimplifiedChineseRowOpensScenario() =>
        CreateMultilanguageEntryScenario(
            "multilanguage_entry_simplified_chinese_row_opens",
            [new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.70, 0.34, 0.06, 0.9)],
            entryOcr: ["\u7b80\u4f53\u4e2d\u6587"],
            addButtonOcr: "\u6dfb\u52a0\u8bed\u8a00",
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Simplified Chinese row should identify the Multilanguage entry."),
                Require(result.Result.Message.Contains("clientLanguage=ZhCn", StringComparison.Ordinal), "Client language should be detected from Add Language button, not entry row."),
                Require(result.Result.Message.Contains("raw=\"\u7b80\u4f53\u4e2d\u6587\"", StringComparison.Ordinal), "Workflow should log Simplified Chinese OCR."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateMultilanguageEntryTextEditIconContextOpensScenario() =>
        CreateMultilanguageEntryScenario(
            "multilanguage_entry_text_edit_icon_context_opens",
            [new AutomationTarget(WorkflowTargetNames.TextEditIcon, 0.86, 0.70, 0.035, 0.04, 0.9)],
            entryOcr: ["Multilanguage"],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "text_edit_icon context OCR should open Multilanguage editor."),
                Require(result.Result.Message.Contains("selectedSource=text_edit_icon_context_ocr", StringComparison.Ordinal), "Workflow should use text_edit_icon_context_ocr."),
                RequireActions(result.Actions, "click:text_edit_icon"),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateMultilanguageEntryFirstClickSwallowedRetriesScenario() =>
        CreateMultilanguageEntryScenario(
            "multilanguage_entry_first_click_swallowed_retries",
            [new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.70, 0.34, 0.06, 0.9)],
            entryOcr: ["Stage Details > Multilanguage Settings"],
            previewFramesAfterClickBeforeEditor: 1,
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Second click should open Multilanguage editor after first click is swallowed."),
                Require(result.Result.Message.Contains("attempt=1 rawPage=PreviewEditor", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("pageAfterClick=PreviewEditor", StringComparison.Ordinal),
                    "First attempt should remain PreviewEditor."),
                Require(result.Result.Message.Contains("attempt=2 rawPage=MultiLanguageEditor", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("pageAfterClick=MultiLanguageEditor", StringComparison.Ordinal),
                    "Second attempt should enter MultilanguageEditor."),
                Require(result.Actions.Count(action => string.Equals(action, "click:edit_row", StringComparison.Ordinal)) >= 2, "Should click entry at least twice."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateMultilanguageEditorMisclassifiedAsTagDialogAcceptedByContextScenario()
    {
        var entryPreview = PreviewFrameWithMultilanguageEntryTargets(
        [
            new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.70, 0.34, 0.06, 0.9)
        ]);
        var editor = MultilanguageEditorMisclassifiedAsTagDialogFrame(rowCount: 2);
        var normalEditor = MultilanguageEditorFrame(rowCount: 2);
        var dialog = LanguageSelectDialogFrame("en", ["en"]);
        var refreshedDialog = LanguageSelectDialogFrame("en", ["en", "zh-CN"]);
        var frames = PreviewEditBaseFrames(entryPreview);
        frames.Add(editor);
        frames.Add(dialog);
        frames.Add(refreshedDialog);
        frames.Add(normalEditor);
        frames.AddRange(Enumerable.Range(0, 8).Select(_ => normalEditor));
        frames.Add(PreviewFrameWithMultilanguageEntryTargets([Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83)]));
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));

        return new Stage1DryRunScenario(
            "multilanguage_editor_raw_tagdialog_accepted_by_context",
            frames,
            result => [
                Require(result.Result.IsSuccess, "Raw TagDialog with Multilanguage table OCR should be accepted as MultiLanguageEditor."),
                Require(result.Result.Message.Contains("rawPage=TagDialog contextualPage=MultiLanguageEditor", StringComparison.Ordinal),
                    "Workflow should log raw and contextual page."),
                Require(result.Result.Message.Contains("accepted=True", StringComparison.Ordinal),
                    "Workflow should record contextual acceptance."),
                RequireActions(result.Actions, "click:edit_row", "click:primary_action_button"),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Multilanguage Settings"),
                OcrText("Stage Details > Multilanguage Settings"),
                OcrText("Language Stage Name Gameplay Details Stage Description Update Log"),
                OcrText("Add Language"),
                OcrText("Add Language"),
                OcrText("OK")
            ],
            ConfigureArchive: archive => AddLocalizedInfo(archive, "zh-CN", "zh-CN"));
    }

    private static Stage1DryRunScenario CreateAddLanguageDialogRawUnknownTitleAcceptedScenario() =>
        CreateGenericAddLanguageDialogContextScenario(
            "add_language_dialog_raw_unknown_title_accepted",
            GamePage.Unknown,
            "Add Language",
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            addButtonOcr: "Add Language",
            dialogOcrAfterClick:
            [
                OcrText("not a confirm button"),
                OcrText("Add Language"),
                OcrText("OK")
            ],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Raw Unknown Add Language dialog should be accepted by title OCR."),
                Require(result.Result.Message.Contains("rawPage=Unknown contextualPage=LanguageSelectDialog", StringComparison.Ordinal),
                    "Workflow should contextualize Unknown as LanguageSelectDialog."),
                Require(result.Result.Message.Contains("accepted by Add Language title OCR", StringComparison.Ordinal),
                    "Workflow should accept by title OCR."),
                RequireActions(result.Actions, "click:choice_option", "click:primary_action_button"),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateAddLanguageDialogRawTagDialogTitleAcceptedScenario() =>
        CreateGenericAddLanguageDialogContextScenario(
            "add_language_dialog_raw_tagdialog_title_accepted",
            GamePage.TagDialog,
            "Add Language",
            selectedBefore: ["en"],
            configuredLanguages: ["zh-CN"],
            addButtonOcr: "Add Language",
            dialogOcrAfterClick:
            [
                OcrText("not a confirm button"),
                OcrText("Add Language"),
                OcrText("OK")
            ],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Raw TagDialog Add Language dialog should be accepted by title OCR."),
                Require(result.Result.Message.Contains("rawPage=TagDialog contextualPage=LanguageSelectDialog", StringComparison.Ordinal),
                    "Workflow should contextualize TagDialog as LanguageSelectDialog."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateZhCnAddLanguageDialogRawUnknownAcceptedScenario() =>
        CreateGenericAddLanguageDialogContextScenario(
            "zh_cn_add_language_dialog_raw_unknown_title_accepted",
            GamePage.Unknown,
            "\u6dfb\u52a0\u8bed\u8a00",
            selectedBefore: ["zh-CN"],
            configuredLanguages: ["en"],
            addButtonOcr: "\u6dfb\u52a0\u8bed\u8a00",
            dialogOcrAfterClick:
            [
                OcrText("\u4e0d\u662f\u786e\u8ba4"),
                OcrText("\u6dfb\u52a0\u8bed\u8a00"),
                OcrText("\u786e\u8ba4")
            ],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "ZH-CN raw Unknown Add Language dialog should be accepted."),
                Require(result.Result.Message.Contains("clientLanguage=ZhCn", StringComparison.Ordinal), "Client language should be ZH-CN."),
                Require(result.Result.Message.Contains("rawPage=Unknown contextualPage=LanguageSelectDialog", StringComparison.Ordinal),
                    "Workflow should contextualize Unknown as LanguageSelectDialog."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateTagDialogNotAcceptedAsAddLanguageDialogScenario()
    {
        var preview = FrameWithTargets(
            GamePage.PreviewEditor,
            Target(WorkflowTargetNames.PreviewMultilanguageArea, 0.72, 0.70),
            Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
            Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83));
        var editor = MultilanguageEditorFrame(rowCount: 2);
        var notAddLanguage = GenericAddLanguageDialogFrame(GamePage.TagDialog, selectedCodes: ["en"], clientLanguage: "en");
        var frames = PreviewEditBaseFrames(preview);
        frames.Add(editor);
        frames.Add(notAddLanguage);
        frames.AddRange(Enumerable.Range(0, 8).Select(_ => notAddLanguage));

        return new Stage1DryRunScenario(
            "tag_dialog_not_accepted_as_add_language_dialog",
            frames,
            result =>
            [
                Require(!result.Result.IsSuccess, "Regular TagDialog should not be accepted as Add Language dialog."),
                Require(result.Result.Message.Contains("add-language-dialog-open-failed", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("Add Language dialog was not contextually confirmed", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("Add Language click reached unexpected page TagDialog", StringComparison.Ordinal),
                    "Failure should identify Add Language dialog contextual open failure."),
                RequireNoAction(result.Actions, "click:choice_option"),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Add Language"),
                OcrText("Action Adventure"),
                OcrText("Tags")
            ],
            ConfigureArchive: archive => AddLocalizedInfo(archive, "zh-CN", "zh-CN"));
    }

    private static Stage1DryRunScenario CreateAddLanguageClickFirstAttemptSuccessScenario() =>
        CreateAddLanguageClickScenario(
            "add_language_click_first_attempt_success",
            afterAddClickFrames:
            [
                GenericAddLanguageDialogFrame(GamePage.Unknown, ["en"], "en")
            ],
            workflowOcrAfterClient:
            [
                OcrText("not a confirm button"),
                OcrText("Add Language"),
                OcrText("OK")
            ],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "First Add Language click should open dialog."),
                Require(result.Result.Message.Contains("action=ClickAddLanguageAttempt attempt=1", StringComparison.Ordinal), "Should log click attempt 1."),
                Require(result.Result.Message.Contains("action=ClickAddLanguageReturned attempt=1", StringComparison.Ordinal), "Should log click returned."),
                Require(result.Result.Message.Contains("action=ClickAddLanguageSucceeded attempt=1", StringComparison.Ordinal), "Should succeed on attempt 1."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateAddLanguageClickFirstAttemptStaysEditorSecondSuccessScenario() =>
        CreateAddLanguageClickScenario(
            "add_language_click_first_attempt_stays_editor_second_success",
            afterAddClickFrames:
            [
                MultilanguageEditorFrame(2),
                MultilanguageEditorFrame(2),
                GenericAddLanguageDialogFrame(GamePage.Unknown, ["en"], "en")
            ],
            workflowOcrAfterClient:
            [
                OcrText("Add Language"),
                OcrText("Add Language"),
                OcrText("not a confirm button"),
                OcrText("Add Language"),
                OcrText("OK")
            ],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Second Add Language click should open dialog after first stays editor."),
                Require(result.Result.Message.Contains("action=ClickAddLanguageRetry attempt=1 reason=still_on_multilanguage_editor", StringComparison.Ordinal), "Should retry after staying on editor."),
                Require(result.Result.Message.Contains("action=ClickAddLanguageSucceeded attempt=2", StringComparison.Ordinal), "Should succeed on attempt 2."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateAddLanguageClickThrowsExceptionLoggedAndRetriedScenario() =>
        CreateAddLanguageClickScenario(
            "add_language_click_throws_exception_logged_and_retried",
            afterAddClickFrames:
            [
                MultilanguageEditorFrame(2),
                GenericAddLanguageDialogFrame(GamePage.Unknown, ["en"], "en")
            ],
            workflowOcrAfterClient:
            [
                OcrText("Add Language"),
                OcrText("not a confirm button"),
                OcrText("Add Language"),
                OcrText("OK")
            ],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Click exception should be logged and retried."),
                Require(result.Result.Message.Contains("action=ClickAddLanguageException attempt=1", StringComparison.Ordinal), "Should log click exception."),
                Require(result.Result.Message.Contains("action=ClickAddLanguageSucceeded attempt=2", StringComparison.Ordinal), "Should succeed on attempt 2."),
                RequireNoPublishActions(result.Actions)
            ],
            clickFailuresByCoordinate: new Dictionary<string, int>(StringComparer.Ordinal) { ["0.780,0.160"] = 1 });

    private static Stage1DryRunScenario CreateAddLanguageClickThreeAttemptsStillEditorFailsScenario() =>
        CreateAddLanguageClickScenario(
            "add_language_click_three_attempts_still_editor_fails",
            afterAddClickFrames:
            [
                MultilanguageEditorFrame(2),
                MultilanguageEditorFrame(2),
                MultilanguageEditorFrame(2),
                MultilanguageEditorFrame(2),
                MultilanguageEditorFrame(2),
                MultilanguageEditorFrame(2),
                MultilanguageEditorFrame(2)
            ],
            workflowOcrAfterClient:
            [
                OcrText("Add Language"),
                OcrText("Add Language")
            ],
            assertions: result =>
            [
                Require(!result.Result.IsSuccess, "Three Add Language click attempts staying on editor should fail."),
                Require(result.Result.Message.Contains("stage1-multilanguage-add-language-click-failed", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("Add Language click failed after 3 attempts", StringComparison.Ordinal),
                    "Failure should use Add Language click diagnostic."),
                Require(result.Result.Message.Contains("action=ClickAddLanguageFailed attempts=3", StringComparison.Ordinal), "Should log final click failure."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateAddLanguageRejectsSubmitOrPublishLikePrimaryButtonScenario() =>
        CreateAddLanguageClickScenario(
            "add_language_rejects_submit_or_publish_like_primary_button",
            afterAddClickFrames:
            [
                MultilanguageEditorFrame(2, includeAddButton: false, includePrimaryAddButton: true),
                MultilanguageEditorFrame(2, includeAddButton: false, includePrimaryAddButton: true),
                MultilanguageEditorFrame(2, includeAddButton: false, includePrimaryAddButton: true),
                MultilanguageEditorFrame(2, includeAddButton: false, includePrimaryAddButton: true),
                MultilanguageEditorFrame(2, includeAddButton: false, includePrimaryAddButton: true)
            ],
            workflowOcrAfterClient:
            [
                OcrText("Publish"),
                OcrText("Submit Now"),
                OcrText("Publish"),
                OcrText("Submit Now")
            ],
            assertions: result =>
            [
                Require(!result.Result.IsSuccess, "Publish-like primary button should not be accepted as Add Language."),
                Require(result.Result.Message.Contains("action=ClickAddLanguageButtonOcr", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("rejected=True", StringComparison.Ordinal),
                    "Workflow should log rejected Publish-like OCR."),
                Require(!result.Result.Message.Contains("action=ClickAddLanguageAttempt attempt=2 target=(primary_action_button", StringComparison.Ordinal) &&
                    !result.Result.Message.Contains("action=ClickAddLanguageAttempt attempt=3 target=(primary_action_button", StringComparison.Ordinal),
                    "Publish-like primary action button should not be clicked by Add Language retry."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateAddLanguageSelectMissingLanguageRefreshesDialogBeforeConfirmScenario()
    {
        var preview = FrameWithTargets(
            GamePage.PreviewEditor,
            Target(WorkflowTargetNames.PreviewMultilanguageArea, 0.72, 0.70),
            Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
            Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83));
        var editor = MultilanguageEditorFrame(rowCount: 2);
        var oldDialogWithoutConfirm = GenericAddLanguageDialogFrame(
            GamePage.Unknown,
            selectedCodes: ["en"],
            clientLanguage: "en",
            includeConfirmButton: false);
        var refreshedDialogWithConfirm = GenericAddLanguageDialogFrame(
            GamePage.LanguageSelectDialog,
            selectedCodes: ["en", "zh-CN"],
            clientLanguage: "en",
            includeConfirmButton: true);

        var frames = PreviewEditBaseFrames(preview);
        frames.Add(editor);
        frames.Add(oldDialogWithoutConfirm);
        frames.Add(refreshedDialogWithConfirm);
        frames.Add(editor);
        frames.AddRange(Enumerable.Range(0, 8).Select(_ => editor));
        frames.Add(PreviewFrameWithMultilanguageEntryTargets([Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83)]));
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));

        return new Stage1DryRunScenario(
            "add_language_select_missing_language_refreshes_dialog_before_confirm",
            frames,
            result =>
            [
                Require(result.Result.IsSuccess, "Refreshed Add Language dialog with OK should let flow succeed."),
                Require(result.Result.Message.Contains("action=RefreshAddLanguageDialogAfterSelect", StringComparison.Ordinal),
                    "Workflow should log dialog refresh after selecting missing language."),
                Require(result.Result.Message.Contains("language=zh-CN", StringComparison.Ordinal), "Refresh log should include selected language."),
                Require(result.Result.Message.Contains("confirmTarget=primary_action_button", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("confirmTarget=language_confirm_button", StringComparison.Ordinal),
                    "Refresh log should include confirm target from refreshed dialog."),
                RequireActions(result.Actions, "click:choice_option", "click:primary_action_button"),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Add Language"),
                OcrText("Add Language"),
                OcrText("OK"),
                OcrText("Add Language"),
                OcrText("OK"),
                OcrText("OK")
            ],
            ConfigureArchive: archive => AddLocalizedInfo(archive, "zh-CN", "zh-CN"));
    }

    private static Stage1DryRunScenario CreateGenericAddLanguageDialogContextScenario(
        string name,
        GamePage rawDialogPage,
        string titleText,
        IReadOnlyList<string> selectedBefore,
        IReadOnlyList<string> configuredLanguages,
        string addButtonOcr,
        IReadOnlyList<IReadOnlyList<WorkflowOcrTextResult>> dialogOcrAfterClick,
        Func<Stage1DryRunAssertionInput, IReadOnlyList<string?>> assertions)
    {
        var preview = FrameWithTargets(
            GamePage.PreviewEditor,
            Target(WorkflowTargetNames.PreviewMultilanguageArea, 0.72, 0.70),
            Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
            Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83));
        var editor = MultilanguageEditorFrame(rowCount: Math.Max(2, configuredLanguages.Count + 1));
        var dialog = GenericAddLanguageDialogFrame(
            rawDialogPage,
            selectedBefore,
            addButtonOcr.Contains("\u6dfb", StringComparison.Ordinal) ? "zh-CN" : "en");
        var frames = PreviewEditBaseFrames(preview);
        frames.Add(editor);
        frames.Add(dialog);
        frames.Add(LanguageSelectDialogFrame(addButtonOcr.Contains("\u6dfb", StringComparison.Ordinal) ? "zh-CN" : "en", selectedBefore));
        frames.Add(editor);
        frames.AddRange(Enumerable.Range(0, Math.Max(0, configuredLanguages.Count * 8)).Select(_ => editor));
        frames.Add(PreviewFrameWithMultilanguageEntryTargets([Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83)]));
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));

        var workflowOcr = new List<IReadOnlyList<WorkflowOcrTextResult>> { OcrText(addButtonOcr) };
        workflowOcr.AddRange(dialogOcrAfterClick);
        workflowOcr.Add(OcrText("OK"));

        return new Stage1DryRunScenario(
            name,
            frames,
            assertions,
            WorkflowOcrResults: workflowOcr,
            ConfigureArchive: archive =>
            {
                foreach (var language in configuredLanguages)
                    AddLocalizedInfo(archive, language, language);
            });
    }

    private static Stage1DryRunScenario CreateAddLanguageClickScenario(
        string name,
        IReadOnlyList<ScriptedPageFrame> afterAddClickFrames,
        IReadOnlyList<IReadOnlyList<WorkflowOcrTextResult>> workflowOcrAfterClient,
        Func<Stage1DryRunAssertionInput, IReadOnlyList<string?>> assertions,
        IReadOnlyDictionary<string, int>? clickFailuresByCoordinate = null)
    {
        var preview = FrameWithTargets(
            GamePage.PreviewEditor,
            Target(WorkflowTargetNames.PreviewMultilanguageArea, 0.72, 0.70),
            Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
            Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83));
        var editor = MultilanguageEditorFrame(rowCount: 2);
        var frames = PreviewEditBaseFrames(preview);
        frames.Add(editor);
        frames.AddRange(afterAddClickFrames);
        frames.Add(LanguageSelectDialogFrame("en", ["en"]));
        frames.Add(editor);
        frames.AddRange(Enumerable.Range(0, 8).Select(_ => editor));
        frames.Add(PreviewFrameWithMultilanguageEntryTargets([Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83)]));
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));

        var workflowOcr = new List<IReadOnlyList<WorkflowOcrTextResult>> { OcrText("Add Language") };
        workflowOcr.AddRange(workflowOcrAfterClient);
        workflowOcr.Add(OcrText("OK"));

        return new Stage1DryRunScenario(
            name,
            frames,
            assertions,
            WorkflowOcrResults: workflowOcr,
            ClickFailuresByCoordinate: clickFailuresByCoordinate,
            ConfigureArchive: archive => AddLocalizedInfo(archive, "zh-CN", "zh-CN"));
    }

    private static Stage1DryRunScenario CreateMultilanguageEntryRejectsOtherRowsScenario() =>
        new(
            "multilanguage_entry_rejects_tags_update_extra_video_stage_gameplay_rows",
            PreviewEditMultilanguageEntryFrames(
                [
                    new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.50, 0.34, 0.06, 0.9),
                    new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.57, 0.34, 0.06, 0.9),
                    new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.64, 0.34, 0.06, 0.9),
                    new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.71, 0.34, 0.06, 0.9),
                    new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.78, 0.34, 0.06, 0.9),
                    new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.85, 0.34, 0.06, 0.9)
                ],
                "Add Language",
                ["en"],
                ["en", "zh-CN"],
                fillLanguageCount: 1,
                appendEditorFlow: false),
            result =>
            [
                Require(!result.Result.IsSuccess, "Rejected rows should not be treated as Multilanguage."),
                RequireNoAction(result.Actions, "click:edit_row"),
                Require(result.Result.Message.Contains("Multilanguage Settings target was not detected", StringComparison.Ordinal), "Should fail with Multilanguage target missing."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Tags"),
                OcrText("Update Log"),
                OcrText("More Showcase Images"),
                OcrText("Showcase Videos"),
                OcrText("Stage Description"),
                OcrText("Gameplay Details"),
                OcrText("Tags"),
                OcrText("Update Log"),
                OcrText("More Showcase Images"),
                OcrText("Showcase Videos"),
                OcrText("Stage Description"),
                OcrText("Gameplay Details"),
                OcrText("Tags"),
                OcrText("Update Log"),
                OcrText("More Showcase Images"),
                OcrText("Showcase Videos"),
                OcrText("Stage Description"),
                OcrText("Gameplay Details"),
                OcrText("Tags"),
                OcrText("Update Log"),
                OcrText("More Showcase Images"),
                OcrText("Showcase Videos"),
                OcrText("Stage Description"),
                OcrText("Gameplay Details"),
                OcrText("Tags"),
                OcrText("Update Log"),
                OcrText("More Showcase Images"),
                OcrText("Showcase Videos"),
                OcrText("Stage Description"),
                OcrText("Gameplay Details")
            ],
            ConfigureArchive: archive => AddLocalizedInfo(archive, "zh-CN", "zh-CN"));

    private static Stage1DryRunScenario CreateMultilanguageEntryFoundAfterScrollScenario() =>
        CreateMultilanguageEntryScenario(
            "multilanguage_entry_found_after_scroll",
            [new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.70, 0.34, 0.06, 0.9)],
            entryOcr: ["Multilanguage Settings"],
            initialPreviewTargets: [],
            assertions: result =>
            [
                Require(result.Result.IsSuccess, "Multilanguage entry should be found after scrolling."),
                RequireActions(result.Actions, "scroll:preview_editor:-480", "click:edit_row"),
                Require(result.Result.Message.Contains("scrollAttempt=1", StringComparison.Ordinal), "Workflow should log scroll attempt."),
                RequireNoPublishActions(result.Actions)
            ]);

    private static Stage1DryRunScenario CreateMultilanguageEntryAmbiguousEditRowsFailsScenario() =>
        new(
            "multilanguage_entry_ambiguous_edit_rows_fails",
            PreviewEditMultilanguageEntryFrames(
                [
                    new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.62, 0.34, 0.06, 0.9),
                    new AutomationTarget(WorkflowTargetNames.EditRow, 0.72, 0.70, 0.34, 0.06, 0.9)
                ],
                "Add Language",
                ["en"],
                ["en", "zh-CN"],
                fillLanguageCount: 1),
            result =>
            [
                Require(!result.Result.IsSuccess, "Multiple Multilanguage edit_row OCR matches should fail safely."),
                Require(result.Result.Message.Contains("matched multiple candidates", StringComparison.Ordinal), "Failure should mention ambiguous edit rows."),
                RequireNoAction(result.Actions, "click:edit_row"),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Multilanguage Settings"),
                OcrText("English")
            ],
            ConfigureArchive: archive => AddLocalizedInfo(archive, "zh-CN", "zh-CN"));

    private static Stage1DryRunScenario CreateExtraImagesMediaAddTileOcrUploadsScenario() =>
        new(
            "preview_extra_images_media_add_tile_ocr_uploads",
            PreviewEditMediaAddTileFrames(mediaTileCount: 1),
            result =>
            [
                Require(result.Result.IsSuccess, "Extra image should upload through media_add_tile OCR."),
                RequireActions(result.Actions, "click:media_add_tile", "file_dialog_select:" + ExistingDryRunResourcePath("extra-image.png")),
                Require(result.Result.Message.Contains("field=ExtraImages[0]", StringComparison.Ordinal), "Workflow should mention ExtraImages[0]."),
                Require(result.Result.Message.Contains("selectedSource=media_add_tile_ocr", StringComparison.Ordinal), "Workflow should show media_add_tile_ocr source."),
                Require(result.Result.Message.Contains("raw=\"More Showcase Images\"", StringComparison.Ordinal), "Workflow should include media tile OCR."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("More Showcase Images")
            ],
            ConfigureArchive: archive =>
            {
                archive.ExtraImagePaths.Add(ExistingDryRunResourcePath("extra-image.png"));
            });

    private static Stage1DryRunScenario CreateVideoMediaAddTileOcrUploadsScenario() =>
        new(
            "preview_video_media_add_tile_ocr_uploads",
            PreviewEditMediaAddTileFrames(mediaTileCount: 1),
            result =>
            [
                Require(result.Result.IsSuccess, "Video should upload through media_add_tile OCR."),
                RequireActions(result.Actions, "click:media_add_tile", "file_dialog_select:" + ExistingDryRunResourcePath("showcase-video.mp4")),
                Require(result.Result.Message.Contains("field=Video", StringComparison.Ordinal), "Workflow should mention Video."),
                Require(result.Result.Message.Contains("selectedSource=media_add_tile_ocr", StringComparison.Ordinal), "Workflow should show media_add_tile_ocr source."),
                Require(result.Result.Message.Contains("raw=\"Showcase Videos\"", StringComparison.Ordinal), "Workflow should include video tile OCR."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Showcase Videos")
            ],
            ConfigureArchive: archive =>
            {
                archive.VideoPath = ExistingDryRunResourcePath("showcase-video.mp4");
            });

    private static Stage1DryRunScenario CreateMediaAddTilesDoNotCrossMatchScenario() =>
        new(
            "preview_media_add_tiles_do_not_cross_match",
            PreviewEditMediaAddTileFrames(mediaTileCount: 2),
            result =>
            [
                Require(result.Result.IsSuccess, "Extra image and video should each use their own media tile."),
                RequireActions(result.Actions,
                    "file_dialog_select:" + ExistingDryRunResourcePath("cross-extra.png"),
                    "file_dialog_select:" + ExistingDryRunResourcePath("cross-video.mp4")),
                Require(result.Result.Message.Contains("field=ExtraImages[0] action=MediaAddTileContextOcr candidateIndex=0", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("field=ExtraImages[0] action=MediaAddTileContextOcr candidateIndex=1", StringComparison.Ordinal),
                    "ExtraImages should inspect both media tiles."),
                Require(result.Result.Message.Contains("field=Video action=MediaAddTileContextOcr candidateIndex=0", StringComparison.Ordinal) &&
                    result.Result.Message.Contains("field=Video action=MediaAddTileContextOcr candidateIndex=1", StringComparison.Ordinal),
                    "Video should inspect both media tiles."),
                RequireNoPublishActions(result.Actions)
            ],
            WorkflowOcrResults:
            [
                OcrText("Showcase Videos"),
                OcrText("More Showcase Images"),
                OcrText("More Showcase Images"),
                OcrText("Showcase Videos")
            ],
            ConfigureArchive: archive =>
            {
                archive.ExtraImagePaths.Add(ExistingDryRunResourcePath("cross-extra.png"));
                archive.VideoPath = ExistingDryRunResourcePath("cross-video.mp4");
            });

    private static Stage1DryRunScenario CreateCoverFileDialogFailureFailsScenario() =>
        new(
            "cover_file_dialog_failure_fails",
            PreviewEditFrames(includeTags: false, includeSubmit: true),
            result =>
            [
                Require(!result.Result.IsSuccess, "Cover file dialog failure should fail."),
                Require(result.Result.Message.Contains("Fake file dialog failure", StringComparison.Ordinal), "Failure should include file dialog error."),
                RequireNoPublishActions(result.Actions)
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelName = "Cover Failure";
                archive.LevelCoverPath = @"C:\Temp\cover.png";
                archive.GameDescription = "Gameplay text";
            },
            FileDialogFails: true);

    private static Stage1DryRunScenario CreatePreviewCoverUsesLeftPreviewCoverTargetWithoutScrollScenario() =>
        new(
            "preview_cover_uses_left_preview_cover_target_without_scroll",
            PreviewEditFrames(includeTags: false, includeSubmit: true),
            result =>
            [
                Require(result.Result.IsSuccess, "Cover upload should succeed with left preview_cover_image_area."),
                RequireActions(result.Actions, "click:preview_cover_image_area"),
                RequireNoActionContaining(result.Actions, "scroll:preview_editor"),
                Require(!result.Result.Message.Contains("field=Cover action=ScrollPreviewEditor", StringComparison.Ordinal),
                    "Cover must not log ScrollPreviewEditor."),
                Require(result.Result.Message.Contains("field=Cover searchArea=LeftPanel scrollAllowed=false result=targetFound", StringComparison.Ordinal),
                    "Workflow log should show left-panel cover target selection."),
                RequireNoPublishActions(result.Actions)
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelCoverPath = ExistingDryRunResourcePath("cover-left-preview.png");
            });

    private static Stage1DryRunScenario CreatePreviewCoverUsesLeftImageUploadAreaWithoutScrollScenario() =>
        new(
            "preview_cover_uses_left_image_upload_area_without_scroll",
            PreviewEditFrames(
                includeTags: false,
                includeSubmit: true,
                coverTargetNames: [WorkflowTargetNames.ImageUploadArea]),
            result =>
            [
                Require(result.Result.IsSuccess, "Cover upload should succeed with left image_upload_area."),
                RequireActions(result.Actions, "click:image_upload_area"),
                RequireNoActionContaining(result.Actions, "scroll:preview_editor"),
                Require(!result.Result.Message.Contains("field=Cover action=ScrollPreviewEditor", StringComparison.Ordinal),
                    "Cover must not log ScrollPreviewEditor."),
                Require(result.Result.Message.Contains("field=Cover searchArea=LeftPanel scrollAllowed=false result=targetFound", StringComparison.Ordinal),
                    "Workflow log should show left-panel image_upload_area selection."),
                RequireNoPublishActions(result.Actions)
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelCoverPath = ExistingDryRunResourcePath("cover-left-image-upload.png");
            });

    private static Stage1DryRunScenario CreatePreviewCoverMissingFailsWithoutScrollScenario() =>
        new(
            "preview_cover_missing_fails_without_scroll",
            PreviewEditFrames(
                includeTags: false,
                includeSubmit: true,
                coverTargetNames: []),
            result =>
            [
                Require(!result.Result.IsSuccess, "Missing cover target should fail."),
                Require(result.Result.Message.Contains("Cover", StringComparison.Ordinal), "Failure should mention Cover."),
                Require(result.Result.Message.Contains("left fixed field", StringComparison.OrdinalIgnoreCase), "Failure should mention left fixed field."),
                Require(result.Result.Message.Contains("must not scroll", StringComparison.OrdinalIgnoreCase), "Failure should mention must not scroll."),
                RequireNoActionContaining(result.Actions, "scroll:preview_editor"),
                Require(!result.Result.Message.Contains("field=Cover action=ScrollPreviewEditor", StringComparison.Ordinal),
                    "Cover failure must not log ScrollPreviewEditor."),
                RequireNoPublishActions(result.Actions)
            ],
            ConfigureArchive: archive =>
            {
                archive.LevelCoverPath = ExistingDryRunResourcePath("cover-missing.png");
            });

    private static Stage1DryRunScenario CreateLoginStartClickStartThenWorldMainScenario() =>
        new(
            "login_start_click_start_then_world_main",
            [
                Frame(GamePage.LoginStart, WorkflowTargetNames.LoginStartGameButton),
                .. StandardSuccessFramesFromWorldMain()
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "LoginStart should click start and continue from WorldMain."),
                RequireActions(result.Actions, "click:login_start_game_button", "press_key:Escape", "click:qianxing_realm_entry"),
                Require(result.Result.Message.Contains("step=EnterGameFromLoginStart", StringComparison.Ordinal), "Workflow log should include LoginStart entry step.")
            ]);

    private static Stage1DryRunScenario CreateLoginStartToDownloadingToTransitionToWorldMainScenario() =>
        new(
            "login_start_to_downloading_to_transition_to_world_main",
            [
                Frame(GamePage.LoginStart, WorkflowTargetNames.LoginStartGameButton),
                Frame(GamePage.LoginDownloadingResources),
                Frame(GamePage.Transition),
                Frame(GamePage.Unknown),
                .. StandardSuccessFramesFromWorldMain()
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "LoginStart should tolerate downloading/transition/unknown before WorldMain."),
                RequireActions(result.Actions, "click:login_start_game_button", "click:qianxing_realm_entry"),
                Require(result.Result.Message.Contains("page=LoginDownloadingResources", StringComparison.Ordinal), "Workflow log should show downloading page."),
                Require(result.Result.Message.Contains("page=Transition", StringComparison.Ordinal), "Workflow log should show transition page.")
            ]);

    private static Stage1DryRunScenario CreateLoginEnterClickEnterThenWorldMainScenario() =>
        new(
            "login_enter_click_enter_then_world_main",
            [
                Frame(GamePage.LoginEnter, WorkflowTargetNames.LoginClickEnter),
                .. StandardSuccessFramesFromWorldMain()
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "LoginEnter should click enter and continue from WorldMain."),
                RequireActions(result.Actions, "click:login_click_enter", "click:qianxing_realm_entry"),
                Require(result.Result.Message.Contains("action=ClickEnter", StringComparison.Ordinal), "Workflow log should show LoginEnter click.")
            ]);

    private static Stage1DryRunScenario CreateLoginServerConfirmConfirmThenWorldMainScenario() =>
        new(
            "login_server_confirm_confirm_then_world_main",
            [
                Frame(GamePage.LoginServerConfirm, WorkflowTargetNames.LoginServerConfirmButton, WorkflowTargetNames.ServerCancelButton),
                Frame(GamePage.Transition),
                .. StandardSuccessFramesFromWorldMain()
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "LoginServerConfirm should click confirm and continue to WorldMain."),
                RequireActions(result.Actions, "click:server_confirm_button", "click:qianxing_realm_entry"),
                RequireNoAction(result.Actions, "click:server_cancel_button"),
                Require(result.Result.Message.Contains("action=ConfirmServer", StringComparison.Ordinal), "Workflow log should show server confirm.")
            ]);

    private static Stage1DryRunScenario CreateConcreteServerStillTodoFailureScenario() =>
        new(
            "concrete_server_incomplete_script_fails_safely",
            [Frame(GamePage.LoginStart, WorkflowTargetNames.LoginStartGameButton)],
            result =>
            [
                Require(!result.Result.IsSuccess, "Concrete server dry-run should fail safely with incomplete scripted frames."),
                RequireActions(result.Actions, "click:login_start_game_button"),
                Require(result.Result.Message.Contains("Timed out", StringComparison.Ordinal) ||
                        result.Result.Message.Contains("timed out", StringComparison.Ordinal) ||
                        result.Result.Message.Contains("stayed on LoginStart", StringComparison.Ordinal),
                    "Failure should be a bounded wait failure when the scripted frames never advance."),
                RequireNoAction(result.Actions, "click:login_start_game_fallback"),
                RequireNoAction(result.Actions, "click:server_cancel_button")
            ],
            Server: UploadServer.Asia);

    private static Stage1DryRunScenario CreateLoginStartMissingStartButtonUsesFallbackScenario() =>
        new(
            "login_start_missing_start_button_fails_safely",
            [
                Frame(GamePage.LoginStart),
                .. StandardSuccessFramesFromWorldMain()
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "LoginStart without a detected start button should fail safely."),
                RequireNoAction(result.Actions, "click:login_start_game_button"),
                Require(result.Result.Message.Contains("could not locate a valid YOLO target login_start_game_button", StringComparison.Ordinal), "Failure should mention missing login_start_game_button target."),
                RequireNoAction(result.Actions, "click:server_cancel_button")
            ]);

    private static Stage1DryRunScenario CreateLoginStartFallbackMaxAttemptsFailsScenario() =>
        new(
            "login_start_missing_start_button_still_fails_safely",
            [Frame(GamePage.LoginStart)],
            result =>
            [
                Require(!result.Result.IsSuccess, "LoginStart should fail safely without a detected start button."),
                Require(result.Result.Message.Contains("could not locate a valid YOLO target login_start_game_button", StringComparison.Ordinal), "Failure should mention missing login_start_game_button target."),
                RequireNoAction(result.Actions, "click:login_start_game_button"),
                RequireNoAction(result.Actions, "click:server_cancel_button")
            ]);

    private static Stage1DryRunScenario CreateFirstFailedThenSandboxThenSuccessScenario() =>
        new(
            "first_failed_then_sandbox_then_success",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                ..CooldownLocalArchiveFrames(2),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after sandbox warmup."),
                RequireActions(result.Actions,
                    "click:upload_failed_close_button",
                    "sandbox_wait_stable:5.0s",
                    "click:upload_new_button",
                    "click:uploaded_archive_dismiss_area"),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 2, "Upload menu item should be clicked twice.")
            ]);

    private static Stage1DryRunScenario CreateSecondFailedScenario() =>
        new(
            "second_failed",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                ..CooldownLocalArchiveFrames(2),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail."),
                Require(result.Result.Message.Contains("Second Stage1 upload failed", StringComparison.Ordinal), "Failure should mention second upload failure."),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 2, "Upload menu item should be clicked twice.")
            ]);

    private static Stage1DryRunScenario CreateMissingTargetScenario() =>
        new(
            "missing_target",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail."),
                Require(result.Result.Message.Contains("target 'qianxing_realm_entry' was not detected", StringComparison.Ordinal), "Failure should mention missing qianxing_realm_entry target."),
                RequireNoAction(result.Actions, "click:import_archive_button"),
                RequireNoAction(result.Actions, "click:local_archive_more_button")
            ]);

    private static Stage1DryRunScenario CreateInvalidStartPageScenario() =>
        new(
            "invalid_start_page",
            [
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "LocalArchive start should now be accepted for Current mode."),
                Require(result.Result.Message.Contains("startPage=LocalArchive", StringComparison.Ordinal), "Workflow log should mention LocalArchive start."),
                RequireNoAction(result.Actions, "click:qianxing_realm_entry")
            ]);

    private static Stage1DryRunScenario CreateFirstUploadTransientLocalArchiveThenUploadFailedScenario() =>
        new(
            "first_upload_transient_local_archive_then_upload_failed",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                ..CooldownLocalArchiveFrames(30),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after transient LocalArchive then UploadFailed."),
                RequireActions(result.Actions, "click:upload_failed_close_button", "sandbox_wait_stable:5.0s"),
                Require(!result.Result.Message.Contains("LocalArchive accepted as sandbox-return candidate", StringComparison.Ordinal), "Transient LocalArchive must not be accepted as sandbox-return candidate."),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 2, "Second upload should still run.")
            ],
            ClockAdvancePerDelay: TimeSpan.FromMilliseconds(500));

    private static Stage1DryRunScenario CreateFirstUploadStableLocalArchiveWithoutMinDurationScenario() =>
        new(
            "first_upload_stable_local_archive_without_min_duration",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                ..CooldownLocalArchiveFrames(30),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed by falling through to UploadFailed branch."),
                RequireActions(result.Actions, "click:upload_failed_close_button", "sandbox_wait_stable:5.0s"),
                Require(!result.Result.Message.Contains("LocalArchive accepted as sandbox-return candidate", StringComparison.Ordinal), "Three LocalArchive observations under 3 seconds must not be accepted.")
            ],
            ClockAdvancePerDelay: TimeSpan.FromMilliseconds(500));

    private static Stage1DryRunScenario CreateFirstUploadStableLocalArchiveAfterMinDurationScenario() =>
        new(
            "first_upload_stable_local_archive_after_min_duration",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                ..CooldownLocalArchiveFrames(8),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after stable LocalArchive sandbox return."),
                Require(result.Result.Message.Contains("LocalArchive accepted as sandbox-return candidate", StringComparison.Ordinal), "Result message should record stable LocalArchive acceptance."),
                RequireActions(result.Actions, "sandbox_wait_stable:5.0s"),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 2, "Second upload should run after sandbox readiness.")
            ],
            ClockAdvancePerDelay: TimeSpan.FromSeconds(2));

    private static Stage1DryRunScenario CreateSecondUploadLocalArchiveNeverAcceptedScenario() =>
        new(
            "second_upload_local_archive_never_accepted",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                ..CooldownLocalArchiveFrames(2),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when second upload only reports LocalArchive."),
                Require(result.Result.Message.Contains("timed out during upload attempt 'SecondUpload'", StringComparison.Ordinal), "Failure should mention SecondUpload timeout."),
                Require(result.Result.Message.Contains("ignored unexpected LocalArchive during final upload wait", StringComparison.Ordinal), "Workflow log should record ignored LocalArchive during final upload wait.")
            ],
            ClockAdvancePerDelay: TimeSpan.FromSeconds(30));

    private static Stage1DryRunScenario CreateImportSuccessMoreButtonVisibleScenario() =>
        new(
            "import_success_more_button_visible",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed when local_archive_more_button is already visible."),
                RequireActions(result.Actions, "click:local_archive_more_button", "click:local_archive_upload_menu_item"),
                RequireActions(result.Actions, "move:local_archive_hover_clear_area", "move:local_archive_card_action_area"),
                Require(result.Result.Message.Contains("action=IgnorePreExistingMoreButtons", StringComparison.Ordinal), "Workflow log should show pre-existing more buttons were ignored.")
            ]);

    private static Stage1DryRunScenario CreateImportSuccessNeedHoverThenMoreButtonVisibleScenario() =>
        new(
            "import_success_need_hover_then_more_button_visible",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after hovering the OCR-confirmed card action area."),
                RequireActions(result.Actions, "move:local_archive_card_action_area", "click:local_archive_more_button"),
                Require(result.Result.Message.Contains("selectedBy=ocr_card_match", StringComparison.Ordinal), "Workflow log should mention OCR card matching.")
            ]);

    private static Stage1DryRunScenario CreateImportSuccessNoCardOrMoreButtonFailsSafelyScenario() =>
        new(
            "import_success_no_card_or_more_button_fails_safely",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when neither card nor more button is detected."),
                Require(result.Result.Message.Contains("could not confirm that the imported run archive appeared in LocalArchive", StringComparison.Ordinal), "Failure should mention imported run archive visibility."),
                RequireNoAction(result.Actions, "click:local_archive_more_button"),
                RequireNoAction(result.Actions, "click:local_archive_upload_menu_item")
            ],
            OcrResults: RepeatOcrWithMatchAt(-1, "none", total: 12));

    private static Stage1DryRunScenario CreateMultipleCardsTargetCodeMatchedScenario() =>
        new(
            "multiple_cards_target_code_matched",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed when OCR uniquely matches the target card code among multiple cards."),
                Require(result.Result.Message.Contains("selectedBy=ocr_card_match", StringComparison.Ordinal), "Workflow log should mention OCR card matching."),
                RequireActions(result.Actions, "click:local_archive_more_button")
            ],
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 1, 15 }, "multiple_cards_target_code_matched_DRY1", total: 28));

    private static Stage1DryRunScenario CreateMultipleCardsNoOcrMatchFailsScenario() =>
        new(
            "multiple_cards_no_ocr_match_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveCard)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when OCR does not match the current run code."),
                Require(result.Result.Message.Contains("could not confirm that the imported run archive appeared in LocalArchive", StringComparison.Ordinal), "Failure should mention imported run archive visibility."),
                RequireNoAction(result.Actions, "click:local_archive_more_button")
            ],
            OcrResults: RepeatOcrWithMatchAt(-1, "none", total: 14));

    private static Stage1DryRunScenario CreateMultipleCardsAmbiguousOcrMatchFailsScenario() =>
        new(
            "multiple_cards_ambiguous_ocr_match_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveCard)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when OCR matches multiple cards."),
                Require(result.Result.Message.Contains("multiple LocalArchive cards matching", StringComparison.Ordinal), "Failure should mention ambiguous OCR matches."),
                RequireNoAction(result.Actions, "click:local_archive_more_button")
            ],
            OcrResults: RepeatOcrWithMatches(new HashSet<int> { 0, 1 }, total: 14));

    private static Stage1DryRunScenario CreateCardOcrMatchThenHoverRevealsMoreButtonScenario() =>
        new(
            "card_ocr_match_then_hover_reveals_more_button",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed when OCR match then hover reveals more button."),
                RequireActions(result.Actions, "move:local_archive_card_action_area", "click:local_archive_more_button"),
                Require(result.Result.Message.Contains("selectedBy=ocr_card_match", StringComparison.Ordinal), "Workflow log should mention OCR card matching.")
            ]);

    private static Stage1DryRunScenario CreateNoYoloCardLayoutCandidateOcrMatchesThenMoreVisibleScenario() =>
        new(
            "no_yolo_card_layout_candidate_ocr_matches_then_more_visible",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), LayoutMiddleCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), LayoutMiddleCardMoreButton()),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed when layout candidate OCR uniquely matches and more button appears."),
                RequireActions(result.Actions, "move:local_archive_card_action_area", "click:local_archive_more_button"),
                Require(result.Result.Message.Contains("selectedBy=ocr_card_match", StringComparison.Ordinal), "Workflow log should mention OCR card matching.")
            ],
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 1, 13 }, "layout_DRY1", total: 24));

    private static Stage1DryRunScenario CreateNoYoloCardLayoutCandidateNoOcrMatchFailsScenario() =>
        new(
            "no_yolo_card_layout_candidate_no_ocr_match_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when layout candidates do not OCR-match the run code."),
                Require(result.Result.Message.Contains("could not confirm that the imported run archive appeared in LocalArchive", StringComparison.Ordinal), "Failure should mention imported run archive visibility."),
                RequireNoAction(result.Actions, "click:local_archive_more_button")
            ],
            OcrResults: RepeatOcrWithMatchAt(-1, "none", total: 12));

    private static Stage1DryRunScenario CreateNoYoloCardLayoutCandidateAmbiguousOcrMatchFailsScenario() =>
        new(
            "no_yolo_card_layout_candidate_ambiguous_ocr_match_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when multiple layout candidates OCR-match the run code."),
                Require(result.Result.Message.Contains("multiple LocalArchive cards matching", StringComparison.Ordinal), "Failure should mention ambiguous layout OCR matches."),
                RequireNoAction(result.Actions, "click:local_archive_more_button")
            ],
            OcrResults: RepeatOcrWithMatches(new HashSet<int> { 1, 2 }, total: 12));

    private static Stage1DryRunScenario CreateLayoutCandidateMatchButMoreButtonNotRevealedFailsScenario() =>
        new(
            "layout_candidate_match_but_more_button_not_revealed_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when OCR-matched layout card does not reveal more button."),
                Require(result.Result.Message.Contains("could not locate a safe local_archive_more_button", StringComparison.Ordinal), "Failure should mention safe more button location failure."),
                RequireActions(result.Actions, "opencv_more_button:False"),
                RequireActions(result.Actions, "move:local_archive_card_action_area"),
                RequireNoAction(result.Actions, "click:local_archive_more_button")
            ],
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 1, 13 }, "layout_DRY1", total: 24));

    private static Stage1DryRunScenario CreateYoloPartialMissesTargetLayoutFindsRunCodeScenario() =>
        new(
            "yolo_partial_misses_target_layout_finds_run_code",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), LayoutMiddleCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), LayoutMiddleCardMoreButton()),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed when layout fills a YOLO card detection hole."),
                Require(result.Result.Message.Contains("candidateSource=layout", StringComparison.Ordinal), "Import visibility should be confirmed by layout candidate."),
                Require(result.Result.Message.Contains("selectedBy=ocr_card_match", StringComparison.Ordinal), "Workflow log should mention OCR-confirmed card selection."),
                RequireActions(result.Actions, "move:local_archive_card_action_area", "click:local_archive_more_button")
            ],
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 1, 15 }, "layout_DRY1", total: 27));

    private static Stage1DryRunScenario CreateYoloAndLayoutSameTargetDedupNotAmbiguousScenario() =>
        new(
            "yolo_and_layout_same_target_dedup_not_ambiguous",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should not treat overlapping YOLO/layout candidates as ambiguous."),
                Require(!result.Result.Message.Contains("multiple LocalArchive cards matching", StringComparison.Ordinal), "Overlapping candidates should be de-duplicated."),
                RequireActions(result.Actions, "click:local_archive_more_button")
            ],
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 0, 13 }, "target_DRY1", total: 26));

    private static Stage1DryRunScenario CreateYoloAndLayoutDifferentTargetsSameCodeStillAmbiguousScenario() =>
        new(
            "yolo_and_layout_different_targets_same_code_still_ambiguous",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when distant YOLO/layout candidates match the same run code."),
                Require(result.Result.Message.Contains("multiple LocalArchive cards matching", StringComparison.Ordinal), "Failure should mention ambiguous OCR matches."),
                RequireNoAction(result.Actions, "click:local_archive_more_button")
            ],
            OcrResults: RepeatOcrWithMatches(new HashSet<int> { 0, 2 }, total: 13));

    private static Stage1DryRunScenario CreateUploadedArchiveLaterDismissThenStableWaitThenEscWorldMainScenario() =>
        new(
            "uploaded_archive_later_dismiss_then_stable_wait_then_esc_world_main",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(
                    GamePage.UploadedArchive,
                    WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon,
                    WorkflowTargetNames.UploadedImageAssetsButton,
                    WorkflowTargetNames.UploadedArchiveLaterButtonAlias),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon, WorkflowTargetNames.UploadedImageAssetsButton),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after stable UploadedArchive wait and one return ESC."),
                RequireActions(result.Actions, "click:upload_failed_close_button"),
                Require(result.Result.Message.Contains("UploadedArchiveStableAfterDismiss", StringComparison.Ordinal), "Workflow log should mention stable wait after dismiss."),
                Require(result.Actions.Count(action => action == "press_key:Escape") == 2, "Should press ESC once for MainMenu and once for UploadedArchive return."),
                RequireNoAction(result.Actions, "click:submit_review_button")
            ]);

    private static Stage1DryRunScenario CreateUploadedArchiveFirstEscStillUploadedSecondEscWorldMainScenario() =>
        new(
            "uploaded_archive_first_esc_still_uploaded_second_esc_world_main",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed when second ESC leaves UploadedArchive."),
                Require(result.Result.Message.Contains("escAttempt=1 result=StillUploadedArchive retry=True", StringComparison.Ordinal), "Workflow log should record first ESC retry."),
                Require(result.Actions.Count(action => action == "press_key:Escape") == 3, "Should press ESC once for MainMenu plus two UploadedArchive return attempts."),
                Require(result.Result.Message.Contains("finalPage=WorldMain", StringComparison.Ordinal), "Final page should be WorldMain.")
            ]);

    private static Stage1DryRunScenario CreateUploadedArchiveTwoEscStillUploadedFailsScenario() =>
        new(
            "uploaded_archive_two_esc_still_uploaded_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when two return ESC attempts stay on UploadedArchive."),
                Require(result.Result.Message.Contains("could not leave UploadedArchive after ESC retries", StringComparison.Ordinal), "Failure should mention UploadedArchive return timeout."),
                Require(result.Actions.Count(action => action == "press_key:Escape") == 3, "Should press ESC once for MainMenu plus two return attempts."),
                RequireNoAction(result.Actions, "click:submit_review_button")
            ]);

    private static Stage1DryRunScenario CreateUploadedArchiveLowConfidenceAfterEscTreatedAsArchiveEditorScenario() =>
        new(
            "uploaded_archive_low_confidence_after_esc_treated_as_archive_editor",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                LowConfidenceFrame(GamePage.UploadedArchive, 0.34, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                Frame(GamePage.ArchiveEditorMenu, WorkflowTargetNames.ArchiveEditorExitButton),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should treat low-confidence UploadedArchive as possible ArchiveEditor."),
                Require(result.Result.Message.Contains("LowConfidenceUploadedArchivePossibleArchiveEditor", StringComparison.Ordinal), "Workflow log should mention the low-confidence return branch."),
                RequireActions(result.Actions, "click:archive_editor_exit_button"),
                Require(result.Result.Message.Contains("finalPage=WorldMain", StringComparison.Ordinal), "Final page should be WorldMain.")
            ]);

    private static Stage1DryRunScenario CreateUploadedArchiveEscToArchiveEditorMenuThenExitWorldMainScenario() =>
        new(
            "uploaded_archive_esc_to_archive_editor_menu_then_exit_world_main",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                Frame(GamePage.ArchiveEditorMenu, WorkflowTargetNames.ArchiveEditorExitButton),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should exit through ArchiveEditorMenu."),
                RequireActions(result.Actions, "click:archive_editor_exit_button"),
                Require(result.Result.Message.Contains("action=ClickExit", StringComparison.Ordinal), "Workflow log should mention archive editor exit."),
                Require(result.Result.Message.Contains("finalPage=WorldMain", StringComparison.Ordinal), "Final page should be WorldMain.")
            ]);

    private static Stage1DryRunScenario CreateUploadedArchiveEscToUnknownThenSecondEscArchiveEditorMenuThenExitWorldScenario() =>
        new(
            "uploaded_archive_esc_to_unknown_then_second_esc_archive_editor_menu_then_exit_world",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                Frame(GamePage.Unknown),
                Frame(GamePage.ArchiveEditorMenu, WorkflowTargetNames.ArchiveEditorExitButton),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should recover from Unknown possible ArchiveEditor by pressing ESC again."),
                Require(result.Result.Message.Contains("UnknownPossibleArchiveEditor", StringComparison.Ordinal), "Workflow log should mention Unknown possible ArchiveEditor branch."),
                RequireActions(result.Actions, "click:archive_editor_exit_button"),
                Require(result.Actions.Count(action => action == "press_key:Escape") == 3, "Should press ESC once for MainMenu, once from UploadedArchive, and once from Unknown editor."),
                Require(result.Result.Message.Contains("finalPage=WorldMain", StringComparison.Ordinal), "Final page should be WorldMain.")
            ],
            ClockAdvancePerDelay: TimeSpan.FromSeconds(30));

    private static Stage1DryRunScenario CreateUploadedArchiveEscToUnknownSecondEscStillUnknownFailsScenario() =>
        new(
            "uploaded_archive_esc_to_unknown_second_esc_still_unknown_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                Frame(GamePage.Unknown),
                Frame(GamePage.Unknown)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when second ESC still leaves Unknown."),
                Require(result.Result.Message.Contains("could not open ArchiveEditorMenu from an Unknown possible ArchiveEditor page", StringComparison.Ordinal), "Failure should mention Unknown editor menu timeout."),
                RequireNoAction(result.Actions, "click:archive_editor_exit_button"),
                RequireNoAction(result.Actions, "click:submit_review_button")
            ],
            ClockAdvancePerDelay: TimeSpan.FromSeconds(30));

    private static Stage1DryRunScenario CreateUploadedArchiveEscToArchiveEditorMenuStillWorksScenario() =>
        new(
            "uploaded_archive_esc_to_archive_editor_menu_still_works",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                Frame(GamePage.ArchiveEditorMenu, WorkflowTargetNames.ArchiveEditorExitButton),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should still handle direct ArchiveEditorMenu return."),
                RequireActions(result.Actions, "click:archive_editor_exit_button"),
                Require(result.Result.Message.Contains("finalPage=WorldMain", StringComparison.Ordinal), "Final page should be WorldMain.")
            ]);

    private static Stage1DryRunScenario CreateReturnUploadedArchiveUnknownShortWaitThenSecondEscMenuScenario() =>
        new(
            "return_uploaded_archive_unknown_short_wait_then_second_esc_menu",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
                Frame(GamePage.Unknown),
                Frame(GamePage.ArchiveEditorMenu, WorkflowTargetNames.ArchiveEditorExitButton),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should use the short Unknown return wait and then open the editor menu."),
                Require(result.Result.Message.Contains("result=UnknownPossibleArchiveEditor timeoutSeconds=3", StringComparison.Ordinal), "Workflow log should mention 3 second return timeout."),
                RequireActions(result.Actions, "click:archive_editor_exit_button"),
                Require(result.Result.Message.Contains("finalPage=WorldMain", StringComparison.Ordinal), "Final page should be WorldMain.")
            ],
            ClockAdvancePerDelay: TimeSpan.FromSeconds(30));

    private static Stage1DryRunScenario CreateImportRunCodeNotVisibleUntilLocalArchiveScrollScenario() =>
        new(
            "import_run_code_not_visible_until_local_archive_scroll",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should find the imported run code after LocalArchive scroll."),
                RequireActions(result.Actions, "scroll:local_archive:-480", "click:local_archive_more_button", "click:local_archive_upload_menu_item"),
                Require(result.Result.Message.Contains("ScrollLocalArchive", StringComparison.Ordinal), "Workflow log should mention LocalArchive scrolling.")
            ],
            ClockAdvancePerDelay: TimeSpan.FromSeconds(1),
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 36, 49 }, "dry-run-gil_DRY1", total: 60));

    private static Stage1DryRunScenario CreateImportRunCodeNeverVisibleAfterMaxScrollsFailsScenario() =>
        new(
            "import_run_code_never_visible_after_max_scrolls_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail if the run code is not visible after max scroll attempts."),
                Require(result.Actions.Count(action => action == "scroll:local_archive:-480") == 3, "Should scroll LocalArchive three times."),
                RequireNoAction(result.Actions, "click:local_archive_more_button"),
                RequireNoAction(result.Actions, "click:local_archive_upload_menu_item")
            ],
            ClockAdvancePerDelay: TimeSpan.FromSeconds(1),
            OcrResults: RepeatOcrWithMatchAt(-1, "none", total: 96));

    private static Stage1DryRunScenario CreateMainMenuEntryFoundAfterBigScrollScenario() =>
        new(
            "main_menu_entry_found_after_big_scroll",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should scroll MainMenu and then enter Miliastra."),
                RequireActions(result.Actions, "scroll:main_menu:-480", "click:qianxing_realm_entry"),
                Require(result.Result.Message.Contains("foundAfterScrollAttempt=1", StringComparison.Ordinal), "Workflow log should mention entry found after scroll.")
            ]);

    private static Stage1DryRunScenario CreateMainMenuEntryMissingAfterMaxScrollsFailsScenario() =>
        new(
            "main_menu_entry_missing_after_max_scrolls_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu),
                Frame(GamePage.MainMenu),
                Frame(GamePage.MainMenu),
                Frame(GamePage.MainMenu),
                Frame(GamePage.MainMenu),
                Frame(GamePage.MainMenu)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when qianxing entry is missing after max scrolls."),
                Require(result.Actions.Count(action => action == "scroll:main_menu:-480") == 5, "Should scroll MainMenu five times."),
                RequireNoAction(result.Actions, "click:qianxing_realm_entry"),
                Require(result.Result.Message.Contains("qianxing_realm_entry", StringComparison.Ordinal), "Failure should mention missing qianxing entry.")
            ]);

    private static Stage1DryRunScenario CreateMainMenuHeaderVisibleEntryFoundAfterThirdScrollScenario() =>
        new(
            "main_menu_header_visible_entry_found_after_third_scroll",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuQianxingSectionHeader),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuQianxingSectionHeader),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuQianxingSectionHeader),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuQianxingSectionHeader, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should keep scrolling when only the Qianxing section header is visible."),
                Require(result.Actions.Count(action => action == "scroll:main_menu:-480") == 3, "Should find qianxing_realm_entry after the third MainMenu scroll."),
                Require(result.Result.Message.Contains("foundAfterScrollAttempt=3", StringComparison.Ordinal), "Workflow log should mention entry found after third scroll."),
                Require(result.Result.Message.Contains("detected_main_menu_qianxing_section_header=True", StringComparison.Ordinal), "Workflow log should record the visible Qianxing section header.")
            ]);

    private static Stage1DryRunScenario CreateMainMenuHeaderVisibleButEntryMissingUntilMaxScrollsFailsScenario() =>
        new(
            "main_menu_header_visible_but_entry_missing_until_max_scrolls_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuQianxingSectionHeader),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuQianxingSectionHeader),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuQianxingSectionHeader),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuQianxingSectionHeader),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuQianxingSectionHeader),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuQianxingSectionHeader)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail only after all MainMenu scroll attempts when header is visible but entry is missing."),
                Require(result.Actions.Count(action => action == "scroll:main_menu:-480") == 5, "Should use all five MainMenu scroll attempts."),
                Require(result.Result.Message.Contains("notFoundAfterScrollAttempt=5", StringComparison.Ordinal), "Workflow log should record the fifth failed scroll attempt."),
                RequireNoAction(result.Actions, "click:qianxing_realm_entry")
            ]);

    private static Stage1DryRunScenario CreateMainMenuScrollUsesMainMenuScrollCoordinatesScenario() =>
        new(
            "main_menu_scroll_uses_main_menu_scroll_coordinates",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuSettingsButton),
                Frame(GamePage.MainMenu, WorkflowTargetNames.MainMenuSettingsButton, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after scrolling the MainMenu once."),
                RequireActions(result.Actions, "input_scroll:0.230,0.780,-480"),
                Require(result.Result.Message.Contains("action=ScrollMainMenu x=0.230 y=0.780 amount=-480", StringComparison.Ordinal), "Workflow log should record the MainMenu-specific scroll coordinates.")
            ]);

    private static Stage1DryRunScenario CreateFirstFailedSandboxStableThenCooldownThenSecondUploadScenario() =>
        new(
            "first_failed_sandbox_stable_then_cooldown_then_second_upload",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                ..CooldownLocalArchiveFrames(2),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after process stable and cooldown."),
                Require(result.Result.Message.Contains("processStable", StringComparison.Ordinal), "Workflow log should include processStable."),
                Require(result.Result.Message.Contains("cooldownFinal page=LocalArchive", StringComparison.Ordinal), "Workflow log should include cooldown final LocalArchive check."),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 2, "Second upload should run after cooldown.")
            ],
            SandboxRunningSequence: [false, true, true, true]);

    private static Stage1DryRunScenario CreateFirstFailedSandboxStableButProcessExitsDuringCooldownFailsScenario() =>
        new(
            "first_failed_sandbox_stable_but_process_exits_during_cooldown_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when sandbox exits before the second upload."),
                Require(
                    result.Result.Message.Contains("sandbox process exited during readiness cooldown", StringComparison.Ordinal) ||
                    result.Result.Message.Contains("sandboxRunningAfterCooldown=False", StringComparison.Ordinal),
                    "Failure should mention sandbox is no longer running after cooldown."),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 1, "Second upload must not run after sandbox process exit.")
            ],
            SandboxRunningSequence: [false, true, true, true, true, true, false]);

    private static Stage1DryRunScenario CreateFirstFailedSandboxStableButPageChangesDuringCooldownFailsScenario() =>
        new(
            "first_failed_sandbox_stable_but_page_changes_during_cooldown_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.Unknown)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when refocus confirmation never returns LocalArchive."),
                Require(result.Result.Message.Contains("could not confirm LocalArchive after sandbox window detection and game refocus", StringComparison.Ordinal), "Failure should mention refocus confirmation failure."),
                Require(result.Result.Message.Contains("cooldownCheck sandboxRunning=True", StringComparison.Ordinal), "Cooldown should continue through page observation."),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 1, "Second upload must not run after refocus failure.")
            ],
            SandboxRunningSequence: [false, true, true, true, true, true, true]);

    private static Stage1DryRunScenario CreateFirstFailedSandboxProcessWindowRefocusThenSecondUploadSuccessScenario() =>
        new(
            "first_failed_sandbox_process_window_refocus_then_second_upload_success",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                ..CooldownLocalArchiveFrames(2),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after sandbox window detection and refocus."),
                Require(result.Result.Message.Contains("waitWindow result=found", StringComparison.Ordinal), "Workflow log should include sandbox window found."),
                Require(result.Result.Message.Contains("refocusGame after=", StringComparison.Ordinal), "Workflow log should include refocus diagnostics."),
                Require(result.Result.Message.Contains("confirmLocalArchiveAfterRefocus page=LocalArchive", StringComparison.Ordinal), "Workflow log should confirm LocalArchive after refocus."),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 2, "Second upload should run after window/refocus readiness.")
            ],
            SandboxRunningSequence: [false, true, true, true]);

    private static Stage1DryRunScenario CreateFirstFailedSandboxProcessStableButWindowMissingFailsScenario() =>
        new(
            "first_failed_sandbox_process_stable_but_window_missing_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                ..CooldownLocalArchiveFrames(1)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when sandbox window is missing."),
                Require(result.Result.Message.Contains("timed out waiting for sandbox window", StringComparison.Ordinal), "Failure should mention sandbox window timeout."),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 1, "Second upload must not run without sandbox window.")
            ],
            SandboxRunningSequence: [false, true, true, true],
            SandboxWindowFound: false);

    private static Stage1DryRunScenario CreateFirstFailedSandboxWindowFoundButRefocusConfirmNotLocalArchiveFailsScenario() =>
        new(
            "first_failed_sandbox_window_found_but_refocus_confirm_not_local_archive_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                ..CooldownLocalArchiveFrames(1),
                Frame(GamePage.Unknown)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when refocus confirmation is not LocalArchive."),
                Require(result.Result.Message.Contains("could not confirm LocalArchive after sandbox window detection and game refocus", StringComparison.Ordinal), "Failure should mention refocus confirmation failure."),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 1, "Second upload must not run after refocus confirmation failure.")
            ],
            SandboxRunningSequence: [false, true, true, true]);

    private static Stage1DryRunScenario CreateImportReturnsLocalArchiveButRunCodeAppearsAfterDelayScenario() =>
        new(
            "import_returns_local_archive_but_run_code_appears_after_delay",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should continue once imported run code appears after delay."),
                Require(result.Result.Message.Contains("ImportRunArchiveVisible scanPage=0 result=notYetVisible", StringComparison.Ordinal), "Workflow log should record not-yet-visible import state."),
                Require(result.Result.Message.Contains("ImportRunArchiveVisible result=found", StringComparison.Ordinal), "Workflow log should record imported run code visibility."),
                RequireActions(result.Actions, "click:local_archive_more_button", "click:local_archive_upload_menu_item")
            ],
            ClockAdvancePerDelay: TimeSpan.FromSeconds(1),
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 13, 26 }, "dry-run-gil_DRY1", total: 39));

    private static Stage1DryRunScenario CreateImportReturnsLocalArchiveButRunCodeNeverVisibleFailsScenario() =>
        new(
            "import_returns_local_archive_but_run_code_never_visible_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when imported run code never becomes visible."),
                Require(result.Result.Message.Contains("could not confirm that the imported run archive appeared in LocalArchive", StringComparison.Ordinal), "Failure should mention imported run archive visibility."),
                RequireNoAction(result.Actions, "click:local_archive_more_button"),
                RequireNoAction(result.Actions, "click:local_archive_upload_menu_item")
            ],
            ClockAdvancePerDelay: TimeSpan.FromSeconds(30),
            OcrResults:
            [
                Ocr("old_archive_PPT4", 0.5, 0.5),
                Ocr("old_archive_RGU6", 0.5, 0.5)
            ]);

    private static Stage1DryRunScenario CreateImportReturnsLocalArchiveButMultipleRunCodeMatchesFailsScenario() =>
        new(
            "import_returns_local_archive_but_multiple_run_code_matches_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(
                    GamePage.LocalArchive,
                    WorkflowTargetNames.ImportArchiveButton,
                    WorkflowTargetNames.LocalArchiveCard,
                    WorkflowTargetNames.LocalArchiveCard)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when imported run code appears in multiple candidates."),
                Require(result.Result.Message.Contains("multiple LocalArchive cards matching the imported run archive code", StringComparison.Ordinal), "Failure should mention multiple imported code matches."),
                RequireNoAction(result.Actions, "click:local_archive_more_button"),
                RequireNoAction(result.Actions, "click:local_archive_upload_menu_item")
            ],
            OcrResults:
            [
                Ocr("copy_a_DRY1", 0.5, 0.5),
                Ocr("copy_b_DRY1", 0.5, 0.5)
            ]);

    private static Stage1DryRunScenario CreateSandboxCooldownAllowsUnknownThenRefocusSuccessScenario() =>
        new(
            "sandbox_cooldown_allows_unknown_when_sandbox_foreground_then_refocus_success",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.Unknown),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should allow Unknown during sandbox cooldown and succeed after refocus."),
                Require(result.Result.Message.Contains("cooldownCheck sandboxRunning=True", StringComparison.Ordinal), "Workflow log should include cooldown running check."),
                Require(result.Result.Message.Contains("page=Unknown", StringComparison.Ordinal), "Workflow log should record Unknown page observation during cooldown."),
                Require(result.Result.Message.Contains("confirmLocalArchiveAfterRefocus page=LocalArchive", StringComparison.Ordinal), "Refocus should confirm LocalArchive."),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 2, "Second upload should run after refocus success.")
            ],
            SandboxRunningSequence: [false, true, true, true]);

    private static Stage1DryRunScenario CreateUploadedArchiveWithSubmitPromptLaterAliasIsDismissedScenario() =>
        new(
            "uploaded_archive_with_submit_prompt_later_alias_is_dismissed",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(
                    GamePage.UploadedArchive,
                    WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon,
                    WorkflowTargetNames.UploadedImageAssetsButton,
                    WorkflowTargetNames.UploadedArchiveLaterButtonAlias),
                Frame(
                    GamePage.UploadedArchive,
                    WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon,
                    WorkflowTargetNames.UploadedImageAssetsButton),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after dismissing UploadedArchive prompt through Later alias."),
                RequireActions(result.Actions, "click:upload_failed_close_button"),
                Require(result.Result.Message.Contains("ClickLaterViaContextualSecondaryButton", StringComparison.Ordinal), "Workflow log should mention contextual secondary button dismiss."),
                RequireNoAction(result.Actions, "click:submit_review_button"),
                Require(result.Result.Message.Contains("finalPage=WorldMain", StringComparison.Ordinal), "Final page should be WorldMain.")
            ]);

    private static Stage1DryRunScenario CreateSandboxCooldownProcessRunningButRefocusNeverReturnsLocalArchiveFailsScenario() =>
        new(
            "sandbox_cooldown_process_running_but_refocus_never_returns_local_archive_fails",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
                Frame(GamePage.Unknown),
                Frame(GamePage.Unknown),
                Frame(GamePage.Unknown)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when refocus never returns LocalArchive."),
                Require(result.Result.Message.Contains("could not confirm LocalArchive after sandbox window detection and game refocus", StringComparison.Ordinal), "Failure should mention refocus confirmation."),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 1, "Second upload must not run without LocalArchive after refocus.")
            ],
            SandboxRunningSequence: [false, true, true, true]);

    private static Stage1DryRunScenario CreateSecondUploadPreexistingMoreButtonOnWrongCardIsIgnoredScenario() =>
        new(
            "second_upload_preexisting_more_button_on_wrong_card_is_ignored",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), TargetCardMoreButton(), WrongCardMoreButton()),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadFailed, WorkflowTargetNames.UploadFailedCloseButton),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), TargetCardMoreButton(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), TargetCardMoreButton(), WrongCardMoreButton()),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after ignoring pre-existing wrong-card more button."),
                RequireActions(result.Actions,
                    "move:local_archive_hover_clear_area",
                    "move:local_archive_card_action_area",
                    "click:local_archive_more_button"),
                Require(result.Actions.Count(action => action == "click:local_archive_upload_menu_item") == 2, "Upload menu item should be clicked twice.")
            ],
            SandboxRunningSequence: [false, true, true, true],
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 0, 14, 28 }, "dry-run-gil_DRY1", total: 42),
            AutoExpandLocalArchiveMenuFrames: false);

    private static Stage1DryRunScenario CreatePreexistingMoreButtonWithoutTargetHoverNotAcceptedScenario() =>
        new(
            "preexisting_more_button_without_target_hover_not_accepted",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton())
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail when target-card hover does not reveal a matching more button."),
                RequireNoAction(result.Actions, "click:local_archive_more_button"),
                Require(result.Actions.Contains("move:local_archive_hover_clear_area", StringComparer.Ordinal), "Clear hover action should run before OCR-bound hover.")
            ],
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 0, 14 }, "dry-run-gil_DRY1", total: 28),
            AutoExpandLocalArchiveMenuFrames: false);

    private static Stage1DryRunScenario CreateVisibleMoreButtonOnTargetBeforeHoverStillRequiresRehoverScenario() =>
        new(
            "visible_more_button_on_target_before_hover_still_requires_rehover",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), TargetCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), TargetCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), TargetCardMoreButton()),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should succeed after re-hovering even when target more button was initially visible."),
                RequireActions(result.Actions,
                    "move:local_archive_hover_clear_area",
                    "move:local_archive_card_action_area",
                    "click:local_archive_more_button"),
                Require(result.Result.Message.Contains("action=IgnorePreExistingMoreButtons", StringComparison.Ordinal), "Workflow log should show pre-existing more buttons were ignored.")
            ],
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 0, 13 }, "dry-run-gil_DRY1", total: 26),
            AutoExpandLocalArchiveMenuFrames: false);

    private static Stage1DryRunScenario CreateOcrUniqueCardYoloMoreMissingUsesOpenCvLocatorScenario() =>
        new(
            "yolo_more_missing_opencv_hits_target_card_opens_menu",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should use OpenCV local ROI locator when YOLO more button is missing."),
                RequireActions(result.Actions, "opencv_more_button:True", "click:local_archive_more_button", "click:local_archive_upload_menu_item"),
                RequireNoAction(result.Actions, "click:local_archive_card_action_area_fallback"),
                Require(result.Result.Message.Contains("openCvMoreButtonMatched=True", StringComparison.Ordinal), "Workflow log should mention OpenCV more button match."),
                Require(result.Result.Message.Contains("yoloMoreButtonMatched=False", StringComparison.Ordinal), "Workflow log should record missing/rejected YOLO more button.")
            ],
            OpenCvMoreButtonResults: [OpenCvHit(SingleCardMoreButton())],
            AutoExpandLocalArchiveMenuFrames: false);

    private static Stage1DryRunScenario CreateOcrUniqueCardWrongYoloMoreIgnoredThenOpenCvLocatorScenario() =>
        new(
            "yolo_more_on_wrong_card_opencv_hits_target_card_opens_menu",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                FrameWithTargets(GamePage.LocalArchive, ImportButton(), TargetCard(), WrongCard(), WrongCardMoreButton()),
                Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
                Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
                Frame(GamePage.WorldMain)
            ],
            result =>
            [
                Require(result.Result.IsSuccess, "ExecuteAsync should ignore wrong-card YOLO more button and use OpenCV on the OCR-confirmed card."),
                RequireActions(result.Actions, "opencv_more_button:True", "click:local_archive_more_button", "click:local_archive_upload_menu_item"),
                RequireNoAction(result.Actions, "click:local_archive_card_action_area_fallback"),
                Require(result.Result.Message.Contains("openCvMoreButtonMatched=True", StringComparison.Ordinal), "Workflow log should mention OpenCV more button match.")
            ],
            OcrResults: RepeatOcrWithMatchAt(new HashSet<int> { 0, 14 }, "dry-run-gil_DRY1", total: 28),
            OpenCvMoreButtonResults: [OpenCvHit(TargetCardMoreButton())],
            AutoExpandLocalArchiveMenuFrames: false);

    private static Stage1DryRunScenario CreateOpenCvLocatorMissingFailsSafelyScenario() =>
        new(
            "yolo_more_missing_opencv_missing_fails_safely",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should fail if YOLO and OpenCV both cannot locate the OCR-confirmed card more button."),
                RequireActions(result.Actions, "opencv_more_button:False"),
                RequireNoAction(result.Actions, "click:local_archive_card_action_area_fallback"),
                RequireNoAction(result.Actions, "click:local_archive_more_button"),
                RequireNoAction(result.Actions, "click:local_archive_upload_menu_item"),
                Require(result.Result.Message.Contains("could not locate a safe local_archive_more_button", StringComparison.Ordinal), "Failure should mention safe more button location failure.")
            ],
            ClockAdvancePerDelay: TimeSpan.FromSeconds(30),
            OpenCvMoreButtonResults: [OpenCvMiss()],
            AutoExpandLocalArchiveMenuFrames: false);

    private static Stage1DryRunScenario CreateOpenCvRightSideEditCandidateRejectedScenario() =>
        new(
            "opencv_right_side_edit_candidate_rejected",
            [
                Frame(GamePage.WorldMain),
                Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard),
                Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveCard)
            ],
            result =>
            [
                Require(!result.Result.IsSuccess, "ExecuteAsync should reject OpenCV candidate outside the left-bottom card action area."),
                RequireNoAction(result.Actions, "click:local_archive_card_action_area_fallback"),
                RequireNoAction(result.Actions, "click:local_archive_more_button"),
                Require(result.Result.Message.Contains("OpenCV matched a candidate outside", StringComparison.Ordinal), "Failure should mention rejected OpenCV candidate.")
            ],
            OpenCvMoreButtonResults: [OpenCvHit(TargetCardEditAreaFalsePositive())],
            AutoExpandLocalArchiveMenuFrames: false);

    private static ScriptedPageFrame Frame(GamePage page, params string[] targetNames) =>
        new(
            page,
            BuildFrameTargets(page, NormalizeFrameTargets(page, targetNames)));

    private static ScriptedPageFrame LowConfidenceFrame(GamePage page, double confidence, params string[] targetNames) =>
        new(
            page,
            BuildFrameTargets(page, NormalizeFrameTargets(page, targetNames)),
            confidence);

    private static ScriptedPageFrame FrameWithTargets(GamePage page, params AutomationTarget[] targets) =>
        new(page, targets);

    private static ScriptedPageFrame[] CooldownLocalArchiveFrames(int count = 30) =>
        Enumerable.Range(0, count)
            .Select(_ => Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton))
            .ToArray();

    private static IReadOnlyList<ScriptedPageFrame> StandardSuccessFramesFromWorldMain() =>
    [
        Frame(GamePage.WorldMain),
        Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
        Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
        Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
        Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
        Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
        Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
        Frame(GamePage.UploadedArchive, WorkflowTargetNames.UploadedArchiveDismissArea),
        Frame(GamePage.WorldMain)
    ];

    private static IReadOnlyList<ScriptedPageFrame> PreviewEditSuccessFrames(bool includeTags) =>
        PreviewEditFrames(includeTags, includeSubmit: true);

    private static IReadOnlyList<ScriptedPageFrame> PreviewEditTagsThenVersionLogMissingFrames()
    {
        var previewTargets = new[]
        {
            WorkflowTargetNames.PreviewLevelNameEditButton,
            WorkflowTargetNames.PreviewGameplayDescription,
            WorkflowTargetNames.PreviewLevelDescription,
            WorkflowTargetNames.PreviewTagsArea,
            WorkflowTargetNames.PreviewCoverImageArea,
            WorkflowTargetNames.PreviewSubmitButton
        };
        var frames = PreviewEditFrames(includeTags: true, includeSubmit: true, tagDialogFrameCount: 3, coverTargetNames: [WorkflowTargetNames.PreviewCoverImageArea])
            .ToList();
        var previewWithoutVersionLog = Frame(GamePage.PreviewEditor, previewTargets);
        for (var index = 0; index < frames.Count; index++)
        {
            if (frames[index].Page == GamePage.PreviewEditor)
                frames[index] = previewWithoutVersionLog;
        }

        frames.AddRange(Enumerable.Range(0, 8).Select(_ => previewWithoutVersionLog));
        return frames;
    }

    private static IReadOnlyList<ScriptedPageFrame> PreviewEditTagsEditRowFrames(int editRowCount)
    {
        var editRows = Enumerable.Range(0, editRowCount)
            .Select(index => new AutomationTarget(
                WorkflowTargetNames.EditRow,
                0.70,
                0.58 + index * 0.08,
                0.32,
                0.06,
                0.9))
            .ToArray();
        var preview = FrameWithTargets(
            GamePage.PreviewEditor,
            [
                Target(WorkflowTargetNames.PreviewLevelNameEditButton, 0.45, 0.45),
                Target(WorkflowTargetNames.PreviewGameplayDescription, 0.55, 0.53),
                Target(WorkflowTargetNames.PreviewLevelDescription, 0.60, 0.57),
                Target(WorkflowTargetNames.PreviewVersionRecordArea, 0.70, 0.65),
                Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
                Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.73),
                .. editRows
            ]);

        var frames = new List<ScriptedPageFrame>
        {
            Frame(GamePage.WorldMain),
            Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
            Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
            Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
            Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
            Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            preview
        };
        frames.AddRange(Enumerable.Range(0, 8).Select(_ => preview));
        frames.Add(Frame(GamePage.TagDialog, WorkflowTargetNames.ChoiceOption, WorkflowTargetNames.PrimaryActionButton));
        frames.Add(Frame(GamePage.TagDialog, WorkflowTargetNames.ChoiceOption, WorkflowTargetNames.PrimaryActionButton));
        frames.Add(Frame(GamePage.TagDialog, WorkflowTargetNames.ChoiceOption, WorkflowTargetNames.PrimaryActionButton));
        frames.Add(preview);
        frames.AddRange(Enumerable.Range(0, 8).Select(_ => preview));
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));
        return frames;
    }

    private static IReadOnlyList<ScriptedPageFrame> PreviewEditVersionLogEditRowFrames()
    {
        var preview = FrameWithTargets(
            GamePage.PreviewEditor,
            Target(WorkflowTargetNames.PreviewLevelNameEditButton, 0.45, 0.45),
            Target(WorkflowTargetNames.PreviewGameplayDescription, 0.55, 0.53),
            Target(WorkflowTargetNames.PreviewLevelDescription, 0.60, 0.57),
            Target(WorkflowTargetNames.PreviewTagsArea, 0.65, 0.61),
            Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
            Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.73),
            new AutomationTarget(WorkflowTargetNames.EditRow, 0.70, 0.66, 0.32, 0.06, 0.9));

        var frames = PreviewEditBaseFrames(preview);
        frames.AddRange(Enumerable.Range(0, 10).Select(_ => preview));
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));
        return frames;
    }

    private static IReadOnlyList<ScriptedPageFrame> PreviewEditVersionLogThenMediaAddTileFrames(int mediaTileCount)
    {
        var mediaTiles = Enumerable.Range(0, mediaTileCount)
            .Select(index => new AutomationTarget(
                WorkflowTargetNames.MediaAddTile,
                0.62 + index * 0.16,
                0.68,
                0.12,
                0.10,
                0.9))
            .ToArray();
        var preview = FrameWithTargets(
            GamePage.PreviewEditor,
            [
                Target(WorkflowTargetNames.PreviewLevelNameEditButton, 0.45, 0.45),
                Target(WorkflowTargetNames.PreviewGameplayDescription, 0.55, 0.53),
                Target(WorkflowTargetNames.PreviewLevelDescription, 0.60, 0.57),
                Target(WorkflowTargetNames.PreviewTagsArea, 0.65, 0.61),
                Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
                Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83),
                new AutomationTarget(WorkflowTargetNames.EditRow, 0.70, 0.64, 0.32, 0.06, 0.9),
                .. mediaTiles
            ]);

        var frames = PreviewEditBaseFrames(preview);
        frames.AddRange(Enumerable.Range(0, 14).Select(_ => preview));
        return frames;
    }

    private static IReadOnlyList<ScriptedPageFrame> PreviewEditMediaAddTileFrames(int mediaTileCount)
    {
        var mediaTiles = Enumerable.Range(0, mediaTileCount)
            .Select(index => new AutomationTarget(
                WorkflowTargetNames.MediaAddTile,
                0.62 + index * 0.16,
                0.68,
                0.12,
                0.10,
                0.9))
            .ToArray();
        var preview = FrameWithTargets(
            GamePage.PreviewEditor,
            [
                Target(WorkflowTargetNames.PreviewLevelNameEditButton, 0.45, 0.45),
                Target(WorkflowTargetNames.PreviewGameplayDescription, 0.55, 0.53),
                Target(WorkflowTargetNames.PreviewLevelDescription, 0.60, 0.57),
                Target(WorkflowTargetNames.PreviewTagsArea, 0.65, 0.61),
                Target(WorkflowTargetNames.PreviewVersionRecordArea, 0.70, 0.65),
                Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
                Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83),
                .. mediaTiles
            ]);

        var frames = PreviewEditBaseFrames(preview);
        frames.AddRange(Enumerable.Range(0, 14).Select(_ => preview));
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));
        return frames;
    }

    private static Stage1DryRunScenario CreateMultilanguageScenario(
        string name,
        string addButtonOcr,
        IReadOnlyList<string> selectedBefore,
        IReadOnlyList<string> configuredLanguages,
        Func<Stage1DryRunAssertionInput, IEnumerable<string?>> assertions,
        bool isUpdate = false,
        string confirmOcrText = "OK")
    {
        var visibleLanguages = selectedBefore
            .Concat(configuredLanguages)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new Stage1DryRunScenario(
            name,
            PreviewEditMultilanguageFrames(addButtonOcr, selectedBefore, visibleLanguages, fillLanguageCount: configuredLanguages.Count),
            assertions,
            WorkflowOcrResults:
            [
                OcrText(addButtonOcr),
                OcrText("Language"),
                OcrText(addButtonOcr),
                OcrText(confirmOcrText)
            ],
            ConfigureArchive: archive =>
            {
                foreach (var code in configuredLanguages)
                    AddLocalizedInfo(archive, code, code);
            },
            IsUpdate: false,
            ExistingArchiveIdentifier: isUpdate ? "DryRunExisting" : "");
    }

    private static Stage1DryRunScenario CreateTextEditIconTableMultilanguageScenario(
        string name,
        bool earlyAccess,
        IReadOnlyList<string> selectedBefore,
        IReadOnlyList<string> configuredLanguages,
        IReadOnlyList<string> tableLanguagesAfter,
        int rowCount,
        int columnCount,
        Func<Stage1DryRunAssertionInput, IEnumerable<string?>> assertions)
    {
        var previewTargets = new List<AutomationTarget>
        {
            Target(WorkflowTargetNames.PreviewLevelNameEditButton, 0.45, 0.45),
            Target(WorkflowTargetNames.PreviewGameplayDescription, 0.55, 0.53),
            Target(WorkflowTargetNames.PreviewLevelDescription, 0.60, 0.57),
            Target(WorkflowTargetNames.PreviewTagsArea, 0.65, 0.61),
            Target(WorkflowTargetNames.PreviewVersionRecordArea, 0.70, 0.65),
            Target(WorkflowTargetNames.PreviewMultilanguageArea, 0.72, 0.70),
            Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
            Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83)
        };
        if (earlyAccess)
            previewTargets.Add(Target(WorkflowTargetNames.ChoiceOption, 0.70, 0.46));
        var preview = FrameWithTargets(GamePage.PreviewEditor, [.. previewTargets]);
        var editorBefore = MultilanguageEditorTextEditIconTableFrame(Math.Max(1, selectedBefore.Count), Math.Max(4, columnCount));
        var editorAfter = MultilanguageEditorTextEditIconTableFrame(rowCount, columnCount);
        var dialog = LanguageSelectDialogFrame("en", selectedBefore);
        var refreshedDialog = LanguageSelectDialogFrame("en", tableLanguagesAfter);
        var frames = PreviewEditBaseFrames(preview);
        frames.Add(preview);
        frames.Add(editorBefore);
        frames.Add(dialog);
        frames.Add(refreshedDialog);
        frames.Add(editorAfter);
        frames.AddRange(Enumerable.Range(0, 32).Select(_ => editorAfter));
        frames.Add(preview);
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));

        var headerText = earlyAccess
            ? "Language Stage Name Early Access Version Description Gameplay Details Stage Description Update Log"
            : "Language Stage Name Gameplay Details Stage Description Update Log";
        var workflowOcr = new List<IReadOnlyList<WorkflowOcrTextResult>>();
        if (earlyAccess)
            workflowOcr.Add(OcrText("Early Access"));
        workflowOcr.AddRange(
        [
            OcrText("Add Language"),
            OcrText("OK"),
            OcrText("Add Language"),
            OcrText("OK"),
            OcrText("Add Language"),
            OcrText(headerText)
        ]);

        return new Stage1DryRunScenario(
            name,
            frames,
            assertions,
            WorkflowOcrResults: workflowOcr,
            ConfigureArchive: archive =>
            {
                archive.IsEarlyAccess = earlyAccess;
                foreach (var code in configuredLanguages)
                    AddLocalizedInfo(archive, code, code);
            });
    }

    private static Stage1DryRunScenario CreateHeaderOcrPartialIconMultilanguageScenario(
        string name,
        string addButtonOcr,
        bool earlyAccess,
        IReadOnlyList<string> selectedBefore,
        IReadOnlyList<string> configuredLanguages,
        IReadOnlyList<string> tableLanguagesAfter,
        double iconX,
        IReadOnlyList<WorkflowOcrTextResult> headerOcr,
        Func<Stage1DryRunAssertionInput, IEnumerable<string?>> assertions)
    {
        var previewTargets = new List<AutomationTarget>
        {
            Target(WorkflowTargetNames.PreviewLevelNameEditButton, 0.45, 0.45),
            Target(WorkflowTargetNames.PreviewGameplayDescription, 0.55, 0.53),
            Target(WorkflowTargetNames.PreviewLevelDescription, 0.60, 0.57),
            Target(WorkflowTargetNames.PreviewTagsArea, 0.65, 0.61),
            Target(WorkflowTargetNames.PreviewVersionRecordArea, 0.70, 0.65),
            Target(WorkflowTargetNames.PreviewMultilanguageArea, 0.72, 0.70),
            Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
            Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83)
        };
        if (earlyAccess)
            previewTargets.Add(Target(WorkflowTargetNames.ChoiceOption, 0.70, 0.46));
        var preview = FrameWithTargets(GamePage.PreviewEditor, [.. previewTargets]);
        var clientLanguage = addButtonOcr.Contains("\u6dfb", StringComparison.Ordinal) ? "zh-CN" : "en";
        var editorBefore = MultilanguageEditorPartialTextEditIconFrame(Math.Max(1, selectedBefore.Count), iconX, addButton: true);
        var editorAfter = MultilanguageEditorPartialTextEditIconFrame(Math.Max(1, tableLanguagesAfter.Count), iconX, addButton: true);
        var dialog = LanguageSelectDialogFrame(clientLanguage, selectedBefore);
        var refreshedDialog = LanguageSelectDialogFrame(clientLanguage, tableLanguagesAfter);
        var frames = PreviewEditBaseFrames(preview);
        if (earlyAccess)
            frames.Add(preview);
        frames.Add(editorBefore);
        frames.Add(dialog);
        frames.Add(refreshedDialog);
        frames.Add(editorAfter);
        frames.AddRange(Enumerable.Range(0, 32).Select(_ => editorAfter));
        frames.Add(preview);
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));

        var workflowOcr = new List<IReadOnlyList<WorkflowOcrTextResult>>();
        if (earlyAccess)
            workflowOcr.Add(OcrText("Early Access"));
        workflowOcr.AddRange(
        [
            OcrText(addButtonOcr),
            OcrText("OK"),
            OcrText(addButtonOcr),
            OcrText("OK"),
            OcrText(addButtonOcr),
            headerOcr,
            headerOcr,
            headerOcr
        ]);

        return new Stage1DryRunScenario(
            name,
            frames,
            assertions,
            WorkflowOcrResults: workflowOcr,
            ConfigureArchive: archive =>
            {
                archive.IsEarlyAccess = earlyAccess;
                foreach (var code in configuredLanguages)
                    AddLocalizedInfo(archive, code, code);
            });
    }

    private static Stage1DryRunScenario CreateMultilanguageEntryScenario(
        string name,
        IReadOnlyList<AutomationTarget> entryTargets,
        IReadOnlyList<string> entryOcr,
        Func<Stage1DryRunAssertionInput, IEnumerable<string?>> assertions,
        string addButtonOcr = "Add Language",
        int previewFramesAfterClickBeforeEditor = 0,
        IReadOnlyList<AutomationTarget>? initialPreviewTargets = null)
    {
        var workflowOcr = entryOcr
            .Select(OcrText)
            .Concat([OcrText(addButtonOcr), OcrText("Language"), OcrText(addButtonOcr)])
            .ToList();
        return new Stage1DryRunScenario(
            name,
            PreviewEditMultilanguageEntryFrames(
                entryTargets,
                addButtonOcr,
                selectedBefore: [addButtonOcr.Contains("\u6dfb", StringComparison.Ordinal) ? "zh-CN" : "en"],
                visibleLanguages: [addButtonOcr.Contains("\u6dfb", StringComparison.Ordinal) ? "zh-CN" : "en", "zh-CN"],
                fillLanguageCount: 1,
                previewFramesAfterClickBeforeEditor,
                initialPreviewTargets),
            assertions,
            WorkflowOcrResults: workflowOcr,
            ConfigureArchive: archive => AddLocalizedInfo(archive, "zh-CN", "zh-CN"));
    }

    private static void AddLocalizedInfo(LevelArchive archive, string code, string name)
    {
        archive.LocalizedInfos.Add(new LocalizedArchiveInfo
        {
            LanguageCode = code,
            LanguageName = name,
            LevelName = $"{code} Stage",
            EarlyAccessDescription = $"{code} Early Access",
            GameDescription = $"{code} Gameplay",
            LevelDescription = $"{code} Description",
            VersionLog = $"{code} Update"
        });
    }

    private static IReadOnlyList<ScriptedPageFrame> PreviewEditMultilanguageFrames(
        string addButtonOcr,
        IReadOnlyList<string> selectedBefore,
        IReadOnlyList<string> visibleLanguages,
        bool includeAddButton = true,
        int fillLanguageCount = 0)
    {
        var preview = FrameWithTargets(
            GamePage.PreviewEditor,
            Target(WorkflowTargetNames.PreviewLevelNameEditButton, 0.45, 0.45),
            Target(WorkflowTargetNames.PreviewGameplayDescription, 0.55, 0.53),
            Target(WorkflowTargetNames.PreviewLevelDescription, 0.60, 0.57),
            Target(WorkflowTargetNames.PreviewTagsArea, 0.65, 0.61),
            Target(WorkflowTargetNames.PreviewVersionRecordArea, 0.70, 0.65),
            Target(WorkflowTargetNames.PreviewMultilanguageArea, 0.72, 0.70),
            Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
            Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83));
        var editor = MultilanguageEditorFrame(visibleLanguages.Count, includeAddButton);
        var clientLanguage = addButtonOcr.Contains("\u6dfb", StringComparison.Ordinal) ? "zh-CN" : "en";
        var dialog = LanguageSelectDialogFrame(
            clientLanguage,
            selectedBefore);
        var refreshedDialog = LanguageSelectDialogFrame(clientLanguage, visibleLanguages);

        var frames = PreviewEditBaseFrames(preview);
        frames.Add(preview);
        frames.Add(editor);
        frames.Add(dialog);
        frames.AddRange(Enumerable.Range(0, Math.Max(0, fillLanguageCount)).Select(_ => refreshedDialog));
        frames.Add(editor);
        frames.AddRange(Enumerable.Range(0, Math.Max(0, fillLanguageCount * 8)).Select(_ => editor));
        frames.Add(preview);
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));
        return frames;
    }

    private static IReadOnlyList<ScriptedPageFrame> PreviewEditMultilanguageEntryFrames(
        IReadOnlyList<AutomationTarget> entryTargets,
        string addButtonOcr,
        IReadOnlyList<string> selectedBefore,
        IReadOnlyList<string> visibleLanguages,
        int fillLanguageCount,
        int previewFramesAfterClickBeforeEditor = 0,
        IReadOnlyList<AutomationTarget>? initialPreviewTargets = null,
        bool appendEditorFlow = true)
    {
        var entryPreview = PreviewFrameWithMultilanguageEntryTargets(entryTargets);
        var firstPreview = initialPreviewTargets == null
            ? entryPreview
            : PreviewFrameWithMultilanguageEntryTargets(initialPreviewTargets);
        var editor = MultilanguageEditorFrame(visibleLanguages.Count);
        var clientLanguage = addButtonOcr.Contains("\u6dfb", StringComparison.Ordinal) ? "zh-CN" : "en";
        var dialog = LanguageSelectDialogFrame(
            clientLanguage,
            selectedBefore);
        var refreshedDialog = LanguageSelectDialogFrame(clientLanguage, visibleLanguages);

        var frames = PreviewEditBaseFrames(firstPreview);
        if (initialPreviewTargets != null)
            frames.Add(entryPreview);
        if (!appendEditorFlow)
        {
            frames.AddRange(Enumerable.Range(0, PreviewEditorMaxScrollAttemptFixtureCount()).Select(_ => firstPreview));
            return frames;
        }

        frames.AddRange(Enumerable.Range(0, previewFramesAfterClickBeforeEditor).Select(_ => entryPreview));
        frames.Add(editor);
        frames.Add(dialog);
        frames.AddRange(Enumerable.Range(0, Math.Max(0, fillLanguageCount)).Select(_ => refreshedDialog));
        frames.Add(editor);
        frames.AddRange(Enumerable.Range(0, Math.Max(0, fillLanguageCount * 8)).Select(_ => editor));
        frames.Add(PreviewFrameWithMultilanguageEntryTargets([Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83)]));
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));
        return frames;
    }

    private static int PreviewEditorMaxScrollAttemptFixtureCount() => 8;

    private static ScriptedPageFrame PreviewFrameWithMultilanguageEntryTargets(IReadOnlyList<AutomationTarget> entryTargets)
    {
        var targets = new List<AutomationTarget>
        {
            Target(WorkflowTargetNames.PreviewLevelNameEditButton, 0.45, 0.45),
            Target(WorkflowTargetNames.PreviewGameplayDescription, 0.55, 0.53),
            Target(WorkflowTargetNames.PreviewLevelDescription, 0.60, 0.57),
            Target(WorkflowTargetNames.PreviewTagsArea, 0.65, 0.61),
            Target(WorkflowTargetNames.PreviewVersionRecordArea, 0.70, 0.65),
            Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
            Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.83)
        };
        targets.AddRange(entryTargets);
        return FrameWithTargets(GamePage.PreviewEditor, [.. targets]);
    }

    private static ScriptedPageFrame MultilanguageEditorFrame(
        int rowCount,
        bool includeAddButton = true,
        bool includePrimaryAddButton = false)
    {
        var targets = new List<AutomationTarget>
        {
            Target(WorkflowTargetNames.MultilanguageCloseButton, 0.92, 0.12)
        };
        if (includeAddButton)
            targets.Add(Target(WorkflowTargetNames.MultilanguageAddButton, 0.78, 0.16));
        if (includePrimaryAddButton)
            targets.Add(Target(WorkflowTargetNames.PrimaryActionButton, 0.78, 0.16));

        for (var index = 0; index < Math.Max(1, rowCount); index++)
        {
            targets.Add(new AutomationTarget(
                WorkflowTargetNames.MultilanguageLevelNameCell,
                0.36,
                0.34 + index * 0.07,
                0.12,
                0.048,
                0.9));
        }

        return FrameWithTargets(GamePage.MultiLanguageEditor, [.. targets]);
    }

    private static ScriptedPageFrame MultilanguageEditorTextEditIconTableFrame(
        int rowCount,
        int columnCount,
        bool includeAddButton = true)
    {
        var targets = new List<AutomationTarget>
        {
            Target(WorkflowTargetNames.MultilanguageCloseButton, 0.92, 0.12)
        };
        if (includeAddButton)
            targets.Add(Target(WorkflowTargetNames.MultilanguageAddButton, 0.78, 0.16));

        for (var row = 0; row < Math.Max(1, rowCount); row++)
        {
            for (var column = 0; column < Math.Max(1, columnCount); column++)
            {
                targets.Add(new AutomationTarget(
                    WorkflowTargetNames.TextEditIcon,
                    0.36 + column * 0.10,
                    0.34 + row * 0.07,
                    0.030,
                    0.038,
                    0.9));
            }
        }

        return FrameWithTargets(GamePage.MultiLanguageEditor, [.. targets]);
    }

    private static ScriptedPageFrame MultilanguageEditorPartialTextEditIconFrame(
        int rowCount,
        double iconX,
        bool addButton)
    {
        var targets = new List<AutomationTarget>
        {
            Target(WorkflowTargetNames.MultilanguageCloseButton, 0.92, 0.12)
        };
        if (addButton)
            targets.Add(Target(WorkflowTargetNames.MultilanguageAddButton, 0.78, 0.16));

        for (var row = 0; row < Math.Max(1, rowCount); row++)
        {
            targets.Add(new AutomationTarget(
                WorkflowTargetNames.TextEditIcon,
                iconX,
                0.34 + row * 0.07,
                0.030,
                0.038,
                0.9));
        }

        return FrameWithTargets(GamePage.MultiLanguageEditor, [.. targets]);
    }

    private static ScriptedPageFrame MultilanguageEditorMisclassifiedAsTagDialogFrame(int rowCount)
    {
        var targets = new List<AutomationTarget>
        {
            Target(WorkflowTargetNames.PrimaryActionButton, 0.78, 0.16),
            Target(WorkflowTargetNames.TextEditIcon, 0.88, 0.34),
            Target(WorkflowTargetNames.EditRow, 0.72, 0.34)
        };

        for (var index = 0; index < Math.Max(1, rowCount); index++)
        {
            targets.Add(new AutomationTarget(
                WorkflowTargetNames.MultilanguageLevelNameCell,
                0.36,
                0.34 + index * 0.07,
                0.12,
                0.048,
                0.9));
        }

        return FrameWithTargets(GamePage.TagDialog, [.. targets]);
    }

    private static ScriptedPageFrame LanguageSelectDialogFrame(string clientLanguage, IReadOnlyList<string> selectedCodes)
    {
        var left = string.Equals(clientLanguage, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? new[] { "zh-CN", "en", "ja", "fr", "th", "de", "it", "tr" }
            : ["en", "zh-TW", "ja", "fr", "th", "de", "it", "tr"];
        var right = string.Equals(clientLanguage, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? new[] { "zh-TW", "ko", "es", "ru", "vi", "id", "pt" }
            : ["zh-CN", "ko", "es", "ru", "vi", "id", "pt"];
        var targets = new List<AutomationTarget>
        {
            new(WorkflowTargetNames.ModalDialog, 0.50, 0.50, 0.56, 0.62, 0.9),
            Target(WorkflowTargetNames.LanguageConfirmButton, 0.78, 0.84)
        };
        targets.AddRange(left.Select((code, index) => new AutomationTarget(
            WorkflowTargetNames.LanguageOption,
            0.38,
            0.28 + index * 0.065,
            0.20,
            0.052,
            selectedCodes.Contains(code, StringComparer.OrdinalIgnoreCase) ? 0.96 : 0.90)));
        targets.AddRange(right.Select((code, index) => new AutomationTarget(
            WorkflowTargetNames.LanguageOption,
            0.62,
            0.28 + index * 0.065,
            0.20,
            0.052,
            selectedCodes.Contains(code, StringComparer.OrdinalIgnoreCase) ? 0.96 : 0.90)));
        return FrameWithTargets(GamePage.LanguageSelectDialog, [.. targets]);
    }

    private static ScriptedPageFrame GenericAddLanguageDialogFrame(
        GamePage rawPage,
        IReadOnlyList<string> selectedCodes,
        string clientLanguage,
        bool includeConfirmButton = true)
    {
        var left = string.Equals(clientLanguage, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? new[] { "zh-CN", "en", "ja", "fr", "th", "de", "it", "tr" }
            : ["en", "zh-TW", "ja", "fr", "th", "de", "it", "tr"];
        var right = string.Equals(clientLanguage, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? new[] { "zh-TW", "ko", "es", "ru", "vi", "id", "pt" }
            : ["zh-CN", "ko", "es", "ru", "vi", "id", "pt"];
        var targets = new List<AutomationTarget>
        {
            new(WorkflowTargetNames.ModalDialog, 0.50, 0.50, 0.56, 0.62, 0.9)
        };
        if (includeConfirmButton)
            targets.Add(Target(WorkflowTargetNames.PrimaryActionButton, 0.78, 0.84));
        targets.AddRange(left.Select((code, index) => new AutomationTarget(
            WorkflowTargetNames.ChoiceOption,
            0.38,
            0.28 + index * 0.065,
            0.20,
            0.052,
            selectedCodes.Contains(code, StringComparer.OrdinalIgnoreCase) ? 0.96 : 0.90)));
        targets.AddRange(right.Select((code, index) => new AutomationTarget(
            WorkflowTargetNames.ChoiceOption,
            0.62,
            0.28 + index * 0.065,
            0.20,
            0.052,
            selectedCodes.Contains(code, StringComparer.OrdinalIgnoreCase) ? 0.96 : 0.90)));
        return FrameWithTargets(rawPage, [.. targets]);
    }

    private static List<ScriptedPageFrame> PreviewEditBaseFrames(ScriptedPageFrame preview) =>
    [
        Frame(GamePage.WorldMain),
        Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
        Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
        Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
        Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
        Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
        Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
        Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
        Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
        Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
        preview
    ];

    private static IReadOnlyList<ScriptedPageFrame> PreviewEditEarlyAccessFrames(
        ScriptedPageFrame? afterToggleFirstFrame = null)
    {
        var beforeToggleTargets = PreviewEditorTargetNames(includeEarlyAccessDescription: false, includeChoiceOption: true);
        var afterToggleTargets = PreviewEditorTargetNames(includeEarlyAccessDescription: true, includeChoiceOption: true);
        var frames = new List<ScriptedPageFrame>
        {
            Frame(GamePage.WorldMain),
            Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
            Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
            Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
            Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
            Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.PreviewEditor, beforeToggleTargets),
            Frame(GamePage.PreviewEditor, beforeToggleTargets)
        };

        frames.Add(afterToggleFirstFrame ?? Frame(GamePage.PreviewEditor, afterToggleTargets));
        frames.AddRange(Enumerable.Range(0, 28).Select(_ => Frame(GamePage.PreviewEditor, afterToggleTargets)));
        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));
        return frames;
    }

    private static string[] PreviewEditorTargetNames(
        bool includeEarlyAccessDescription,
        bool includeChoiceOption)
    {
        var names = new List<string>
        {
            WorkflowTargetNames.PreviewLevelNameEditButton,
            WorkflowTargetNames.PreviewGameplayDescription,
            WorkflowTargetNames.PreviewLevelDescription,
            WorkflowTargetNames.PreviewTagsArea,
            WorkflowTargetNames.PreviewVersionRecordArea,
            WorkflowTargetNames.PreviewCoverImageArea,
            WorkflowTargetNames.PreviewSubmitButton
        };
        if (includeEarlyAccessDescription)
            names.Add(WorkflowTargetNames.PreviewEarlyAccessDescription);
        if (includeChoiceOption)
            names.Add(WorkflowTargetNames.ChoiceOption);
        return names.ToArray();
    }

    private static ScriptedPageFrame EarlyAccessLowerScrolledFrame() =>
        FrameWithTargets(
            GamePage.PreviewEditor,
            Target(WorkflowTargetNames.PreviewLevelNameEditButton, 0.45, 0.45),
            Target(WorkflowTargetNames.PreviewGameplayDescription, 0.55, 0.53),
            Target(WorkflowTargetNames.PreviewLevelDescription, 0.60, 0.57),
            Target(WorkflowTargetNames.PreviewTagsArea, 0.65, 0.61),
            Target(WorkflowTargetNames.PreviewVersionRecordArea, 0.70, 0.65),
            new AutomationTarget(WorkflowTargetNames.TextInputArea, 0.64, 0.45, 0.30, 0.16, 0.9),
            Target(WorkflowTargetNames.PreviewCoverImageArea, 0.30, 0.38),
            Target(WorkflowTargetNames.PreviewSubmitButton, 0.80, 0.73),
            Target(WorkflowTargetNames.ChoiceOption, 0.78, 0.45));

    private static IReadOnlyList<ScriptedPageFrame> PreviewEditFrames(
        bool includeTags,
        bool includeSubmit,
        int tagDialogFrameCount = 3,
        IReadOnlyList<string>? coverTargetNames = null)
    {
        coverTargetNames ??= [WorkflowTargetNames.PreviewCoverImageArea];
        var previewTargets = new List<string>
        {
            WorkflowTargetNames.PreviewLevelNameEditButton,
            WorkflowTargetNames.PreviewGameplayDescription,
            WorkflowTargetNames.PreviewLevelDescription,
            WorkflowTargetNames.PreviewTagsArea,
            WorkflowTargetNames.PreviewVersionRecordArea
        };
        previewTargets.AddRange(coverTargetNames);
        if (includeSubmit)
            previewTargets.Add(WorkflowTargetNames.PreviewSubmitButton);
        else
            previewTargets.Add(WorkflowTargetNames.PrimaryActionButton);

        var frames = new List<ScriptedPageFrame>
        {
            Frame(GamePage.WorldMain),
            Frame(GamePage.MainMenu, WorkflowTargetNames.QianxingRealmEntry),
            Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
            Frame(GamePage.LocalArchive, WorkflowTargetNames.ImportArchiveButton, WorkflowTargetNames.LocalArchiveMoreButton),
            Frame(GamePage.LocalArchiveMenu, WorkflowTargetNames.LocalArchiveUploadMenuItem),
            Frame(GamePage.UploadLocationDialog, WorkflowTargetNames.UploadNewButton),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon),
            Frame(GamePage.PreviewEditor, previewTargets.ToArray())
        };

        frames.AddRange(Enumerable.Range(0, 24).Select(_ => Frame(GamePage.PreviewEditor, previewTargets.ToArray())));
        if (includeTags)
        {
            frames.AddRange(Enumerable.Range(0, tagDialogFrameCount).Select(_ =>
                Frame(GamePage.TagDialog, WorkflowTargetNames.ChoiceOption, WorkflowTargetNames.PrimaryActionButton)));
            frames.Add(Frame(GamePage.PreviewEditor, previewTargets.ToArray()));
            frames.AddRange(Enumerable.Range(0, 8).Select(_ => Frame(GamePage.PreviewEditor, previewTargets.ToArray())));
        }

        frames.Add(Frame(GamePage.UploadedArchive, WorkflowTargetNames.InfoPanelRow, WorkflowTargetNames.TextEditIcon));
        frames.Add(Frame(GamePage.WorldMain));
        return frames;
    }

    private static IReadOnlyList<ScriptedPageFrame> ExpandLocalArchiveMenuFramesForHoverClear(
        IReadOnlyList<ScriptedPageFrame> frames)
    {
        var expanded = new List<ScriptedPageFrame>();
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            expanded.Add(frame);

            if (frame.Page == GamePage.LocalArchive &&
                index + 1 < frames.Count &&
                frames[index + 1].Page == GamePage.LocalArchiveMenu)
            {
                expanded.Add(frame);
                expanded.Add(frame);
            }
        }

        return expanded;
    }

    private static IReadOnlyList<AutomationTarget> BuildFrameTargets(GamePage page, IReadOnlyList<string> targetNames)
    {
        if (page == GamePage.UploadedArchive)
        {
            return targetNames
                .Select((name, index) => name switch
                {
                    _ when string.Equals(name, WorkflowTargetNames.InfoPanelRow, StringComparison.Ordinal) =>
                        new AutomationTarget(name, 0.80, 0.45 + index * 0.02, 0.25, 0.07, 0.9),
                    _ when string.Equals(name, WorkflowTargetNames.TextEditIcon, StringComparison.Ordinal) =>
                        new AutomationTarget(name, 0.90, 0.45, 0.03, 0.04, 0.9),
                    _ when string.Equals(name, WorkflowTargetNames.UploadedImageAssetsButton, StringComparison.Ordinal) =>
                        new AutomationTarget(name, 0.90, 0.53, 0.03, 0.04, 0.9),
                    _ => Target(name, 0.45 + index * 0.05, 0.45 + index * 0.04)
                })
                .ToList();
        }

        if (page == GamePage.PreviewEditor)
        {
            return targetNames
                .Select((name, index) => name switch
                {
                    _ when string.Equals(name, WorkflowTargetNames.PreviewCoverImageArea, StringComparison.Ordinal) =>
                        new AutomationTarget(name, 0.30, 0.38, 0.28, 0.24, 0.9),
                    _ when string.Equals(name, WorkflowTargetNames.ImageUploadArea, StringComparison.Ordinal) =>
                        new AutomationTarget(name, 0.30, 0.38, 0.28, 0.24, 0.9),
                    _ => Target(name, 0.45 + index * 0.05, 0.45 + index * 0.04)
                })
                .ToList();
        }

        if (page != GamePage.LocalArchive)
        {
            return targetNames
                .Select((name, index) => Target(name, 0.45 + index * 0.05, 0.45 + index * 0.04))
                .ToList();
        }

        var cardTotal = targetNames.Count(name => string.Equals(name, WorkflowTargetNames.LocalArchiveCard, StringComparison.Ordinal));
        var cardIndex = 0;
        return targetNames
            .Select(name =>
            {
                if (string.Equals(name, WorkflowTargetNames.ImportArchiveButton, StringComparison.Ordinal))
                    return Target(name, 0.12, 0.90);

                if (string.Equals(name, WorkflowTargetNames.PrimaryActionButton, StringComparison.Ordinal))
                    return Target(name, 0.12, 0.90);

                if (string.Equals(name, WorkflowTargetNames.LocalArchiveCard, StringComparison.Ordinal))
                {
                    var centerX = cardTotal > 1 ? 0.35 + 0.30 * cardIndex : 0.45;
                    cardIndex++;
                    return Target(name, centerX, 0.45);
                }

                if (string.Equals(name, WorkflowTargetNames.LocalArchiveMoreButton, StringComparison.Ordinal))
                    return Target(name, cardTotal > 1 ? 0.56 : 0.36, 0.53);

                return Target(name, 0.45, 0.45);
            })
            .ToList();
    }

    private static IReadOnlyList<string> NormalizeFrameTargets(GamePage page, IReadOnlyList<string> targetNames)
    {
        var names = targetNames.ToList();
        if (page == GamePage.LocalArchive &&
            names.Contains(WorkflowTargetNames.LocalArchiveMoreButton, StringComparer.Ordinal) &&
            !names.Contains(WorkflowTargetNames.LocalArchiveCard, StringComparer.Ordinal))
        {
            names.Insert(0, WorkflowTargetNames.LocalArchiveCard);
        }

        if (page == GamePage.LocalArchive &&
            names.Contains(WorkflowTargetNames.ImportArchiveButton, StringComparer.Ordinal) &&
            !names.Contains(WorkflowTargetNames.PrimaryActionButton, StringComparer.Ordinal))
        {
            names.Add(WorkflowTargetNames.PrimaryActionButton);
        }

        if (page == GamePage.LocalArchiveMenu &&
            names.Contains(WorkflowTargetNames.LocalArchiveUploadMenuItem, StringComparer.Ordinal) &&
            !names.Contains(WorkflowTargetNames.MenuActionItem, StringComparer.Ordinal))
        {
            names.Add(WorkflowTargetNames.MenuActionItem);
            names.Add(WorkflowTargetNames.MenuActionItem);
            names.Add(WorkflowTargetNames.MenuActionItem);
        }

        if (page == GamePage.UploadLocationDialog &&
            names.Contains(WorkflowTargetNames.UploadNewButton, StringComparer.Ordinal) &&
            !names.Contains(WorkflowTargetNames.SecondaryActionButton, StringComparer.Ordinal))
        {
            names.Add(WorkflowTargetNames.SecondaryActionButton);
        }

        if (page == GamePage.UploadFailed &&
            names.Contains(WorkflowTargetNames.UploadFailedCloseButton, StringComparer.Ordinal) &&
            !names.Contains(WorkflowTargetNames.SecondaryActionButton, StringComparer.Ordinal))
        {
            names.Add(WorkflowTargetNames.SecondaryActionButton);
        }

        return names;
    }

    private static AutomationTarget Target(string name, double centerX, double centerY)
    {
        var (width, height) =
            string.Equals(name, WorkflowTargetNames.LocalArchiveCard, StringComparison.Ordinal)
                ? (0.25, 0.16)
                : string.Equals(name, WorkflowTargetNames.InfoPanelRow, StringComparison.Ordinal)
                    ? (0.25, 0.07)
                : string.Equals(name, WorkflowTargetNames.TextEditIcon, StringComparison.Ordinal)
                    ? (0.03, 0.04)
                : string.Equals(name, WorkflowTargetNames.LoginStartGameButton, StringComparison.Ordinal)
                    ? (0.20, 0.08)
                    : (0.08, 0.05);
        return new(name, centerX, centerY, width, height, 0.9);
    }

    private static AutomationTarget ImportButton() =>
        Target(WorkflowTargetNames.ImportArchiveButton, 0.12, 0.90);

    private static AutomationTarget TargetCard() =>
        Target(WorkflowTargetNames.LocalArchiveCard, 0.35, 0.45);

    private static AutomationTarget WrongCard() =>
        Target(WorkflowTargetNames.LocalArchiveCard, 0.65, 0.45);

    private static AutomationTarget TargetCardMoreButton() =>
        Target(WorkflowTargetNames.LocalArchiveMoreButton, 0.26, 0.53);

    private static AutomationTarget SingleCardMoreButton() =>
        Target(WorkflowTargetNames.LocalArchiveMoreButton, 0.36, 0.53);

    private static AutomationTarget WrongCardMoreButton() =>
        Target(WorkflowTargetNames.LocalArchiveMoreButton, 0.56, 0.53);

    private static AutomationTarget TargetCardEditAreaFalsePositive() =>
        Target(WorkflowTargetNames.LocalArchiveMoreButton, 0.44, 0.45);

    private static AutomationTarget LayoutMiddleCardMoreButton() =>
        Target(WorkflowTargetNames.LocalArchiveMoreButton, 0.495, 0.267);

    private static LocalArchiveMoreButtonLocationResult OpenCvHit(AutomationTarget target) =>
        new(
            true,
            target,
            0.86,
            new NormalizedBox(target.CenterX, target.CenterY, 0.09, 0.08),
            "fake_opencv_hit");

    private static LocalArchiveMoreButtonLocationResult OpenCvMiss() =>
        new(
            false,
            null,
            0,
            new NormalizedBox(0.26, 0.53, 0.09, 0.08),
            "fake_opencv_miss");

    private static LocalArchiveCardTextResult Ocr(string text, double centerX, double centerY) =>
        new(text, new NormalizedBox(centerX, centerY, 0.08, 0.04), 1.0);

    private static IReadOnlyList<WorkflowOcrTextResult> OcrText(string text) =>
    [
        new(
            text,
            RapidOcrWorkflowOcrService.NormalizePreviewText(text),
            new NormalizedBox(0.5, 0.5, 0.10, 0.05),
            1.0)
    ];

    private static IReadOnlyList<WorkflowOcrTextResult> EarlyAccessHeaderOcr() =>
    [
        WorkflowOcr("Stage Name", 0.26, 0.27),
        WorkflowOcr("Early Access Version Description", 0.38, 0.27),
        WorkflowOcr("Gameplay Details", 0.50, 0.27),
        WorkflowOcr("Stage Description", 0.62, 0.27),
        WorkflowOcr("Update Log", 0.74, 0.27)
    ];

    private static IReadOnlyList<WorkflowOcrTextResult> NonEarlyAccessHeaderOcr() =>
    [
        WorkflowOcr("Stage Name", 0.26, 0.27),
        WorkflowOcr("Gameplay Details", 0.40, 0.27),
        WorkflowOcr("Stage Description", 0.53, 0.27),
        WorkflowOcr("Update Log", 0.66, 0.27)
    ];

    private static WorkflowOcrTextResult WorkflowOcr(string text, double centerX, double centerY) =>
        new(
            text,
            RapidOcrWorkflowOcrService.NormalizePreviewText(text),
            new NormalizedBox(centerX, centerY, 0.08, 0.04),
            1.0);

    private static string ExistingDryRunResourcePath(string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MiliastraArchiveTool", "stage1-dry-run-resources");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            File.WriteAllBytes(path, [0]);
        return path;
    }

    private static IReadOnlyList<LocalArchiveCardTextResult> RepeatOcrWithMatchAt(int matchIndex, string matchText, int total) =>
        Enumerable.Range(0, total)
            .Select(index => Ocr(index == matchIndex ? matchText : $"other_{index:00}", 0.5, 0.5))
            .ToList();

    private static IReadOnlyList<LocalArchiveCardTextResult> RepeatOcrWithMatchAt(IReadOnlySet<int> matchIndexes, string matchText, int total) =>
        Enumerable.Range(0, total)
            .Select(index => Ocr(matchIndexes.Contains(index) ? matchText : $"other_{index:00}", 0.5, 0.5))
            .ToList();

    private static IReadOnlyList<LocalArchiveCardTextResult> RepeatOcrWithMatches(IReadOnlySet<int> matchIndexes, int total) =>
        Enumerable.Range(0, total)
            .Select(index => Ocr(matchIndexes.Contains(index) ? $"copy_{index:00}_DRY1" : $"other_{index:00}", 0.5, 0.5))
            .ToList();

    private static string LatestDiagnosticJsonFromMessage(string message)
    {
        const string marker = "LatestDiagnosticJson=";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        start += marker.Length;
        var end = message.IndexOf(", AppContext.BaseDirectory=", start, StringComparison.Ordinal);
        if (end < 0)
            end = message.Length;

        return message[start..end];
    }

    private static bool RecoverableVersionLogDiagnosticIsMarked(string message)
    {
        var runId = ExtractRunId(message);
        if (string.IsNullOrWhiteSpace(runId))
            return false;

        var diagnosticJson = Directory
            .GetFiles(ApplicationPaths.DiagnosticsDirectory, $"stage1-preview-versionlog-optional-unavailable-{runId}-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (diagnosticJson == null)
            return false;

        var content = File.ReadAllText(diagnosticJson);
        return content.Contains("\"IsFinalFailure\": false", StringComparison.Ordinal) &&
            content.Contains("\"Recoverable\": true", StringComparison.Ordinal) &&
            content.Contains("\"Phase\": \"VersionLog\"", StringComparison.Ordinal);
    }

    private static string? ExtractRunId(string message)
    {
        const string marker = "RunId=";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        start += marker.Length;
        var end = start;
        while (end < message.Length && message[end] != ',' && !char.IsWhiteSpace(message[end]))
            end++;

        return end > start ? message[start..end] : null;
    }

    private static string? RequireMessageContainsInOrder(string message, params string[] orderedParts)
    {
        var searchStart = 0;
        foreach (var requiredPart in orderedParts)
        {
            var index = message.IndexOf(requiredPart, searchStart, StringComparison.Ordinal);
            if (index < 0)
                return $"Missing ordered message part after index {searchStart}: {requiredPart}.";

            searchStart = index + requiredPart.Length;
        }

        return null;
    }

    private static string? Require(bool condition, string message) =>
        condition ? null : message;

    private static string? RequireNoAction(IReadOnlyList<string> actions, string forbiddenAction) =>
        actions.Contains(forbiddenAction, StringComparer.Ordinal)
            ? $"Unexpected action recorded: {forbiddenAction}."
            : null;

    private static string? RequireNoActionContaining(IReadOnlyList<string> actions, string forbiddenActionPart) =>
        actions.Any(action => action.Contains(forbiddenActionPart, StringComparison.Ordinal))
            ? $"Unexpected action containing '{forbiddenActionPart}' was recorded."
            : null;

    private static string? RequireNoActionBeforeAction(
        IReadOnlyList<string> actions,
        string forbiddenAction,
        string boundaryAction)
    {
        var boundaryIndex = -1;
        for (var i = 0; i < actions.Count; i++)
        {
            if (string.Equals(actions[i], boundaryAction, StringComparison.Ordinal))
            {
                boundaryIndex = i;
                break;
            }
        }
        if (boundaryIndex < 0)
            return $"Boundary action was not recorded: {boundaryAction}.";

        return actions.Take(boundaryIndex).Contains(forbiddenAction, StringComparer.Ordinal)
            ? $"Unexpected action before {boundaryAction}: {forbiddenAction}."
            : null;
    }

    private static string? RequireNoPublishActions(IReadOnlyList<string> actions) =>
        actions.Any(action =>
            (action.StartsWith("click:", StringComparison.Ordinal) ||
                action.StartsWith("input_click:", StringComparison.Ordinal)) &&
            (action.Contains("post", StringComparison.OrdinalIgnoreCase) ||
                action.Contains("publish", StringComparison.OrdinalIgnoreCase) ||
                action.Contains("发布", StringComparison.Ordinal)))
            ? "Unexpected Post/Publish action was recorded."
            : null;

    private static string? RequireActionsInOrder(IReadOnlyList<string> actions, params string[] orderedActions)
    {
        var searchStart = 0;
        foreach (var requiredAction in orderedActions)
        {
            var index = -1;
            for (var i = searchStart; i < actions.Count; i++)
            {
                if (string.Equals(actions[i], requiredAction, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
                return $"Missing ordered action after index {searchStart}: {requiredAction}.";

            searchStart = index + 1;
        }

        return null;
    }

    private static string? RequireActions(IReadOnlyList<string> actions, params string[] requiredActions)
    {
        var missing = requiredActions
            .Where(action => !actions.Contains(action, StringComparer.Ordinal))
            .ToList();

        return missing.Count == 0
            ? null
            : $"Missing actions: {string.Join(", ", missing)}.";
    }

    private sealed record Stage1DryRunScenario(
        string Name,
        IReadOnlyList<ScriptedPageFrame> Frames,
        Func<Stage1DryRunAssertionInput, IEnumerable<string?>> Assert,
        bool SandboxStable = true,
        UploadServer Server = UploadServer.None,
        bool IsUpdate = false,
        string ExistingArchiveIdentifier = "",
        TimeSpan? ClockAdvancePerDelay = null,
        IReadOnlyList<LocalArchiveCardTextResult>? OcrResults = null,
        IReadOnlyList<LocalArchiveMoreButtonLocationResult>? OpenCvMoreButtonResults = null,
        IReadOnlyList<UpdateArchiveButtonLocationResult>? OpenCvUpdateButtonResults = null,
        IReadOnlyList<UpdateStartUploadButtonLocationResult>? OpenCvUpdateStartUploadButtonResults = null,
        IReadOnlyList<UploadedArchiveLaterButtonLocationResult>? OpenCvUploadedArchiveLaterButtonResults = null,
        IReadOnlyList<IReadOnlyList<WorkflowOcrTextResult>>? WorkflowOcrResults = null,
        Action<LevelArchive>? ConfigureArchive = null,
        bool FileDialogFails = false,
        string? FileDialogFailPathContains = null,
        IReadOnlyDictionary<string, int>? ClickFailuresByCoordinate = null,
        IReadOnlyList<bool>? SandboxRunningSequence = null,
        bool SandboxWindowFound = true,
        bool AutoExpandLocalArchiveMenuFrames = true);

    private sealed record Stage1DryRunAssertionInput(
        UploadExecutionResult Result,
        IReadOnlyList<string> Actions,
        IReadOnlyList<UploadTaskState> ProgressStates);
}




