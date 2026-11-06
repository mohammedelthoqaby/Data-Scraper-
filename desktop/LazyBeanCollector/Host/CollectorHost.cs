using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Jobs;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collector.App.Security;
using LazyBean.Collector.App.Settings;
using LazyBean.Collectors.Http;
using LazyBean.Collectors.Woo;
using Microsoft.Extensions.DependencyInjection;

namespace LazyBean.Collector.App.Host;

// Seams for tests. The app passes none of them and gets the real server, the real handlers and real timings.
public sealed record CollectorHostOptions(
    Func<string, ICollectorApi>? ApiFactory = null,
    IMetaPageCollector? MetaCollector = null,
    IGoogleAdsTransparencyClient? GoogleClient = null,
    TimeSpan? Heartbeat = null,
    Func<TimeSpan, CancellationToken, Task>? Delay = null,
    TimeSpan? SignOutTimeout = null);

// Owns the pairing, the device token, the API client and the worker loop. The window and the headless mode
// both drive the app through this class. Nothing here depends on a window: the loop runs on its own task, so
// closing or disposing the window never stops collection.
public sealed class CollectorHost : IAsyncDisposable
{
    private static readonly Func<TimeSpan, CancellationToken, Task> DefaultDelay = (duration, ct) => Task.Delay(duration, ct);

    // How long a stop, a sign-out or a server change waits for the worker loop before going on regardless.
    private static readonly TimeSpan LoopStopLimit = TimeSpan.FromSeconds(5);

    private readonly SettingsStore _settingsStore;
    private readonly TokenStore _tokenStore;
    private readonly ActivityLog _log;
    private readonly CollectorState _state;
    private readonly ServiceProvider _services;
    private readonly WooStoreApiAdapter _woo;
    private readonly CollectorHostOptions _options;
    private readonly IMetaPageCollector _metaCollector;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _gate = new();
    private readonly CollectorSettings _settings;
    private readonly TimeSpan _signOutTimeout;

    private ICollectorApi? _api;
    private HttpClient? _apiHttp;
    private CancellationTokenSource? _loopCts;
    private Task? _loop;

    public CollectorHost(AppPaths paths, ActivityLog log, CollectorState state, CollectorHostOptions? options = null)
    {
        _log = log;
        _state = state;
        _options = options ?? new CollectorHostOptions();
        _delay = _options.Delay ?? DefaultDelay;
        _metaCollector = _options.MetaCollector ?? new MetaPageCollector();
        _settingsStore = new SettingsStore(paths.SettingsFile);
        _tokenStore = new TokenStore(paths.TokenFile);
        _settings = _settingsStore.Load();
        _signOutTimeout = _options.SignOutTimeout ?? TimeSpan.FromSeconds(10);
        _services = CollectorServices.Build();
        _woo = new WooStoreApiAdapter(_services.GetRequiredService<ICollectorHttpClient>());

        _state.LoadLastSync(_settings.LastSync);
        _state.SyncRecorded += SaveLastSync;
    }

    public CollectorSettings Settings => _settings;

    public bool HasDevice => _settings.DeviceId is not null && _tokenStore.Load() is not null;

    // Starts collecting if this PC is already paired; otherwise the window asks for pairing.
    public Task StartAsync()
    {
        var token = _tokenStore.Load();
        if (token is null || _settings.DeviceId is null)
        {
            _state.SetConnection(ConnectionState.NotPaired, "Pair this PC with your LazyBean account to start collecting.");
            return Task.CompletedTask;
        }

        if (ServerPolicy.NeedsProductionServer(_settings.ServerUrl, ServerPolicy.IsReleaseBuild))
        {
            _log.Warn($"This PC is paired with {_settings.ServerUrl}, a local address. Release builds use {ServerPolicy.ProductionServerUrl}; the window offers the switch.");
        }

        _api = CreateApi(_settings.ServerUrl);
        _api.BearerToken = token;
        _log.Info($"Starting as {_settings.DeviceName ?? "this PC"} on {_settings.ServerUrl}.");
        StartLoop();
        return Task.CompletedTask;
    }

    public async Task PairAsync(string serverUrl, string username, string password, string deviceName, CancellationToken ct)
    {
        await StopLoopWithinAsync();
        var api = CreateApi(serverUrl);
        var result = await api.PairAsync(username, password, deviceName, ct);

        var previousServer = _settings.DeviceId is not null ? _settings.ServerUrl : null;
        _tokenStore.Save(result.Token);
        _settings.ServerUrl = serverUrl.Trim();
        _settings.DeviceId = result.DeviceId;
        _settings.DeviceName = result.DeviceName;
        _settings.PairedBy = username;
        SaveSettings();

        if (previousServer is not null && !SameServer(previousServer, _settings.ServerUrl))
        {
            _log.Warn($"This PC was paired with {previousServer} before. That device is still listed there; revoke it on that server if you no longer need it.");
        }

        api.BearerToken = result.Token;
        _api = api;
        _log.Info($"Paired as {result.DeviceName}. Collection will start on the next poll.");
        StartLoop();
    }

