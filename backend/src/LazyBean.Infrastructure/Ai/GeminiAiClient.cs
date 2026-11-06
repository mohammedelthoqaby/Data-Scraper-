using System.Net;
using System.Text;
using System.Text.Json;
using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Ai;

public class GeminiAiClient : IAiClient
{
    private readonly HttpClient _httpClient;
    private readonly LazyBeanDbContext _dbContext;
    private readonly IAiKeyProtector _protector;
    private readonly ILogger<GeminiAiClient> _logger;

    public const string DefaultModel = "gemini-3.5-flash";
    private const string BaseApiUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public GeminiAiClient(
        IHttpClientFactory httpClientFactory,
        LazyBeanDbContext dbContext,
        IAiKeyProtector protector,
        ILogger<GeminiAiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("GeminiClient");
        _dbContext = dbContext;
        _protector = protector;
        _logger = logger;
    }

    public async Task<AiTestResultDto> TestConnectionAsync(string? apiKeyOverride, string? modelOverride, CancellationToken ct = default)
    {
        var model = !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride.Trim() : DefaultModel;
        var key = !string.IsNullOrWhiteSpace(apiKeyOverride) ? apiKeyOverride.Trim() : await GetDecryptedKeyAsync(ct);

        if (string.IsNullOrWhiteSpace(key))
        {
            return new AiTestResultDto(false, "No API key configured or provided.");
        }

        try
        {
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = "Respond with 'OK' only." }
                        }
                    }
                }
            };

            var json = await PostWithRetryAsync(model, key, requestBody, ct);
            return new AiTestResultDto(true, $"Successfully connected to Google AI Studio ({model}).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Gemini test connection failed: {Error}", ex.Message);
            return new AiTestResultDto(false, SanitizeErrorMessage(ex.Message));
        }
    }

    public async Task<IReadOnlyList<AiReviewTriageResult>> TriageReviewBatchAsync(
        IReadOnlyList<AiReviewTriagePairRequest> batch,
        CancellationToken ct = default)
    {
        if (batch.Count == 0)
            return [];

        var setting = await _dbContext.AiSettings.FirstOrDefaultAsync(ct);
        if (setting == null || !setting.IsEnabled)
            return [];

        var key = await GetDecryptedKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(key))
            return [];

        if (!await IncrementAndCheckDailyCapAsync(setting, ct))
        {
            _logger.LogWarning("Daily AI request cap reached ({Cap}). Skipping Gemini review triage.", setting.DailyRequestCap);
            return [];
        }

        var model = !string.IsNullOrWhiteSpace(setting.ModelName) ? setting.ModelName : DefaultModel;

        var pairsPromptList = batch.Select(p => new
        {
            reviewId = p.ReviewId.ToString(),
            reference = new
            {
                title = p.RefTitle,
                brand = p.RefBrand,
                category = p.RefCategory,
                weightGrams = p.RefWeightGrams,
                unitCount = p.RefUnitCount,
                capsuleSystem = p.RefCapsuleSystem,
                format = p.RefFormat
            },
            competitor = new
            {
                title = p.CompTitle,
                brand = p.CompBrand,
                category = p.CompCategory,
                weightGrams = p.CompWeightGrams,
                unitCount = p.CompUnitCount,
                capsuleSystem = p.CompCapsuleSystem,
                format = p.CompFormat
            }
        });

        var systemPrompt = @"You are an expert product matching assistant for a specialty coffee and beverage retail intelligence system in Egypt.
