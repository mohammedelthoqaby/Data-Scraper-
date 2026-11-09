using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using LazyBean.Infrastructure.External.LazyBeanApi;
using LazyBean.Application.Collector.Exceptions;

namespace LazyBean.Collector.App.Tests;

public class RetryPolicyTests
{
    private static readonly Func<TimeSpan, CancellationToken, Task> NoWait = (_, _) => Task.CompletedTask;

    [Fact]
    public async Task RetriesRateLimitTwice_ThenSucceeds()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var policy = new RetryPolicy(2, (d, _) => { delays.Add(d); return Task.CompletedTask; });

        var result = await policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new CollectorApiException(ApiErrorKind.RateLimited, "slow down", (int)HttpStatusCode.TooManyRequests);
            }

            return Task.FromResult(42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)], delays);
    }

    [Fact]
    public async Task GivesUpAfterTwoRetries()
    {
        var attempts = 0;
        var policy = new RetryPolicy(2, NoWait);

        await Assert.ThrowsAsync<CollectorApiException>(() => policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new CollectorApiException(ApiErrorKind.ServerError, "boom", (int)HttpStatusCode.InternalServerError);
        }, CancellationToken.None));

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ServerErrorsCanBeLeftOutOfRetries()
    {
        var attempts = 0;
        var policy = new RetryPolicy(2, NoWait);

        await Assert.ThrowsAsync<CollectorApiException>(() => policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new CollectorApiException(ApiErrorKind.ServerError, "boom", (int)HttpStatusCode.InternalServerError);
        }, CancellationToken.None, retryServerErrors: false));

        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(ApiErrorKind.ServerNotFound)]
    [InlineData(ApiErrorKind.Offline)]
    [InlineData(ApiErrorKind.Unauthorized)]
    [InlineData(ApiErrorKind.BadRequest)]
    [InlineData(ApiErrorKind.NotFound)]
    [InlineData(ApiErrorKind.Conflict)]
    public async Task DoesNotRetryDnsConnectionAnd4xx(ApiErrorKind kind)
    {
        var attempts = 0;
        var policy = new RetryPolicy(2, NoWait);

        await Assert.ThrowsAsync<CollectorApiException>(() => policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new CollectorApiException(kind, "no");
        }, CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetriesTimeouts()
    {
        var attempts = 0;
        var policy = new RetryPolicy(2, NoWait);

        var result = await policy.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new CollectorApiException(ApiErrorKind.Timeout, "slow");
            }

            return Task.FromResult("ok");
        }, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task HonorsRetryAfter_CappedAtOneMinute()
    {
        var delays = new List<TimeSpan>();
        var attempts = 0;
        var policy = new RetryPolicy(2, (d, _) => { delays.Add(d); return Task.CompletedTask; });

        await policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new CollectorApiException(ApiErrorKind.RateLimited, "slow", (int)HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(90));
            }

            return Task.FromResult(1);
        }, CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(60)], delays);
    }
}

public class CollectorApiClientTests
{
    private static readonly string JobJson =
        """{"id":"0d3c8c4e-6c1f-4d2a-9a52-1f1b2c3d4e5f","kind":"StoreCollection","runId":"11111111-2222-3333-4444-555555555555","attempt":1,"competitor":{"id":"66666666-7777-8888-9999-000000000000","slug":"strong-coffee","name":"Strong Coffee","baseUrl":"https://strongcoffee-eg.com","platform":"WooCommerce","minDelayMs":3000,"maxConcurrency":1}}""";

    private static readonly Func<TimeSpan, CancellationToken, Task> NoWait = (_, _) => Task.CompletedTask;

