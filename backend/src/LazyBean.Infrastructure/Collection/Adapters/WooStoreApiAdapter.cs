using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collection.Adapters;

// GET /wp-json/wc/store/v1/products?per_page=100&page=N, paginating until X-WP-TotalPages.
// For type=variable, variations are fetched in bulk upfront via:
// GET /wp-json/wc/store/v1/products?type=variation&per_page=100&page=N
// and mapped in-memory, falling back to per-variation requests if needed.
public class WooStoreApiAdapter : ICompetitorAdapter
{
    private const int PageSize = 100;
    public const int DefaultTimeoutSeconds = 900;

    private readonly ICollectorHttpClient _http;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<WooStoreApiAdapter>? _logger;
    private readonly TimeSpan? _explicitTimeout;

    public WooStoreApiAdapter(ICollectorHttpClient http)
        : this(http, (IConfiguration?)null, null)
    {
    }

    public WooStoreApiAdapter(
        ICollectorHttpClient http,
        IConfiguration? configuration,
        ILogger<WooStoreApiAdapter>? logger = null)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public WooStoreApiAdapter(
        ICollectorHttpClient http,
        TimeSpan timeout,
        ILogger<WooStoreApiAdapter>? logger = null)
    {
        _http = http;
        _explicitTimeout = timeout;
        _logger = logger;
    }

    public CompetitorPlatform Platform => CompetitorPlatform.WooCommerce;

    public async IAsyncEnumerable<RawProduct> FetchAsync(
        Competitor competitor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var timeout = _explicitTimeout
            ?? TimeSpan.FromSeconds(int.TryParse(_configuration?["Collection:WooCommerce:TimeoutSeconds"] ?? _configuration?["Collection:CompetitorTimeoutSeconds"], out var sec) ? sec : DefaultTimeoutSeconds);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout <= TimeSpan.Zero)
        {
            timeoutCts.Cancel();
        }
        else
        {
            timeoutCts.CancelAfter(timeout);
        }
        var linkedToken = timeoutCts.Token;

        var baseUrl = competitor.BaseUrl.TrimEnd('/');

        // Fetch all variations in bulk upfront to avoid N individual variation HTTP requests.
        Dictionary<long, WooVariationDetail> variationsById;
        try
        {
            variationsById = await FetchAllVariationsAsync(competitor, baseUrl, linkedToken);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            _logger?.LogWarning(
                "WooCommerce adapter for {Competitor} timed out after {Timeout}. Stopping and marking Degraded.",
                competitor.Key, timeout);
            CollectionRunContext.MarkDegraded($"Per-competitor timeout of {timeout} exceeded.");
            yield break;
        }

        var totalPages = 1;

