using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Exceptions;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Jobs;
using LazyBean.Collector.App.Runtime;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Collectors.Meta;

namespace LazyBean.Collector.App.Tests;

// Records every call and answers from scripted queues. Claims are scripted; when the queue is empty the
// app is idle (null), which is how the worker loop is stopped in tests.
public sealed class FakeCollectorApi : ICollectorApi
{
    public Queue<Func<CancellationToken, Task<CollectorJobDto?>>> Claims { get; } = new();

    public CollectorIdentities Identities { get; set; } = new([], []);

    public List<string> Calls { get; } = [];

    public List<(Guid RunId, string Slug, IngestProductsBatchRequest Request)> ProductBatches { get; } = [];

    public List<(string Slug, CompleteCompetitorRunRequest Request)> CompetitorCompletions { get; } = [];

    public List<(Guid JobId, CompleteCollectorJobRequest Request)> JobCompletions { get; } = [];

    public List<MetaAdsIngestRequest> MetaIngests { get; } = [];

    public List<GoogleAdsIngestRequest> GoogleIngests { get; } = [];

    public Func<MetaAdsIngestRequest, Exception?>? MetaIngestFailure { get; set; }

    public Func<CompleteCollectorJobRequest, Exception?>? JobCompleteFailure { get; set; }

    public int Heartbeats { get; private set; }

    public string? BearerToken { get; set; }

    public List<Guid> Releases { get; } = [];

    public Task<PairCollectorDeviceResultDto> PairAsync(string username, string password, string deviceName, CancellationToken ct) =>
        Task.FromResult(new PairCollectorDeviceResultDto(Guid.NewGuid(), deviceName, "token", ["ads:ingest", "store:ingest"]));

    public Task<CollectorJobDto?> ClaimNextJobAsync(CancellationToken ct)
    {
        Calls.Add("claim");
        return Claims.Count == 0 ? Task.FromResult<CollectorJobDto?>(null) : Claims.Dequeue()(ct);
    }

    public List<CollectorHeartbeatRequest?> HeartbeatProgress { get; } = [];

    public Task HeartbeatAsync(Guid jobId, CollectorHeartbeatRequest? progress, CancellationToken ct)
    {
        Heartbeats++;
        HeartbeatProgress.Add(progress);
        return Task.CompletedTask;
    }

    public Task ReleaseJobAsync(Guid jobId, CancellationToken ct)
    {
        Releases.Add(jobId);
        return Task.CompletedTask;
    }

    public Task CompleteJobAsync(Guid jobId, CompleteCollectorJobRequest request, CancellationToken ct)
    {
        Calls.Add($"complete-job:{request.Status}");
        if (JobCompleteFailure?.Invoke(request) is { } failure)
        {
            throw failure;
        }

        JobCompletions.Add((jobId, request));
        return Task.CompletedTask;
    }

    public Task<CollectorIdentities> GetIdentitiesAsync(CancellationToken ct) => Task.FromResult(Identities);

    public Task IngestProductsAsync(Guid runId, string slug, IngestProductsBatchRequest request, CancellationToken ct)
    {
        Calls.Add($"products:{request.Page}");
        ProductBatches.Add((runId, slug, request));
        return Task.CompletedTask;
    }

    public Task CompleteCompetitorAsync(Guid runId, string slug, CompleteCompetitorRunRequest request, CancellationToken ct)
    {
        Calls.Add($"competitor:{request.Status}");
        CompetitorCompletions.Add((slug, request));
        return Task.CompletedTask;
    }

    public Task<AdsIngestResponse> IngestMetaAdsAsync(MetaAdsIngestRequest request, CancellationToken ct)
    {
        Calls.Add($"meta:{request.PageId}:{request.Status}");
        if (MetaIngestFailure?.Invoke(request) is { } failure)
        {
            throw failure;
        }

        MetaIngests.Add(request);
        return Task.FromResult(new AdsIngestResponse("Success", IngestedCount: request.Ads?.Count ?? 0));
    }

    public Task<AdsIngestResponse> IngestGoogleAdsAsync(GoogleAdsIngestRequest request, CancellationToken ct)
    {
        Calls.Add($"google:{request.AdvertiserId}:{request.Status}");
        GoogleIngests.Add(request);
        return Task.FromResult(new AdsIngestResponse(request.Status ?? "Success"));
    }

