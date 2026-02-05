using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TbxExecutor;

public sealed class WindowsDisplayEnvironmentProvider : IDisplayEnvironmentProvider
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();

        // Enumerate monitors
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, hdc, ref RECT lprc, IntPtr data) =>
        {
            var index = list.Count;

            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();

            if (!GetMonitorInfo(hMonitor, ref mi))
            {
                return true;
            }

            var bounds = RectFrom(mi.rcMonitor);
            var work = RectFrom(mi.rcWork);
            var isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
            var deviceName = mi.szDevice;

            // DPI
            var (dpiX, dpiY, ok) = TryGetDpiForMonitor(hMonitor);
            if (!ok)
            {
                // Fallback: device context caps
                var fallback = GetDpiFromDeviceName(deviceName);
                dpiX = fallback.dpiX;
                dpiY = fallback.dpiY;
            }

            var scaleX = dpiX / 96.0;
            var scaleY = dpiY / 96.0;

            list.Add(new DisplayInfo(
                Index: index,
                DeviceName: deviceName,
                IsPrimary: isPrimary,
                BoundsRectPx: bounds,
                WorkAreaRectPx: work,
                DpiX: dpiX,
                DpiY: dpiY,
                ScaleX: scaleX,
                ScaleY: scaleY));

            return true;
        }, IntPtr.Zero);

        return list;
    }

    public RectPx GetVirtualScreenRectPx()
    {
        // Use system metrics for virtual screen bounds
        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return new RectPx(x, y, w, h);
    }

    private static RectPx RectFrom(RECT r)
    {
        var w = Math.Max(0, r.Right - r.Left);
        var h = Math.Max(0, r.Bottom - r.Top);
        return new RectPx(r.Left, r.Top, w, h);
    }

    private static (int dpiX, int dpiY, bool ok) TryGetDpiForMonitor(IntPtr hMonitor)
    {
        try
        {
            var hr = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY);
            if (hr == 0)
            {
                return ((int)dpiX, (int)dpiY, true);
            }
        }
        catch { }

        return (96, 96, false);
    }

    private static (int dpiX, int dpiY) GetDpiFromDeviceName(string deviceName)
    {
        try
        {
            var hdc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                try
                {
                    var dx = GetDeviceCaps(hdc, LOGPIXELSX);
                    var dy = GetDeviceCaps(hdc, LOGPIXELSY);
                    return (dx > 0 ? dx : 96, dy > 0 ? dy : 96);
                }
                finally
                {
                    DeleteDC(hdc);
                }
            }
        }
        catch { }

        return (96, 96);
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const int LOGPIXELSX = 88;
    private const int LOGPIXELSY = 90;

    private const int MONITORINFOF_PRIMARY = 1;

    // MDT_EFFECTIVE_DPI = 0
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    // GetDpiForMonitor is in shcore.dll (Win 8.1+)
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
