namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public static class Stage1RecognitionCapabilities
{
    public static readonly IReadOnlySet<GamePage> SafeActionablePages = new HashSet<GamePage>
    {
        GamePage.WorldMain,
        GamePage.MainMenu,
        GamePage.ExitMenu,
        GamePage.LocalArchive,
        GamePage.LocalArchiveMenu,
        GamePage.UploadLocationDialog,
        GamePage.UploadFailed,
        GamePage.UploadedArchive,
        GamePage.PreviewEditor,
        GamePage.TagDialog,
        GamePage.TextEditDialog,
        GamePage.UpdateArchiveSelect,
        GamePage.UpdateArchiveDialog,
        GamePage.ArchiveEditorMenu,
        GamePage.LoginStart,
        GamePage.LoginServerSelect,
        GamePage.LoginServerConfirm,
        GamePage.LoginEnter
    };

    public static readonly IReadOnlySet<GamePage> UnsafeOrUnsupportedPages = new HashSet<GamePage>
    {
        GamePage.MultiLanguageEditor,
        GamePage.LanguageSelectDialog,
        GamePage.ImageAssetEditor,
        GamePage.ImageAssetDraftBox,
        GamePage.ArchiveEditor,
        GamePage.LoginDownloadingResources,
        GamePage.Transition,
        GamePage.ArchiveUploading,
        GamePage.SandboxNotStarted,
        GamePage.UploadValidationFailed,
        GamePage.UploadCompletedPrompt
    };

    public static bool IsSafeForStage1(GamePage page) =>
        SafeActionablePages.Contains(page);

    public static bool IsUnsupportedOrUnsafe(GamePage page) =>
        UnsafeOrUnsupportedPages.Contains(page);

    public static string GetStage1CapabilityReason(GamePage page) => page switch
    {
        GamePage.MultiLanguageEditor or
        GamePage.LanguageSelectDialog or
        GamePage.ImageAssetEditor or
        GamePage.ImageAssetDraftBox =>
            "Stage1 offline evaluation is not accurate enough for this editor or image-assets page; automated operation is disabled.",

        GamePage.PreviewEditor or
        GamePage.TagDialog or
        GamePage.TextEditDialog =>
            "Stage1 preview-info editing is allowed only inside the guarded preview workflow with OCR-confirmed actions.",

        GamePage.ArchiveEditor =>
            "Stage1 does not have reliable trained targets for the archive editor main page; automated operation is disabled.",

        GamePage.ArchiveUploading =>
            "Stage1 has no reliable upload-progress target and offline evaluation can misrecognize this waiting page; waiting must be handled separately.",

        GamePage.SandboxNotStarted =>
            "Stage1 has no reliable YOLO target for sandbox-not-started; sandbox waiting should rely on the process watcher and page fallback.",

        GamePage.Transition or GamePage.LoginDownloadingResources =>
            "Stage1 has no trained target for this waiting page; it should not be judged by YOLO alone.",

        GamePage.UploadValidationFailed =>
            "Stage1 has no reliable validation-failure target yet; treat it as unsupported until dedicated samples are trained.",

        GamePage.UploadCompletedPrompt =>
            "Stage1 merged upload-completed handling into uploaded_archive targets; this prompt is not separately supported.",

        GamePage.ExitMenu =>
            "Stage1 can recognize the exit menu for future multi-server logout flow, but the current upload workflow must not click it.",

        GamePage.LoginStart or
        GamePage.LoginServerSelect or
        GamePage.LoginServerConfirm =>
            "Stage1 can recognize login and server-selection pages for future multi-server workflow; current Stage1 upload still only supports current-server mode.",

        var safe when IsSafeForStage1(safe) =>
            "Stage1 page is allowed for guarded automation.",

        GamePage.Unknown =>
            "Page is unknown.",

        _ =>
            "Stage1 capability has not been declared for this page."
    };
}
