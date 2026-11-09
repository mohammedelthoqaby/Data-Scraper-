using LazyBean.Collector.App.Runtime;

namespace LazyBean.Collector.App.UI;

// The notification-area icon: the menu, the status tooltip, the working badge and the one-time hint. It only
// shows state and raises the actions it is given; TrayApplicationContext decides what they do.
public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly Icon _idleIcon;
    private readonly Icon _workingIcon;
    private bool _working;

    public TrayController(Action openWindow, Action adsNow, Action exit)
    {
        _idleIcon = AppIcons.Load(SystemInformation.SmallIconSize);
        _workingIcon = TrayIcons.WithWorkingBadge(_idleIcon, SystemInformation.SmallIconSize);

        var open = new ToolStripMenuItem("Open LazyBean Collector", null, (_, _) => openWindow())
        {
            Font = new Font(UiStyle.BodyFont, FontStyle.Bold),
        };
        _statusItem = new ToolStripMenuItem("Starting…") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(open);
        menu.Items.Add("Collect ads now", null, (_, _) => adsNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = "LazyBean Collector: Starting…",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => openWindow();
    }

    // Updates the status line, the tooltip and, while a job runs, the icon.
    public void Show(string status, bool working)
    {
        _statusItem.Text = status;
        var tooltip = $"LazyBean Collector: {status}";

        // NotifyIcon tooltips are limited to 63 characters.
        _icon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;

        if (working != _working)
        {
            _working = working;
            _icon.Icon = working ? _workingIcon : _idleIcon;
        }
    }

    public void ShowBalloon(string text) => _icon.ShowBalloonTip(5000, "LazyBean Collector", text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _workingIcon.Dispose();
        _idleIcon.Dispose();
    }
}
