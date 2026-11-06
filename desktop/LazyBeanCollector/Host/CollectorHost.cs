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

// Owns the pairing, the device token, the API client and the worker loop. The window and the headless
// mode both drive the app through this class.
public sealed class CollectorHost : IAsyncDisposable
{
    private static readonly Func<TimeSpan, CancellationToken, Task> Delay = (duration, ct) => Task.Delay(duration, ct);

    private readonly SettingsStore _settingsStore;
    private readonly TokenStore _tokenStore;
    private readonly ActivityLog _log;
    private readonly CollectorState _state;
    private readonly ServiceProvider _services;
    private readonly WooStoreApiAdapter _woo;
    private readonly IMetaPageCollector _metaCollector = new MetaPageCollector();
    private readonly object _gate = new();
    private readonly CollectorSettings _settings;

    private CollectorApiClient? _api;
    private HttpClient? _apiHttp;
    private CancellationTokenSource? _loopCts;
    private Task? _loop;

    public CollectorHost(AppPaths paths, ActivityLog log, CollectorState state)
    {
        _log = log;
        _state = state;
        _settingsStore = new SettingsStore(paths.SettingsFile);
        _tokenStore = new TokenStore(paths.TokenFile);
        _settings = _settingsStore.Load();
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

        _api = CreateApi(_settings.ServerUrl);
        _api.BearerToken = token;
        _log.Info($"Starting as {_settings.DeviceName ?? "this PC"} on {_settings.ServerUrl}.");
        StartLoop();
        return Task.CompletedTask;
    }

    public async Task PairAsync(string serverUrl, string username, string password, string deviceName, CancellationToken ct)
    {
        await StopLoopAsync();
        var api = CreateApi(serverUrl);
        var result = await api.PairAsync(username, password, deviceName, ct);

        _tokenStore.Save(result.Token);
        _settings.ServerUrl = serverUrl.Trim();
        _settings.DeviceId = result.DeviceId;
        _settings.DeviceName = result.DeviceName;
        _settings.PairedBy = username;
        SaveSettings();

        api.BearerToken = result.Token;
        _api = api;
        _log.Info($"Paired as {result.DeviceName}. Collection will start on the next poll.");
        StartLoop();
    }

    // Tells the server to revoke this device, then forgets the token on this PC either way.
    public async Task SignOutAsync(CancellationToken ct)
    {
        await StopLoopAsync();
        if (_api is { BearerToken: not null })
        {
            try
            {
                await _api.UnpairAsync(ct);
            }
            catch (CollectorApiException ex)
            {
                _log.Warn($"The server could not be told about the sign-out ({ex.Message}). Revoke this PC on the dashboard if it is still listed.");
            }
        }

        _tokenStore.Clear();
        _settings.DeviceId = null;
        _settings.PairedBy = null;
        SaveSettings();
        _state.SetConnection(ConnectionState.NotPaired, "Signed out. Pair this PC again to collect.");
        _log.Info("Signed out on this PC.");
    }

    public async Task RequestAdsNowAsync(CancellationToken ct)
    {
        var api = RequireApi();
        await api.RequestAdsNowAsync(ct);
        _log.Info("Queued Meta and Google ads collection.");
    }

    public Task StopAsync() => StopLoopAsync();

    private CollectorApiClient RequireApi() =>
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
                    // Stopped by the user, by sign-out, or by the app closing.
                }
                catch (PairingRequiredException ex)
                {
                    _tokenStore.Clear();
                    _api!.BearerToken = null;
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

    private JobRunner CreateRunner(CollectorApiClient api)
    {
        var google = _services.GetRequiredService<IGoogleAdsTransparencyClient>();
        return new JobRunner(
            api,
            new StoreJobHandler(api, _woo, _log, _state),
            new MetaJobHandler(api, _metaCollector, _log, _state, Delay),
            new GoogleJobHandler(api, google, _log, _state, Delay),
            _log,
            _state,
            Delay);
    }

    private CollectorApiClient CreateApi(string serverUrl)
    {
        var trimmed = serverUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new CollectorApiException(ApiErrorKind.BadRequest, "The server URL must start with https://.");
        }

        _apiHttp?.Dispose();
        _apiHttp = new HttpClient
        {
            BaseAddress = new Uri(trimmed + "/"),
            Timeout = TimeSpan.FromSeconds(60),
        };
        return new CollectorApiClient(_apiHttp, new RetryPolicy(maxRetries: 2, delay: Delay));
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
