using System;
using System.Collections.Generic;
using System.Threading;

namespace TbxExecutor;

public sealed record MacroRunRequest(
    MacroStep[]? Steps,
    MacroDefaults? Defaults,
    bool? FailFast);

public sealed record MacroDefaults(MacroHumanize? Humanize);

public sealed record MacroHumanize(int[]? DelayMs, int? JitterPx);

public sealed record MacroStep(
    string? Kind,
    MacroWindowMatch? Match,
    string? Mode,
    MacroWindowMatch? Window,
    MacroCaptureRegion? Region,
    string? Format,
    int? Quality,
    int? DisplayIndex,
    int? X,
    int? Y,
    string? Button,
    int? Dx,
    int? Dy,
    int? X2,
    int? Y2,
    string[]? Keys,
    int? Ms,
    MacroHumanize? Humanize,
    // Wheel-specific fields
    int? Delta,
    bool? Horizontal);

public sealed record MacroWindowMatch(
    string? TitleContains,
    string? TitleRegex,
    string? ProcessName);

public sealed record MacroCaptureRegion(int X, int Y, int W, int H);

public sealed record MacroStepResult(
    string StepId,
    bool Ok,
    int? Status = null,
    string? Error = null,
    object? Data = null);

public sealed record MacroRunResult(
    string RunId,
    bool Ok,
    MacroStepResult[] Steps,
    int? Status = null,
    string? Error = null);

public sealed class MacroRunner
{
    private readonly IWindowManager _windowManager;
    private readonly ICaptureProvider _captureProvider;
    private readonly IMouseInputProvider _mouseInputProvider;
    private readonly IKeyInputProvider _keyInputProvider;
    private readonly Random _random = new();

    public MacroRunner(
        IWindowManager windowManager,
        ICaptureProvider captureProvider,
        IMouseInputProvider mouseInputProvider,
        IKeyInputProvider keyInputProvider)
    {
        _windowManager = windowManager;
        _captureProvider = captureProvider;
        _mouseInputProvider = mouseInputProvider;
        _keyInputProvider = keyInputProvider;
    }

    public MacroRunResult Execute(MacroRunRequest request, string runId)
    {
        if (request.Steps is null || request.Steps.Length == 0)
        {
            return new MacroRunResult(runId, false, Array.Empty<MacroStepResult>(), 400, "BAD_REQUEST: steps array is required");
        }

        var failFast = request.FailFast ?? true;
        var defaults = request.Defaults;
        var results = new List<MacroStepResult>();
        var allOk = true;

        for (var i = 0; i < request.Steps.Length; i++)
        {
            var step = request.Steps[i];
            var stepId = Guid.NewGuid().ToString("n");

            var result = ExecuteStep(step, stepId, defaults);
            results.Add(result);

            if (!result.Ok)
            {
                allOk = false;
                if (failFast)
                {
                    break;
                }
            }
        }

        return new MacroRunResult(runId, allOk, results.ToArray());
    }

    private MacroStepResult ExecuteStep(MacroStep step, string stepId, MacroDefaults? defaults)
    {
        if (string.IsNullOrWhiteSpace(step.Kind))
        {
            return new MacroStepResult(stepId, false, 400, "BAD_REQUEST: step kind is required");
        }

        var kind = step.Kind.ToLowerInvariant();

        try
        {
            return kind switch
            {
                "window.focus" => ExecuteWindowFocus(step, stepId),
                "capture" => ExecuteCapture(step, stepId),
                "input.mouse" => ExecuteMouseInput(step, stepId, defaults),
                "input.mouse.move" => ExecuteMouseInput(step, stepId, defaults),
                "input.mouse.click" => ExecuteMouseInput(step, stepId, defaults),
                "input.mouse.double" => ExecuteMouseInput(step, stepId, defaults),
                "input.mouse.right" => ExecuteMouseInput(step, stepId, defaults),
                "input.mouse.drag" => ExecuteMouseInput(step, stepId, defaults),
                "input.mouse.wheel" => ExecuteMouseInput(step, stepId, defaults),
                "input.wheel" => ExecuteWheelInput(step, stepId),
                "input.key" => ExecuteKeyInput(step, stepId, defaults),
                "input.key.press" => ExecuteKeyInput(step, stepId, defaults),
                "sleep" => ExecuteSleep(step, stepId),
                _ => new MacroStepResult(stepId, false, 400, $"BAD_REQUEST: unknown step kind '{step.Kind}'")
            };
        }
        catch (UacRequiredException)
        {
            return new MacroStepResult(stepId, false, 412, "UAC_REQUIRED");
        }
        catch (Exception ex)
        {
            return new MacroStepResult(stepId, false, 500, $"INTERNAL_ERROR: {ex.Message}");
        }
    }