    public Task RequestAdsNowAsync(CancellationToken ct) => Task.CompletedTask;

    public Task UnpairAsync(CancellationToken ct) => Task.CompletedTask;
}

public sealed class FakeAdapter(IEnumerable<RawProduct> products, Action? beforeFinish = null) : ICompetitorAdapter
{
    public CompetitorPlatform Platform => CompetitorPlatform.WooCommerce;

    public async IAsyncEnumerable<RawProduct> FetchAsync(Competitor competitor, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var product in products)
        {
            ct.ThrowIfCancellationRequested();
            yield return product;
            await Task.Yield();
        }

        beforeFinish?.Invoke();
    }
}

public sealed class FakeMetaCollector : IMetaPageCollector
{
    public Dictionary<string, MetaPageResult> Results { get; } = new();

    public List<string> PagesRequested { get; } = [];

    public Task<MetaPageResult> CollectPageAsync(string pageId, string country, string status, int maxResults, CancellationToken ct)
    {
        PagesRequested.Add(pageId);
        return Task.FromResult(Results[pageId]);
    }
}

public sealed class FakeGoogleClient : IGoogleAdsTransparencyClient
{
    public Dictionary<string, Queue<GoogleCreativesPageResult>> Pages { get; } = new();

    public Func<string, Exception?>? Failure { get; set; }

    public List<string> Requested { get; } = [];

    public Task<string?> SearchAdvertiserIdAsync(string query, CancellationToken ct = default) => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<GoogleAdvertiserCandidateDto>> SearchCandidatesAsync(string query, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GoogleAdvertiserCandidateDto>>([]);

    public Task<GoogleCreativesPageResult> SearchCreativesAsync(string advertiserId, string? pageToken = null, CancellationToken ct = default)
    {
        Requested.Add(advertiserId);
        if (Failure?.Invoke(advertiserId) is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(Pages[advertiserId].Dequeue());
    }
}

public sealed class DelayRecorder
{
    public List<TimeSpan> Delays { get; } = [];

    public Func<TimeSpan, CancellationToken, Task> Delay => (duration, _) =>
    {
        Delays.Add(duration);
        return Task.CompletedTask;
    };
}

public static class Builders
{
    public static CollectorJobDto StoreJob(Guid runId, string slug = "strong-coffee") => new(
        Guid.NewGuid(),
        "StoreCollection",
        runId,
        1,
        new CollectorJobCompetitorDto(Guid.NewGuid(), slug, "Strong Coffee", "https://strongcoffee-eg.com", "WooCommerce", 3000, 1));

    public static CollectorJobDto AdsJob(string kind) => new(Guid.NewGuid(), kind, null, 1, null);

    public static RawProduct Product(int index) => new(
        ExternalId: index.ToString(),
        Title: $"منتج {index}",
        CanonicalUrl: $"https://strongcoffee-eg.com/product/{index}",
        Category: "Coffee",
        Variants: [new RawVariant(index.ToString(), "250g", 275m, null, "EGP", true, null)]);

    public static MetaIdentityDto MetaIdentity(string pageId, string name, DateTimeOffset? blockedUntil = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "strong-coffee", name, pageId, name, "Approved", blockedUntil);

    public static MetaPageResult MetaResult(string pageId, string status, params MetaAdDto[] ads) => new(
        pageId,
        "EG",
        status,
        ads,
        status == "Success" ? null : "rate limit",
        status == "Success" ? null : "RateLimitError");

    public static MetaAdDto Ad(string id) => new(
        id,
        "2026-09-09T07:00:00+00:00",
        null,
        "Active",
        "عرض خاص",
        "Title",
        "https://strongcoffee-eg.com/offer",
        $"https://www.facebook.com/ads/library/?id={id}",
        "https://img.example/a.jpg",
        ["FACEBOOK"]);

    public static GoogleIdentityDto GoogleIdentity(string externalId, string name, DateTimeOffset? blockedUntil = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "strong-coffee", name, externalId, name, "Approved", blockedUntil);

    public static GoogleCreativeDto Creative(string id) => new(
        id,
        "AR01064852753525768193",
        "Strong Coffee",
        1,
        "Text",
        "Offer",
        "Body",
        "https://strongcoffee-eg.com/offer",
        null,
        null,
        DateTimeOffset.UtcNow.AddDays(-2),
        DateTimeOffset.UtcNow,
        "{}");
}
