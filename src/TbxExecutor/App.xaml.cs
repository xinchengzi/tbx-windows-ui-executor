using System;
using System.Threading;

namespace TbxExecutor;

public partial class App : System.Windows.Application
{
    private TrayHost? _tray;
    private ApiHost? _api;
    private Mutex? _singleInstanceMutex;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        _singleInstanceMutex = new Mutex(true, "TbxExecutor.Singleton", out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        var config = ExecutorConfig.LoadOrCreate();
        var listenHost = config.PickListenHost();

        _tray = new TrayHost(config);
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.RotateTokenRequested += (_, _) =>
        {
            config.RotateToken();
            _tray!.UpdateStatus("Token rotated");
        };

        _api = new ApiHost(config);
        _tray.UpdateStatus($"Starting API on {listenHost}:{config.ListenPort}");

        _api.BusyIndicator.BusyStateChanged += isBusy =>
        {
            Dispatcher.BeginInvoke(() => _tray!.SetBusy(isBusy));
        };

        try
        {
            await _api.StartAsync();
            _tray.UpdateStatus($"API listening on {listenHost}:{config.ListenPort}");
        }
        catch (Exception ex)
        {
            _tray.UpdateStatus($"API failed: {ex.Message}");
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _api?.Dispose();
        _tray?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
