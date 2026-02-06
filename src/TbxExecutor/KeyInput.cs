using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace TbxExecutor;

public sealed record KeyInputRequest(
    string Kind,
    string[]? Keys,
    KeyHumanize? Humanize);

public sealed record KeyHumanize(int[]? DelayMs);

public interface IKeyInputProvider
{
    KeyInputResult Execute(KeyInputRequest request);
}

public sealed record KeyInputResult(bool Ok, string? Error = null, int? StatusCode = null, int? LastError = null);

public sealed class NullKeyInputProvider : IKeyInputProvider
{
    public KeyInputResult Execute(KeyInputRequest request) =>
        new(false, "NOT_IMPLEMENTED", 501);
}

public sealed class WindowsKeyInputProvider : IKeyInputProvider
{
    private readonly Random _random = new();
    private int _lastWin32Error;

    private static readonly Dictionary<string, (ushort vk, ushort scan, bool extended)> KeyMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CTRL"] = (0x11, 0x1D, false),
        ["ALT"] = (0x12, 0x38, false),
        ["SHIFT"] = (0x10, 0x2A, false),
        ["WIN"] = (0x5B, 0x5B, true),

        ["ENTER"] = (0x0D, 0x1C, false),
        ["ESC"] = (0x1B, 0x01, false),
        ["TAB"] = (0x09, 0x0F, false),
        ["BACKSPACE"] = (0x08, 0x0E, false),
        ["DELETE"] = (0x2E, 0x53, true),
        ["HOME"] = (0x24, 0x47, true),
        ["END"] = (0x23, 0x4F, true),
        ["PAGEUP"] = (0x21, 0x49, true),
        ["PAGEDOWN"] = (0x22, 0x51, true),
        ["SPACE"] = (0x20, 0x39, false),

        ["UP"] = (0x26, 0x48, true),
        ["DOWN"] = (0x28, 0x50, true),
        ["LEFT"] = (0x25, 0x4B, true),
        ["RIGHT"] = (0x27, 0x4D, true),

        ["A"] = (0x41, 0x1E, false), ["B"] = (0x42, 0x30, false), ["C"] = (0x43, 0x2E, false),
        ["D"] = (0x44, 0x20, false), ["E"] = (0x45, 0x12, false), ["F"] = (0x46, 0x21, false),
        ["G"] = (0x47, 0x22, false), ["H"] = (0x48, 0x23, false), ["I"] = (0x49, 0x17, false),
        ["J"] = (0x4A, 0x24, false), ["K"] = (0x4B, 0x25, false), ["L"] = (0x4C, 0x26, false),
        ["M"] = (0x4D, 0x32, false), ["N"] = (0x4E, 0x31, false), ["O"] = (0x4F, 0x18, false),
        ["P"] = (0x50, 0x19, false), ["Q"] = (0x51, 0x10, false), ["R"] = (0x52, 0x13, false),
        ["S"] = (0x53, 0x1F, false), ["T"] = (0x54, 0x14, false), ["U"] = (0x55, 0x16, false),
        ["V"] = (0x56, 0x2F, false), ["W"] = (0x57, 0x11, false), ["X"] = (0x58, 0x2D, false),
        ["Y"] = (0x59, 0x15, false), ["Z"] = (0x5A, 0x2C, false),

        ["0"] = (0x30, 0x0B, false), ["1"] = (0x31, 0x02, false), ["2"] = (0x32, 0x03, false),
        ["3"] = (0x33, 0x04, false), ["4"] = (0x34, 0x05, false), ["5"] = (0x35, 0x06, false),
        ["6"] = (0x36, 0x07, false), ["7"] = (0x37, 0x08, false), ["8"] = (0x38, 0x09, false),
        ["9"] = (0x39, 0x0A, false),

        ["F1"] = (0x70, 0x3B, false), ["F2"] = (0x71, 0x3C, false), ["F3"] = (0x72, 0x3D, false),
        ["F4"] = (0x73, 0x3E, false), ["F5"] = (0x74, 0x3F, false), ["F6"] = (0x75, 0x40, false),
        ["F7"] = (0x76, 0x41, false), ["F8"] = (0x77, 0x42, false), ["F9"] = (0x78, 0x43, false),
        ["F10"] = (0x79, 0x44, false), ["F11"] = (0x7A, 0x57, false), ["F12"] = (0x7B, 0x58, false),
    };

    public KeyInputResult Execute(KeyInputRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Kind))
        {
            return new(false, "BAD_REQUEST: kind is required", 400);
        }

        var kind = request.Kind.ToLowerInvariant();

        if (kind != "press")
        {
            return new(false, $"BAD_REQUEST: unknown kind '{request.Kind}'", 400);
        }

        if (request.Keys is null || request.Keys.Length == 0)
        {
            return new(false, "BAD_REQUEST: keys array is required and must not be empty", 400);
        }

        var keyInfos = new List<(ushort vk, ushort scan, bool extended, string name)>();
        foreach (var key in request.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return new(false, "BAD_REQUEST: key name cannot be empty", 400);
            }

            if (!KeyMappings.TryGetValue(key.Trim(), out var mapping))
            {
                return new(false, $"BAD_REQUEST: unknown key '{key}'", 400);
            }

            keyInfos.Add((mapping.vk, mapping.scan, mapping.extended, key.Trim().ToUpperInvariant()));
        }

        try
        {
            return DoPress(keyInfos, request.Humanize);
        }
        catch (UacRequiredException)
        {
            return new(false, "UAC_REQUIRED", 412);
        }
    }

    private KeyInputResult DoPress(List<(ushort vk, ushort scan, bool extended, string name)> keys, KeyHumanize? humanize)
    {
        foreach (var key in keys)
        {
            ApplyHumanizeDelay(humanize);

            if (!SendKeyDown(key.vk, key.scan, key.extended))
            {
                return new(false, $"INPUT_FAILED: keydown {key.name} (vk=0x{key.vk:X2}, scan=0x{key.scan:X2})", 500, _lastWin32Error);
            }
        }

        for (var i = keys.Count - 1; i >= 0; i--)
        {
            var key = keys[i];
            ApplyHumanizeDelay(humanize);

            if (!SendKeyUp(key.vk, key.scan, key.extended))
            {
                return new(false, $"INPUT_FAILED: keyup {key.name} (vk=0x{key.vk:X2}, scan=0x{key.scan:X2})", 500, _lastWin32Error);
            }
        }

        return new(true);
    }

    private void ApplyHumanizeDelay(KeyHumanize? humanize)
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

    private bool SendKeyDown(ushort vk, ushort scan, bool extended)
    {
        uint flags = 0;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
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

    private bool SendKeyUp(ushort vk, ushort scan, bool extended)
    {
        uint flags = KEYEVENTF_KEYUP;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
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

    private static void CheckAndThrowUac(int error)
    {
        if (error == 5)
        {
            throw new UacRequiredException();
        }
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

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
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
