using System;
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
/// </remarks>
public sealed record CaptureMetadata(
    RectPx RegionRectPx,
    RectPx? WindowRectPx,
    long Ts,
    double Scale,
    int Dpi);

/// <summary>
/// Result of a capture operation.
/// </summary>
public sealed record CaptureResult(
    byte[] ImageBytes,
    CaptureFormat Format,
    CaptureMetadata Metadata);

/// <summary>
/// Interface for screen/window/region capture.
/// </summary>
public interface ICaptureProvider
{
    /// <summary>
    /// Captures an image based on the request parameters.
    /// </summary>
    /// <param name="request">Capture request specifying mode, target, format, etc.</param>
    /// <param name="windowManager">Window manager for resolving window matches.</param>
    /// <returns>CaptureResult on success; null if the target cannot be found or captured.</returns>
    CaptureResult? Capture(CaptureRequest request, IWindowManager windowManager);
}

/// <summary>
/// Null implementation for non-Windows platforms.
/// </summary>
public sealed class NullCaptureProvider : ICaptureProvider
{
    public CaptureResult? Capture(CaptureRequest request, IWindowManager windowManager) => null;
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

    private CaptureResult? CaptureWindow(CaptureRequest request, IWindowManager windowManager)
    {
        if (request.Window is null) return null;

        // Find the window
        var windows = windowManager.ListWindows();
        WindowInfo? target = null;

        foreach (var window in windows)
        {
            if (MatchesWindow(window, request.Window))
            {
                target = window;
                break;
            }
        }

        if (target is null) return null;

        var hwnd = new IntPtr(target.Hwnd);
        if (!GetWindowRect(hwnd, out var rect)) return null;

        var windowRect = new RectPx(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));

        if (windowRect.W <= 0 || windowRect.H <= 0) return null;

        // Try PrintWindow first, then fall back to BitBlt
        var result = TryPrintWindow(hwnd, windowRect, request.Format, request.Quality);
        if (result is not null) return result;

        // Fallback to BitBlt (may not work for off-screen/occluded windows)
        return CaptureRectWithBitBlt(windowRect, windowRect, request.Format, request.Quality);
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

        var targetDisplay = DisplaySelector.ByRectCenter(displays, region);
        if (targetDisplay is not null)
        {
            // Prefer X axis for single-value scale/dpi.
            scale = targetDisplay.ScaleX;
            dpi = targetDisplay.DpiX;
        }

        var metadata = new CaptureMetadata(
            RegionRectPx: region,
            WindowRectPx: windowRect,
            Ts: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Scale: scale,
            Dpi: dpi);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
