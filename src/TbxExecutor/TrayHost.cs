using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace TbxExecutor;

public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ExecutorConfig _config;

    public event EventHandler? ExitRequested;
    public event EventHandler? RotateTokenRequested;

    public TrayHost(ExecutorConfig config)
    {
        _config = config;
        _icon = new NotifyIcon
        {
            Text = "TbxExecutor",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        UpdateStatus("Ready");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var copyEndpoint = new ToolStripMenuItem("Copy endpoint")
        {
            ToolTipText = "Copies http://<listenHost>:<listenPort>"
        };
        copyEndpoint.Click += (_, _) =>
        {
            var host = _config.PickListenHost();
            var text = $"http://{host}:{_config.ListenPort}";
            Clipboard.SetText(text);
            UpdateStatus("Endpoint copied");
        };

        var copyToken = new ToolStripMenuItem("Copy token");
        copyToken.Click += (_, _) =>
        {
            Clipboard.SetText(_config.Token);
            UpdateStatus("Token copied");
        };

        var rotateToken = new ToolStripMenuItem("Rotate token");
        rotateToken.Click += (_, _) => RotateTokenRequested?.Invoke(this, EventArgs.Empty);

        var openConfig = new ToolStripMenuItem("Open config folder");
        openConfig.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ExecutorConfig.ConfigDir,
                    UseShellExecute = true
                });
            }
            catch { }
        };

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(copyEndpoint);
        menu.Items.Add(copyToken);
        menu.Items.Add(rotateToken);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openConfig);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        return menu;
    }

    public void UpdateStatus(string status)
    {
        _icon.Text = $"TbxExecutor - {Trim(status)}";
    }

    private static string Trim(string s)
    {
        // NotifyIcon.Text max length is limited.
        if (s.Length <= 48) return s;
        return s.Substring(0, 45) + "...";
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
