using MiliastraArchiveTool.Automation.Recognition;
using MiliastraArchiveTool.Models;
using MiliastraArchiveTool.Services;

namespace MiliastraArchiveTool.Automation.Workflows;

public sealed partial class Stage1GameUploadExecutor
{
    // Deliberately not InvalidOperationException: the inner submit continuation
    // must let this reach the upload retry loop rather than waiting for the user.
    private sealed class ImageUploadFailedException(StablePageResult page) : Exception("Image upload failure icon confirmed")
    {
        public StablePageResult Page { get; } = page;
    }

    private async Task ThrowIfImageUploadFailedAsync(StablePageResult page, ImageAssetResourceSpec resource,
        List<string> log, CancellationToken ct)
    {
        if (page.Recognition.Page != GamePage.ImageAssetDraftBox) return;
        var first = TextEditIconTemplate.LocateUploadFailure(page.Capture.Bitmap, ct);
        if (first == null)
        {
            LogWorkflow(log, $"step=EditImageAssets field={resource.FieldName} action=CheckUploadFailureIcon firstMatched=False confirmed=False reason=no_template_match");
            return;
        }
        await DelayWithTimingAsync(TimeSpan.FromMilliseconds(350), ct);
        var fresh = await RecognizeOnceAsync(ct);
        var context = await DetectImageAssetDraftBoxContextAsync(fresh, resource, ct);
        if (!context.Evidence.Accepted)
        {
            LogWorkflow(log, $"step=EditImageAssets field={resource.FieldName} action=CheckUploadFailureIcon firstScore={first.Confidence:F3} confirmed=False reason=draft_context_unconfirmed rawPage={fresh.Recognition.Page}");
            return;
        }
        var second = TextEditIconTemplate.LocateUploadFailure(fresh.Capture.Bitmap, ct);
        var confirmed = second != null && TextEditIconTemplate.SameLocation(first, second);
        LogWorkflow(log, $"step=EditImageAssets field={resource.FieldName} action=CheckUploadFailureIcon " +
            $"firstScore={first.Confidence:F3} secondScore={second?.Confidence:F3} confirmed={confirmed}");
        if (confirmed) throw new ImageUploadFailedException(WithContextualPage(fresh, GamePage.ImageAssetDraftBox));
    }

    private Task<StablePageResult> UploadImageAssetWithFailureRetryAsync(UploadRequest request,
        StablePageResult draft, ImageAssetResourceSpec resource, string path, List<string> log, CancellationToken ct)
    {
        var attempts = 0; // Original upload + at most two reuploads. Survives Continue.
        return ContinueStepAsync("Upload image resource: " + resource.FieldName, async fresh =>
        {
            var current = fresh ?? draft;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (attempts < 3)
                    {
                        var inspected = await InspectDraftImageBeforeUploadAsync(current, resource, log, ct);
                        current = inspected.Page;
                        if (inspected.State == DraftImageState.Existing)
                        {
                            LogWorkflow(log, $"step=EditImageAssets field={resource.FieldName} action=SubmitExistingImage reason=hover_text_and_icons_confirmed");
                            return await SubmitImageAssetDraftAsync(request, current, resource, log, ct);
                        }
                        // On confirmed failure the warning itself is the same image
                        // selection surface, even when YOLO misses image_upload_area.
                        var area = FindImageAssetDraftUploadArea(current) ??
                            TextEditIconTemplate.LocateUploadFailure(current.Capture.Bitmap, ct);
                        if (area == null) throw new InvalidOperationException("Image upload area could not be located. Keep the draft page open.");
                        attempts++;
                        LogWorkflow(log, $"step=EditImageAssets field={resource.FieldName} action=ClickUploadArea uploadAttempt={attempts} maxAttempts=3 path=\"{path}\"");
                        await ClickSpecificTargetAsync(current, area, ct);
                        var selectionTimer = System.Diagnostics.Stopwatch.StartNew();
                        LogWorkflow(log, $"step=EditImageAssets field={resource.FieldName} action=AwaitWindowsFileDialog uploadAttempt={attempts} frame={current.Capture.Bitmap.PixelWidth}x{current.Capture.Bitmap.PixelHeight} target=({FormatTarget(area)}) window=({current.Capture.Window})");
                        try { await _fileDialog.SelectFileAsync(current.Capture.Window, path, WindowsFileDialogAutomationService.SelectionTimeout, ct); }
                        catch (Exception ex)
                        {
                            LogWorkflow(log, $"step=EditImageAssets field={resource.FieldName} action=FileDialogFailed uploadAttempt={attempts} elapsedMs={selectionTimer.ElapsedMilliseconds} error=\"{ex.GetType().Name}: {ex.Message}\"");
                            throw;
                        }
                        LogWorkflow(log, $"step=EditImageAssets field={resource.FieldName} action=FileDialogCompleted uploadAttempt={attempts} elapsedMs={selectionTimer.ElapsedMilliseconds}");
                        _activation.RestoreAndActivate(current.Capture.Window);
                        current = await WaitForImageAssetDraftBoxContextAsync(request, resource,
                            $"EditImageAssets.{resource.FieldName}.WaitDraftBoxAfterFile", log, MediumWait, ct);
                    }
                    return await SubmitImageAssetDraftAsync(request, current, resource, log, ct);
                }
                catch (ImageUploadFailedException failure)
                {
                    current = failure.Page;
                    draft = current;
                    LogWorkflow(log, $"step=EditImageAssets field={resource.FieldName} action=UploadFailed uploadAttempt={attempts} maxAttempts=3");
                    SaveDiagnostic(current, $"stage1-image-upload-failed-{resource.FieldName}-{attempts}",
                        isFinalFailure: false, recoverable: true, phase: "EditImageAssets");
                    if (attempts == 0)
                        throw new InvalidOperationException("已有图片提交时出现上传失败图标，请保持当前草稿页后继续检查。");
                    if (attempts >= 3)
                        throw new InvalidOperationException($"图片资源上传失败：{HudField(resource.FieldName)} 已上传 3 次，均检测到失败图标。请检查网络或图片文件后终止任务，或在当前页面手工处理后继续。文件：{path}");
                    ReportOperation($"Retrying image upload: {HudField(resource.FieldName)} ({attempts + 1}/3)");
                    await DelayWithTimingAsync(TimeSpan.FromSeconds(1), ct);
                }
            }
        }, ct, expectedPage: GamePage.ImageAssetDraftBox);
    }
}