        for (var page = 1; page <= totalPages; page++)
        {
            if (linkedToken.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger?.LogWarning(
                    "WooCommerce adapter for {Competitor} timed out after {Timeout}. Stopping and marking Degraded.",
                    competitor.Key, timeout);
                CollectionRunContext.MarkDegraded($"Per-competitor timeout of {timeout} exceeded.");
                yield break;
            }

            var url = $"{baseUrl}/wp-json/wc/store/v1/products?per_page={PageSize}&page={page}";
            CollectedResponse response;
            try
            {
                response = await _http.GetAsync(competitor, url, linkedToken);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                _logger?.LogWarning(
                    "WooCommerce adapter for {Competitor} timed out after {Timeout}. Stopping and marking Degraded.",
                    competitor.Key, timeout);
                CollectionRunContext.MarkDegraded($"Per-competitor timeout of {timeout} exceeded.");
                yield break;
            }

            if (!response.Success)
            {
                yield break;
            }

            if (page == 1 && int.TryParse(response.GetHeader("X-WP-TotalPages"), out var pages))
            {
                totalPages = Math.Max(pages, 1);
                CollectionRunContext.SetTotalPages(totalPages);
            }

            CollectionRunContext.SetStatusMessage($"Page {page} of {totalPages}");
            var products = JsonSerializer.Deserialize<List<WooProduct>>(response.Body) ?? [];
            CollectionRunContext.IncrementPageCount();

            foreach (var product in products)
            {
                if (product.Type == "variable" && product.Variations.Count > 0)
                {
                    yield return await MapVariableAsync(product, competitor, baseUrl, variationsById, linkedToken);
                }
                else
                {
                    yield return MapSimple(product);
                }
            }
        }
    }

    private async Task<Dictionary<long, WooVariationDetail>> FetchAllVariationsAsync(
        Competitor competitor, string baseUrl, CancellationToken ct)
    {
        var variations = new Dictionary<long, WooVariationDetail>();
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var url = $"{baseUrl}/wp-json/wc/store/v1/products?type=variation&per_page={PageSize}&page={page}";
            var response = await _http.GetAsync(competitor, url, ct);
            if (!response.Success)
            {
                // Bulk endpoint not supported or failed - break and let per-variation fallback handle it
                break;
            }

            var items = JsonSerializer.Deserialize<List<WooVariationDetail>>(response.Body);
            if (items is null || items.Count == 0)
            {
                break;
            }

            if (int.TryParse(response.GetHeader("X-WP-TotalPages"), out var pages))
            {
                totalPages = Math.Max(pages, 1);
            }
            else if (items.Count == PageSize)
            {
                totalPages = Math.Max(totalPages, page + 1);
            }

            foreach (var item in items)
            {
                variations[item.Id] = item;
            }

            page++;
        }

        return variations;
    }

    private static RawProduct MapSimple(WooProduct product) => new(
        ExternalId: product.Id.ToString(CultureInfo.InvariantCulture),
        Title: product.Name,
        CanonicalUrl: product.Permalink,
        Category: product.Categories.Count > 0 ? product.Categories[0].Name : null,
        Variants: [ToRawVariant(product.Id.ToString(CultureInfo.InvariantCulture), null, product.Sku, product.IsInStock, product.Prices)],
        ProductType: product.Type,
        Vendor: null,
        Tags: product.Categories.Count > 0 ? string.Join(", ", product.Categories.Select(c => c.Name)) : null,
        Handle: product.Slug ?? CreativeHashHelper.ExtractProductHandle(product.Permalink) ?? product.Permalink);

    private async Task<RawProduct> MapVariableAsync(
        WooProduct product,
        Competitor competitor,
        string baseUrl,
        IReadOnlyDictionary<long, WooVariationDetail> variationsById,
        CancellationToken ct)
    {
        var variants = new List<RawVariant>();

        foreach (var variationRef in product.Variations)
        {
            if (!variationsById.TryGetValue(variationRef.Id, out var variation))
            {
                // Fallback to individual request if not present in bulk cache
                var url = $"{baseUrl}/wp-json/wc/store/v1/products/{variationRef.Id}";
                var response = await _http.GetAsync(competitor, url, ct);
                if (!response.Success)
                {
                    continue;
                }

                variation = JsonSerializer.Deserialize<WooVariationDetail>(response.Body);
                if (variation is null)
                {
                    continue;
                }
            }

            // The listing's own variations[] entry carries the attribute value (e.g.
            // "250g") - the detail endpoint's own "attributes" is empty on the real site,
            // so it's only a fallback via its formatted "variation" string (e.g. "color: black").
            var attributeTitle = variationRef.Attributes.Count > 0
                ? string.Join(", ", variationRef.Attributes.Select(a => a.Value))
                : variation.Variation;

            variants.Add(ToRawVariant(
                variation.Id.ToString(CultureInfo.InvariantCulture),
                attributeTitle,
                variation.Sku,
                variation.IsInStock,
                variation.Prices));
        }

        return new RawProduct(
            ExternalId: product.Id.ToString(CultureInfo.InvariantCulture),
            Title: product.Name,
            CanonicalUrl: product.Permalink,
            Category: product.Categories.Count > 0 ? product.Categories[0].Name : null,
            Variants: variants,
            ProductType: product.Type,
            Vendor: null,
            Tags: product.Categories.Count > 0 ? string.Join(", ", product.Categories.Select(c => c.Name)) : null,
            Handle: product.Slug ?? CreativeHashHelper.ExtractProductHandle(product.Permalink) ?? product.Permalink);
    }

    private static RawVariant ToRawVariant(
        string externalId, string? title, string? sku, bool inStock, WooPrices prices) => new(
        ExternalId: externalId,
        Title: title,
        Price: FromMinorUnits(prices.Price, prices.CurrencyMinorUnit),
        CompareAtPrice: prices.RegularPrice != prices.Price
            ? FromMinorUnits(prices.RegularPrice, prices.CurrencyMinorUnit)
            : null,
        Currency: prices.CurrencyCode,
        InStock: inStock,
        Sku: sku);

    private static decimal FromMinorUnits(string minorUnitsValue, int minorUnitDigits)
    {
        var minorUnits = decimal.Parse(minorUnitsValue, CultureInfo.InvariantCulture);
        return minorUnits / (decimal)Math.Pow(10, minorUnitDigits);
    }
}
