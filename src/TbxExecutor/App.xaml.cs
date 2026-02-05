using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TbxExecutor;

public partial class App : Application
{
    private TrayHost? _tray;
    private ApiHost? _api;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // No main window; tray-only.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Ensure single instance (best-effort).
        var createdNew = false;
        using var mutex = new Mutex(true, "TbxExecutor.Singleton", out createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        var config = ExecutorConfig.LoadOrCreate();

        _tray = new TrayHost(config);
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.RotateTokenRequested += (_, _) =>
        {
            config.RotateToken();
            _tray!.UpdateStatus("Token rotated");
        };

        _api = new ApiHost(config);
        _tray.UpdateStatus($"Starting API on {config.ListenHost}:{config.ListenPort}");

        try
        {
            await _api.StartAsync();
            _tray.UpdateStatus($"API listening on {config.ListenHost}:{config.ListenPort}");
        }
        catch (Exception ex)
        {
            _tray.UpdateStatus($"API failed: {ex.Message}");
        }

        // Keep mutex alive until app exits.
        _ = Task.Run(() =>
        {
            while (!Dispatcher.HasShutdownStarted)
                Thread.Sleep(500);
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _api?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
