namespace LazyBean.Collector.App.Settings;

// Everything the app keeps lives under %LOCALAPPDATA%\LazyBeanCollector, for the current Windows user only.
public sealed class AppPaths(string root)
{
    // LAZYBEAN_DATA_DIR moves everything to another folder (support, and test runs that must not touch a real profile).
    public static AppPaths Default { get; } = new(ResolveRoot());

    private static string ResolveRoot() =>
        Environment.GetEnvironmentVariable("LAZYBEAN_DATA_DIR") is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyBeanCollector");

    // True when LAZYBEAN_DATA_DIR points the app at its own folder (developer and test runs).
    public static bool DataDirOverridden => Environment.GetEnvironmentVariable("LAZYBEAN_DATA_DIR") is { Length: > 0 };

    public string Root { get; } = root;

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string TokenFile => Path.Combine(Root, "device-token.bin");

    public string LogsDirectory => Path.Combine(Root, "logs");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }
}
