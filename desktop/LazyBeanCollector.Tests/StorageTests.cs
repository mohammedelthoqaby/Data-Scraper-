using System.Text;
using LazyBean.Collector.App.Security;
using LazyBean.Collector.App.Settings;

namespace LazyBean.Collector.App.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lazybean-collector-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void TokenStore_RoundTrips_AndNeverWritesTheTokenInPlainText()
    {
        var store = new TokenStore(Path.Combine(_root, "device-token.bin"));

        store.Save("secret-device-token-123");

        var onDisk = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_root, "device-token.bin")));
        Assert.DoesNotContain("secret-device-token-123", onDisk);
        Assert.Equal("secret-device-token-123", store.Load());
    }

    [Fact]
    public void TokenStore_Clear_ForgetsTheToken()
    {
        var store = new TokenStore(Path.Combine(_root, "device-token.bin"));
        store.Save("token");

        store.Clear();

        Assert.Null(store.Load());
    }

    [Fact]
    public void TokenStore_UnreadableFile_LoadsAsNothing()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "device-token.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);

        Assert.Null(new TokenStore(path).Load());
    }

    [Fact]
    public void SettingsStore_RoundTripsTheLastSync()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new SettingsStore(path);
        var settings = new CollectorSettings
        {
            ServerUrl = "https://lazy-bean.runasp.net",
            DeviceId = Guid.NewGuid(),
            DeviceName = "Office PC",
            LastSync = new Dictionary<string, SourceSync>
            {
                ["meta"] = new(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero), "Meta: 12 ad(s)."),
            },
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(settings.DeviceId, loaded.DeviceId);
        Assert.Equal("Office PC", loaded.DeviceName);
        Assert.Equal("Meta: 12 ad(s).", loaded.LastSync["meta"].Result);
    }

    [Fact]
    public void SettingsStore_DamagedFile_IsSetAsideAndDefaultsUsed()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{ not json");

        var loaded = new SettingsStore(path).Load();

        Assert.Equal(CollectorSettings.DefaultServerUrl, loaded.ServerUrl);
        Assert.True(File.Exists(path + ".damaged"));
    }
}
