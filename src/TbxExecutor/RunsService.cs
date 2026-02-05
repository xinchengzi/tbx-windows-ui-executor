using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TbxExecutor;

public sealed record RunSummary(
    string RunId,
    DateTime LastWriteUtc,
    int StepsCount,
    bool HasScreenshots);

public sealed class RunsService
{
    private static readonly string RunsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TbxExecutor",
        "runs");

    private static readonly Regex SafeRunIdPattern = new Regex(
        @"^[a-zA-Z0-9_-]+$",
        RegexOptions.Compiled);

    public static bool IsValidRunId(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId)) return false;
        return SafeRunIdPattern.IsMatch(runId);
    }

    public IReadOnlyList<RunSummary> ListRuns(int limit = 20)
    {
        if (!Directory.Exists(RunsDir))
            return Array.Empty<RunSummary>();

        var dirs = Directory.GetDirectories(RunsDir)
            .Select(d => new DirectoryInfo(d))
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .Take(limit);

        var results = new List<RunSummary>();
        foreach (var dir in dirs)
        {
            if (!IsValidRunId(dir.Name)) continue;

            var stepsPath = Path.Combine(dir.FullName, "steps.jsonl");
            var screenshotsDir = Path.Combine(dir.FullName, "screenshots");

            var stepsCount = 0;
            if (File.Exists(stepsPath))
            {
                try
                {
                    stepsCount = File.ReadLines(stepsPath).Count();
                }
                catch { }
            }

            var hasScreenshots = Directory.Exists(screenshotsDir) &&
                                 Directory.GetFiles(screenshotsDir, "*.*").Any();

            results.Add(new RunSummary(
                dir.Name,
                dir.LastWriteTimeUtc,
                stepsCount,
                hasScreenshots));
        }

        return results;
    }

    public (bool Found, string? Content) GetSteps(string runId)
    {
        if (!IsValidRunId(runId))
            return (false, null);

        var stepsPath = Path.Combine(RunsDir, runId, "steps.jsonl");
        if (!File.Exists(stepsPath))
            return (false, null);

        var fullPath = Path.GetFullPath(stepsPath);
        if (!fullPath.StartsWith(Path.GetFullPath(RunsDir), StringComparison.OrdinalIgnoreCase))
            return (false, null);

        try
        {
            var content = File.ReadAllText(stepsPath);
            return (true, content);
        }
        catch
        {
            return (false, null);
        }
    }

    public (bool Found, string? FilePath, string? ContentType) GetScreenshot(string runId, string stepId)
    {
        if (!IsValidRunId(runId) || !IsValidRunId(stepId))
            return (false, null, null);

        var screenshotsDir = Path.Combine(RunsDir, runId, "screenshots");
        var fullScreenshotsDir = Path.GetFullPath(screenshotsDir);

        if (!fullScreenshotsDir.StartsWith(Path.GetFullPath(RunsDir), StringComparison.OrdinalIgnoreCase))
            return (false, null, null);

        if (!Directory.Exists(screenshotsDir))
            return (false, null, null);

        var pngPath = Path.Combine(screenshotsDir, $"step_{stepId}.png");
        var jpgPath = Path.Combine(screenshotsDir, $"step_{stepId}.jpg");

        if (File.Exists(pngPath))
        {
            var fullPath = Path.GetFullPath(pngPath);
            if (fullPath.StartsWith(fullScreenshotsDir, StringComparison.OrdinalIgnoreCase))
                return (true, pngPath, "image/png");
        }

        if (File.Exists(jpgPath))
        {
            var fullPath = Path.GetFullPath(jpgPath);
            if (fullPath.StartsWith(fullScreenshotsDir, StringComparison.OrdinalIgnoreCase))
                return (true, jpgPath, "image/jpeg");
        }

        return (false, null, null);
    }

    public string GetRunsDir() => RunsDir;
}
