using System;
using System.Runtime.InteropServices;

namespace TbxExecutor;

public interface ILockStateProvider
{
    bool IsLocked();
}

public sealed class NullLockStateProvider : ILockStateProvider
{
    public bool IsLocked() => false;
}

public sealed class WindowsLockStateProvider : ILockStateProvider
{
    public bool IsLocked()
    {
        var desktop = OpenInputDesktop(0, false, DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero)
        {
            // TODO: Investigate better detection when OpenInputDesktop fails.
            return false;
        }

        try
        {
            var size = 0;
            _ = GetUserObjectInformation(desktop, UoiName, IntPtr.Zero, 0, ref size);
            if (size <= 0)
            {
                // TODO: If size lookup fails, consider alternative lock checks.
                return false;
            }

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetUserObjectInformation(desktop, UoiName, buffer, size, ref size))
                {
                    // TODO: Investigate error handling for GetUserObjectInformation failures.
                    return false;
                }

                var name = Marshal.PtrToStringUni(buffer);
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                return string.Equals(name, "Winlogon", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }
    }

    private const int UoiName = 2;
    private const uint DesktopSwitchDesktop = 0x0100;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj,
        int nIndex,
        IntPtr pvInfo,
        int nLength,
        ref int lpnLengthNeeded);
}
