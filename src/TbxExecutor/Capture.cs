using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace TbxExecutor;

/// <summary>
/// Capture mode: screen (full/specific display), window, or region.
/// </summary>
public enum CaptureMode
{
    /// <summary>Capture the primary screen or a specific display.</summary>
    Screen,

    /// <summary>Capture a specific window by hwnd or match criteria.</summary>
    Window,

    /// <summary>Capture a specified rectangular region in physical pixels.</summary>
    Region
}

/// <summary>
/// Output format for captured image.
/// </summary>
public enum CaptureFormat
{
    Png,
    Jpeg
}

/// <summary>
/// Request for capture operation.
/// </summary>
public sealed record CaptureRequest(
    CaptureMode Mode,
    WindowMatch? Window,
    RectPx? Region,
    CaptureFormat Format = CaptureFormat.Png,
    int Quality = 90,
    int? DisplayIndex = null);

/// <summary>
/// Metadata returned with captured image.
/// </summary>
/// <remarks>
/// All coordinates are in physical pixels (DPI-aware).
/// - regionRectPx: The actual captured region in screen coordinates.
/// - windowRectPx: If capturing a window, the window's rect; otherwise null.
/// - ts: Unix timestamp in milliseconds.
/// - scale: Display scale factor for the monitor containing the capture rect center (e.g., 1.75 for 175% scaling).
/// - dpi: Display DPI (X axis) for the monitor containing the capture rect center (typically 96 * scale).
/// - displayIndex: Index of the display used for scale/dpi derivation (null if unknown).
/// - deviceName: Device name of the display (e.g., "\\\\.\\DISPLAY1"); null if unknown.
/// </remarks>
public sealed record CaptureMetadata(
    RectPx RegionRectPx,
    RectPx? WindowRectPx,
    long Ts,
    double Scale,
    int Dpi,
    int? DisplayIndex = null,
    string? DeviceName = null);

/// <summary>
/// Result of a capture operation.
/// </summary>
public sealed record CaptureResult(
    byte[] ImageBytes,
    CaptureFormat Format,
    CaptureMetadata Metadata,
    WindowCaptureInfo? SelectedWindow = null);

/// <summary>
/// Information about a window selected for capture, used for auditing.
/// </summary>
public sealed record WindowCaptureInfo(
    long Hwnd,
    string Title,
    string ProcessName,
    RectPx RectPx,
    bool IsVisible,
    bool IsMinimized,
    int Score);

/// <summary>
/// Capture failure details with diagnostic information.
/// </summary>
public sealed record CaptureFailure(
    string Reason,
    WindowCandidateSummary[]? Candidates = null);

/// <summary>
/// Summary of a candidate window for diagnostic output.
/// </summary>
public sealed record WindowCandidateSummary(
    long Hwnd,
    string Title,
    string ProcessName,
    int Score,
    bool IsVisible,
    bool IsMinimized,
    int Width,
    int Height);

/// <summary>
/// Interface for screen/window/region capture.
/// </summary>
public interface ICaptureProvider
{
    CaptureResult? Capture(CaptureRequest request, IWindowManager windowManager);

    (CaptureResult? Result, CaptureFailure? Failure) CaptureWithDiagnostics(CaptureRequest request, IWindowManager windowManager);
}

/// <summary>
/// Null implementation for non-Windows platforms.
/// </summary>
public sealed class NullCaptureProvider : ICaptureProvider
{
    public CaptureResult? Capture(CaptureRequest request, IWindowManager windowManager) => null;

    public (CaptureResult? Result, CaptureFailure? Failure) CaptureWithDiagnostics(CaptureRequest request, IWindowManager windowManager)
        => (null, new CaptureFailure("NOT_IMPLEMENTED"));
}

/// <summary>
/// Windows implementation using PrintWindow with BitBlt fallback.
/// Assumes Per-Monitor DPI Aware V2 is enabled.
/// All coordinates are in physical pixels.
/// </summary>
public sealed class WindowsCaptureProvider : ICaptureProvider
{
    private readonly IDisplayEnvironmentProvider _displayEnv;

    public WindowsCaptureProvider(IDisplayEnvironmentProvider displayEnv)
    {
        _displayEnv = displayEnv;
    }

    public CaptureResult? Capture(CaptureRequest request, IWindowManager windowManager)
    {
        return request.Mode switch
        {
            CaptureMode.Screen => CaptureScreen(request),
            CaptureMode.Window => CaptureWindow(request, windowManager),
            CaptureMode.Region => CaptureRegion(request),
            _ => null
        };
    }

    public (CaptureResult? Result, CaptureFailure? Failure) CaptureWithDiagnostics(CaptureRequest request, IWindowManager windowManager)
    {
        return request.Mode switch
        {
            CaptureMode.Screen => (CaptureScreen(request), null),
            CaptureMode.Window => CaptureWindowWithDiagnostics(request, windowManager),
            CaptureMode.Region => (CaptureRegion(request), null),
            _ => (null, new CaptureFailure("UNKNOWN_MODE"))
        };
    }

