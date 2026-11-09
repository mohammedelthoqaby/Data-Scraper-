using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collector.Dtos;

namespace LazyBean.Collector.App.Api;

// HTTP client for /api/collector/*. Every call except pairing goes through the retry policy, and every
// failure is classified into a CollectorApiException the app can explain to the user.
public sealed class CollectorApiClient(HttpClient http, RetryPolicy retry) : ICollectorApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Set after pairing and when the app starts with a saved token.
    public string? BearerToken { get; set; }

    public async Task<PairCollectorDeviceResultDto> PairAsync(string username, string password, string deviceName, CancellationToken ct)
    {
        // No retries for pairing: a retry after a dropped response could create a second device.
        try
        {
            return await ReadRequiredAsync<PairCollectorDeviceResultDto>(
                HttpMethod.Post,
                "api/collector/pair",
                new { username, password, deviceName },
                auth: false,
                ct);
        }
        catch (CollectorApiException ex) when (ex.Kind == ApiErrorKind.Unauthorized)
        {
            throw new CollectorApiException(ApiErrorKind.Unauthorized, "The username or password is not correct.", ex.StatusCode, inner: ex);
        }
        catch (CollectorApiException ex) when (ex.Kind == ApiErrorKind.RateLimited)
        {
            throw new CollectorApiException(ApiErrorKind.RateLimited, "Too many attempts. Wait 10 minutes, then try again.", ex.StatusCode, inner: ex);
        }
    }

    public Task<CollectorJobDto?> ClaimNextJobAsync(CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var response = await SendAsync(HttpMethod.Get, "api/collector/jobs/next", null, auth: true, c);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            return await ReadBodyAsync<CollectorJobDto>(response, c);
        }, ct);

    // Sends what the app is doing with the job, so the dashboard can show it ("Meta page 4/5: Fengany").
    public Task HeartbeatAsync(Guid jobId, CollectorHeartbeatRequest? progress, CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var _ = await SendAsync(HttpMethod.Post, $"api/collector/jobs/{jobId}/heartbeat", progress, auth: true, c);
            return true;
        }, ct);

    // Used on the way out, so it makes one attempt and no retries: exiting must not wait on retry delays.
    public async Task ReleaseJobAsync(Guid jobId, CancellationToken ct)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"api/collector/jobs/{jobId}/release", null, auth: true, ct);
    }

    public Task CompleteJobAsync(Guid jobId, CompleteCollectorJobRequest request, CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var _ = await SendAsync(HttpMethod.Post, $"api/collector/jobs/{jobId}/complete", request, auth: true, c);
            return true;
        }, ct);

    public Task<CollectorIdentities> GetIdentitiesAsync(CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var response = await SendAsync(HttpMethod.Get, "api/collector/identities", null, auth: true, c);
            var body = await ReadBodyAsync<IdentitiesBody>(response, c)
                ?? throw new CollectorApiException(ApiErrorKind.ServerError, "The server returned no identities.");
            return new CollectorIdentities(body.Meta ?? [], body.Google ?? []);
        }, ct);

    public Task IngestProductsAsync(Guid runId, string slug, IngestProductsBatchRequest request, CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var _ = await SendAsync(HttpMethod.Post, $"api/collector/runs/{runId}/competitors/{slug}/products", request, auth: true, c);
            return true;
        }, ct);

    public Task CompleteCompetitorAsync(Guid runId, string slug, CompleteCompetitorRunRequest request, CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var _ = await SendAsync(HttpMethod.Post, $"api/collector/runs/{runId}/competitors/{slug}/complete", request, auth: true, c);
            return true;
        }, ct);

    public Task<AdsIngestResponse> IngestMetaAdsAsync(MetaAdsIngestRequest request, CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var response = await SendAsync(HttpMethod.Post, "api/collector/ads/meta", request, auth: true, c);
            return await ReadBodyAsync<AdsIngestResponse>(response, c)
                ?? throw new CollectorApiException(ApiErrorKind.ServerError, "The server returned no ingest result.");
        }, ct);

    // A batch of creative details that the server does not store is not retried here: the creatives keep their missing detail,
    // so the next run reads them again.
    // A batch the server refuses is not retried: its ads keep their images, and the next run asks for them again.
    public Task<AdThumbnailUploadResponse> UploadAdThumbnailsAsync(AdThumbnailUploadRequest request, CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var response = await SendAsync(HttpMethod.Post, "api/collector/ads/thumbnails", request, auth: true, c);
            return await ReadBodyAsync<AdThumbnailUploadResponse>(response, c)
                ?? throw new CollectorApiException(ApiErrorKind.ServerError, "The server returned no image result.");
        }, ct);

    public Task<GoogleCreativeDetailsResponse> IngestGoogleCreativeDetailsAsync(GoogleCreativeDetailsRequest request, CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var response = await SendAsync(HttpMethod.Post, "api/collector/google/creative-details", request, auth: true, c);
            return await ReadBodyAsync<GoogleCreativeDetailsResponse>(response, c)
                ?? throw new CollectorApiException(ApiErrorKind.ServerError, "The server returned no creative detail result.");
        }, ct);

    // Server errors are not retried here: GoogleJobHandler retries the upload once, after 30 seconds, with the same ads.
    public Task<AdsIngestResponse> IngestGoogleAdsAsync(GoogleAdsIngestRequest request, CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var response = await SendAsync(HttpMethod.Post, "api/collector/ads/google", request, auth: true, c);
            return await ReadBodyAsync<AdsIngestResponse>(response, c)
                ?? throw new CollectorApiException(ApiErrorKind.ServerError, "The server returned no ingest result.");
        }, ct, retryServerErrors: false);

    public Task RequestAdsNowAsync(CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var _ = await SendAsync(HttpMethod.Post, "api/collector/ads-now", null, auth: true, c);
            return true;
        }, ct);

    public Task UnpairAsync(CancellationToken ct) =>
        retry.ExecuteAsync(async c =>
        {
            using var _ = await SendAsync(HttpMethod.Post, "api/collector/unpair", null, auth: true, c);
            return true;
        }, ct);

    private async Task<T> ReadRequiredAsync<T>(HttpMethod method, string path, object? body, bool auth, CancellationToken ct)
    {
        using var response = await SendAsync(method, path, body, auth, ct);
        return await ReadBodyAsync<T>(response, ct)
            ?? throw new CollectorApiException(ApiErrorKind.ServerError, "The server returned an empty response.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, bool auth, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (auth)
        {
            if (string.IsNullOrEmpty(BearerToken))
            {
                throw new CollectorApiException(ApiErrorKind.Unauthorized, "This PC is not paired. Pair it again.", HttpStatusCode.Unauthorized);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (HttpRequestException ex) when (IsNameResolutionFailure(ex))
        {
            throw new CollectorApiException(
                ApiErrorKind.ServerNotFound,
                "The server address could not be found. Check the server URL in the settings.",
                inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new CollectorApiException(ApiErrorKind.Offline, "Could not reach the server. Check the internet connection.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new CollectorApiException(ApiErrorKind.Timeout, "The server did not answer in time.", inner: ex);
        }

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
        {
            return response;
        }

        var retryAfter = response.Headers.RetryAfter?.Delta;
        var detail = await ReadDetailAsync(response, ct);
        var status = response.StatusCode;
        response.Dispose();
        throw Classify(status, detail, retryAfter);
    }

    private static bool IsNameResolutionFailure(HttpRequestException ex) =>
        ex.InnerException is SocketException socket &&
        socket.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData;

    private static CollectorApiException Classify(HttpStatusCode status, string detail, TimeSpan? retryAfter) => status switch
    {
        HttpStatusCode.Unauthorized => new CollectorApiException(
            ApiErrorKind.Unauthorized,
            "The server no longer accepts this PC's token. Pair the app again.",
            status),
        HttpStatusCode.Forbidden => new CollectorApiException(ApiErrorKind.Forbidden, $"The server refused this request: {detail}", status),
        HttpStatusCode.Conflict => new CollectorApiException(
            ApiErrorKind.Conflict,
            "This job is no longer claimed by this PC. It will be picked up again.",
            status),
        HttpStatusCode.NotFound => new CollectorApiException(ApiErrorKind.NotFound, $"Not found on the server: {detail}", status),
        HttpStatusCode.TooManyRequests => new CollectorApiException(
            ApiErrorKind.RateLimited,
            "Too many requests. The server or the source asked to slow down.",
            status,
            retryAfter),
        _ when (int)status >= 500 => new CollectorApiException(
            ApiErrorKind.ServerError,
            string.IsNullOrWhiteSpace(detail)
                ? $"The server reported an error ({(int)status}). Try again shortly."
                : $"The server reported an error ({(int)status}): {detail}",
            status),
        _ => new CollectorApiException(ApiErrorKind.BadRequest, $"The server rejected the request ({(int)status}): {detail}", status),
    };

    private static async Task<T?> ReadBodyAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (JsonException ex)
        {
            throw new CollectorApiException(ApiErrorKind.ServerError, "The server sent a response the app does not understand.", inner: ex);
        }
    }

    // The most useful text in a failed response: the validation messages, else the error, detail or title, with the
    // server's reference when it sends one, so the failure can be found in the server log.
    private static async Task<string> ReadDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (text.Length == 0)
            {
                return response.ReasonPhrase ?? string.Empty;
            }

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Trimmed(text);
            }

            var validation = ValidationMessages(root);
            if (validation.Count > 0)
            {
                return string.Join(" ", validation);
            }

            var message = FirstString(root, "error", "detail", "title", "message");
            if (message is null)
            {
                return Trimmed(text);
            }

            var reference = FirstString(root, "correlationId", "traceId");
            return reference is null ? message : $"{message} (reference {reference})";
        }
        catch (JsonException)
        {
            return response.ReasonPhrase ?? string.Empty;
        }
    }

    private static string Trimmed(string text) => text.Length > 200 ? text[..200] : text;

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!.Trim();
            }
        }

        return null;
    }

    private static List<string> ValidationMessages(JsonElement root)
    {
        var messages = new List<string>();
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            return messages;
        }

        foreach (var item in errors.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && FirstString(item, "errorMessage", "ErrorMessage") is { } text)
            {
                messages.Add(text);
            }
        }

        return messages;
    }

    // Shape of GET api/collector/identities.
    private sealed record IdentitiesBody(IReadOnlyList<MetaIdentityDto>? Meta, IReadOnlyList<GoogleIdentityDto>? Google);
}
