using LazyBean.Collectors.Google;
using LazyBean.Collectors.Http;
using LazyBean.Collectors.Woo;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collection.Adapters;

// GET /products.json?limit=250&page=N until a page comes back with an empty "products"
// array. Guards against pagination loops (same IDs returned repeatedly), excessive page counts,
// and per-competitor fetch timeouts.
public class ShopifyAdapter : ICompetitorAdapter
{
    private const int PageSize = 250;
    private const string Currency = "EGP";
    public const int DefaultMaxPages = 250;
    public const int DefaultTimeoutSeconds = 900;

    private readonly ICollectorHttpClient _http;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<ShopifyAdapter>? _logger;
    private readonly int? _explicitMaxPages;
    private readonly TimeSpan? _explicitTimeout;

    public ShopifyAdapter(
        ICollectorHttpClient http,
        IConfiguration? configuration = null,
        ILogger<ShopifyAdapter>? logger = null)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public ShopifyAdapter(
        ICollectorHttpClient http,
        int maxPages,
        TimeSpan? timeout = null,
        ILogger<ShopifyAdapter>? logger = null)
    {
        _http = http;
        _explicitMaxPages = maxPages;
        _explicitTimeout = timeout;
        _logger = logger;
    }

    public CompetitorPlatform Platform => CompetitorPlatform.Shopify;

    public async IAsyncEnumerable<RawProduct> FetchAsync(
        Competitor competitor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var baseUrl = competitor.BaseUrl.TrimEnd('/');
        var seenIds = new HashSet<long>();
        var maxPages = _explicitMaxPages
            ?? (int.TryParse(_configuration?["Collection:Shopify:MaxPages"] ?? _configuration?["Collection:MaxPages"], out var cap) ? cap : DefaultMaxPages);
        var timeout = _explicitTimeout
            ?? TimeSpan.FromSeconds(int.TryParse(_configuration?["Collection:Shopify:TimeoutSeconds"] ?? _configuration?["Collection:CompetitorTimeoutSeconds"], out var sec) ? sec : DefaultTimeoutSeconds);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var linkedToken = timeoutCts.Token;

        for (var page = 1; ; page++)
        {
            if (page > maxPages)
            {
                _logger?.LogWarning(
                    "Shopify adapter for {Competitor} reached page cap of {MaxPages}. Stopping and marking Degraded.",
                    competitor.Key, maxPages);
                CollectionRunContext.MarkDegraded($"Page cap of {maxPages} exceeded.");
                yield break;
            }

            if (linkedToken.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger?.LogWarning(
                    "Shopify adapter for {Competitor} timed out after {Timeout}. Stopping and marking Degraded.",
                    competitor.Key, timeout);
                CollectionRunContext.MarkDegraded($"Per-competitor timeout of {timeout} exceeded.");
                yield break;
            }

            var url = $"{baseUrl}/products.json?limit={PageSize}&page={page}";
            CollectedResponse response;
            try
            {
                response = await _http.GetAsync(competitor, url, linkedToken);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                _logger?.LogWarning(
                    "Shopify adapter for {Competitor} timed out after {Timeout}. Stopping and marking Degraded.",
                    competitor.Key, timeout);
                CollectionRunContext.MarkDegraded($"Per-competitor timeout of {timeout} exceeded.");
                yield break;
            }

            if (!response.Success)
            {
                var errorMsg = $"HTTP {response.StatusCode} on {url}: {response.SkippedReason ?? "Request failed"}";
                _logger?.LogWarning("Shopify adapter for {Competitor} failed: {Message}", competitor.Key, errorMsg);
                CollectionRunContext.MarkDegraded(errorMsg);
                yield break;
            }

            var parsed = JsonSerializer.Deserialize<ShopifyProductsResponse>(response.Body);
            if (parsed is null || parsed.Products.Count == 0)
            {
                yield break;
            }

            var hasNewIds = parsed.Products.Any(p => !seenIds.Contains(p.Id));
            if (!hasNewIds)
            {
                _logger?.LogWarning(
                    "Shopify adapter for {Competitor} received page {Page} with only already-seen product IDs. Pagination loop detected - stopping and marking Degraded.",
                    competitor.Key, page);
                CollectionRunContext.MarkDegraded(
                    $"Pagination loop detected: page {page} returned only already-seen product IDs.");
                yield break;
            }

            CollectionRunContext.IncrementPageCount();
            CollectionRunContext.SetStatusMessage($"Page {page} ({seenIds.Count} products collected)");

            foreach (var product in parsed.Products)
            {
                seenIds.Add(product.Id);
                yield return Map(product, baseUrl);
            }
        }
    }

    private static RawProduct Map(ShopifyProduct product, string baseUrl) => new(
        ExternalId: product.Id.ToString(CultureInfo.InvariantCulture),
        Title: product.Title,
        CanonicalUrl: $"{baseUrl}/products/{product.Handle}",
        Category: string.IsNullOrWhiteSpace(product.ProductType) ? null : product.ProductType,
        ProductType: product.ProductType,
        Vendor: product.Vendor,
        Tags: product.FormattedTags,
        Brand: product.Vendor,
        Handle: product.Handle,
        ImageUrl: product.Image?.Src,
        Variants: product.Variants.Select(v => new RawVariant(
            ExternalId: v.Id.ToString(CultureInfo.InvariantCulture),
            Title: v.Title,
            Price: decimal.Parse(v.Price, CultureInfo.InvariantCulture),
            CompareAtPrice: string.IsNullOrWhiteSpace(v.CompareAtPrice)
                ? null
                : decimal.Parse(v.CompareAtPrice, CultureInfo.InvariantCulture),
            Currency: Currency,
            InStock: v.Available,
            Sku: v.Sku)).ToList());
}