    private CaptureResult? CaptureScreen(CaptureRequest request)
    {
        // Capture primary monitor by default, or a specific display when displayIndex is provided.
        var displays = _displayEnv.GetDisplays();
        RectPx rect;

        if (request.DisplayIndex is int idx)
        {
            var d = displays.FirstOrDefault(x => x.Index == idx);
            if (d is null) return null;
            rect = d.BoundsRectPx;
        }
        else
        {
            var primary = displays.FirstOrDefault(x => x.IsPrimary);
            rect = primary?.BoundsRectPx ?? _displayEnv.GetVirtualScreenRectPx();
        }

        if (rect.W <= 0 || rect.H <= 0) return null;
        return CaptureRectWithBitBlt(rect, null, request.Format, request.Quality);
    }

    private (CaptureResult? Result, CaptureFailure? Failure) CaptureWindowWithDiagnostics(CaptureRequest request, IWindowManager windowManager)
    {
        if (request.Window is null)
            return (null, new CaptureFailure("NO_WINDOW_MATCH_PROVIDED"));

        var windows = windowManager.ListWindows();
        var scored = ScoreAndRankWindows(windows, request.Window);

        if (scored.Count == 0)
            return (null, new CaptureFailure("NO_MATCHING_WINDOWS", GetTopCandidates(windows, 5)));

        var target = scored[0].Window;
        var targetScore = scored[0].Score;

        var hwnd = new IntPtr(target.Hwnd);
        if (!GetWindowRect(hwnd, out var rect))
            return (null, new CaptureFailure("WINDOW_RECT_UNAVAILABLE", GetScoredCandidates(scored, 5)));

        var windowRect = new RectPx(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));

        if (windowRect.W <= 0 || windowRect.H <= 0)
            return (null, new CaptureFailure("WINDOW_RECT_INVALID", GetScoredCandidates(scored, 5)));

        var result = TryPrintWindow(hwnd, windowRect, request.Format, request.Quality);
        if (result is null)
            result = CaptureRectWithBitBlt(windowRect, windowRect, request.Format, request.Quality);

        if (result is null)
            return (null, new CaptureFailure("CAPTURE_OPERATION_FAILED", GetScoredCandidates(scored, 5)));

        var selectedWindow = new WindowCaptureInfo(
            target.Hwnd,
            target.Title,
            target.ProcessName,
            windowRect,
            target.IsVisible,
            target.IsMinimized,
            targetScore);

