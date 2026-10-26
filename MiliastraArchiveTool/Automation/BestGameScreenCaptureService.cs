using MiliastraArchiveTool.Services;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace MiliastraArchiveTool.Automation;

public sealed record GameCaptureResult(
    GameWindowInfo Window,
    BitmapSource Bitmap,
    string CaptureMethod,
    double GdiQuality,
    double? PrintWindowQuality,
    double? DxgiQuality,
    string? Warning,
    GameCaptureStrategy Strategy = GameCaptureStrategy.DefaultBest,
    IReadOnlyList<GameCaptureCandidateQuality>? CandidateQualities = null,
    bool IsLikelyBlackFrame = false,
    bool FallbackToGdi = false);

public enum GameCaptureStrategy
{
    DefaultBest,
    LiveGameplay,
    LoginOrLoading,
    DiagnosticsAllCandidates
}

public sealed record GameCaptureCandidateQuality(
    string Method,
    double? Quality,
    bool Succeeded,
    bool IsLikelyBlackFrame,
    string? ErrorMessage);

public sealed class BestGameScreenCaptureService
{
    private readonly GameWindowCaptureService _windowCapture = new();
    private readonly DesktopDuplicationCaptureService _desktopCapture = new();

    public GameCaptureResult Capture()
    {
        return Capture(GameCaptureStrategy.DefaultBest);
    }

    public GameCaptureResult Capture(GameCaptureStrategy strategy)
    {
        using var performanceTiming = MiliastraArchiveTool.Infrastructure.WorkflowPerformanceTrace.Measure("ScreenCapture", new { Strategy = strategy.ToString() });
        return strategy switch
        {
            GameCaptureStrategy.DefaultBest => CaptureDefaultBest(),
            GameCaptureStrategy.LiveGameplay => CaptureLiveGameplay(),
            GameCaptureStrategy.LoginOrLoading => CaptureLoginOrLoading(),
            GameCaptureStrategy.DiagnosticsAllCandidates => CaptureDiagnosticsAllCandidates(),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown game capture strategy.")
        };
    }

    public GameCaptureResult CaptureDefaultBest()
    {
        using var overlayGuard = OverlayCaptureGuard.Begin();
        var window = _windowCapture.FindGameWindow();
        BitmapSource? selectedBitmap = null;
        Exception? gdiError = null;
        var selectedMethod = "GDI client area";
        var selectedQuality = 0.0;
        var gdiQuality = 0.0;
        double? printWindowQuality = null;
        double? dxgiQuality = null;
        var warnings = new List<string>();
        var candidates = new List<GameCaptureCandidateQuality>();

        try
        {
            selectedBitmap = _windowCapture.CaptureClientArea(window);
            gdiQuality = GameWindowCaptureService.CalculateVisualQuality(selectedBitmap);
            candidates.Add(Quality("GDI client area", selectedBitmap, gdiQuality));
            selectedQuality = gdiQuality;
            Debug.WriteLine($"Capture candidate: GDI client area, quality={gdiQuality:F2}");
        }
        catch (Exception ex)
        {
            gdiError = ex;
            candidates.Add(FailedQuality("GDI client area", ex));
            warnings.Add($"GDI capture failed: {ex.Message}");
        }

        if (window.IsFullscreenLike || selectedBitmap == null || gdiQuality < 1.5)
        {
            TryDesktopClientCrop(window, ref selectedBitmap, ref selectedQuality, ref selectedMethod, warnings, candidates);
            TryPrintWindow(window, ref selectedBitmap, ref selectedQuality, ref selectedMethod, ref printWindowQuality, warnings, candidates);
            TryDxgiCapture(window, ref selectedBitmap, ref selectedQuality, ref selectedMethod, ref dxgiQuality, warnings, candidates);
        }

        if (selectedBitmap == null)
            throw new InvalidOperationException(string.Join("; ", warnings), gdiError);

        if (selectedQuality < 1.5)
        {
            warnings.Add(
                $"All capture candidates have low quality. Selected {selectedMethod}, quality={selectedQuality:F2}; the image may be black or obscured.");
        }

        Debug.WriteLine($"Final capture method: {selectedMethod}, quality={selectedQuality:F2}");
        return new GameCaptureResult(
            window,
            selectedBitmap,
            selectedMethod,
            gdiQuality,
            printWindowQuality,
            dxgiQuality,
            warnings.Count == 0 ? null : string.Join("; ", warnings),
            GameCaptureStrategy.DefaultBest,
            candidates,
            GameWindowCaptureService.IsLikelyBlackFrame(selectedBitmap),
            string.Equals(selectedMethod, "GDI client area", StringComparison.Ordinal));
    }

