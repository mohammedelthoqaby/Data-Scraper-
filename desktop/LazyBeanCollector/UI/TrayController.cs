namespace LazyBean.Collector.App.UI;

// The notification-area icon. Lets the app keep collecting while its window is closed.
public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _hintShown;

    public TrayController(Action openWindow, Action adsNow, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open LazyBean Collector", null, (_, _) => openWindow());
        menu.Items.Add("Collect ads now", null, (_, _) => adsNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = AppIcons.Load(SystemInformation.SmallIconSize),
            Text = "LazyBean Collector",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => openWindow();
    }

    public void UpdateText(string text)
    {
        // NotifyIcon tooltips are limited to 63 characters.
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void ShowHiddenHint()
    {
        if (_hintShown)
        {
            return;
        }

        _hintShown = true;
        _icon.ShowBalloonTip(4000, "LazyBean Collector", "Still running in the system tray. Right-click the icon to exit.", ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