Your task is to compare pairs of candidate product listings (one from Lazy Bean, one from a competitor) and determine whether they are the EXACT SAME product or DIFFERENT products.
Guidelines:
- Return 'same' if brand, roast/flavor/variant line, weight/unit count, format, and capsule system match (minor naming or language differences like 'Ground' vs 'مطحون' or 'Antica Bottega' vs 'Antica Bottega Whole Bean' are the same).
- Return 'different' if they differ in bean origin/flavor line (e.g., Brasile vs Selection, Intenso vs Classico, Crema vs Oro), roast profile, volume/weight, pack count, or capsule compatibility.
- Return 'unsure' if critical details are missing or ambiguous.
- Provide confidence between 0.0 and 1.0.
- Provide a concise one-line reason (under 120 chars) in English explaining why.
Output MUST be a JSON array of objects matching this schema:
[
  {
    ""reviewId"": ""<string>"",
    ""verdict"": ""same"" | ""different"" | ""unsure"",
    ""confidence"": <number 0.0-1.0>,
    ""reason"": ""<one-line reason>""
  }
]";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = systemPrompt + "\n\nCandidate Pairs to Evaluate:\n" + JsonSerializer.Serialize(pairsPromptList) }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        try
        {
            var responseJson = await PostWithRetryAsync(model, key, requestBody, ct);
            return GeminiResponseParser.ParseTriageResults(responseJson, batch, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute Gemini triage for batch of {Count} pairs.", batch.Count);
            throw;
        }
    }

    public async Task<IReadOnlyList<RankedAttentionItem>> RankAndPhraseAttentionItemsAsync(
        IReadOnlyList<AttentionFactItem> facts,
        CancellationToken ct = default)
    {
        if (facts.Count == 0)
            return [];

        var setting = await _dbContext.AiSettings.FirstOrDefaultAsync(ct);
        if (setting == null || !setting.IsEnabled)
            return FallbackToRuleBased(facts);

        var key = await GetDecryptedKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(key))
            return FallbackToRuleBased(facts);

        if (!await IncrementAndCheckDailyCapAsync(setting, ct))
        {
            _logger.LogWarning("Daily AI request cap reached. Using rule-based attention ranking.");
            return FallbackToRuleBased(facts);
        }

        var model = !string.IsNullOrWhiteSpace(setting.ModelName) ? setting.ModelName : DefaultModel;

        var factsPayload = facts.Select(f => new
        {
            id = f.Id,
            type = f.Type,
            productTitle = f.ProductTitle,
            brand = f.Brand,
            competitorName = f.CompetitorName,
            lazyBeanPrice = f.LazyBeanPrice,
            competitorPrice = f.CompetitorPrice,
            difference = f.Difference,
            discountPercent = f.DiscountPercent,
            adCount = f.AdCount,
            allowedNumbers = f.Numbers
        });

        var prompt = @"You are an executive commercial intelligence assistant for Lazy Bean coffee in Egypt.
