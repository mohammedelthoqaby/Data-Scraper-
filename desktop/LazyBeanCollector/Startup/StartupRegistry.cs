using Microsoft.Win32;

namespace LazyBean.Collector.App.Startup;

// "Start with Windows" is off by default. It adds a per-user Run entry (no administrator rights needed).
public static class StartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LazyBeanCollector";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{exePath}\" --background", RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
