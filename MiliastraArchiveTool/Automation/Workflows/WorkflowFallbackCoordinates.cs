using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Automation.Workflows;

public readonly record struct WorkflowFallbackCoordinate(double NormalizedX, double NormalizedY);

public static class WorkflowFallbackCoordinates
{
    public static readonly WorkflowFallbackCoordinate LoginStartGame = new(0.50, 0.86);
    public static readonly WorkflowFallbackCoordinate LoginServerSelector = new(0.50, 0.72);
    public static readonly WorkflowFallbackCoordinate LoginServerOption = new(0.50, 0.45);
    public static readonly WorkflowFallbackCoordinate LoginServerConfirmButton = new(0.50, 0.72);
    public static readonly WorkflowFallbackCoordinate LoginClickEnter = new(0.50, 0.86);

    public static readonly WorkflowFallbackCoordinate WorldPaimonIcon = new(0.035, 0.055);
    public static readonly WorkflowFallbackCoordinate QianxingRealmEntry = new(0.50, 0.82);

    public static readonly WorkflowFallbackCoordinate LocalArchiveCard = new(0.50, 0.38);
    public static readonly WorkflowFallbackCoordinate ImportArchiveButton = new(0.88, 0.88);
    public static readonly WorkflowFallbackCoordinate LocalArchiveMoreButton = new(0.83, 0.38);
    public static readonly WorkflowFallbackCoordinate LocalArchiveUploadMenuItem = new(0.72, 0.56);
    public static readonly WorkflowFallbackCoordinate LocalArchiveHoverClearArea = new(0.94, 0.88);

    public static readonly WorkflowFallbackCoordinate UploadNewButton = new(0.50, 0.62);
    public static readonly WorkflowFallbackCoordinate UploadFailedCloseButton = new(0.50, 0.74);
    public static readonly WorkflowFallbackCoordinate UploadCompletedDismissArea = new(0.08, 0.16);

    public static readonly WorkflowFallbackCoordinate UploadedImageAssetsButton = new(0.82, 0.40);
    public static readonly WorkflowFallbackCoordinate UploadedArchiveCloseButton = new(0.94, 0.06);

    public static readonly WorkflowFallbackCoordinate PreviewSubmitButton = new(0.84, 0.88);
    public static readonly WorkflowFallbackCoordinate TagConfirmButton = new(0.82, 0.88);
    public static readonly WorkflowFallbackCoordinate TextInputArea = new(0.50, 0.45);
    public static readonly WorkflowFallbackCoordinate TextConfirmButton = new(0.82, 0.88);
    public static readonly WorkflowFallbackCoordinate MultilanguageAddButton = new(0.78, 0.88);
    public static readonly WorkflowFallbackCoordinate MultilanguageCloseButton = new(0.94, 0.06);
    public static readonly WorkflowFallbackCoordinate LanguageConfirmButton = new(0.82, 0.88);
    public static readonly WorkflowFallbackCoordinate ImageAssetDraftBoxButton = new(0.78, 0.86);
    public static readonly WorkflowFallbackCoordinate ImageAssetCloseButton = new(0.94, 0.06);
    public static readonly WorkflowFallbackCoordinate ImageAssetSubmitReviewButton = new(0.86, 0.88);
    public static readonly WorkflowFallbackCoordinate ArchiveEditorExitButton = new(0.055, 0.88);

    public static WorkflowFallbackCoordinate GetServerOption(UploadServer server) => server switch
    {
        UploadServer.Us => new WorkflowFallbackCoordinate(0.50, 0.34),
        UploadServer.Europe => new WorkflowFallbackCoordinate(0.50, 0.45),
        UploadServer.Asia => new WorkflowFallbackCoordinate(0.50, 0.56),
        UploadServer.HkTw => new WorkflowFallbackCoordinate(0.50, 0.67),
        _ => LoginServerOption
    };
}
