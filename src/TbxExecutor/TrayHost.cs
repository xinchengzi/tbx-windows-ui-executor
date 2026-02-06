using System;
using System.Drawing;
using System.Windows.Forms;

namespace TbxExecutor;

public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ExecutorConfig _config;
    private readonly Icon _normalIcon;
    private readonly Icon _busyIcon;

    public event EventHandler? ExitRequested;
    public event EventHandler? RotateTokenRequested;

    public TrayHost(ExecutorConfig config)
    {
        _config = config;
        _normalIcon = SystemIcons.Application;
        _busyIcon = CreateBusyIcon();
        
        _icon = new NotifyIcon
        {
            Text = "TbxExecutor",
            Icon = _normalIcon,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        UpdateStatus("Ready");
    }

    private static Icon CreateBusyIcon()
    {
        const int size = 16;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(255, 0, 120, 215));
            g.FillEllipse(brush, 1, 1, size - 3, size - 3);
            using var pen = new Pen(Color.White, 2f);
            g.DrawLine(pen, size / 2, 4, size / 2, size / 2);
            g.DrawLine(pen, size / 2, size / 2, size - 4, size / 2);
        }
        return Icon.FromHandle(bmp.GetHicon());
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
            System.Windows.Forms.Clipboard.SetText(text);
            UpdateStatus("Endpoint copied");
        };

        var copyToken = new ToolStripMenuItem("Copy token");
        copyToken.Click += (_, _) =>
        {
            System.Windows.Forms.Clipboard.SetText(_config.Token);
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

    public void SetBusy(bool isBusy)
    {
        _icon.Icon = isBusy ? _busyIcon : _normalIcon;
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
