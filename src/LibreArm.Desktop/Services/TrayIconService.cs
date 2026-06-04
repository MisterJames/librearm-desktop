namespace LibreArm_Desktop.Services;

using System.Drawing;
using Microsoft.UI.Dispatching;
using WinForms = System.Windows.Forms;

public sealed class TrayIconService : IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _watchItem;
    private bool _watchPaused;

    public TrayIconService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _watchItem = new WinForms.ToolStripMenuItem("Pause Qardio Watch", null, (_, _) => Raise(ToggleWatchRequested));

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(new WinForms.ToolStripMenuItem("Open LibreArm", null, (_, _) => Raise(OpenRequested)));
        menu.Items.Add(_watchItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("Device Setup", null, (_, _) => Raise(DeviceSetupRequested)));
        menu.Items.Add(new WinForms.ToolStripMenuItem("Profiles", null, (_, _) => Raise(ProfilesRequested)));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("Exit", null, (_, _) => Raise(ExitRequested)));

        _notifyIcon = new WinForms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = LoadIcon(),
            Text = "LibreArm",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Raise(OpenRequested);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ToggleWatchRequested;

    public event EventHandler? DeviceSetupRequested;

    public event EventHandler? ProfilesRequested;

    public event EventHandler? ExitRequested;

    public bool WatchPaused
    {
        get => _watchPaused;
        set
        {
            _watchPaused = value;
            _watchItem.Text = value ? "Resume Qardio Watch" : "Pause Qardio Watch";
            _notifyIcon.Text = value ? "LibreArm - Qardio watch paused" : "LibreArm - watching for Qardio";
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void Raise(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        _dispatcher.TryEnqueue(() => handler(this, EventArgs.Empty));
    }

    private static Icon LoadIcon()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "AppIcon.ico"),
            "Assets/AppIcon.ico"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return new Icon(candidate);
            }
        }

        return SystemIcons.Application;
    }
}
