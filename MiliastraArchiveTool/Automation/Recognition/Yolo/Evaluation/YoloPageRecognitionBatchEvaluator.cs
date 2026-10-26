using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Automation.Recognition.Yolo.Evaluation;

public sealed class YoloPageRecognitionBatchEvaluator
{
    public const int DefaultMaxImagesPerCategory = 50;
    public const string DefaultOutputDirectoryName = "_page_recognition_eval_stage1";

    private static readonly IReadOnlyList<CategoryExpectation> SupportedCategories =
    [
        new("world_main", GamePage.WorldMain),
        new("main_menu", GamePage.MainMenu),
        new("exit_menu", GamePage.ExitMenu),
        new("local_archive", GamePage.LocalArchive),
        new("local_archive_menu", GamePage.LocalArchiveMenu),
        new("upload_location_dialog", GamePage.UploadLocationDialog),
        new("upload_failed", GamePage.UploadFailed),
        new("uploaded_archive", GamePage.UploadedArchive),
        new("archive_editor_menu", GamePage.ArchiveEditorMenu),
        new("preview_editor", GamePage.PreviewEditor),
        new("tag_dialog", GamePage.TagDialog),
        new("text_edit_dialog", GamePage.TextEditDialog),
        new("multilanguage_editor", GamePage.MultiLanguageEditor),
        new("language_select_dialog", GamePage.LanguageSelectDialog),
        new("image_asset_editor", GamePage.ImageAssetEditor),
        new("image_asset_draft_box", GamePage.ImageAssetDraftBox),
        new("update_archive_select", GamePage.UpdateArchiveSelect),
        new("update_archive_dialog", GamePage.UpdateArchiveDialog),
        new("login_start", GamePage.LoginStart),
        new("login_server_select", GamePage.LoginServerSelect),
        new("login_server_confirm", GamePage.LoginServerConfirm),
        new("login_enter", GamePage.LoginEnter)
    ];

    private static readonly IReadOnlyList<CategoryExpectation> ExpectedUnknownCategories =
    [
        new("login_downloading_resources", null, ExpectedUnknown: true),
        new("transition", null, ExpectedUnknown: true),
        new("archive_uploading", null, ExpectedUnknown: true),
        new("sandbox_not_started", null, ExpectedUnknown: true),
        new("upload_validation_failed", null, ExpectedUnknown: true)
    ];

    public async Task<PageRecognitionBatchReport> EvaluateAsync(
        string? datasetRoot = null,
        string? outputDirectory = null,
        int maxImagesPerCategory = DefaultMaxImagesPerCategory,
        CancellationToken cancellationToken = default)
    {
        if (maxImagesPerCategory <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxImagesPerCategory), maxImagesPerCategory, "Max images per category must be greater than zero.");

        datasetRoot ??= GetDefaultDatasetRoot();
        outputDirectory ??= Path.Combine(datasetRoot, DefaultOutputDirectoryName);

        Directory.CreateDirectory(outputDirectory);

        using var inferenceService = new YoloOnnxInferenceService();
        using var recognizer = new YoloPageRecognizer(inferenceService);

        var expectations = SupportedCategories
            .Concat(ExpectedUnknownCategories)
            .ToList();

        var items = new List<PageRecognitionBatchItem>();
        var skippedCategories = new List<string>();

        foreach (var expectation in expectations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var categoryDirectory = Path.Combine(datasetRoot, expectation.Category);
            if (!Directory.Exists(categoryDirectory))
            {
                skippedCategories.Add(expectation.Category);
                continue;
            }

            var images = Directory
                .EnumerateFiles(categoryDirectory, "*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(maxImagesPerCategory)
                .ToList();

            foreach (var imagePath in images)
            {
                items.Add(await EvaluateImageAsync(
                    recognizer,
                    expectation,
                    imagePath,
                    cancellationToken));
            }
        }

        var totalActionableMatched = items.Count(item => !item.ExpectedUnknown && item.Matched);
        var totalExpectedUnknownMatched = items.Count(item => item.ExpectedUnknown && item.Matched);
        var totalActionableUnknown = items.Count(item =>
            !item.ExpectedUnknown &&
            item.RecognizedPage == GamePage.Unknown &&
            item.ErrorMessage == null);
        var totalActionableMismatched = items.Count(item =>
            !item.ExpectedUnknown &&
            !item.Matched &&
            item.RecognizedPage != GamePage.Unknown &&
            item.ErrorMessage == null);
        var totalUnexpectedRecognized = items.Count(item =>
            item.UnexpectedRecognized &&
            item.ErrorMessage == null);
        var totalErrors = items.Count(item => item.ErrorMessage != null);
        var summaries = BuildSummaries(expectations, items);
        var report = new PageRecognitionBatchReport(
            datasetRoot,
            outputDirectory,
            DateTimeOffset.Now,
            maxImagesPerCategory,
            items.Count,
            totalActionableMatched + totalExpectedUnknownMatched,
            totalActionableMatched,
            totalExpectedUnknownMatched,
            totalActionableUnknown,
            totalActionableMismatched,
            totalUnexpectedRecognized,
            totalErrors,
            summaries,
            items,
            skippedCategories);

        await WriteReportsAsync(report, cancellationToken);
        return report;
    }

