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

public sealed record KeyInputResult(
    bool Ok, 
    string? Error = null, 
    int? StatusCode = null, 
    int? LastError = null,
    KeyInputDiagnostics? Diagnostics = null);

/// <summary>
/// Diagnostics for debugging keyboard input issues.
/// </summary>
public sealed record KeyInputDiagnostics(
    string[] KeysAttempted,
    KeyInputAttempt[] Attempts);

public sealed record KeyInputAttempt(
    string Key,
    string Action,
    ushort Vk,
    uint Flags,
    uint SendInputResult,
    int? Win32Error);

public sealed class NullKeyInputProvider : IKeyInputProvider
{
    public KeyInputResult Execute(KeyInputRequest request) =>
        new(false, "NOT_IMPLEMENTED", 501);
}

public sealed class WindowsKeyInputProvider : IKeyInputProvider
{
    private readonly Random _random = new();
    private int _lastWin32Error;
    private readonly List<KeyInputAttempt> _attempts = new();

    // Key mappings: only VK codes are used for injection (pure VK mode).
    // Scan codes are kept for reference but not used in SendInput.
    // Extended flag is critical for navigation keys.
    private static readonly Dictionary<string, (ushort vk, bool extended)> KeyMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Modifier keys
        ["CTRL"] = (0x11, false),
        ["CONTROL"] = (0x11, false),
        ["ALT"] = (0x12, false),
        ["SHIFT"] = (0x10, false),
        ["WIN"] = (0x5B, true),
        ["LWIN"] = (0x5B, true),
        ["RWIN"] = (0x5C, true),

        // Special keys
        ["ENTER"] = (0x0D, false),
        ["RETURN"] = (0x0D, false),
        ["ESC"] = (0x1B, false),
        ["ESCAPE"] = (0x1B, false),
        ["TAB"] = (0x09, false),
        ["BACKSPACE"] = (0x08, false),
        ["DELETE"] = (0x2E, true),
        ["DEL"] = (0x2E, true),
        ["INSERT"] = (0x2D, true),
        ["INS"] = (0x2D, true),
        ["HOME"] = (0x24, true),
        ["END"] = (0x23, true),
        ["PAGEUP"] = (0x21, true),
        ["PGUP"] = (0x21, true),
        ["PAGEDOWN"] = (0x22, true),
        ["PGDN"] = (0x22, true),
        ["SPACE"] = (0x20, false),
        ["PRINTSCREEN"] = (0x2C, false),
        ["PRTSC"] = (0x2C, false),
        ["PAUSE"] = (0x13, false),
        ["CAPSLOCK"] = (0x14, false),
        ["NUMLOCK"] = (0x90, false),
        ["SCROLLLOCK"] = (0x91, false),

        // Arrow keys (extended)
        ["UP"] = (0x26, true),
        ["DOWN"] = (0x28, true),
        ["LEFT"] = (0x25, true),
        ["RIGHT"] = (0x27, true),

        // Letter keys
        ["A"] = (0x41, false), ["B"] = (0x42, false), ["C"] = (0x43, false),
        ["D"] = (0x44, false), ["E"] = (0x45, false), ["F"] = (0x46, false),
        ["G"] = (0x47, false), ["H"] = (0x48, false), ["I"] = (0x49, false),
        ["J"] = (0x4A, false), ["K"] = (0x4B, false), ["L"] = (0x4C, false),
        ["M"] = (0x4D, false), ["N"] = (0x4E, false), ["O"] = (0x4F, false),
        ["P"] = (0x50, false), ["Q"] = (0x51, false), ["R"] = (0x52, false),
        ["S"] = (0x53, false), ["T"] = (0x54, false), ["U"] = (0x55, false),
        ["V"] = (0x56, false), ["W"] = (0x57, false), ["X"] = (0x58, false),
        ["Y"] = (0x59, false), ["Z"] = (0x5A, false),

