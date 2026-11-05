using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collection.Adapters;

// Adapter for Lazy Bean's own catalog API (https://api.lazzybean.com).
// Reference catalog (IsReference = true).
// 1. Fetches categories from GET /api/categories?lang=en-US
// 2. Fetches products per category from GET /api/products?lang=en-US&category={uuid}&limit=100&page={N}&sort=date_asc
// 3. Paginates to lastPage per category, dedupes products by id.
// 4. Extracts vendor, price/beforeDiscount, stock, package weight.
public class LazzyBeanAdapter : ICompetitorAdapter
{
    private const int PageSize = 100;
    public const int DefaultTimeoutSeconds = 900;
    public const int DefaultMaxPages = 200;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly ICollectorHttpClient _http;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<LazzyBeanAdapter>? _logger;
    private readonly TimeSpan? _explicitTimeout;
    private readonly int? _explicitMaxPages;

    public LazzyBeanAdapter(ICollectorHttpClient http)
        : this(http, (IConfiguration?)null, null)
    {
    }

    public LazzyBeanAdapter(
        ICollectorHttpClient http,
        IConfiguration? configuration,
        ILogger<LazzyBeanAdapter>? logger = null)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public LazzyBeanAdapter(
        ICollectorHttpClient http,
        TimeSpan timeout,
        int? maxPages = null,
        ILogger<LazzyBeanAdapter>? logger = null)
    {
        _http = http;
        _explicitTimeout = timeout;
        _explicitMaxPages = maxPages;
        _logger = logger;
    }

    public CompetitorPlatform Platform => CompetitorPlatform.LazzyBeanApi;

    public async IAsyncEnumerable<RawProduct> FetchAsync(
        Competitor competitor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var timeout = _explicitTimeout
            ?? TimeSpan.FromSeconds(int.TryParse(_configuration?["Collection:LazzyBean:TimeoutSeconds"] ?? _configuration?["Collection:CompetitorTimeoutSeconds"], out var sec) ? sec : DefaultTimeoutSeconds);
        var maxPages = _explicitMaxPages
            ?? (int.TryParse(_configuration?["Collection:LazzyBean:MaxPages"] ?? _configuration?["Collection:MaxPages"], out var cap) ? cap : DefaultMaxPages);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var linkedToken = timeoutCts.Token;

        var apiBase = competitor.BaseUrl.TrimEnd('/');
        if (!apiBase.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            apiBase += "/api";
        }

        var categoriesUrl = $"{apiBase}/categories?lang=en-US";
        CollectedResponse catResponse;
        try
        {
            catResponse = await _http.GetAsync(competitor, categoriesUrl, linkedToken);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            _logger?.LogWarning("LazzyBean adapter for {Competitor} timed out after {Timeout}. Stopping and marking Degraded.", competitor.Key, timeout);
            CollectionRunContext.MarkDegraded($"Per-competitor timeout of {timeout} exceeded.");
            yield break;
        }

        if (!catResponse.Success)
        {
            _logger?.LogError("Failed to fetch categories from {Url} (status {Status})", categoriesUrl, catResponse.StatusCode);
            yield break;
        }

        var categoriesData = JsonSerializer.Deserialize<LazzyBeanCategoriesResponse>(catResponse.Body, JsonOpts);
        var categories = categoriesData?.Data ?? [];
        if (categories.Count == 0)
        {
            _logger?.LogWarning("No categories returned from {Url}", categoriesUrl);
            yield break;
        }

        var seenProductIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalPagesRecorded = 0;

        // Fetch brands dictionary from product filters probe
        var brandMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var probeUrl = $"{apiBase}/products?lang=en-US&limit=1";
            var probeResp = await _http.GetAsync(competitor, probeUrl, linkedToken);
            if (probeResp.Success)
            {
                using var probeDoc = JsonDocument.Parse(probeResp.Body);
                if (probeDoc.RootElement.TryGetProperty("data", out var dEl) &&
                    dEl.TryGetProperty("filters", out var filtersEl) &&
                    filtersEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var filter in filtersEl.EnumerateArray())
                    {
                        var isBrand = (filter.TryGetProperty("key", out var k) && k.GetString() == "brand") ||
                                      (filter.TryGetProperty("name", out var n) && n.GetString() == "Brands");
                        if (isBrand && filter.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var opt in opts.EnumerateArray())
                            {
                                var id = opt.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                                var name = opt.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                                {
                                    brandMap[id] = name;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load brand map from filters probe for {Competitor}", competitor.Key);
        }

        var categoryLastPages = new Dictionary<string, int>();

        foreach (var cat in categories)
        {
            var catId = cat.Id;
            var catName = cat.Category ?? "Other";

            for (var page = 1; ; page++)
            {
                if (totalPagesRecorded >= maxPages)
                {
                    _logger?.LogWarning("LazzyBean adapter for {Competitor} reached page cap of {MaxPages}. Stopping and marking Degraded.", competitor.Key, maxPages);
                    CollectionRunContext.MarkDegraded($"Page cap of {maxPages} exceeded.");
                    yield break;
                }

                if (linkedToken.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    _logger?.LogWarning("LazzyBean adapter for {Competitor} timed out after {Timeout}. Stopping and marking Degraded.", competitor.Key, timeout);
                    CollectionRunContext.MarkDegraded($"Per-competitor timeout of {timeout} exceeded.");
                    yield break;
                }

                var productsUrl = $"{apiBase}/products?lang=en-US&category={catId}&limit={PageSize}&page={page}&sort=date_asc";
                CollectedResponse prodResponse;
                try
                {
                    prodResponse = await _http.GetAsync(competitor, productsUrl, linkedToken);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
                {
                    _logger?.LogWarning("LazzyBean adapter for {Competitor} timed out after {Timeout}. Stopping and marking Degraded.", competitor.Key, timeout);
                    CollectionRunContext.MarkDegraded($"Per-competitor timeout of {timeout} exceeded.");
                    yield break;
                }

                if (!prodResponse.Success)
                {
                    _logger?.LogWarning("Failed to fetch page {Page} for category {Category} from {Url}", page, catName, productsUrl);
                    break;
                }

                CollectionRunContext.IncrementPageCount();
                totalPagesRecorded++;

                LazzyBeanProductsResponse? prodData = null;
                try
                {
                    using var doc = JsonDocument.Parse(prodResponse.Body);
                    if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
                    {
                        // Array or non-object data means no products in this category/page
                        break;
                    }
                    prodData = JsonSerializer.Deserialize<LazzyBeanProductsResponse>(prodResponse.Body, JsonOpts);
                    if (prodData?.Data?.Pagination is { } pag)
                    {
                        categoryLastPages[catId] = pag.LastPage;
                        var knownPages = categoryLastPages.Values.Sum();
                        var remainingCategories = Math.Max(0, categories.Count - categoryLastPages.Count);
                        var totalEstimated = knownPages + remainingCategories;
                        CollectionRunContext.SetTotalPages(totalEstimated);
                        CollectionRunContext.SetStatusMessage($"{catName}: page {page} of {pag.LastPage} ({seenProductIds.Count} items)");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to parse products response for category {Category} page {Page}", catName, page);
                    break;
                }

                var items = prodData?.Data?.Data ?? [];
                if (items.Count == 0)
                {
                    break;
                }

                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Id))
                        continue;

                    if (!seenProductIds.Add(item.Id))
                        continue;

                    var storefrontBase = (_configuration?["Reference:StorefrontBaseUrl"] ?? "https://lazybean.co").TrimEnd('/');
                    var canonicalUrl = !string.IsNullOrWhiteSpace(item.Id)
                        ? $"{storefrontBase}/single-product/{item.Id}"
                        : string.Empty;
                    var vendorName = !string.IsNullOrWhiteSpace(item.Vendor?.Name)
                        ? item.Vendor.Name
                        : item.Vendor?.CompanyName;
                    var vendorId = item.VendorId ?? item.Vendor?.Id;

                    var price = item.Price;
                    decimal? compareAtPrice = null;
                    if (item.BeforeDiscount.HasValue && item.BeforeDiscount.Value > item.Price)
                    {
                        compareAtPrice = item.BeforeDiscount.Value;
                    }

                    var inStock = item.Stock > 0;
                    var weightGrams = item.Weight.HasValue && item.Weight.Value > 0
                        ? (int?)Math.Round(item.Weight.Value)
                        : null;

                    string? brand = null;
                    if (!string.IsNullOrWhiteSpace(item.BrandId) && brandMap.TryGetValue(item.BrandId, out var bName))
                    {
                        brand = bName;
                    }

                    var variant = new RawVariant(
                        ExternalId: item.Id,
                        Title: item.Name,
                        Price: price,
                        CompareAtPrice: compareAtPrice,
                        Currency: "EGP",
                        InStock: inStock,
                        Sku: null);

                    var rawProduct = new RawProduct(
                        ExternalId: item.Id,
                        Title: item.Name,
                        CanonicalUrl: canonicalUrl,
                        Category: item.Category?.Name ?? catName,
                        Variants: [variant],
                        ProductType: item.SubCategory?.Name,
                        Vendor: vendorName,
                        Tags: item.GrindSizeName,
                        VendorId: vendorId,
                        WeightGrams: weightGrams,
                        Roast: item.RoastName,
                        Brand: brand,
                        Handle: item.Id);

                    yield return rawProduct;
                }

                var pagination = prodData?.Data?.Pagination;
                if (pagination is null || page >= pagination.LastPage)
                {
                    break;
                }
            }
        }
    }

    private sealed record LazzyBeanCategoriesResponse(
        [property: JsonPropertyName("data")] List<LazzyBeanCategoryItem>? Data);

    private sealed record LazzyBeanCategoryItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("description")] string? Description);

