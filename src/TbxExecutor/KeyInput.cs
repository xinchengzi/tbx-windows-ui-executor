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

public sealed record KeyInputResult(bool Ok, string? Error = null, int? StatusCode = null);

public sealed class NullKeyInputProvider : IKeyInputProvider
{
    public KeyInputResult Execute(KeyInputRequest request) =>
        new(false, "NOT_IMPLEMENTED", 501);
}

public sealed class WindowsKeyInputProvider : IKeyInputProvider
{
    private readonly Random _random = new();

    // Virtual key code mappings
    private static readonly Dictionary<string, ushort> VkCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Modifier keys
        ["CTRL"] = 0x11,       // VK_CONTROL
        ["ALT"] = 0x12,        // VK_MENU
        ["SHIFT"] = 0x10,      // VK_SHIFT
        ["WIN"] = 0x5B,        // VK_LWIN

        // Special keys
        ["ENTER"] = 0x0D,      // VK_RETURN
        ["ESC"] = 0x1B,        // VK_ESCAPE
        ["TAB"] = 0x09,        // VK_TAB
        ["BACKSPACE"] = 0x08,  // VK_BACK
        ["DELETE"] = 0x2E,     // VK_DELETE
        ["HOME"] = 0x24,       // VK_HOME
        ["END"] = 0x23,        // VK_END
        ["PAGEUP"] = 0x21,     // VK_PRIOR
        ["PAGEDOWN"] = 0x22,   // VK_NEXT
        ["SPACE"] = 0x20,      // VK_SPACE

        // Arrow keys
        ["UP"] = 0x26,         // VK_UP
        ["DOWN"] = 0x28,       // VK_DOWN
        ["LEFT"] = 0x25,       // VK_LEFT
        ["RIGHT"] = 0x27,      // VK_RIGHT

        // Letters A-Z (0x41-0x5A)
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45,
        ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A,
        ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F,
        ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59,
        ["Z"] = 0x5A,

        // Numbers 0-9 (0x30-0x39)
        ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33, ["4"] = 0x34,
        ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37, ["8"] = 0x38, ["9"] = 0x39,

        // Function keys F1-F12 (0x70-0x7B)
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
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

        // Validate all keys first
        var vkCodes = new List<ushort>();
        foreach (var key in request.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return new(false, "BAD_REQUEST: key name cannot be empty", 400);
            }

            if (!VkCodes.TryGetValue(key.Trim(), out var vk))
            {
                return new(false, $"BAD_REQUEST: unknown key '{key}'", 400);
            }

            vkCodes.Add(vk);
        }

        try
        {
            return DoPress(vkCodes, request.Humanize);
        }
        catch (UacRequiredException)
        {
            return new(false, "UAC_REQUIRED", 412);
        }
    }

    private KeyInputResult DoPress(List<ushort> vkCodes, KeyHumanize? humanize)
    {
        // Press: key down in order, then key up in reverse order

        // Key down events
        foreach (var vk in vkCodes)
        {
            ApplyHumanizeDelay(humanize);

            if (!SendKeyDown(vk))
            {
                return new(false, "INPUT_FAILED", 500);
            }
        }

        // Key up events in reverse order
        for (var i = vkCodes.Count - 1; i >= 0; i--)
        {
            ApplyHumanizeDelay(humanize);

            if (!SendKeyUp(vkCodes[i]))
            {
                return new(false, "INPUT_FAILED", 500);
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

    private bool SendKeyDown(ushort vk)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = 0, // KEYEVENTF_KEYDOWN = 0
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0) CheckAndThrowUac();
        return sent == 1;
    }

    private bool SendKeyUp(ushort vk)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0) CheckAndThrowUac();
        return sent == 1;
    }

    private static void CheckAndThrowUac()
    {
        var error = Marshal.GetLastWin32Error();
        if (error == 5) // ERROR_ACCESS_DENIED: UAC/secure desktop
        {
            throw new UacRequiredException();
        }
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

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