        // Number keys (top row)
        ["0"] = (0x30, false), ["1"] = (0x31, false), ["2"] = (0x32, false),
        ["3"] = (0x33, false), ["4"] = (0x34, false), ["5"] = (0x35, false),
        ["6"] = (0x36, false), ["7"] = (0x37, false), ["8"] = (0x38, false),
        ["9"] = (0x39, false),

        // Function keys
        ["F1"] = (0x70, false), ["F2"] = (0x71, false), ["F3"] = (0x72, false),
        ["F4"] = (0x73, false), ["F5"] = (0x74, false), ["F6"] = (0x75, false),
        ["F7"] = (0x76, false), ["F8"] = (0x77, false), ["F9"] = (0x78, false),
        ["F10"] = (0x79, false), ["F11"] = (0x7A, false), ["F12"] = (0x7B, false),

        // Punctuation and symbols
        ["MINUS"] = (0xBD, false),
        ["PLUS"] = (0xBB, false),
        ["EQUALS"] = (0xBB, false),
        ["LBRACKET"] = (0xDB, false),
        ["RBRACKET"] = (0xDD, false),
        ["BACKSLASH"] = (0xDC, false),
        ["SEMICOLON"] = (0xBA, false),
        ["QUOTE"] = (0xDE, false),
        ["COMMA"] = (0xBC, false),
        ["PERIOD"] = (0xBE, false),
        ["SLASH"] = (0xBF, false),
        ["TILDE"] = (0xC0, false),
        ["GRAVE"] = (0xC0, false),
    };

    public KeyInputResult Execute(KeyInputRequest request)
    {
        _attempts.Clear();

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

        var keyInfos = new List<(ushort vk, bool extended, string name)>();
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

            keyInfos.Add((mapping.vk, mapping.extended, key.Trim().ToUpperInvariant()));
        }

        try
        {
            return DoPress(keyInfos, request.Humanize, request.Keys);
        }
        catch (UacRequiredException)
        {
            return new(false, "UAC_REQUIRED", 412, null, BuildDiagnostics(request.Keys));
        }
    }

    private KeyInputResult DoPress(List<(ushort vk, bool extended, string name)> keys, KeyHumanize? humanize, string[] originalKeys)
    {
        foreach (var key in keys)
        {
            ApplyHumanizeDelay(humanize);

            if (!SendKeyDown(key.vk, key.extended, key.name))
            {
                return new(false, $"INPUT_FAILED: keydown {key.name} (vk=0x{key.vk:X2})", 500, _lastWin32Error, BuildDiagnostics(originalKeys));
            }
        }

        for (var i = keys.Count - 1; i >= 0; i--)
        {
            var key = keys[i];
            ApplyHumanizeDelay(humanize);

            if (!SendKeyUp(key.vk, key.extended, key.name))
            {
                return new(false, $"INPUT_FAILED: keyup {key.name} (vk=0x{key.vk:X2})", 500, _lastWin32Error, BuildDiagnostics(originalKeys));
            }
        }

        return new(true, Diagnostics: BuildDiagnostics(originalKeys));
    }

    private KeyInputDiagnostics BuildDiagnostics(string[] keys)
    {
        return new KeyInputDiagnostics(keys, _attempts.ToArray());
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

    private bool SendKeyDown(ushort vk, bool extended, string keyName)
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
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        _attempts.Add(new KeyInputAttempt(keyName, "down", vk, flags, sent, sent == 0 ? Marshal.GetLastWin32Error() : null));

        if (sent == 0)
        {
            _lastWin32Error = Marshal.GetLastWin32Error();
            CheckAndThrowUac(_lastWin32Error);
        }
        return sent == 1;
    }

    private bool SendKeyUp(ushort vk, bool extended, string keyName)
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
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        _attempts.Add(new KeyInputAttempt(keyName, "up", vk, flags, sent, sent == 0 ? Marshal.GetLastWin32Error() : null));

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
        public MOUSEINPUT mi;
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

    // MOUSEINPUT is needed to ensure correct union size
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