    private sealed record LazzyBeanProductsResponse(
        [property: JsonPropertyName("data")] LazzyBeanProductsData? Data);

    private sealed record LazzyBeanProductsData(
        [property: JsonPropertyName("data")] List<LazzyBeanProductItem>? Data,
        [property: JsonPropertyName("pagination")] LazzyBeanPagination? Pagination);

    private sealed record LazzyBeanPagination(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("currentPage")] int CurrentPage,
        [property: JsonPropertyName("perPage")] int PerPage,
        [property: JsonPropertyName("lastPage")] int LastPage);

    private sealed record LazzyBeanProductItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("price")] decimal Price,
        [property: JsonPropertyName("beforeDiscount")] decimal? BeforeDiscount,
        [property: JsonPropertyName("stock")] int Stock,
        [property: JsonPropertyName("weight")] double? Weight,
        [property: JsonPropertyName("roastName")] string? RoastName,
        [property: JsonPropertyName("flavorName")] string? FlavorName,
        [property: JsonPropertyName("grindSizeName")] string? GrindSizeName,
        [property: JsonPropertyName("brand_id")] string? BrandId,
        [property: JsonPropertyName("vendor_id")] string? VendorId,
        [property: JsonPropertyName("vendor")] LazzyBeanVendor? Vendor,
        [property: JsonPropertyName("category")] LazzyBeanNamedRef? Category,
        [property: JsonPropertyName("sub_category")] LazzyBeanNamedRef? SubCategory);

    private sealed record LazzyBeanVendor(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("company_name")] string? CompanyName);

    private sealed record LazzyBeanNamedRef(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name);
}