Analyze the provided competitor market facts and generate a prioritized list of commercial attention items for the operator dashboard.
Rules:
1. Prioritize items by business urgency ('high', 'medium', 'low').
2. Provide a crisp headline in English ('headline') and Arabic ('headlineAr').
3. Provide a clear explanation in English ('explanation') and Arabic ('explanationAr').
4. CRITICAL ACCURACY RULE: EVERY SINGLE NUMBER mentioned in your headlines and explanations MUST come directly from the item's 'allowedNumbers'. DO NOT invent, round differently, or alter any prices, percentages, or counts.
Output JSON schema:
[
  {
    ""id"": ""<fact id>"",
    ""priority"": ""high"" | ""medium"" | ""low"",
    ""rank"": <1, 2, 3...>,
    ""headline"": ""<English headline>"",
    ""headlineAr"": ""<Arabic headline>"",
    ""explanation"": ""<English explanation>"",
    ""explanationAr"": ""<Arabic explanation>""
  }
]";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt + "\n\nFacts:\n" + JsonSerializer.Serialize(factsPayload) }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        try
        {
            var responseJson = await PostWithRetryAsync(model, key, requestBody, ct);
            using var doc = JsonDocument.Parse(responseJson);

            var textPart = doc.RootElement.GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrWhiteSpace(textPart))
                return FallbackToRuleBased(facts);

            using var resultsDoc = JsonDocument.Parse(textPart);
            var ranked = new List<RankedAttentionItem>();

            foreach (var elem in resultsDoc.RootElement.EnumerateArray())
            {
                var id = elem.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var fact = facts.FirstOrDefault(f => f.Id == id);
                if (fact == null) continue;

                var priority = elem.TryGetProperty("priority", out var pProp) ? pProp.GetString()?.ToLowerInvariant() ?? "medium" : "medium";
                var rank = elem.TryGetProperty("rank", out var rProp) && rProp.TryGetInt32(out var rVal) ? rVal : ranked.Count + 1;
                var headline = elem.TryGetProperty("headline", out var hlProp) ? hlProp.GetString() ?? fact.FallbackHeadline : fact.FallbackHeadline;
                var headlineAr = elem.TryGetProperty("headlineAr", out var hlArProp) ? hlArProp.GetString() ?? fact.FallbackHeadlineAr : fact.FallbackHeadlineAr;
                var explanation = elem.TryGetProperty("explanation", out var exProp) ? exProp.GetString() ?? fact.FallbackExplanation : fact.FallbackExplanation;
                var explanationAr = elem.TryGetProperty("explanationAr", out var exArProp) ? exArProp.GetString() ?? fact.FallbackExplanationAr : fact.FallbackExplanationAr;

                // VALIDATE NUMBERS: Every number must exist in fact.Numbers
                var isValid = AiFactNumberValidator.ValidateNumbers(fact, headline, headlineAr, explanation, explanationAr);
                if (!isValid)
                {
                    _logger.LogWarning("Number validation failed for AI attention item {Id}. Falling back to factual template.", fact.Id);
                    headline = fact.FallbackHeadline;
                    headlineAr = fact.FallbackHeadlineAr;
                    explanation = fact.FallbackExplanation;
                    explanationAr = fact.FallbackExplanationAr;
                }

                ranked.Add(new RankedAttentionItem(
                    fact.Id,
                    fact.Type,
                    headline,
                    headlineAr,
                    explanation,
                    explanationAr,
                    priority,
                    rank,
                    fact.ProductUrl,
                    fact.Brand,
                    fact.CompetitorName,
                    fact.LazyBeanPrice,
                    fact.CompetitorPrice,
                    IsAiGenerated: isValid));
            }

            // Include any facts that were omitted by the model
            var includedIds = ranked.Select(r => r.Id).ToHashSet();
            foreach (var f in facts.Where(f => !includedIds.Contains(f.Id)))
            {
                ranked.Add(new RankedAttentionItem(
                    f.Id,
                    f.Type,
                    f.FallbackHeadline,
                    f.FallbackHeadlineAr,
                    f.FallbackExplanation,
                    f.FallbackExplanationAr,
                    "medium",
                    ranked.Count + 1,
                    f.ProductUrl,
                    f.Brand,
                    f.CompetitorName,
                    f.LazyBeanPrice,
                    f.CompetitorPrice,
                    IsAiGenerated: false));
            }

            return ranked.OrderBy(r => r.Rank).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rank attention items with Gemini. Falling back to rule-based.");
            return FallbackToRuleBased(facts);
        }
    }

    private static IReadOnlyList<RankedAttentionItem> FallbackToRuleBased(IReadOnlyList<AttentionFactItem> facts)
    {
        return facts.Select((f, index) => new RankedAttentionItem(
            f.Id,
            f.Type,
            f.FallbackHeadline,
            f.FallbackHeadlineAr,
            f.FallbackExplanation,
            f.FallbackExplanationAr,
            f.Type == "out_of_stock" || (f.Difference ?? 0) > 100m ? "high" : "medium",
            index + 1,
            f.ProductUrl,
            f.Brand,
            f.CompetitorName,
            f.LazyBeanPrice,
            f.CompetitorPrice,
            IsAiGenerated: false
        )).ToList();
    }

    private async Task<string> PostWithRetryAsync(string model, string apiKey, object payload, CancellationToken ct)
    {
        var url = $"{BaseApiUrl}/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var jsonContent = JsonSerializer.Serialize(payload);

        var delays = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };
        const int maxAttempts = 3;
        var attempts = 0;

        while (true)
        {
            attempts++;
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            // 30s timeout per Gemini API call
            using var callCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            callCts.CancelAfter(TimeSpan.FromSeconds(30));

            HttpResponseMessage resp;
            try
            {
                resp = await _httpClient.SendAsync(req, callCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && callCts.IsCancellationRequested)
            {
                _logger.LogWarning("Gemini request timed out after 30 seconds (attempt {Attempt}/{Max}).", attempts, maxAttempts);
                await RecordCallStatusAsync("timeout", CancellationToken.None);
                throw new HttpRequestException("timeout");
            }
            catch (Exception ex) when (attempts < maxAttempts && !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Network exception during Gemini request (attempt {Attempt}): {Message}", attempts, ex.Message);
                await Task.Delay(delays[attempts - 1], ct);
                continue;
            }

            if (resp.IsSuccessStatusCode)
            {
                await RecordCallStatusAsync("200 OK", CancellationToken.None);
                return await resp.Content.ReadAsStringAsync(ct);
            }

            var responseBody = await resp.Content.ReadAsStringAsync(ct);

            // On 401 Unauthorized / Forbidden: immediate failure with no retry
            if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Gemini API key rejected with HTTP {Status}.", (int)resp.StatusCode);
                await RecordCallStatusAsync("401 invalid key", CancellationToken.None);
                throw new HttpRequestException("401 invalid key");
            }

            // On 429 Too Many Requests or 503 Transient Demand
            if (resp.StatusCode == (HttpStatusCode)429 || resp.StatusCode == (HttpStatusCode)503)
            {
                var retryAfter = resp.Headers.RetryAfter?.Delta;
                if (retryAfter.HasValue && retryAfter.Value > TimeSpan.FromSeconds(60))
                {
                    _logger.LogWarning("Gemini returned 429 with Retry-After {Seconds}s (> 60s). Aborting call.", retryAfter.Value.TotalSeconds);
                    await RecordCallStatusAsync("429 quota", CancellationToken.None);
                    throw new HttpRequestException("429 quota");
                }

                if (attempts >= maxAttempts)
                {
                    _logger.LogWarning("Gemini returned 429 and retry limit ({Max}) reached.", maxAttempts);
                    await RecordCallStatusAsync("429 quota", CancellationToken.None);
                    throw new HttpRequestException("429 quota");
                }

                var delay = retryAfter ?? delays[attempts - 1];
                if (delay > TimeSpan.FromSeconds(60))
                {
                    delay = TimeSpan.FromSeconds(60);
                }

                _logger.LogWarning("Gemini returned {StatusCode}. Retrying in {Seconds}s (attempt {Attempt}/{Max}).",
                    (int)resp.StatusCode, delay.TotalSeconds, attempts, maxAttempts);

                await Task.Delay(delay, ct);
                continue;
            }

            var errDesc = $"Gemini API error (Status {(int)resp.StatusCode}): {SanitizeErrorMessage(responseBody)}";
            await RecordCallStatusAsync($"HTTP {(int)resp.StatusCode}", CancellationToken.None);
            throw new HttpRequestException(errDesc);
        }
    }

    private async Task RecordCallStatusAsync(string status, CancellationToken noneCt)
    {
        try
        {
            var setting = await _dbContext.AiSettings.FirstOrDefaultAsync(noneCt);
            if (setting != null)
            {
                setting.LastCallAt = DateTimeOffset.UtcNow;
                setting.LastCallStatus = status;
                await _dbContext.SaveChangesAsync(noneCt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist AI call status {Status}", status);
        }
    }

    private async Task<string?> GetDecryptedKeyAsync(CancellationToken ct)
    {
        var setting = await _dbContext.AiSettings.FirstOrDefaultAsync(ct);
        if (setting == null || string.IsNullOrWhiteSpace(setting.EncryptedApiKey))
            return null;

        try
        {
            return _protector.DecryptKey(setting.EncryptedApiKey);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to decrypt Gemini API key from database: {Error}", ex.Message);
            return null;
        }
    }

    private async Task<bool> IncrementAndCheckDailyCapAsync(LazyBean.Domain.Entities.AiSetting setting, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (setting.UsageDate != today)
        {
            setting.UsageDate = today;
            setting.RequestsUsedToday = 0;
        }

        if (setting.RequestsUsedToday >= setting.DailyRequestCap)
        {
            return false;
        }

        setting.RequestsUsedToday++;
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    private static string SanitizeErrorMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unknown error";
        // Strip any key parameter or URL query string if present
        return System.Text.RegularExpressions.Regex.Replace(raw, @"key=[A-Za-z0-9_\-\.]+", "key=***");
    }
}
