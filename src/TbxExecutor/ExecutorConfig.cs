using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace TbxExecutor;

public sealed class ExecutorConfig
{
    public string ListenHost { get; set; } = "0.0.0.0"; // should be set to tailnet IP by user
    public int ListenPort { get; set; } = 17890;
    public List<string> AllowlistIps { get; set; } = new() { "100.64.0.1" };
    public string Token { get; set; } = "";

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TbxExecutor");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static ExecutorConfig LoadOrCreate()
    {
        Directory.CreateDirectory(ConfigDir);

        // Seed from appsettings.json if present
        var seeded = LoadFromAppSettings();

        if (File.Exists(ConfigPath))
        {
            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<ExecutorConfig>(json, JsonOptions()) ?? seeded;
            if (string.IsNullOrWhiteSpace(cfg.Token))
                cfg.Token = GenerateToken();
            cfg.Save();
            return cfg;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(seeded.Token))
                seeded.Token = GenerateToken();
            seeded.Save();
            return seeded;
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions());
        File.WriteAllText(ConfigPath, json);
    }

    public void RotateToken()
    {
        Token = GenerateToken();
        Save();
    }

    private static ExecutorConfig LoadFromAppSettings()
    {
        try
        {
            // appsettings.json is copied to output; during dev it sits beside the exe.
            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "appsettings.json");
            if (!File.Exists(path)) return new ExecutorConfig();

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            var cfg = new ExecutorConfig();

            if (doc.RootElement.TryGetProperty("listenPort", out var lp) && lp.TryGetInt32(out var port))
                cfg.ListenPort = port;

            if (doc.RootElement.TryGetProperty("allowlistIps", out var ips) && ips.ValueKind == JsonValueKind.Array)
                cfg.AllowlistIps = ips.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()!;

            if (doc.RootElement.TryGetProperty("token", out var tok) && tok.ValueKind == JsonValueKind.String)
                cfg.Token = tok.GetString() ?? "";

            return cfg;
        }
        catch
        {
            return new ExecutorConfig();
        }
    }

    private static string GenerateToken()
    {
        Span<byte> b = stackalloc byte[32];
        RandomNumberGenerator.Fill(b);
        return Convert.ToBase64String(b).TrimEnd('=');
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