    public GameCaptureResult CaptureLiveGameplay()
    {
        using var overlayGuard = OverlayCaptureGuard.Begin();
        var window = _windowCapture.FindGameWindow();
        BitmapSource? selectedBitmap = null;
        var selectedMethod = string.Empty;
        var selectedQuality = 0.0;
        var gdiQuality = 0.0;
        double? printWindowQuality = null;
        double? dxgiQuality = null;
        var warnings = new List<string>();
        var candidates = new List<GameCaptureCandidateQuality>();

        TryDesktopClientCrop(window, ref selectedBitmap, ref selectedQuality, ref selectedMethod, warnings, candidates, preferFirstUsable: true);
        if (selectedBitmap == null || selectedQuality < 1.5)
            TryDxgiCapture(window, ref selectedBitmap, ref selectedQuality, ref selectedMethod, ref dxgiQuality, warnings, candidates, preferFirstUsable: true);

        if (selectedBitmap == null || selectedQuality < 1.5)
            TryPrintWindow(window, ref selectedBitmap, ref selectedQuality, ref selectedMethod, ref printWindowQuality, warnings, candidates);

        if (selectedBitmap == null || selectedQuality < 1.5)
        {
            try
            {
                var bitmap = _windowCapture.CaptureClientArea(window);
                gdiQuality = GameWindowCaptureService.CalculateVisualQuality(bitmap);
                candidates.Add(Quality("GDI client area", bitmap, gdiQuality));
                if (selectedBitmap == null || gdiQuality > selectedQuality)
                {
                    selectedBitmap = bitmap;
                    selectedQuality = gdiQuality;
                    selectedMethod = "GDI client area";
                }
            }
            catch (Exception ex)
            {
                candidates.Add(FailedQuality("GDI client area", ex));
                warnings.Add($"GDI capture failed: {ex.Message}");
            }
        }

        if (selectedBitmap == null)
            throw new InvalidOperationException(string.Join("; ", warnings));

        var fallbackToGdi = string.Equals(selectedMethod, "GDI client area", StringComparison.Ordinal);
        if (fallbackToGdi)
            warnings.Add("LiveGameplay fell back to GDI client area; GDI may be stale for DirectX gameplay.");

        if (selectedQuality < 1.5)
            warnings.Add($"Selected LiveGameplay candidate has low quality={selectedQuality:F2}; the image may be black or obscured.");

        return new GameCaptureResult(
            window,
            selectedBitmap,
            selectedMethod,
            gdiQuality,
            printWindowQuality,
            dxgiQuality,
            warnings.Count == 0 ? null : string.Join("; ", warnings),
            GameCaptureStrategy.LiveGameplay,
            candidates,
            GameWindowCaptureService.IsLikelyBlackFrame(selectedBitmap),
            fallbackToGdi);
    }

    public GameCaptureResult CaptureLoginOrLoading()
    {
        var result = CaptureDiagnosticsAllCandidates();
        return result with { Strategy = GameCaptureStrategy.LoginOrLoading };
    }

    public GameCaptureResult CaptureDiagnosticsAllCandidates()
    {
        using var overlayGuard = OverlayCaptureGuard.Begin();
        var window = _windowCapture.FindGameWindow();
        BitmapSource? selectedBitmap = null;
        var selectedMethod = string.Empty;
        var selectedQuality = 0.0;
        var gdiQuality = 0.0;
        double? printWindowQuality = null;
        double? dxgiQuality = null;
        var warnings = new List<string>();
        var candidates = new List<GameCaptureCandidateQuality>();

        try
        {
            var bitmap = _windowCapture.CaptureClientArea(window);
            gdiQuality = GameWindowCaptureService.CalculateVisualQuality(bitmap);
            candidates.Add(Quality("GDI client area", bitmap, gdiQuality));
            selectedBitmap = bitmap;
            selectedMethod = "GDI client area";
            selectedQuality = gdiQuality;
        }
        catch (Exception ex)
        {
            candidates.Add(FailedQuality("GDI client area", ex));
            warnings.Add($"GDI capture failed: {ex.Message}");
        }

        TryDesktopClientCrop(window, ref selectedBitmap, ref selectedQuality, ref selectedMethod, warnings, candidates);
        TryPrintWindow(window, ref selectedBitmap, ref selectedQuality, ref selectedMethod, ref printWindowQuality, warnings, candidates);
        TryDxgiCapture(window, ref selectedBitmap, ref selectedQuality, ref selectedMethod, ref dxgiQuality, warnings, candidates);

        if (selectedBitmap == null)
            throw new InvalidOperationException(string.Join("; ", warnings));

        if (selectedQuality < 1.5)
            warnings.Add($"All capture candidates have low quality. Selected {selectedMethod}, quality={selectedQuality:F2}; the image may be black or obscured.");

        return new GameCaptureResult(
            window,
            selectedBitmap,
            selectedMethod,
            gdiQuality,
            printWindowQuality,
            dxgiQuality,
            warnings.Count == 0 ? null : string.Join("; ", warnings),
            GameCaptureStrategy.DiagnosticsAllCandidates,
            candidates,
            GameWindowCaptureService.IsLikelyBlackFrame(selectedBitmap),
            string.Equals(selectedMethod, "GDI client area", StringComparison.Ordinal));
    }

