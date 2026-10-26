namespace MiliastraArchiveTool.Automation.Recognition.Yolo;

public static class YoloModelAssetServiceSelfCheck
{
    public static YoloModelAssets Run() =>
        new YoloModelAssetService().LoadStage1Assets();
}