    // Forgets this PC's token first, so sign-out completes even when the server cannot be reached. The server is then
    // asked to revoke the device, for at most SignOutTimeout.
    public async Task SignOutAsync(CancellationToken ct)
    {
        var api = _api;
        await StopLoopWithinAsync();
        ForgetDevice();
        _state.SetConnection(ConnectionState.NotPaired, "Signed out. Pair this PC again to collect.");
        _log.Info("Signed out on this PC.");

        if (api is null)
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_signOutTimeout);
        try
        {
            await api.UnpairAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is CollectorApiException or OperationCanceledException)
        {
            _log.Warn("The server could not be told about the sign-out. Revoke this PC on the dashboard if it is still listed.");
        }
    }

    // Switches a release build from a local test server to production. The pairing belongs to the test server, so this
    // PC forgets it; the pairing form then opens at production.
    public async Task UseProductionServerAsync()
    {
        var previousServer = _settings.ServerUrl;
        await StopLoopWithinAsync();
        ForgetDevice();
        _settings.ServerUrl = ServerPolicy.ProductionServerUrl;
        SaveSettings();
        _state.SetConnection(ConnectionState.NotPaired, $"Pair this PC with {ServerPolicy.ProductionServerUrl} to collect.");
        _log.Warn($"Switched from {previousServer} to {ServerPolicy.ProductionServerUrl}. This PC forgot its local pairing; that device is still listed on {previousServer}.");
    }

    public async Task RequestAdsNowAsync(CancellationToken ct)
    {
        var api = RequireApi();
        await api.RequestAdsNowAsync(ct);
        _log.Info("Queued Meta and Google ads collection.");
    }

    // Stops the worker loop. A job in progress is handed back to the server first.
    public Task StopAsync() => StopLoopAsync();

    // Remembers that the first-time "still running in the background" hint has been shown.
    public void MarkTrayHintShown()
    {
        _settings.TrayHintShown = true;
        SaveSettings();
    }

    // Stops the worker loop without waiting longer than LoopStopLimit: a dead server must not hold up sign-out or a change of server.
    private async Task StopLoopWithinAsync() => await Task.WhenAny(StopLoopAsync(), Task.Delay(LoopStopLimit));

    private void ForgetDevice()
    {
        _tokenStore.Clear();
        _api = null;
        _settings.DeviceId = null;
        _settings.PairedBy = null;
        SaveSettings();
    }

    private static bool SameServer(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private ICollectorApi RequireApi() =>
        _api ?? throw new CollectorApiException(ApiErrorKind.Unauthorized, "This PC is not paired. Pair it first.");

    private void StartLoop()
    {
        lock (_gate)
        {
            var api = RequireApi();
            _loopCts = new CancellationTokenSource();
            var token = _loopCts.Token;
            var runner = CreateRunner(api);
            _loop = Task.Run(async () =>
            {
                try
                {
                    await runner.RunAsync(token);
                }
                catch (OperationCanceledException)
                {
                    // Stopped by the user, by sign-out, or by the app exiting.
                }
                catch (PairingRequiredException ex)
                {
                    _tokenStore.Clear();
                    api.BearerToken = null;
                    _state.SetConnection(ConnectionState.PairingRequired, "This PC was signed out or revoked on the server. Pair it again.");
                    _log.Error($"Stopped: {ex.Message} Pair this PC again.");
                }
                catch (Exception ex)
                {
                    _state.SetConnection(ConnectionState.Stopped, $"Stopped unexpectedly: {ex.Message}");
                    _log.Error($"The collector stopped unexpectedly: {ex}");
                }
            }, token);
        }
    }

    private async Task StopLoopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            loop = _loop;
            cts = _loopCts;
            _loop = null;
            _loopCts = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            if (loop is not null)
            {
                await loop;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the loop is cancelled.
        }
        finally
        {
            cts.Dispose();
        }
    }

    private JobRunner CreateRunner(ICollectorApi api)
    {
        var google = _options.GoogleClient ?? _services.GetRequiredService<IGoogleAdsTransparencyClient>();
        return new JobRunner(
            api,
            new StoreJobHandler(api, _woo, _log, _state),
            new MetaJobHandler(api, _metaCollector, _log, _state, _delay),
            new GoogleJobHandler(api, google, _log, _state, _delay),
            _log,
            _state,
            _delay,
            _options.Heartbeat,
            new MetaPageResolveJobHandler(
                new FacebookPageSource(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }),
                _log,
                _state));
    }

    private ICollectorApi CreateApi(string serverUrl)
    {
        var trimmed = serverUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new CollectorApiException(ApiErrorKind.BadRequest, "The server URL must start with https://.");
        }

        if (_options.ApiFactory is { } factory)
        {
            return factory(trimmed);
        }

        _apiHttp?.Dispose();
        _apiHttp = new HttpClient
        {
            BaseAddress = new Uri(trimmed + "/"),
            Timeout = TimeSpan.FromSeconds(60),
        };
        return new CollectorApiClient(_apiHttp, new RetryPolicy(maxRetries: 2, delay: _delay));
    }

    private void SaveLastSync(IReadOnlyDictionary<string, SourceSync> snapshot)
    {
        _settings.LastSync = snapshot.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        SaveSettings();
    }

    private void SaveSettings()
    {
        lock (_gate)
        {
            _settingsStore.Save(_settings);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopLoopAsync();
        _apiHttp?.Dispose();
        await _services.DisposeAsync();
    }
}
