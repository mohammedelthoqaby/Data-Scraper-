using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Collection.Adapters;

namespace LazyBean.CollectorCli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("LazyBean External Competitor Collector CLI");
        Console.WriteLine("==================================================");

        var runIdStr = GetArg(args, "--run-id");
        var slug = GetArg(args, "--slug") ?? "strong-coffee";
        var apiUrl = GetArg(args, "--api-url");
        var token = GetArg(args, "--token");
        var baseUrl = GetArg(args, "--base-url") ?? "https://strongcoffeeegypt.com";

        if (string.IsNullOrWhiteSpace(runIdStr) || !Guid.TryParse(runIdStr, out var runId))
        {
            Console.Error.WriteLine("Error: --run-id <Guid> is required.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            Console.Error.WriteLine("Error: --api-url <url> is required.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("Error: --token <token> is required.");
            return 1;
        }

        apiUrl = apiUrl.TrimEnd('/');
        Console.WriteLine($"Target Run: {runId}");
        Console.WriteLine($"Competitor Slug: {slug}");
        Console.WriteLine($"Base URL: {baseUrl}");
        Console.WriteLine($"API Endpoint: {apiUrl}");

        var competitor = new Competitor
        {
            Id = Guid.NewGuid(),
            Key = slug,
            Name = slug,
            BaseUrl = baseUrl,
            Platform = CompetitorPlatform.WooCommerce,
            MinDelayMs = 1500,
            Enabled = true
        };

        using var backendHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        backendHttp.DefaultRequestHeaders.Add("X-Internal-Token", token);

        using var scrapeHttp = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        scrapeHttp.DefaultRequestHeaders.UserAgent.ParseAdd("LazyBeanBot/1.0 (+https://lazy-bean.runasp.net)");
        scrapeHttp.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var collectorHttp = new CliCollectorHttpClient(scrapeHttp);
        var adapter = new WooStoreApiAdapter(collectorHttp);

        var totalSeen = 0;
        var batchNumber = 0;
        var batch = new List<RawProduct>(50);
        using var cts = new CancellationTokenSource();

        try
        {
            Console.WriteLine("Starting WooCommerce fetch iteration...");

            await foreach (var raw in adapter.FetchAsync(competitor, cts.Token))
            {
                totalSeen++;
                batch.Add(raw);

                if (batch.Count >= 50)
                {
                    batchNumber++;
                    await SendBatchAsync(backendHttp, apiUrl, runId, slug, batchNumber, batch, cts.Token);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                batchNumber++;
                await SendBatchAsync(backendHttp, apiUrl, runId, slug, batchNumber, batch, cts.Token);
                batch.Clear();
            }

            var totalPages = CollectionRunContext.TotalPages ?? Math.Max(1, batchNumber);
            Console.WriteLine($"Scraping complete. Total products collected: {totalSeen}. Reporting complete to backend...");

            var completePayload = new CompleteCompetitorRunRequest(
                Status: "Completed",
                TotalPages: totalPages,
                TotalProducts: totalSeen,
                Error: null);

            var completeResp = await backendHttp.PostAsJsonAsync(
                $"{apiUrl}/api/internal/runs/{runId}/competitors/{slug}/complete",
                completePayload,
                cts.Token);

            if (!completeResp.IsSuccessStatusCode)
            {
                var err = await completeResp.Content.ReadAsStringAsync(cts.Token);
                Console.Error.WriteLine($"Warning: Backend complete reporting returned HTTP {completeResp.StatusCode}: {err}");
            }
            else
            {
                Console.WriteLine("Successfully marked competitor collection completed in backend.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal collection error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);

            try
            {
                var failPayload = new CompleteCompetitorRunRequest(
                    Status: "Failed",
                    TotalPages: Math.Max(1, batchNumber),
                    TotalProducts: totalSeen,
                    Error: ex.Message);

                await backendHttp.PostAsJsonAsync(
                    $"{apiUrl}/api/internal/runs/{runId}/competitors/{slug}/complete",
                    failPayload,
                    CancellationToken.None);
            }
            catch (Exception reportEx)
            {
                Console.Error.WriteLine($"Failed to report failure status to backend: {reportEx.Message}");
            }

            return 1;
        }
    }

    private static async Task SendBatchAsync(
        HttpClient backendHttp,
        string apiUrl,
        Guid runId,
        string slug,
        int pageNumber,
        List<RawProduct> batch,
        CancellationToken ct)
    {
        Console.WriteLine($"Posting batch {pageNumber} ({batch.Count} products) to {apiUrl}...");

        var payload = new IngestProductsBatchRequest(
            Page: pageNumber,
            TotalPages: CollectionRunContext.TotalPages,
            Products: batch.ToList());

        var response = await backendHttp.PostAsJsonAsync(
            $"{apiUrl}/api/internal/runs/{runId}/competitors/{slug}/products",
            payload,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Backend rejected batch {pageNumber} with HTTP {response.StatusCode}: {err}");
        }

        Console.WriteLine($"Batch {pageNumber} accepted successfully.");
    }

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(name.Length + 1)..];
            }
        }
        return null;
    }
}

public class CliCollectorHttpClient(HttpClient httpClient) : ICollectorHttpClient
{
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private readonly TimeSpan _minDelay = TimeSpan.FromMilliseconds(1500);

    public async Task<CollectedResponse> GetAsync(Competitor competitor, string url, CancellationToken ct)
    {
        var elapsed = DateTimeOffset.UtcNow - _lastRequestAt;
        if (elapsed < _minDelay)
        {
            await Task.Delay(_minDelay - elapsed, ct);
        }

        var attempts = 0;
        const int maxAttempts = 3;

        while (true)
        {
            attempts++;
            try
            {
                _lastRequestAt = DateTimeOffset.UtcNow;
                using var resp = await httpClient.GetAsync(url, ct);

                if ((int)resp.StatusCode == 429 && attempts < maxAttempts)
                {
                    var delay = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                    Console.WriteLine($"Rate limited (429) on {url}. Retrying after {delay.TotalSeconds}s...");
                    await Task.Delay(delay, ct);
                    continue;
                }

                var body = await resp.Content.ReadAsStringAsync(ct);
                var headers = resp.Headers
                    .Concat(resp.Content.Headers)
                    .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);

                return new CollectedResponse(
                    (int)resp.StatusCode,
                    body,
                    resp.IsSuccessStatusCode,
                    resp.IsSuccessStatusCode ? null : $"HTTP {(int)resp.StatusCode}",
                    headers);
            }
            catch (Exception ex) when (attempts < maxAttempts && !ct.IsCancellationRequested)
            {
                Console.WriteLine($"Request attempt {attempts} failed on {url}: {ex.Message}. Retrying...");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempts), ct);
            }
        }
    }
}
