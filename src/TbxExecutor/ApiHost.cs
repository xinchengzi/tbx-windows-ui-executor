using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TbxExecutor;

public sealed class ApiHost : IDisposable
{
    private readonly ExecutorConfig _config;
    private readonly RunLogger _runLogger = new();
    private WebApplication? _app;

    public ApiHost(ExecutorConfig config)
    {
        _config = config;
    }

    public async System.Threading.Tasks.Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "TbxExecutor.Api",
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });

        var listenHost = _config.PickListenHost();
        builder.WebHost.ConfigureKestrel(o =>
        {
            // Bind only to configured host/port.
            o.Listen(IPAddress.Parse(listenHost), _config.ListenPort);
        });

        var app = builder.Build();

        // Middleware: run/step ids
        app.Use(async (ctx, next) =>
        {
            var runId = ctx.Request.Headers["X-Run-Id"].ToString();
            if (string.IsNullOrWhiteSpace(runId)) runId = Guid.NewGuid().ToString("n");

            ctx.Items["runId"] = runId;
            ctx.Items["stepId"] = Guid.NewGuid().ToString("n");

            await next();
        });

        // Middleware: allowlist IP
        app.Use(async (ctx, next) =>
        {
            var remoteIp = ctx.Connection.RemoteIpAddress;
            if (remoteIp is null)
            {
                await Results.Json(ApiResponse.Error(ctx, 401, "NO_REMOTE_IP")).ExecuteAsync(ctx);
                return;
            }

            var remote = remoteIp.ToString();
            var ok = _config.AllowlistIps.Contains(remote);
            if (!ok)
            {
                await Results.Json(ApiResponse.Error(ctx, 403, $"IP_NOT_ALLOWED: {remote}"))
                    .ExecuteAsync(ctx);
                return;
            }

            await next();
        });

        // Middleware: Bearer token
        app.Use(async (ctx, next) =>
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                await Results.Json(ApiResponse.Error(ctx, 401, "MISSING_BEARER"))
                    .ExecuteAsync(ctx);
                return;
            }

            var token = auth.Substring("Bearer ".Length).Trim();
            if (!TimingSafeEquals(token, _config.Token))
            {
                await Results.Json(ApiResponse.Error(ctx, 401, "BAD_TOKEN"))
                    .ExecuteAsync(ctx);
                return;
            }

            await next();
        });

        IWindowManager windowManager = OperatingSystem.IsWindows()
            ? new WindowsWindowManager()
            : new NullWindowManager();
        ILockStateProvider lockStateProvider = OperatingSystem.IsWindows()
            ? new WindowsLockStateProvider()
            : new NullLockStateProvider();
        IDisplayEnvironmentProvider displayEnvProvider = OperatingSystem.IsWindows()
            ? new WindowsDisplayEnvironmentProvider()
            : new NullDisplayEnvironmentProvider();
        ICaptureProvider captureProvider = OperatingSystem.IsWindows()
            ? new WindowsCaptureProvider(displayEnvProvider)
            : new NullCaptureProvider();
        IMouseInputProvider mouseInputProvider = OperatingSystem.IsWindows()
            ? new WindowsMouseInputProvider()
            : new NullMouseInputProvider();
        IKeyInputProvider keyInputProvider = OperatingSystem.IsWindows()
            ? new WindowsKeyInputProvider()
            : new NullKeyInputProvider();

        // Middleware: lock state guard
        app.Use(async (ctx, next) =>
        {
            if (!lockStateProvider.IsLocked())
            {
                await next();
                return;
            }

            var path = ctx.Request.Path;
            if (path.StartsWithSegments("/window/focus")
                || path.StartsWithSegments("/input")
                || path.StartsWithSegments("/macro"))
            {
                await Results.Json(ApiResponse.Error(ctx, 409, "LOCKED"))
                    .ExecuteAsync(ctx);
                return;
            }

            await next();
        });

        // Routes
        app.MapGet("/health", (HttpContext ctx) =>
        {
            var payload = new
            {
                version = "0.1.0",
                uptimeMs = (long)(Environment.TickCount64),
                locked = lockStateProvider.IsLocked(),
                paused = false,
            };
            return Results.Json(ApiResponse.Ok(ctx, payload));
        });

        app.MapGet("/env", (HttpContext ctx) =>
        {
            // Windows: report per-monitor bounds + dpi/scale (physical pixels)
            // Non-Windows: empty list
            var displays = displayEnvProvider.GetDisplays()
                .Select(d => new
                {
                    index = d.Index,
                    deviceName = d.DeviceName,
                    isPrimary = d.IsPrimary,
                    boundsRectPx = new { x = d.BoundsRectPx.X, y = d.BoundsRectPx.Y, w = d.BoundsRectPx.W, h = d.BoundsRectPx.H },
                    workAreaRectPx = new { x = d.WorkAreaRectPx.X, y = d.WorkAreaRectPx.Y, w = d.WorkAreaRectPx.W, h = d.WorkAreaRectPx.H },
                    dpi = new { x = d.DpiX, y = d.DpiY },
                    scale = new { x = d.ScaleX, y = d.ScaleY }
                })
                .ToArray();

            var vs = displayEnvProvider.GetVirtualScreenRectPx();

            var payload = new
            {
                os = Environment.OSVersion.VersionString,
                coordinateSystem = "physicalPixels",
                dpiAwareness = DpiAwareness.GetCurrentModeString(),
                virtualScreenRectPx = new { x = vs.X, y = vs.Y, w = vs.W, h = vs.H },
                displays
            };
            return Results.Json(ApiResponse.Ok(ctx, payload));
        });

        app.MapGet("/config", (HttpContext ctx) =>
        {
            var payload = new
            {
                listenHost = listenHost,
                listenPort = _config.ListenPort,
                allowlistIps = _config.AllowlistIps,
                tokenSet = !string.IsNullOrWhiteSpace(_config.Token)
            };
            return Results.Json(ApiResponse.Ok(ctx, payload));
        });

        app.MapPost("/window/list", (HttpContext ctx) =>
        {
            var windows = windowManager.ListWindows();
            return Results.Json(ApiResponse.Ok(ctx, windows));
        });

        app.MapPost("/window/focus", async (HttpContext ctx) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.Json(ApiResponse.Error(ctx, 501, "NOT_IMPLEMENTED"));
            }

            var payload = await ctx.Request.ReadFromJsonAsync<WindowFocusRequest>();
            if (payload?.Match is null)
            {
                return Results.Json(ApiResponse.Error(ctx, 400, "BAD_MATCH"));
            }

            if (!WindowMatchValidator.IsValidRegex(payload.Match.TitleRegex))
            {
                return Results.Json(ApiResponse.Error(ctx, 400, "BAD_REGEX"));
            }

            var focused = windowManager.FocusWindow(payload.Match);
            if (focused is null)
            {
                return Results.Json(ApiResponse.Error(ctx, 404, "WINDOW_NOT_FOUND"));
            }

            return Results.Json(ApiResponse.Ok(ctx, focused));
        });

        app.MapPost("/capture", async (HttpContext ctx) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.Json(ApiResponse.Error(ctx, 501, "NOT_IMPLEMENTED"));
            }

            CaptureApiRequest? payload;
            try
            {
                payload = await ctx.Request.ReadFromJsonAsync<CaptureApiRequest>();
            }
            catch
            {
                return Results.Json(ApiResponse.Error(ctx, 400, "BAD_REQUEST"));
            }

            if (payload is null)
            {
                return Results.Json(ApiResponse.Error(ctx, 400, "BAD_REQUEST"));
            }

            var mode = payload.Mode?.ToLowerInvariant() switch
            {
                "screen" => CaptureMode.Screen,
                "window" => CaptureMode.Window,
                "region" => CaptureMode.Region,
                _ => CaptureMode.Screen
            };

            var format = payload.Format?.ToLowerInvariant() switch
            {
                "jpeg" or "jpg" => CaptureFormat.Jpeg,
                _ => CaptureFormat.Png
            };

            WindowMatch? windowMatch = null;
            if (payload.Window is not null)
            {
                windowMatch = new WindowMatch(
                    payload.Window.TitleContains,
                    payload.Window.TitleRegex,
                    payload.Window.ProcessName);
            }

            RectPx? region = null;
            if (payload.Region is not null)
            {
                region = new RectPx(
                    payload.Region.X,
                    payload.Region.Y,
                    payload.Region.W,
                    payload.Region.H);
            }

            var request = new CaptureRequest(
                Mode: mode,
                Window: windowMatch,
                Region: region,
                Format: format,
                Quality: payload.Quality ?? 90,
                DisplayIndex: payload.DisplayIndex);

            var result = captureProvider.Capture(request, windowManager);
            if (result is null)
            {
                return Results.Json(ApiResponse.Error(ctx, 404, "CAPTURE_FAILED"));
            }

            var runId = (string?)ctx.Items["runId"];
            var stepId = (string?)ctx.Items["stepId"];
            var hasExplicitRunId = !string.IsNullOrEmpty(ctx.Request.Headers["X-Run-Id"].ToString());
            string? screenshotPath = null;

            if (hasExplicitRunId && runId is not null && stepId is not null)
            {
                var formatStr = result.Format == CaptureFormat.Png ? "png" : "jpeg";
                screenshotPath = _runLogger.SaveScreenshot(runId, stepId, result.ImageBytes, formatStr);
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
                deviceName = result.Metadata.DeviceName
            };

            return Results.Json(ApiResponse.Ok(ctx, responseData));
        });

        app.MapGet("/capture/selfcheck", (HttpContext ctx) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.Json(ApiResponse.Ok(ctx, new
                {
                    ok = false,
                    reason = "NOT_WINDOWS",
                    captureAvailable = false
                }));
            }

            var request = new CaptureRequest(
                Mode: CaptureMode.Screen,
                Window: null,
                Region: null,
                Format: CaptureFormat.Png,
                Quality: 90,
                DisplayIndex: null);

            var result = captureProvider.Capture(request, windowManager);
            if (result is null)
            {
                return Results.Json(ApiResponse.Ok(ctx, new
                {
                    ok = false,
                    reason = "CAPTURE_FAILED",
                    captureAvailable = false
                }));
            }

            return Results.Json(ApiResponse.Ok(ctx, new
            {
                ok = true,
                captureAvailable = true,
                testImageSize = result.ImageBytes.Length,
                testRegionPx = new { w = result.Metadata.RegionRectPx.W, h = result.Metadata.RegionRectPx.H },
                scale = result.Metadata.Scale,
                dpi = result.Metadata.Dpi,
                displayIndex = result.Metadata.DisplayIndex,
                deviceName = result.Metadata.DeviceName
            }));
        });

        app.MapPost("/input/mouse", async (HttpContext ctx) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.Json(ApiResponse.Error(ctx, 501, "NOT_IMPLEMENTED"));
            }

            MouseInputRequest? payload;
            try
            {
                payload = await ctx.Request.ReadFromJsonAsync<MouseInputRequest>();
            }
            catch
            {
                return Results.Json(ApiResponse.Error(ctx, 400, "BAD_REQUEST"));
            }

            if (payload is null)
            {
                return Results.Json(ApiResponse.Error(ctx, 400, "BAD_REQUEST"));
            }

            var result = mouseInputProvider.Execute(payload);
            if (!result.Ok)
            {
                var statusCode = result.StatusCode ?? 500;
                return Results.Json(ApiResponse.Error(ctx, statusCode, result.Error ?? "UNKNOWN_ERROR"));
            }

            return Results.Json(ApiResponse.Ok(ctx, new { success = true }));
        });

        app.MapPost("/input/key", async (HttpContext ctx) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.Json(ApiResponse.Error(ctx, 501, "NOT_IMPLEMENTED"));
            }

            KeyInputRequest? payload;
            try
            {
                payload = await ctx.Request.ReadFromJsonAsync<KeyInputRequest>();
            }
            catch
            {
                return Results.Json(ApiResponse.Error(ctx, 400, "BAD_REQUEST"));
            }

            if (payload is null)
            {
                return Results.Json(ApiResponse.Error(ctx, 400, "BAD_REQUEST"));
            }

            var result = keyInputProvider.Execute(payload);
            if (!result.Ok)
            {
                var statusCode = result.StatusCode ?? 500;
                return Results.Json(ApiResponse.Error(ctx, statusCode, result.Error ?? "UNKNOWN_ERROR"));
            }

            return Results.Json(ApiResponse.Ok(ctx, new { success = true }));
        });

        app.MapPost("/macro/run", async (HttpContext ctx) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.Json(ApiResponse.Error(ctx, 501, "NOT_IMPLEMENTED"));
            }

            MacroRunRequest? payload;
            try
            {
                payload = await ctx.Request.ReadFromJsonAsync<MacroRunRequest>();
            }
            catch
            {
                return Results.Json(ApiResponse.Error(ctx, 400, "BAD_REQUEST"));
            }

            if (payload?.Steps is null || payload.Steps.Length == 0)
            {
                return Results.Json(ApiResponse.Error(ctx, 400, "BAD_REQUEST: steps array required"));
            }

            var runId = (string?)ctx.Items["runId"] ?? Guid.NewGuid().ToString("n");
            var stepResults = new List<object>();
            var overallOk = true;

            foreach (var step in payload.Steps)
            {
                var stepId = Guid.NewGuid().ToString("n");
                var sw = Stopwatch.StartNew();
                var tsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                object? stepResponse = null;
                bool stepOk = false;
                string? stepError = null;
                string? screenshotPath = null;

                try
                {
                    var stepType = step.Type?.ToLowerInvariant() ?? "";

                    if (stepType == "capture")
                    {
                        var captureReq = step.Capture ?? new MacroCaptureRequest(null, null, null, null, null, null);
                        var mode = captureReq.Mode?.ToLowerInvariant() switch
                        {
                            "window" => CaptureMode.Window,
                            "region" => CaptureMode.Region,
                            _ => CaptureMode.Screen
                        };
                        var format = captureReq.Format?.ToLowerInvariant() switch
                        {
                            "jpeg" or "jpg" => CaptureFormat.Jpeg,
                            _ => CaptureFormat.Png
                        };
                        WindowMatch? windowMatch = null;
                        if (captureReq.Window is not null)
                        {
                            windowMatch = new WindowMatch(
                                captureReq.Window.TitleContains,
                                captureReq.Window.TitleRegex,
                                captureReq.Window.ProcessName);
                        }
                        RectPx? region = null;
                        if (captureReq.Region is not null)
                        {
                            region = new RectPx(captureReq.Region.X, captureReq.Region.Y, captureReq.Region.W, captureReq.Region.H);
                        }

                        var request = new CaptureRequest(mode, windowMatch, region, format, captureReq.Quality ?? 90, captureReq.DisplayIndex);
                        var result = captureProvider.Capture(request, windowManager);

                        if (result is not null)
                        {
                            stepOk = true;
                            var formatStr = result.Format == CaptureFormat.Png ? "png" : "jpeg";
                            screenshotPath = _runLogger.SaveScreenshot(runId, stepId, result.ImageBytes, formatStr);
                            stepResponse = new
                            {
                                screenshotPath,
                                format = formatStr,
                                regionRectPx = new { x = result.Metadata.RegionRectPx.X, y = result.Metadata.RegionRectPx.Y, w = result.Metadata.RegionRectPx.W, h = result.Metadata.RegionRectPx.H },
                                ts = result.Metadata.Ts,
                                scale = result.Metadata.Scale,
                                dpi = result.Metadata.Dpi
                            };
                        }
                        else
                        {
                            stepError = "CAPTURE_FAILED";
                        }
                    }
                    else if (stepType == "mouse")
                    {
                        var mouseReq = step.Mouse;
                        if (mouseReq is null)
                        {
                            stepError = "BAD_REQUEST: mouse object required for type=mouse";
                        }
                        else
                        {
                            var result = mouseInputProvider.Execute(mouseReq);
                            stepOk = result.Ok;
                            stepError = result.Error;
                            stepResponse = new { success = result.Ok };
                        }
                    }
                    else if (stepType == "key")
                    {
                        var keyReq = step.Key;
                        if (keyReq is null)
                        {
                            stepError = "BAD_REQUEST: key object required for type=key";
                        }
                        else
                        {
                            var result = keyInputProvider.Execute(keyReq);
                            stepOk = result.Ok;
                            stepError = result.Error;
                            stepResponse = new { success = result.Ok };
                        }
                    }
                    else if (stepType == "delay")
                    {
                        var delayMs = step.DelayMs ?? 0;
                        if (delayMs > 0)
                        {
                            await System.Threading.Tasks.Task.Delay(delayMs);
                        }
                        stepOk = true;
                        stepResponse = new { delayMs };
                    }
                    else
                    {
                        stepError = $"BAD_REQUEST: unknown step type '{step.Type}'";
                    }
                }
                catch (Exception ex)
                {
                    stepError = ex.Message;
                }

                sw.Stop();
                var durationMs = sw.ElapsedMilliseconds;

                if (!stepOk) overallOk = false;

                var logEntry = new StepLogEntry
                {
                    StepId = stepId,
                    Endpoint = $"/macro/run#{step.Type}",
                    TsMs = tsMs,
                    Request = step,
                    Response = stepResponse,
                    Ok = stepOk,
                    Error = stepError,
                    DurationMs = durationMs,
                    ScreenshotPath = screenshotPath
                };
                _runLogger.LogStep(runId, logEntry);

                stepResults.Add(new
                {
                    stepId,
                    type = step.Type,
                    ok = stepOk,
                    error = stepError,
                    durationMs,
                    response = stepResponse,
                    screenshotPath
                });

                if (!stepOk && (step.OnFailure?.ToLowerInvariant() ?? "stop") == "stop")
                {
                    break;
                }
            }

            return Results.Json(new
            {
                runId,
                ok = overallOk,
                steps = stepResults
            });
        });

        _app = app;
        await app.StartAsync();
    }

    public void Dispose()
    {
        if (_app is null) return;
        try { _app.StopAsync().GetAwaiter().GetResult(); } catch { }
        try { _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        _app = null;
        _runLogger.Dispose();
    }

    private static bool TimingSafeEquals(string a, string b)
    {
        if (a is null || b is null) return false;
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}

public static class ApiResponse
{
    public static object Ok(HttpContext ctx, object data) => new
    {
        runId = (string?)ctx.Items["runId"],
        stepId = (string?)ctx.Items["stepId"],
        ok = true,
        data
    };

    public static object Error(HttpContext ctx, int status, string error) => new
    {
        runId = (string?)ctx.Items["runId"],
        stepId = (string?)ctx.Items["stepId"],
        ok = false,
        status,
        error
    };
}

public sealed record CaptureApiRequest(
    string? Mode,
    CaptureWindowMatch? Window,
    CaptureRegion? Region,
    string? Format,
    int? Quality,
    int? DisplayIndex);

public sealed record CaptureWindowMatch(
    string? TitleContains,
    string? TitleRegex,
    string? ProcessName);

public sealed record CaptureRegion(int X, int Y, int W, int H);

