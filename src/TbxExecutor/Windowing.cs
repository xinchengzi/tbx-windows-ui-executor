using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TbxExecutor;

public sealed record RectPx(int X, int Y, int W, int H);

public sealed record WindowInfo(
    long Hwnd,
    string Title,
    string ProcessName,
    RectPx RectPx,
    bool IsVisible,
    bool IsMinimized);

public sealed record WindowMatch(string? TitleContains, string? TitleRegex, string? ProcessName);

public sealed record WindowFocusRequest(WindowMatch? Match);

public interface IWindowManager
{
    IReadOnlyList<WindowInfo> ListWindows();

    // TODO: Implement focus logic matching by title/process and bringing window to foreground.
    WindowInfo? FocusWindow(WindowMatch match);
}

public sealed class NullWindowManager : IWindowManager
{
    public IReadOnlyList<WindowInfo> ListWindows() => Array.Empty<WindowInfo>();

    public WindowInfo? FocusWindow(WindowMatch match) => null;
}

public sealed class WindowsWindowManager : IWindowManager
{
    public IReadOnlyList<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == IntPtr.Zero) return true;

            var title = GetWindowTitle(hwnd);
            var isVisible = IsWindowVisible(hwnd);
            var isMinimized = IsMinimizedWindow(hwnd);
            if (!GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var processName = GetProcessName(hwnd);
            var rectPx = new RectPx(
                rect.Left,
                rect.Top,
                Math.Max(0, rect.Right - rect.Left),
                Math.Max(0, rect.Bottom - rect.Top));

            windows.Add(new WindowInfo(
                hwnd.ToInt64(),
                title,
                processName,
                rectPx,
                isVisible,
                isMinimized));

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public WindowInfo? FocusWindow(WindowMatch match)
    {
        return null;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;
        var buffer = new char[length + 1];
        var copied = GetWindowText(hwnd, buffer, buffer.Length);
        if (copied <= 0) return string.Empty;
        return new string(buffer, 0, copied);
    }

    private static string GetProcessName(IntPtr hwnd)
    {
        if (GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0) return string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsMinimizedWindow(IntPtr hwnd)
    {
        var placement = new WINDOWPLACEMENT();
        placement.length = Marshal.SizeOf<WINDOWPLACEMENT>();
        if (!GetWindowPlacement(hwnd, ref placement)) return false;
        return placement.showCmd == ShowCmdMinimized;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    private const int ShowCmdMinimized = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }
}
