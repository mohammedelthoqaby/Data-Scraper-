using System.Text.Json;

namespace LazyBean.Collector.App.Settings;

// Non-secret settings. The device token is never stored here (see Security/TokenStore).
public sealed class CollectorSettings
{
    public const string DefaultServerUrl = "https://lazy-bean.runasp.net";

    public string ServerUrl { get; set; } = DefaultServerUrl;

    public Guid? DeviceId { get; set; }

    public string? DeviceName { get; set; }

    public string? PairedBy { get; set; }

    // Set once the first-time "still running in the background" hint has been shown.
    public bool TrayHintShown { get; set; }

    // Keyed by source: "store:<slug>", "meta" or "google".
    public Dictionary<string, SourceSync> LastSync { get; set; } = new(StringComparer.Ordinal);
}

public sealed record SourceSync(DateTimeOffset At, string Result);

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public CollectorSettings Load()
    {
        if (!File.Exists(path))
        {
            return new CollectorSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<CollectorSettings>(File.ReadAllText(path), Options) ?? new CollectorSettings();
        }
        catch (JsonException)
        {
            // A damaged settings file must not stop the app: start from defaults and keep the broken copy aside.
            File.Move(path, path + ".damaged", overwrite: true);
            return new CollectorSettings();
        }
    }

    public void Save(CollectorSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
