using System;
using System.Runtime.InteropServices;

namespace TbxExecutor;

public static class DpiAwareness
{
    public static string GetCurrentModeString()
    {
        if (!OperatingSystem.IsWindows())
            return "NotApplicable";

        try
        {
            var ctx = GetThreadDpiAwarenessContext();
            if (ctx == IntPtr.Zero)
                return "Unknown";

            if (AreDpiAwarenessContextsEqual(ctx, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                return "PerMonitorV2";
            if (AreDpiAwarenessContextsEqual(ctx, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE))
                return "PerMonitor";
            if (AreDpiAwarenessContextsEqual(ctx, DPI_AWARENESS_CONTEXT_SYSTEM_AWARE))
                return "SystemAware";
            if (AreDpiAwarenessContextsEqual(ctx, DPI_AWARENESS_CONTEXT_UNAWARE))
                return "Unaware";
            if (AreDpiAwarenessContextsEqual(ctx, DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED))
                return "UnawareGdiScaled";

            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    public static bool IsPerMonitorV2()
    {
        return GetCurrentModeString() == "PerMonitorV2";
    }

    // Pseudo-handle values for DPI awareness contexts
    // https://learn.microsoft.com/en-us/windows/win32/hidpi/dpi-awareness-context
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_UNAWARE = (IntPtr)(-1);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = (IntPtr)(-2);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = (IntPtr)(-3);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED = (IntPtr)(-5);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr dpiContextA, IntPtr dpiContextB);
}