    public static string GetDefaultDatasetRoot() => ApplicationPaths.DatasetRoot;

    private static async Task<PageRecognitionBatchItem> EvaluateImageAsync(
        YoloPageRecognizer recognizer,
        CategoryExpectation expectation,
        string imagePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = LoadBitmap(imagePath);
            var recognition = await recognizer.RecognizeAsync(bitmap, cancellationToken);
            var matched = expectation.ExpectedUnknown
                ? recognition.Page == GamePage.Unknown
                : recognition.Page == expectation.ExpectedPage;
            var unexpectedRecognized = expectation.ExpectedUnknown && recognition.Page != GamePage.Unknown;

            return new PageRecognitionBatchItem(
                expectation.Category,
                imagePath,
                expectation.ExpectedPage,
                recognition.Page,
                recognition.Confidence,
                matched,
                expectation.ExpectedUnknown,
                unexpectedRecognized,
                recognition.Targets.Count,
                BuildTopTargets(recognition.Targets),
                null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PageRecognitionBatchItem(
                expectation.Category,
                imagePath,
                expectation.ExpectedPage,
                GamePage.Unknown,
                0,
                false,
                expectation.ExpectedUnknown,
                false,
                0,
                [],
                ex.Message);
        }
    }

    private static BitmapSource LoadBitmap(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var bitmap = decoder.Frames.First();
        bitmap.Freeze();
        return bitmap;
    }

    private static IReadOnlyList<PageRecognitionTopTarget> BuildTopTargets(
        IReadOnlyList<AutomationTarget> targets) =>
        targets
            .OrderByDescending(target => target.Confidence)
            .Take(8)
            .Select(target => new PageRecognitionTopTarget(
                target.Name,
                target.Confidence,
                target.CenterX,
                target.CenterY,
                target.Width,
                target.Height))
            .ToList();

    private static IReadOnlyList<PageRecognitionCategorySummary> BuildSummaries(
        IReadOnlyList<CategoryExpectation> expectations,
        IReadOnlyList<PageRecognitionBatchItem> items)
    {
        var summaries = new List<PageRecognitionCategorySummary>();
        foreach (var expectation in expectations)
        {
            var categoryItems = items
                .Where(item => string.Equals(item.Category, expectation.Category, StringComparison.Ordinal))
                .ToList();

            if (categoryItems.Count == 0)
                continue;

            var errorCount = categoryItems.Count(item => item.ErrorMessage != null);
            var evaluableCount = categoryItems.Count - errorCount;
            var matchedCount = categoryItems.Count(item => item.Matched);
            var unknownCount = categoryItems.Count(item => item.RecognizedPage == GamePage.Unknown && item.ErrorMessage == null);
            var mismatchedCount = categoryItems.Count(item =>
                !item.ExpectedUnknown &&
                !item.Matched &&
                item.RecognizedPage != GamePage.Unknown &&
                item.ErrorMessage == null);
            var accuracy = evaluableCount == 0 ? 0 : (double)matchedCount / evaluableCount;

            summaries.Add(new PageRecognitionCategorySummary(
                expectation.Category,
                expectation.ExpectedPage,
                expectation.ExpectedUnknown,
                categoryItems.Count,
                matchedCount,
                unknownCount,
                mismatchedCount,
                errorCount,
                categoryItems.Count(item => item.UnexpectedRecognized),
                accuracy,
                BuildTopMismatches(categoryItems)));
        }

        return summaries;
    }

    private static IReadOnlyList<string> BuildTopMismatches(IReadOnlyList<PageRecognitionBatchItem> items) =>
        items
            .Where(item => !item.Matched || item.ErrorMessage != null)
            .GroupBy(item => item.ErrorMessage == null
                ? $"{item.ExpectedPage?.ToString() ?? "ExpectedUnknown"} -> {item.RecognizedPage}"
                : $"Error: {item.ErrorMessage}")
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(5)
            .Select(group => $"{group.Key} ({group.Count()})")
            .ToList();