    public string SaveDiagnosticScreenshot(string diagnosticsDirectory, string fileNamePrefix = "game-window")
    {
        var result = Capture();
        Directory.CreateDirectory(diagnosticsDirectory);
        var path = Path.Combine(diagnosticsDirectory, $"{fileNamePrefix}-{DateTime.Now:HHmmssfff}.png");
        GameWindowCaptureService.SavePng(result.Bitmap, path);
        return path;
    }

    private void TryDesktopClientCrop(
        GameWindowInfo window,
        ref BitmapSource? selectedBitmap,
        ref double selectedQuality,
        ref string selectedMethod,
        List<string> warnings,
        List<GameCaptureCandidateQuality> candidates,
        bool preferFirstUsable = false)
    {
        try
        {
            var bitmap = _windowCapture.CaptureClientAreaFromScreen(window);
            var screenQuality = GameWindowCaptureService.CalculateVisualQuality(bitmap);
            candidates.Add(Quality("Desktop client crop", bitmap, screenQuality));
            Debug.WriteLine($"Capture candidate: desktop client crop, quality={screenQuality:F2}");
            if (selectedBitmap == null || screenQuality > selectedQuality || (preferFirstUsable && screenQuality >= 1.5))
            {
                selectedBitmap = bitmap;
                selectedQuality = screenQuality;
                selectedMethod = "Desktop client crop";
            }
        }
        catch (Exception ex)
        {
            candidates.Add(FailedQuality("Desktop client crop", ex));
            warnings.Add($"Desktop client crop failed: {ex.Message}");
        }
    }

    private void TryPrintWindow(
        GameWindowInfo window,
        ref BitmapSource? selectedBitmap,
        ref double selectedQuality,
        ref string selectedMethod,
        ref double? printWindowQuality,
        List<string> warnings,
        List<GameCaptureCandidateQuality> candidates)
    {
        try
        {
            var bitmap = _windowCapture.CaptureClientAreaWithPrintWindow(window);
            printWindowQuality = GameWindowCaptureService.CalculateVisualQuality(bitmap);
            candidates.Add(Quality("PrintWindow", bitmap, printWindowQuality.Value));
            Debug.WriteLine($"Capture candidate: PrintWindow, quality={printWindowQuality:F2}");
            if (selectedBitmap == null || printWindowQuality > selectedQuality)
            {
                selectedBitmap = bitmap;
                selectedQuality = printWindowQuality.Value;
                selectedMethod = "PrintWindow";
            }
        }
        catch (Exception ex)
        {
            candidates.Add(FailedQuality("PrintWindow", ex));
            warnings.Add($"PrintWindow capture failed: {ex.Message}");
        }
    }

    private void TryDxgiCapture(
        GameWindowInfo window,
        ref BitmapSource? selectedBitmap,
        ref double selectedQuality,
        ref string selectedMethod,
        ref double? dxgiQuality,
        List<string> warnings,
        List<GameCaptureCandidateQuality> candidates,
        bool preferFirstUsable = false)
    {
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var desktopFrame = _desktopCapture.CaptureMonitor(window.Handle);
                var bitmap = DesktopDuplicationCaptureService.CropToClientArea(desktopFrame, window);
                var quality = GameWindowCaptureService.CalculateVisualQuality(bitmap);
                dxgiQuality = dxgiQuality.HasValue ? Math.Max(dxgiQuality.Value, quality) : quality;
                candidates.Add(Quality($"DXGI Desktop Duplication frame {attempt}", bitmap, quality));
                Debug.WriteLine($"Capture candidate: DXGI Desktop Duplication frame {attempt}, quality={quality:F2}");
                if (selectedBitmap == null || quality > selectedQuality || (preferFirstUsable && quality >= 1.5))
                {
                    selectedBitmap = bitmap;
                    selectedQuality = quality;
                    selectedMethod = "DXGI Desktop Duplication";
                }

                if (quality >= 1.5)
                    break;

                Thread.Sleep(120);
            }
        }
        catch (Exception ex)
        {
            candidates.Add(FailedQuality("DXGI Desktop Duplication", ex));
            warnings.Add($"DXGI capture failed: {ex.Message}");
        }
    }

    private static GameCaptureCandidateQuality Quality(string method, BitmapSource bitmap, double quality) =>
        new(method, quality, true, GameWindowCaptureService.IsLikelyBlackFrame(bitmap), null);

    private static GameCaptureCandidateQuality FailedQuality(string method, Exception ex) =>
        new(method, null, false, true, ex.Message);
}