        return (result with { SelectedWindow = selectedWindow }, null);
    }

    private CaptureResult? CaptureWindow(CaptureRequest request, IWindowManager windowManager)
    {
        var (result, _) = CaptureWindowWithDiagnostics(request, windowManager);
        return result;
    }

    private List<(WindowInfo Window, int Score)> ScoreAndRankWindows(IReadOnlyList<WindowInfo> windows, WindowMatch match)
    {
        var scored = new List<(WindowInfo Window, int Score)>();

        foreach (var window in windows)
        {
            if (!MatchesWindow(window, match)) continue;

            int score = 0;

            // Visible and non-minimized: +100
            if (window.IsVisible && !window.IsMinimized) score += 100;
            else if (window.IsVisible) score += 50;

            // Valid rect (w/h > 0): +50
            if (window.RectPx.W > 0 && window.RectPx.H > 0) score += 50;

            // Larger window area: +0~30 (normalized)
            var area = window.RectPx.W * window.RectPx.H;
            if (area > 0) score += Math.Min(30, area / 100000);

            // Title match quality: +20 for exact contains, +10 for regex
            if (!string.IsNullOrWhiteSpace(match.TitleContains) &&
                window.Title?.IndexOf(match.TitleContains, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 20;
            }

            // ProcessName exact match: +30
            if (!string.IsNullOrWhiteSpace(match.ProcessName) &&
                string.Equals(window.ProcessName, match.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
            }

            // Check if foreground window (hwnd matches foreground)
            var foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && window.Hwnd == foreground.ToInt64())
            {
                score += 200;
            }

            scored.Add((window, score));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored;
    }

    private static WindowCandidateSummary[] GetScoredCandidates(List<(WindowInfo Window, int Score)> scored, int limit)
    {
        var result = new WindowCandidateSummary[Math.Min(limit, scored.Count)];
        for (int i = 0; i < result.Length; i++)
        {
            var (w, s) = scored[i];
            result[i] = new WindowCandidateSummary(
                w.Hwnd,
                TruncateTitle(w.Title, 60),
                w.ProcessName,
                s,
                w.IsVisible,
                w.IsMinimized,
                w.RectPx.W,
                w.RectPx.H);
        }
        return result;
    }

    private static WindowCandidateSummary[] GetTopCandidates(IReadOnlyList<WindowInfo> windows, int limit)
    {
        var visible = windows
            .Where(w => w.IsVisible && !w.IsMinimized && w.RectPx.W > 0 && w.RectPx.H > 0)
            .Take(limit)
            .Select(w => new WindowCandidateSummary(
                w.Hwnd,
                TruncateTitle(w.Title, 60),
                w.ProcessName,
                0,
                w.IsVisible,
                w.IsMinimized,
                w.RectPx.W,
                w.RectPx.H))
            .ToArray();
        return visible;
    }

    private static string TruncateTitle(string? title, int maxLen)
    {
        if (string.IsNullOrEmpty(title)) return string.Empty;
        return title.Length <= maxLen ? title : title.Substring(0, maxLen - 3) + "...";
    }

    private CaptureResult? CaptureRegion(CaptureRequest request)
    {
        if (request.Region is null) return null;

        var region = request.Region;
        if (region.W <= 0 || region.H <= 0) return null;

        return CaptureRectWithBitBlt(region, null, request.Format, request.Quality);
    }

    private CaptureResult? TryPrintWindow(IntPtr hwnd, RectPx windowRect, CaptureFormat format, int quality)
    {
        try
        {
            using var bitmap = new Bitmap(windowRect.W, windowRect.H, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);

            var hdc = graphics.GetHdc();
            try
            {
                // PW_RENDERFULLCONTENT = 2: includes layered windows, DirectX content
                var success = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                if (!success)
                {
                    // Try without the flag
                    success = PrintWindow(hwnd, hdc, 0);
                }

                if (!success) return null;
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            return CreateResult(bitmap, windowRect, windowRect, format, quality);
        }
        catch
        {
            return null;
        }
    }

    private CaptureResult? CaptureRectWithBitBlt(RectPx region, RectPx? windowRect, CaptureFormat format, int quality)
    {
        try
        {
            var hdcScreen = GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return null;

            try
            {
                var hdcMem = CreateCompatibleDC(hdcScreen);
                if (hdcMem == IntPtr.Zero) return null;

                try
                {
                    var hBitmap = CreateCompatibleBitmap(hdcScreen, region.W, region.H);
                    if (hBitmap == IntPtr.Zero) return null;

                    try
                    {
                        var hOld = SelectObject(hdcMem, hBitmap);
                        try
                        {
                            // SRCCOPY = 0x00CC0020
                            if (!BitBlt(hdcMem, 0, 0, region.W, region.H,
                                hdcScreen, region.X, region.Y, SRCCOPY))
                            {
                                return null;
                            }

                            using var bitmap = Image.FromHbitmap(hBitmap);
                            return CreateResult(bitmap, region, windowRect, format, quality);
                        }
                        finally
                        {
                            SelectObject(hdcMem, hOld);
                        }
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
                finally
                {
                    DeleteDC(hdcMem);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }
        catch
        {
            return null;
        }
    }

    private CaptureResult CreateResult(Bitmap bitmap, RectPx region, RectPx? windowRect, CaptureFormat format, int quality)
    {
        using var ms = new MemoryStream();

        if (format == CaptureFormat.Jpeg)
        {
            var encoder = GetEncoder(ImageFormat.Jpeg);
            if (encoder is not null)
            {
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                bitmap.Save(ms, encoder, encoderParams);
            }
            else
            {
                bitmap.Save(ms, ImageFormat.Jpeg);
            }
        }
        else
        {
            bitmap.Save(ms, ImageFormat.Png);
        }

        var displays = _displayEnv.GetDisplays();
        var scale = 1.0;
        var dpi = 96;
        int? displayIndex = null;
        string? deviceName = null;

        var targetDisplay = DisplaySelector.ByRectCenter(displays, region);
        if (targetDisplay is not null)
        {
            scale = targetDisplay.ScaleX;
            dpi = targetDisplay.DpiX;
            displayIndex = targetDisplay.Index;
            deviceName = targetDisplay.DeviceName;
        }

        var metadata = new CaptureMetadata(
            RegionRectPx: region,
            WindowRectPx: windowRect,
            Ts: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Scale: scale,
            Dpi: dpi,
            DisplayIndex: displayIndex,
            DeviceName: deviceName);

        return new CaptureResult(ms.ToArray(), format, metadata);
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid) return codec;
        }
        return null;
    }

    // (removed) GetSystemScale/GetSystemDpi: metadata now uses per-monitor DPI from WindowsDisplayEnvironmentProvider

    private static bool MatchesWindow(WindowInfo window, WindowMatch match)
    {
        if (!string.IsNullOrWhiteSpace(match.TitleContains))
        {
            if (window.Title is null ||
                window.Title.IndexOf(match.TitleContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(match.TitleRegex))
        {
            try
            {
                var regex = new System.Text.RegularExpressions.Regex(
                    match.TitleRegex,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);

                if (!regex.IsMatch(window.Title ?? string.Empty)) return false;
            }
            catch
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(match.ProcessName))
        {
            if (!string.Equals(window.ProcessName, match.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    // P/Invoke declarations
    private const int LOGPIXELSX = 88;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint PW_RENDERFULLCONTENT = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