    private static async Task WriteReportsAsync(
        PageRecognitionBatchReport report,
        CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(report.OutputDirectory, "page_recognition_report.json");
        var markdownPath = Path.Combine(report.OutputDirectory, "page_recognition_summary.md");

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            markdownPath,
            BuildMarkdownSummary(report),
            Encoding.UTF8,
            cancellationToken);
    }

    private static string BuildMarkdownSummary(PageRecognitionBatchReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# YOLO Page Recognition Evaluation");
        builder.AppendLine();
        builder.AppendLine($"- DatasetRoot: `{report.DatasetRoot}`");
        builder.AppendLine($"- OutputDirectory: `{report.OutputDirectory}`");
        builder.AppendLine($"- CreatedAt: `{report.CreatedAt:O}`");
        builder.AppendLine($"- MaxImagesPerCategory: `{report.MaxImagesPerCategory}`");
        builder.AppendLine($"- TotalImages: `{report.TotalImages}`");
        builder.AppendLine($"- TotalMatched: `{report.TotalMatched}`");
        builder.AppendLine($"- TotalActionableMatched: `{report.TotalActionableMatched}`");
        builder.AppendLine($"- TotalExpectedUnknownMatched: `{report.TotalExpectedUnknownMatched}`");
        builder.AppendLine($"- TotalActionableUnknown: `{report.TotalActionableUnknown}`");
        builder.AppendLine($"- TotalActionableMismatched: `{report.TotalActionableMismatched}`");
        builder.AppendLine($"- TotalUnexpectedRecognized: `{report.TotalUnexpectedRecognized}`");
        builder.AppendLine($"- TotalErrors: `{report.TotalErrors}`");
        builder.AppendLine();

        if (report.SkippedCategories.Count > 0)
        {
            builder.AppendLine("## Skipped Categories");
            builder.AppendLine();
            foreach (var category in report.SkippedCategories)
                builder.AppendLine($"- `{category}`");
            builder.AppendLine();
        }

        AppendActionablePagesOnlySummary(builder, report);

        builder.AppendLine("## Category Summary");
        builder.AppendLine();
        builder.AppendLine("| Category | Expected | Total | Matched | Unknown | Mismatched | UnexpectedRecognized | Errors | Accuracy | Top mismatches |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");

        foreach (var summary in report.Categories)
        {
            var expected = summary.ExpectedUnknown
                ? "ExpectedUnknown"
                : summary.ExpectedPage?.ToString() ?? "";
            var topMismatches = summary.TopMismatches.Count == 0
                ? ""
                : string.Join("<br>", summary.TopMismatches.Select(EscapeMarkdownCell));

            builder.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| `{summary.Category}` | {expected} | {summary.Total} | {summary.Matched} | {summary.Unknown} | {summary.Mismatched} | {summary.UnexpectedRecognized} | {summary.Errors} | {summary.Accuracy:P1} | {topMismatches} |"));
        }

        builder.AppendLine();
        builder.AppendLine("## Mismatch Details");
        builder.AppendLine();

        var mismatches = report.Items
            .Where(item => !item.Matched || item.ErrorMessage != null)
            .Take(200)
            .ToList();

        if (mismatches.Count == 0)
        {
            builder.AppendLine("No mismatches or errors in the evaluated sample.");
        }
        else
        {
            foreach (var item in mismatches)
            {
                var expected = item.ExpectedUnknown
                    ? "ExpectedUnknown"
                    : item.ExpectedPage?.ToString() ?? "";
                var topTargets = item.TopTargets.Count == 0
                    ? "none"
                    : string.Join(", ", item.TopTargets.Take(4).Select(target => $"{target.Name}:{target.Confidence:0.###}"));
                var error = item.ErrorMessage == null ? "" : $" error=`{item.ErrorMessage}`";

                builder.AppendLine(
                    $"- `{item.Category}` expected={expected} recognized={item.RecognizedPage} confidence={item.Confidence:0.###} targets={item.TargetCount} topTargets={topTargets}{error} file=`{Path.GetFileName(item.ImagePath)}`");
            }
        }

        return builder.ToString();
    }

    private static void AppendActionablePagesOnlySummary(
        StringBuilder builder,
        PageRecognitionBatchReport report)
    {
        var actionableItems = report.Items
            .Where(item => !item.ExpectedUnknown)
            .ToList();
        var total = actionableItems.Count;
        var matched = actionableItems.Count(item => item.Matched);
        var unknown = actionableItems.Count(item =>
            item.RecognizedPage == GamePage.Unknown &&
            item.ErrorMessage == null);
        var mismatched = actionableItems.Count(item =>
            !item.Matched &&
            item.RecognizedPage != GamePage.Unknown &&
            item.ErrorMessage == null);
        var errors = actionableItems.Count(item => item.ErrorMessage != null);
        var evaluable = total - errors;
        var accuracy = evaluable == 0 ? 0 : (double)matched / evaluable;

        builder.AppendLine("## Actionable Pages Only");
        builder.AppendLine();
        builder.AppendLine($"- Total: `{total}`");
        builder.AppendLine($"- Matched: `{matched}`");
        builder.AppendLine($"- Unknown: `{unknown}`");
        builder.AppendLine($"- Mismatched: `{mismatched}`");
        builder.AppendLine($"- Errors: `{errors}`");
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- Accuracy: `{accuracy:P1}`"));
        builder.AppendLine();
    }

    private static string EscapeMarkdownCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);

    private sealed record CategoryExpectation(
        string Category,
        GamePage? ExpectedPage,
        bool ExpectedUnknown = false);
}