    [Fact]
    public async Task Claim_NoContent_MeansNoWork()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.NoContent));
        var api = Api(handler);
        api.BearerToken = "token";

        Assert.Null(await api.ClaimNextJobAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Claim_Ok_ReturnsTheJobAndSendsTheToken()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.OK, JobJson));
        var api = Api(handler);
        api.BearerToken = "device-token";

        var job = await api.ClaimNextJobAsync(CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal("StoreCollection", job!.Kind);
        Assert.Equal("strong-coffee", job.Competitor!.Slug);
        Assert.Equal(3000, job.Competitor.MinDelayMs);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
        Assert.Equal("device-token", handler.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Claim_Unauthorized_IsNotRetried_AndSaysPairAgain()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.Unauthorized, """{"error":"revoked"}"""));
        var api = Api(handler);
        api.BearerToken = "revoked";

        var error = await Assert.ThrowsAsync<CollectorApiException>(() => api.ClaimNextJobAsync(CancellationToken.None));

        Assert.Equal(ApiErrorKind.Unauthorized, error.Kind);
        Assert.Contains("Pair the app again", error.Message);
        Assert.Equal(1, handler.Requests.Count);
    }

    [Fact]
    public async Task Claim_ServiceUnavailable_IsRetried_ThenSucceeds()
    {
        var handler = new ScriptedHandler(
            _ => Respond(HttpStatusCode.ServiceUnavailable),
            _ => Respond(HttpStatusCode.OK, JobJson));
        var api = Api(handler);
        api.BearerToken = "token";

        var job = await api.ClaimNextJobAsync(CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Claim_TooManyRequests_CarriesRetryAfter()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        var handler = new ScriptedHandler(_ => Task.FromResult(response));
        var api = new CollectorApiClient(Http(handler), new RetryPolicy(0, NoWait));
        api.BearerToken = "token";

        var error = await Assert.ThrowsAsync<CollectorApiException>(() => api.ClaimNextJobAsync(CancellationToken.None));

        Assert.Equal(ApiErrorKind.RateLimited, error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Fact]
    public async Task UnknownHost_IsServerNotFound_AndNotRetried()
    {
        var handler = new ScriptedHandler(_ => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("name not resolved", new SocketException((int)SocketError.HostNotFound))));
        var api = Api(handler);
        api.BearerToken = "token";

        var error = await Assert.ThrowsAsync<CollectorApiException>(() => api.ClaimNextJobAsync(CancellationToken.None));

        Assert.Equal(ApiErrorKind.ServerNotFound, error.Kind);
        Assert.Contains("server URL", error.Message);
        Assert.Equal(1, handler.Requests.Count);
    }

    [Fact]
    public async Task ConnectionFailure_IsOffline()
    {
        var handler = new ScriptedHandler(_ => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused))));
        var api = Api(handler);
        api.BearerToken = "token";

        var error = await Assert.ThrowsAsync<CollectorApiException>(() => api.ClaimNextJobAsync(CancellationToken.None));

        Assert.Equal(ApiErrorKind.Offline, error.Kind);
    }

    [Fact]
    public async Task Pair_WrongPassword_SaysSo()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.Unauthorized, """{"error":"Invalid credentials"}"""));
        var api = Api(handler);

        var error = await Assert.ThrowsAsync<CollectorApiException>(() =>
            api.PairAsync("admin", "wrong", "Office PC", CancellationToken.None));

        Assert.Equal(ApiErrorKind.Unauthorized, error.Kind);
        Assert.Contains("username or password", error.Message);
        Assert.Equal(1, handler.Requests.Count);
    }

    [Fact]
    public async Task Pair_TooManyAttempts_SaysToWait()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.TooManyRequests));
        var api = Api(handler);

        var error = await Assert.ThrowsAsync<CollectorApiException>(() =>
            api.PairAsync("admin", "x", "Office PC", CancellationToken.None));

        Assert.Equal(ApiErrorKind.RateLimited, error.Kind);
        Assert.Contains("10 minutes", error.Message);
    }

    [Fact]
    public async Task GoogleIngest_ServerError_IsSentOnce_AndCarriesTheServerReference()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.InternalServerError,
            "{\"status\":500,\"title\":\"An unexpected error occurred.\",\"traceId\":\"0HN\",\"correlationId\":\"531c91fb\"}"));
        var api = Api(handler);
        api.BearerToken = "test-token";

        var error = await Assert.ThrowsAsync<CollectorApiException>(() =>
            api.IngestGoogleAdsAsync(
                new LazyBean.Application.Ads.Dtos.GoogleAdsIngestRequest("AR01064852753525768193", "Success", null, null, []),
                CancellationToken.None));

        // One request only: the job retries the upload itself, once, after 30 seconds.
        Assert.Single(handler.Requests);
        Assert.Equal(ApiErrorKind.ServerError, error.Kind);
        Assert.Contains("An unexpected error occurred.", error.Message);
        Assert.Contains("(reference 531c91fb)", error.Message);
    }

    [Fact]
    public async Task GoogleIngest_ValidationFailure_ShowsTheServerMessage()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.BadRequest,
            "{\"status\":400,\"title\":\"Validation failed\",\"errors\":[{\"PropertyName\":\"ExternalId\",\"ErrorMessage\":\"Enter the Google advertiser ID.\"}]}"));
        var api = Api(handler);
        api.BearerToken = "test-token";

        var error = await Assert.ThrowsAsync<CollectorApiException>(() =>
            api.IngestGoogleAdsAsync(
                new LazyBean.Application.Ads.Dtos.GoogleAdsIngestRequest("cafelax.com", "Success", null, null, []),
                CancellationToken.None));

        Assert.Equal(ApiErrorKind.BadRequest, error.Kind);
        Assert.Contains("Enter the Google advertiser ID.", error.Message);
    }

    private static CollectorApiClient Api(HttpMessageHandler handler) => new(Http(handler), new RetryPolicy(2, NoWait));

    private static HttpClient Http(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://lazy-bean.test/"),
    };

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string? json = null)
    {
        var response = new HttpResponseMessage(status);
        if (json is not null)
        {
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return Task.FromResult(response);
    }

    // Answers each request with the next scripted response, and remembers the requests.
    private sealed class ScriptedHandler(params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] script) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _script = new(script);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var responder = _script.Count > 0 ? _script.Dequeue() : throw new InvalidOperationException("No scripted response left.");
            return responder(request);
        }
    }
}
