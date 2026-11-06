using System.Drawing.Drawing2D;

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
            Icon = CreateIcon(),
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

    // A simple brown tile with "LB" so the icon needs no image file.
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(111, 78, 55));
            graphics.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point);
            var size = graphics.MeasureString("LB", font);
            graphics.DrawString("LB", font, Brushes.White, (32 - size.Width) / 2, (32 - size.Height) / 2 - 1);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