    private MacroStepResult ExecuteWindowFocus(MacroStep step, string stepId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MacroStepResult(stepId, false, 501, "NOT_IMPLEMENTED");
        }

        if (step.Match is null)
        {
            return new MacroStepResult(stepId, false, 400, "BAD_MATCH");
        }

        var match = new WindowMatch(
            step.Match.TitleContains,
            step.Match.TitleRegex,
            step.Match.ProcessName);

        if (!WindowMatchValidator.IsValidRegex(match.TitleRegex))
        {
            return new MacroStepResult(stepId, false, 400, "BAD_REGEX");
        }

        var focused = _windowManager.FocusWindow(match);
        if (focused is null)
        {
            return new MacroStepResult(stepId, false, 404, "WINDOW_NOT_FOUND");
        }

        return new MacroStepResult(stepId, true, Data: new
        {
            hwnd = focused.Hwnd,
            title = focused.Title,
            processName = focused.ProcessName,
            rectPx = new { x = focused.RectPx.X, y = focused.RectPx.Y, w = focused.RectPx.W, h = focused.RectPx.H },
            isVisible = focused.IsVisible,
            isMinimized = focused.IsMinimized
        });
    }

    private MacroStepResult ExecuteCapture(MacroStep step, string stepId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MacroStepResult(stepId, false, 501, "NOT_IMPLEMENTED");
        }

        var mode = step.Mode?.ToLowerInvariant() switch
        {
            "screen" => CaptureMode.Screen,
            "window" => CaptureMode.Window,
            "region" => CaptureMode.Region,
            _ => CaptureMode.Screen
        };

        var format = step.Format?.ToLowerInvariant() switch
        {
            "jpeg" or "jpg" => CaptureFormat.Jpeg,
            _ => CaptureFormat.Png
        };

        WindowMatch? windowMatch = null;
        if (step.Window is not null)
        {
            windowMatch = new WindowMatch(
                step.Window.TitleContains,
                step.Window.TitleRegex,
                step.Window.ProcessName);
        }

        RectPx? region = null;
        if (step.Region is not null)
        {
            region = new RectPx(
                step.Region.X,
                step.Region.Y,
                step.Region.W,
                step.Region.H);
        }

        var request = new CaptureRequest(
            Mode: mode,
            Window: windowMatch,
            Region: region,
            Format: format,
            Quality: step.Quality ?? 90,
            DisplayIndex: step.DisplayIndex);

        var (result, failure) = _captureProvider.CaptureWithDiagnostics(request, _windowManager);
        if (result is null)
        {
            var errorData = failure is not null ? new
            {
                reason = failure.Reason,
                candidates = failure.Candidates
            } : null;
            return new MacroStepResult(stepId, false, 404, $"CAPTURE_FAILED: {failure?.Reason}", errorData);
        }

        var responseData = new
        {
            imageB64 = Convert.ToBase64String(result.ImageBytes),
            format = result.Format == CaptureFormat.Png ? "png" : "jpeg",
            regionRectPx = new { x = result.Metadata.RegionRectPx.X, y = result.Metadata.RegionRectPx.Y, w = result.Metadata.RegionRectPx.W, h = result.Metadata.RegionRectPx.H },
            windowRectPx = result.Metadata.WindowRectPx is not null
                ? new { x = result.Metadata.WindowRectPx.X, y = result.Metadata.WindowRectPx.Y, w = result.Metadata.WindowRectPx.W, h = result.Metadata.WindowRectPx.H }
                : null,
            ts = result.Metadata.Ts,
            scale = result.Metadata.Scale,
            dpi = result.Metadata.Dpi,
            displayIndex = result.Metadata.DisplayIndex,
            deviceName = result.Metadata.DeviceName,
            selectedWindow = result.SelectedWindow is not null ? new
            {
                hwnd = result.SelectedWindow.Hwnd,
                title = result.SelectedWindow.Title,
                processName = result.SelectedWindow.ProcessName,
                rectPx = new { x = result.SelectedWindow.RectPx.X, y = result.SelectedWindow.RectPx.Y, w = result.SelectedWindow.RectPx.W, h = result.SelectedWindow.RectPx.H },
                isVisible = result.SelectedWindow.IsVisible,
                isMinimized = result.SelectedWindow.IsMinimized,
                score = result.SelectedWindow.Score
            } : null
        };

        return new MacroStepResult(stepId, true, Data: responseData);
    }

    private MacroStepResult ExecuteMouseInput(MacroStep step, string stepId, MacroDefaults? defaults)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MacroStepResult(stepId, false, 501, "NOT_IMPLEMENTED");
        }

        MouseHumanize? humanize = null;
        var stepHumanize = step.Humanize;
        var defaultHumanize = defaults?.Humanize;

        if (stepHumanize is not null || defaultHumanize is not null)
        {
            humanize = new MouseHumanize(
                JitterPx: stepHumanize?.JitterPx ?? defaultHumanize?.JitterPx,
                DelayMs: stepHumanize?.DelayMs ?? defaultHumanize?.DelayMs);
        }

        var mouseKind = step.Kind!.ToLowerInvariant();
        if (mouseKind.StartsWith("input.mouse."))
        {
            mouseKind = mouseKind.Substring("input.mouse.".Length);
        }
        else if (mouseKind == "input.mouse")
        {
            if (step.X is not null && step.Y is not null)
            {
                mouseKind = "click";
            }
            else
            {
                return new MacroStepResult(stepId, false, 400, "BAD_REQUEST: mouse action type required (use input.mouse.click, input.mouse.move, etc.)");
            }
        }

        var request = new MouseInputRequest(
            Kind: mouseKind,
            X: step.X,
            Y: step.Y,
            Button: step.Button,
            Dx: step.Dx,
            Dy: step.Dy,
            X2: step.X2,
            Y2: step.Y2,
            Humanize: humanize);

        var result = _mouseInputProvider.Execute(request);
        if (!result.Ok)
        {
            return new MacroStepResult(stepId, false, result.StatusCode ?? 500, result.Error ?? "UNKNOWN_ERROR");
        }

        return new MacroStepResult(stepId, true, Data: new { success = true });
    }

    private MacroStepResult ExecuteKeyInput(MacroStep step, string stepId, MacroDefaults? defaults)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MacroStepResult(stepId, false, 501, "NOT_IMPLEMENTED");
        }

        KeyHumanize? humanize = null;
        var stepHumanize = step.Humanize;
        var defaultHumanize = defaults?.Humanize;

        if (stepHumanize is not null || defaultHumanize is not null)
        {
            humanize = new KeyHumanize(
                DelayMs: stepHumanize?.DelayMs ?? defaultHumanize?.DelayMs);
        }

        var keyKind = step.Kind!.ToLowerInvariant();
        if (keyKind.StartsWith("input.key."))
        {
            keyKind = keyKind.Substring("input.key.".Length);
        }
        else if (keyKind == "input.key")
        {
            keyKind = "press";
        }

        var request = new KeyInputRequest(
            Kind: keyKind,
            Keys: step.Keys,
            Humanize: humanize);

        var result = _keyInputProvider.Execute(request);
        if (!result.Ok)
        {
            return new MacroStepResult(stepId, false, result.StatusCode ?? 500, result.Error ?? "UNKNOWN_ERROR");
        }

        return new MacroStepResult(stepId, true, Data: new { success = true });
    }

    private MacroStepResult ExecuteSleep(MacroStep step, string stepId)
    {
        var ms = step.Ms ?? 0;
        if (ms < 0)
        {
            return new MacroStepResult(stepId, false, 400, "BAD_REQUEST: ms must be >= 0");
        }

        if (ms > 0)
        {
            Thread.Sleep(ms);
        }

        return new MacroStepResult(stepId, true, Data: new { sleptMs = ms });
    }

    private MacroStepResult ExecuteWheelInput(MacroStep step, string stepId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MacroStepResult(stepId, false, 501, "NOT_IMPLEMENTED");
        }

        var delta = step.Delta ?? step.Dy ?? -120;
        var horizontal = step.Horizontal ?? false;
        
        var dx = horizontal ? delta : (step.Dx ?? 0);
        var dy = horizontal ? 0 : delta;

        var request = new MouseInputRequest(
            Kind: "wheel",
            X: step.X,
            Y: step.Y,
            Button: null,
            Dx: dx,
            Dy: dy,
            X2: null,
            Y2: null,
            Humanize: null);

        var result = _mouseInputProvider.Execute(request);
        if (!result.Ok)
        {
            return new MacroStepResult(stepId, false, result.StatusCode ?? 500, result.Error ?? "UNKNOWN_ERROR", 
                Data: result.LastError.HasValue ? new { lastError = result.LastError } : null);
        }

        return new MacroStepResult(stepId, true, Data: new { success = true, delta, horizontal, x = step.X, y = step.Y });
    }
}
