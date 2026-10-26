namespace MiliastraArchiveTool.Automation.Recognition;

public enum GamePage
{
    Unknown,
    LoginStart,
    LoginServerSelect,
    LoginServerConfirm,
    LoginDownloadingResources,
    LoginEnter,
    Transition,
    WorldMain,
    MainMenu,
    ExitMenu,
    LocalArchive,
    LocalArchiveMenu,
    UploadLocationDialog,
    ArchiveUploading,
    SandboxNotStarted,
    UploadFailed,
    UploadCompletedPrompt,
    UploadedArchive,
    PreviewEditor,
    TagDialog,
    TextEditDialog,
    MultiLanguageEditor,
    LanguageSelectDialog,
    ImageAssetEditor,
    ImageAssetDraftBox,
    ArchiveEditor,
    ArchiveEditorMenu,
    UpdateArchiveSelect,
    UpdateArchiveDialog,
    UploadValidationFailed
}

public enum PageKind
{
    Actionable,
    Waiting,
    Blocking,
    Unknown
}

public static class GamePageExtensions
{
    public static PageKind GetKind(this GamePage page) => page switch
    {
        GamePage.LoginStart or
        GamePage.LoginServerSelect or
        GamePage.LoginServerConfirm or
        GamePage.LoginEnter or
        GamePage.WorldMain or
        GamePage.MainMenu or
        GamePage.ExitMenu or
        GamePage.LocalArchive or
        GamePage.LocalArchiveMenu or
        GamePage.UploadLocationDialog or
        GamePage.UploadedArchive or
        GamePage.PreviewEditor or
        GamePage.TagDialog or
        GamePage.TextEditDialog or
        GamePage.MultiLanguageEditor or
        GamePage.LanguageSelectDialog or
        GamePage.ImageAssetEditor or
        GamePage.ImageAssetDraftBox or
        GamePage.ArchiveEditor or
        GamePage.ArchiveEditorMenu or
        GamePage.UpdateArchiveSelect or
        GamePage.UpdateArchiveDialog => PageKind.Actionable,

        GamePage.LoginDownloadingResources or
        GamePage.Transition or
        GamePage.ArchiveUploading or
        GamePage.SandboxNotStarted or
        GamePage.UploadCompletedPrompt => PageKind.Waiting,

        GamePage.UploadFailed or
        GamePage.UploadValidationFailed => PageKind.Blocking,

        GamePage.Unknown => PageKind.Unknown,
        _ => PageKind.Unknown
    };

    public static bool IsActionable(this GamePage page) =>
        page.GetKind() == PageKind.Actionable;

    public static bool IsWaiting(this GamePage page) =>
        page.GetKind() == PageKind.Waiting;

    public static bool IsBlocking(this GamePage page) =>
        page.GetKind() == PageKind.Blocking;
}
