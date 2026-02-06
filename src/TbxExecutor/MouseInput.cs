using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TbxExecutor;

public sealed record MouseInputRequest(
    string Kind,
    int? X,
    int? Y,
    string? Button,
    int? Dx,
    int? Dy,
    int? X2,
    int? Y2,
    MouseHumanize? Humanize);

public sealed record MouseHumanize(int? JitterPx, int[]? DelayMs);

public interface IMouseInputProvider
{
    MouseInputResult Execute(MouseInputRequest request);
}

public sealed record MouseInputResult(bool Ok, string? Error = null, int? StatusCode = null, int? LastError = null);

public sealed class NullMouseInputProvider : IMouseInputProvider
{
    public MouseInputResult Execute(MouseInputRequest request) =>
        new(false, "NOT_IMPLEMENTED", 501);
}

public sealed class WindowsMouseInputProvider : IMouseInputProvider
{
    private readonly Random _random = new();
    private int _lastWin32Error;

    public MouseInputResult Execute(MouseInputRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Kind))
        {
            return new(false, "BAD_REQUEST: kind is required", 400);
        }

        var kind = request.Kind.ToLowerInvariant();

        switch (kind)
        {
            case "move":
                if (request.X is null || request.Y is null)
                    return new(false, "BAD_REQUEST: x and y are required for move", 400);
                break;

            case "click":
            case "double":
            case "right":
                if (request.X is null || request.Y is null)
                    return new(false, "BAD_REQUEST: x and y are required for click", 400);
                break;

            case "wheel":
                break;

            case "drag":
                if (request.X is null || request.Y is null)
                    return new(false, "BAD_REQUEST: x and y are required for drag", 400);
                if (request.X2 is null || request.Y2 is null)
                    return new(false, "BAD_REQUEST: x2 and y2 are required for drag", 400);
                break;

            default:
                return new(false, $"BAD_REQUEST: unknown kind '{request.Kind}'", 400);
        }

        ApplyHumanizeDelay(request.Humanize);

        try
        {
            return kind switch
            {
                "move" => DoMove(request.X!.Value, request.Y!.Value, request.Humanize),
                "click" => DoClick(request.X!.Value, request.Y!.Value, GetButton(request.Button), 1, request.Humanize),
                "double" => DoClick(request.X!.Value, request.Y!.Value, GetButton(request.Button), 2, request.Humanize),
                "right" => DoClick(request.X!.Value, request.Y!.Value, MouseButton.Right, 1, request.Humanize),
                "wheel" => DoWheel(request.X, request.Y, request.Dx ?? 0, request.Dy ?? -120),
                "drag" => DoDrag(request.X!.Value, request.Y!.Value, request.X2!.Value, request.Y2!.Value, request.Humanize),
                _ => new(false, $"BAD_REQUEST: unknown kind '{request.Kind}'", 400)
            };
        }
        catch (UacRequiredException)
        {
            return new(false, "UAC_REQUIRED", 412);
        }
    }

    private MouseInputResult DoMove(int x, int y, MouseHumanize? humanize)
    {
        var (absX, absY) = PhysicalToAbsolute(x, y, humanize?.JitterPx ?? 0);
        if (!SendMouseMove(absX, absY))
        {
            return new(false, $"INPUT_FAILED: move to ({x},{y})", 500, _lastWin32Error);
        }
        return new(true);
    }

    private MouseInputResult DoClick(int x, int y, MouseButton button, int clicks, MouseHumanize? humanize)
    {
        var moveResult = DoMove(x, y, humanize);
        if (!moveResult.Ok) return moveResult;

        ApplyHumanizeDelay(humanize);

        for (var i = 0; i < clicks; i++)
        {
            if (i > 0) ApplyHumanizeDelay(humanize);

            if (!SendMouseClick(button))
            {
                return new(false, $"INPUT_FAILED: click {button}", 500, _lastWin32Error);
            }
        }

        return new(true);
    }

    private MouseInputResult DoWheel(int? x, int? y, int dx, int dy)
    {
        if (x.HasValue && y.HasValue)
        {
            var (absX, absY) = PhysicalToAbsolute(x.Value, y.Value, 0);
            if (!SendMouseMove(absX, absY))
            {
                return new(false, $"INPUT_FAILED: wheel move to ({x},{y})", 500, _lastWin32Error);
            }
        }

        if (!SendMouseWheel(dx, dy))
        {
            return new(false, $"INPUT_FAILED: wheel dx={dx} dy={dy}", 500, _lastWin32Error);
        }

        return new(true);
    }

    private MouseInputResult DoDrag(int x1, int y1, int x2, int y2, MouseHumanize? humanize)
    {
        var (absX1, absY1) = PhysicalToAbsolute(x1, y1, humanize?.JitterPx ?? 0);
        if (!SendMouseMove(absX1, absY1))
        {
            return new(false, $"INPUT_FAILED: drag move to ({x1},{y1})", 500, _lastWin32Error);
        }

        ApplyHumanizeDelay(humanize);

        if (!SendMouseDown(MouseButton.Left))
        {
            return new(false, "INPUT_FAILED: drag mouse down", 500, _lastWin32Error);
        }

        ApplyHumanizeDelay(humanize);

        var (absX2, absY2) = PhysicalToAbsolute(x2, y2, humanize?.JitterPx ?? 0);
        if (!SendMouseMove(absX2, absY2))
        {
            return new(false, $"INPUT_FAILED: drag move to ({x2},{y2})", 500, _lastWin32Error);
        }

        ApplyHumanizeDelay(humanize);

        if (!SendMouseUp(MouseButton.Left))
        {
            return new(false, "INPUT_FAILED: drag mouse up", 500, _lastWin32Error);
        }

        return new(true);
    }

    private void ApplyHumanizeDelay(MouseHumanize? humanize)
    {
        if (humanize?.DelayMs is { Length: >= 2 } delay)
        {
            var min = Math.Max(0, delay[0]);
            var max = Math.Max(min, delay[1]);
            if (max > 0)
            {
                var ms = _random.Next(min, max + 1);
                if (ms > 0) Thread.Sleep(ms);
            }
        }
    }

    private (int absX, int absY) PhysicalToAbsolute(int physX, int physY, int jitterPx)
    {
        var vsX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vsY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vsW = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vsH = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (jitterPx > 0)
        {
            physX += _random.Next(-jitterPx, jitterPx + 1);
            physY += _random.Next(-jitterPx, jitterPx + 1);
        }

        // SendInput absolute coords: abs = ((phys - vsOrigin) * 65536 + vsSize/2) / vsSize
        var absX = ((physX - vsX) * 65536 + vsW / 2) / vsW;
        var absY = ((physY - vsY) * 65536 + vsH / 2) / vsH;

        absX = Math.Clamp(absX, 0, 65535);
        absY = Math.Clamp(absY, 0, 65535);

        return (absX, absY);
    }

    private static MouseButton GetButton(string? button) =>
        button?.ToLowerInvariant() switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left
        };

    private bool SendMouseMove(int absX, int absY)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                    mouseData = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            _lastWin32Error = Marshal.GetLastWin32Error();
            CheckAndThrowUac(_lastWin32Error);
        }
        return sent == 1;
    }

    private bool SendMouseClick(MouseButton button)
    {
        var (downFlag, upFlag) = GetButtonFlags(button);

        var inputs = new[]
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = downFlag,
                        mouseData = 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            },
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = upFlag,
                        mouseData = 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        };

        var sent = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            _lastWin32Error = Marshal.GetLastWin32Error();
            CheckAndThrowUac(_lastWin32Error);
        }
        return sent == 2;
    }

    private bool SendMouseDown(MouseButton button)
    {
        var (downFlag, _) = GetButtonFlags(button);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = downFlag,
                    mouseData = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            _lastWin32Error = Marshal.GetLastWin32Error();
            CheckAndThrowUac(_lastWin32Error);
        }
        return sent == 1;
    }

    private bool SendMouseUp(MouseButton button)
    {
        var (_, upFlag) = GetButtonFlags(button);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = upFlag,
                    mouseData = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            _lastWin32Error = Marshal.GetLastWin32Error();
            CheckAndThrowUac(_lastWin32Error);
        }
        return sent == 1;
    }

    private bool SendMouseWheel(int dx, int dy)
    {
        var inputs = new System.Collections.Generic.List<INPUT>();

        if (dy != 0)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = MOUSEEVENTF_WHEEL,
                        mouseData = (uint)dy,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });
        }

        if (dx != 0)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = MOUSEEVENTF_HWHEEL,
                        mouseData = (uint)dx,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });
        }

        if (inputs.Count == 0) return true;

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            _lastWin32Error = Marshal.GetLastWin32Error();
            CheckAndThrowUac(_lastWin32Error);
        }
        return sent == inputs.Count;
    }

    private static (uint downFlag, uint upFlag) GetButtonFlags(MouseButton button) =>
        button switch
        {
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
        };

    private static void CheckAndThrowUac(int error)
    {
        if (error == 5)
        {
            throw new UacRequiredException();
        }
    }

    private enum MouseButton { Left, Right, Middle }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const uint INPUT_MOUSE = 0;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}

public sealed class UacRequiredException : Exception
{
    public UacRequiredException() : base("Input blocked by UAC/secure desktop") { }
}
