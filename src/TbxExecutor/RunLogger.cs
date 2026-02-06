using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace TbxExecutor;

public sealed class RunLogger : IDisposable
{
    private static readonly string RunsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TbxExecutor",
        "runs");

    private const int MaxCachedRuns = 50;
    private readonly ConcurrentDictionary<string, RunContext> _runs = new();
    private readonly ReaderWriterLockSlim _globalLock = new();

    public RunContext GetOrCreateRun(string runId)
    {
        var ctx = _runs.GetOrAdd(runId, id => new RunContext(id, RunsDir));
        TrimOldRunsIfNeeded();
        return ctx;
    }

    private void TrimOldRunsIfNeeded()
    {
        if (_runs.Count <= MaxCachedRuns) return;

        var toRemove = _runs
            .OrderBy(kv => kv.Value.LastAccessUtc)
            .Take(_runs.Count - MaxCachedRuns)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            if (_runs.TryRemove(key, out var ctx))
            {
                ctx.Dispose();
            }
        }
    }

    public bool HasRun(string runId) => _runs.ContainsKey(runId);

    public void LogStep(string runId, StepLogEntry entry)
    {
        var ctx = GetOrCreateRun(runId);
        ctx.AppendStep(entry);
    }

    public string SaveScreenshot(string runId, string stepId, byte[] imageBytes, string format)
    {
        var ctx = GetOrCreateRun(runId);
        return ctx.SaveScreenshot(stepId, imageBytes, format);
    }

    public void Dispose()
    {
        _globalLock.Dispose();
        foreach (var ctx in _runs.Values)
        {
            ctx.Dispose();
        }
        _runs.Clear();
    }
}

public sealed class RunContext : IDisposable
{
    private readonly string _runDir;
    private readonly string _stepsPath;
    private readonly string _screenshotsDir;
    private readonly object _fileLock = new();
    private StreamWriter? _stepsWriter;
    private bool _disposed;

    public string RunId { get; }
    public DateTime LastAccessUtc { get; private set; } = DateTime.UtcNow;

    public RunContext(string runId, string runsBaseDir)
    {
        RunId = runId;
        _runDir = Path.Combine(runsBaseDir, runId);
        _stepsPath = Path.Combine(_runDir, "steps.jsonl");
        _screenshotsDir = Path.Combine(_runDir, "screenshots");
    }

    public void AppendStep(StepLogEntry entry)
    {
        lock (_fileLock)
        {
            LastAccessUtc = DateTime.UtcNow;
            EnsureDirectory();
            EnsureStepsWriter();
            var json = JsonSerializer.Serialize(entry, StepLogJsonOptions);
            _stepsWriter!.WriteLine(json);
            _stepsWriter.Flush();
        }
    }

    public string SaveScreenshot(string stepId, byte[] imageBytes, string format)
    {
        lock (_fileLock)
        {
            LastAccessUtc = DateTime.UtcNow;
            Directory.CreateDirectory(_screenshotsDir);
            var ext = format?.ToLowerInvariant() switch
            {
                "jpeg" or "jpg" => ".jpg",
                _ => ".png"
            };
            var filename = $"step_{stepId}{ext}";
            var fullPath = Path.Combine(_screenshotsDir, filename);
            File.WriteAllBytes(fullPath, imageBytes);
            return $"screenshots/{filename}";
        }
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(_runDir);
    }

    private void EnsureStepsWriter()
    {
        _stepsWriter ??= new StreamWriter(
            new FileStream(_stepsPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        { AutoFlush = false };
    }

    private static JsonSerializerOptions StepLogJsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_fileLock)
        {
            _stepsWriter?.Dispose();
            _stepsWriter = null;
        }
    }
}

public sealed record StepLogEntry
{
    public required string StepId { get; init; }
    public required string Endpoint { get; init; }
    public required long TsMs { get; init; }
    public object? Request { get; init; }
    public object? Response { get; init; }
    public bool? Ok { get; init; }
    public string? Error { get; init; }
    public long? DurationMs { get; init; }
    public string? ScreenshotPath { get; init; }
}
