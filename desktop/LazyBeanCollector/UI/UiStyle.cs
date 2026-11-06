namespace LazyBean.Collector.App.UI;

// Shared look: Segoe UI renders Arabic text (titles and log lines from the catalog) without extra setup.
public static class UiStyle
{
    public static readonly Font BodyFont = new("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

    public static readonly Font TitleFont = new("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Point);

    public static readonly Font SmallFont = new("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

    public static readonly Color Good = Color.FromArgb(21, 128, 61);

    public static readonly Color Warn = Color.FromArgb(180, 83, 9);

    public static readonly Color Bad = Color.FromArgb(185, 28, 28);

    public static readonly Color Muted = Color.FromArgb(100, 116, 139);

    public static readonly Color Idle = Color.FromArgb(148, 163, 184);
}
