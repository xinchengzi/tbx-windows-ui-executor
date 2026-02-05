using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
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

public static class WindowMatchValidator
{
    public static bool IsValidRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

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
        var windows = ListWindows();
        if (windows.Count == 0) return null;

        Regex? titleRegex = null;
        if (!string.IsNullOrWhiteSpace(match.TitleRegex))
        {
            try
            {
                titleRegex = new Regex(
                    match.TitleRegex,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        WindowInfo? best = null;
        foreach (var window in windows)
        {
            if (!Matches(window, match, titleRegex)) continue;
            if (window.IsVisible && !window.IsMinimized)
            {
                best = window;
                break;
            }

            best ??= window;
        }

        if (best is null) return null;

        var hwnd = new IntPtr(best.Hwnd);
        ShowWindow(hwnd, ShowCmdRestore);
        if (!SetForegroundWindow(hwnd))
        {
            if (TryAttachForeground(hwnd, out var foregroundThread, out var targetThread, out var currentThread))
            {
                try
                {
                    _ = SetForegroundWindow(hwnd);
                }
                finally
                {
                    if (foregroundThread != 0)
                    {
                        _ = AttachThreadInput(foregroundThread, currentThread, false);
                    }

                    if (targetThread != 0)
                    {
                        _ = AttachThreadInput(targetThread, currentThread, false);
                    }
                }
            }
        }

        return best;
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

    private static bool Matches(WindowInfo window, WindowMatch match, Regex? titleRegex)
    {
        if (!string.IsNullOrWhiteSpace(match.TitleContains))
        {
            if (window.Title is null || window.Title.IndexOf(
                    match.TitleContains,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        if (titleRegex is not null && !titleRegex.IsMatch(window.Title ?? string.Empty))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(match.ProcessName))
        {
            if (!string.Equals(
                    window.ProcessName,
                    match.ProcessName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAttachForeground(
        IntPtr hwnd,
        out uint foregroundThread,
        out uint targetThread,
        out uint currentThread)
    {
        var foreground = GetForegroundWindow();
        foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        targetThread = GetWindowThreadProcessId(hwnd, out _);
        currentThread = GetCurrentThreadId();

        if (foregroundThread != 0)
        {
            _ = AttachThreadInput(foregroundThread, currentThread, true);
        }

        if (targetThread != 0)
        {
            _ = AttachThreadInput(targetThread, currentThread, true);
        }

        return foregroundThread != 0 || targetThread != 0;
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

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int ShowCmdMinimized = 2;
    private const int ShowCmdRestore = 9;

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
