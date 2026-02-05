using System;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TbxExecutor;

public sealed class ApiHost : IDisposable
{
    private readonly ExecutorConfig _config;
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

        builder.WebHost.ConfigureKestrel(o =>
        {
            // Bind only to configured host/port.
            // Note: ListenHost defaults to 0.0.0.0; user should set it to tailnet IP.
            o.Listen(IPAddress.Parse(_config.ListenHost), _config.ListenPort);
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

        var windowManager = OperatingSystem.IsWindows()
            ? new WindowsWindowManager()
            : new NullWindowManager();
        var lockStateProvider = OperatingSystem.IsWindows()
            ? new WindowsLockStateProvider()
            : new NullLockStateProvider();

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
            // placeholders; filled in later (DPI, monitors)
            var payload = new
            {
                os = Environment.OSVersion.VersionString,
                displays = Array.Empty<object>(),
                coordinateSystem = "physicalPixels"
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

        app.MapPost("/capture", (HttpContext ctx) =>
        {
            return Results.Json(ApiResponse.Error(ctx, 501, "NOT_IMPLEMENTED"));
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
