using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LazyBean.Collector.App.UI;

public static class TrayIcons
{
    // The app icon with a green dot in its corner. The tray shows it while a job runs.
    public static Icon WithWorkingBadge(Icon icon, Size size)
    {
        using var source = icon.ToBitmap();
        using var canvas = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawImage(source, 0, 0, size.Width, size.Height);

            var dot = Math.Max(6, size.Width / 2);
            var x = size.Width - dot;
            var y = size.Height - dot;
            using var fill = new SolidBrush(UiStyle.Good);
            using var outline = new Pen(Color.White, 1f);
            g.FillEllipse(fill, x, y, dot - 1, dot - 1);
            g.DrawEllipse(outline, x, y, dot - 1, dot - 1);
        }

        // Icon.FromHandle does not own the handle. Clone it into an icon that does, then release the original.
        var handle = canvas.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
