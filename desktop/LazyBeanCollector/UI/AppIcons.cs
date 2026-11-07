using System.Drawing;

namespace LazyBean.Collector.App.UI;

// The LazyBean mark, embedded in the exe. Used for the window, the pairing dialog and the tray icon.
public static class AppIcons
{
    public static Icon Load(Size size)
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream("app.ico")
            ?? throw new InvalidOperationException("The app icon is missing from the executable.");
        return new Icon(stream, size);
    }

    public static Image LoadLogo()
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream("logo.png")
            ?? throw new InvalidOperationException("The logo is missing from the executable.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
