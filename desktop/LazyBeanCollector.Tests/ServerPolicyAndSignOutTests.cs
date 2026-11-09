using System.Diagnostics;
using LazyBean.Infrastructure.External.LazyBeanApi;
using LazyBean.Application.Collector.Exceptions;
using LazyBean.Collector.App.Host;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collector.App.Security;
using LazyBean.Collector.App.Settings;

namespace LazyBean.Collector.App.Tests;

// Which server a release build talks to: the production default, and the warning for a local address.
public sealed class ServerPolicyTests
{
    [Theory]
    [InlineData("http://localhost:5188", true)]
    [InlineData("https://127.0.0.1", true)]
    [InlineData("http://127.0.0.2:8000", true)]
    [InlineData("http://[::1]:5188", true)]
    [InlineData("https://dev.localhost", true)]
    [InlineData("https://lazy-bean.runasp.net", false)]
    [InlineData("https://localhost.example.com", false)]
    [InlineData("", false)]
    public void LocalAddressesAreRecognised(string url, bool expected)
    {
        Assert.Equal(expected, ServerPolicy.IsLocalAddress(url));
    }

    [Fact]
    public void ReleaseBuildSavedToALocalServer_NeedsProduction_ButADebugBuildDoesNot()
    {
        Assert.True(ServerPolicy.NeedsProductionServer("http://localhost:5188", releaseBuild: true));
        Assert.False(ServerPolicy.NeedsProductionServer("http://localhost:5188", releaseBuild: false));
        Assert.False(ServerPolicy.NeedsProductionServer(ServerPolicy.ProductionServerUrl, releaseBuild: true));
    }

    [Fact]
    public void ProductionIsTheDefaultServer()
    {
        Assert.Equal("https://lazy-bean.runasp.net", ServerPolicy.ProductionServerUrl);
        Assert.Equal(ServerPolicy.ProductionServerUrl, new CollectorSettings().ServerUrl);
    }

    [Fact]
    public void PairingFormStartsAtProduction_WhenTheSavedServerIsLocalInARelease()
    {
        Assert.Equal(ServerPolicy.ProductionServerUrl, ServerPolicy.PairingDefault("http://localhost:5188", releaseBuild: true));
        Assert.Equal(ServerPolicy.ProductionServerUrl, ServerPolicy.PairingDefault("", releaseBuild: true));
        Assert.Equal("https://staging.example.com", ServerPolicy.PairingDefault("https://staging.example.com", releaseBuild: true));
        Assert.Equal("http://localhost:5188", ServerPolicy.PairingDefault("http://localhost:5188", releaseBuild: false));
    }
}

// Sign-out and the server switch must finish even when the server cannot be reached.
public sealed class SignOutAndServerSwitchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lazybean-signout-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SignOut_WhenTheServerIsOffline_StillForgetsThisPc()
    {
        var paths = Paired("https://lazy-bean.runasp.net");
        var api = new FakeCollectorApi
        {
            UnpairBehavior = _ => throw new CollectorApiException(ApiErrorKind.Offline, "Could not reach the server."),
        };
        await using var host = CreateHost(paths, api, TimeSpan.FromSeconds(2));
        await host.StartAsync();

        await host.SignOutAsync(CancellationToken.None);

        Assert.Null(new TokenStore(paths.TokenFile).Load());
        Assert.Null(new SettingsStore(paths.SettingsFile).Load().DeviceId);
        Assert.False(host.HasDevice);
    }

    [Fact]
    public async Task SignOut_WhenTheServerHangs_FinishesWithinTheTimeLimit()
    {
        var paths = Paired("https://lazy-bean.runasp.net");
        var api = new FakeCollectorApi { UnpairBehavior = ct => Task.Delay(Timeout.Infinite, ct) };
        await using var host = CreateHost(paths, api, TimeSpan.FromMilliseconds(300));
        await host.StartAsync();

        var stopwatch = Stopwatch.StartNew();
        await host.SignOutAsync(CancellationToken.None);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Sign-out took {stopwatch.Elapsed}.");
        Assert.Null(new TokenStore(paths.TokenFile).Load());
        Assert.False(host.HasDevice);
    }

    [Fact]
    public async Task SignOut_OnTheServer_AsksItToRevokeTheDevice()
    {
        var paths = Paired("https://lazy-bean.runasp.net");
        var revoked = false;
        var api = new FakeCollectorApi
        {
            UnpairBehavior = _ =>
            {
                revoked = true;
                return Task.CompletedTask;
            },
        };
        await using var host = CreateHost(paths, api, TimeSpan.FromSeconds(2));
        await host.StartAsync();

        await host.SignOutAsync(CancellationToken.None);

        Assert.True(revoked);
        Assert.Null(new TokenStore(paths.TokenFile).Load());
    }

    [Fact]
    public async Task UseProductionServer_ForgetsALocalPairing_AndPointsAtProduction()
    {
        var paths = Paired("http://localhost:5188");
        await using var host = CreateHost(paths, new FakeCollectorApi(), TimeSpan.FromSeconds(2));
        await host.StartAsync();

        await host.UseProductionServerAsync();

        Assert.Equal(ServerPolicy.ProductionServerUrl, host.Settings.ServerUrl);
        Assert.False(host.HasDevice);
        Assert.Null(new TokenStore(paths.TokenFile).Load());
        var saved = new SettingsStore(paths.SettingsFile).Load();
        Assert.Equal(ServerPolicy.ProductionServerUrl, saved.ServerUrl);
        Assert.Null(saved.DeviceId);
    }

    private AppPaths Paired(string serverUrl)
    {
        var paths = new AppPaths(_root);
        paths.EnsureDirectories();
        new SettingsStore(paths.SettingsFile).Save(new CollectorSettings
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Test PC",
            ServerUrl = serverUrl,
            PairedBy = "admin",
        });
        new TokenStore(paths.TokenFile).Save("device-token");
        return paths;
    }

    private static CollectorHost CreateHost(AppPaths paths, FakeCollectorApi api, TimeSpan signOutTimeout) =>
        new(
            paths,
            new ActivityLog(paths.LogsDirectory),
            new CollectorState(),
            new CollectorHostOptions(
                ApiFactory: _ => api,
                GoogleClient: new FakeGoogleClient(),
                Heartbeat: TimeSpan.FromMilliseconds(40),
                Delay: (duration, ct) => Task.Delay(TimeSpan.FromMilliseconds(Math.Min(20, duration.TotalMilliseconds)), ct),
                SignOutTimeout: signOutTimeout));
}

// The "unreachable for more than a minute" rule behind the Change server button.
public sealed class UnreachableTimeTests
{
    [Fact]
    public void UnreachableTime_CountsFromTheFirstFailedClaim_AndClearsWhenTheServerAnswers()
    {
        var now = DateTimeOffset.Parse("2026-11-07T10:00:00Z");
        var state = new CollectorState(() => now);
        Assert.Null(state.UnreachableFor);

        state.SetConnection(ConnectionState.Waiting, "Could not reach the server.");
        now = now.AddSeconds(45);
        Assert.Equal(TimeSpan.FromSeconds(45), state.UnreachableFor);

        now = now.AddSeconds(30);
        state.SetConnection(ConnectionState.Waiting, "Could not reach the server.");
        Assert.Equal(TimeSpan.FromSeconds(75), state.UnreachableFor);

        state.SetConnection(ConnectionState.Connected, "Connected to the server.");
        Assert.Null(state.UnreachableFor);
    }
}
