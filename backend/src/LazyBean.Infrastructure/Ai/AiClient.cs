using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Infrastructure.Ai.Providers;
using LazyBean.Infrastructure.Ai.RateLimiting;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LazyBean.Infrastructure.Ai;

/// <summary>
/// Routes every AI call: primary provider first (Groq by default), then the fallback on
/// quota, rate-limit, timeout or server errors. If no provider can serve the call it throws
/// <see cref="AiUnavailableException"/>, which callers turn into a graceful stop.
/// </summary>
public sealed class AiClient : IAiClient
{
    /// <summary>Cool-down after a 429 that carries no Retry-After header.</summary>
    private static readonly TimeSpan DefaultCooldownAfter429 = TimeSpan.FromSeconds(60);

    private readonly LazyBeanDbContext _db;
    private readonly IAiKeyProtector _protector;
    private readonly IReadOnlyList<IAiProvider> _providers;
    private readonly AiRateGate _gate;
    private readonly IOptions<AiOptions> _options;
    private readonly ILogger<AiClient> _logger;

    public AiClient(
        LazyBeanDbContext db,
        IAiKeyProtector protector,
        IEnumerable<IAiProvider> providers,
        AiRateGate gate,
        IOptions<AiOptions> options,
        ILogger<AiClient> logger)
    {
        _db = db;
        _protector = protector;
        _providers = providers.ToList();
        _gate = gate;
        _options = options;
        _logger = logger;
    }

    public async Task<AiCompletionResult> CompleteJsonAsync(AiJsonRequest request, CancellationToken ct = default)
    {
        var settings = await _db.AiSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || !settings.IsEnabled)
            throw new AiUnavailableException("AI assistance is disabled.", quotaReached: false);

        var today = UtcToday();
        var usageToday = await _db.AiProviderUsages.AsNoTracking()
            .Where(u => u.UsageDate == today)
            .ToListAsync(ct);

        var totalToday = usageToday.Sum(u => u.RequestCount);
        if (totalToday >= settings.DailyRequestCap)
            throw new AiUnavailableException($"Daily AI request cap reached ({totalToday}/{settings.DailyRequestCap}).", quotaReached: true);

        var primary = AiProviders.Normalize(settings.PrimaryProvider);
        string[] order = primary == AiProviders.Gemini
            ? [AiProviders.Gemini, AiProviders.Groq]
            : [AiProviders.Groq, AiProviders.Gemini];

        var reasons = new List<string>();
        var quotaHit = false;
        var providerRequest = new AiProviderRequest(request.SystemPrompt, request.UserPrompt, request.MaxOutputTokens);

        foreach (var name in order)
        {
            var provider = _providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (provider is null)
            {
                reasons.Add($"{name}: no client registered");
                continue;
            }

            var row = await _db.AiProviderSettings.AsNoTracking().FirstOrDefaultAsync(p => p.Provider == name, ct);
            var key = AiProviderConfigResolver.ResolveKey(name, _options.Value, row, _protector, _logger);
            if (key is null)
            {
                reasons.Add($"{name}: not configured");
                continue;
            }
            var model = AiProviderConfigResolver.ModelFor(name, row);

            if (_gate.IsBlocked(name, out var cooldown))
            {
                reasons.Add($"{name}: cooling down for {cooldown.TotalSeconds:0}s");
                quotaHit = true;
                continue;
            }

            var limits = _options.Value.ForProvider(name);
            var usedByProvider = usageToday.FirstOrDefault(u => u.Provider == name)?.RequestCount ?? 0;
            if (limits.RequestsPerDay > 0 && usedByProvider >= limits.RequestsPerDay)
            {
                reasons.Add($"{name}: daily limit reached");
                quotaHit = true;
                continue;
            }

            var estimate = EstimateTokens(request);
            var maxWait = TimeSpan.FromSeconds(Math.Max(0, _options.Value.MaxRateWaitSeconds));
            if (!await _gate.TryAcquireAsync(name, estimate, maxWait, ct))
            {
                reasons.Add($"{name}: rate-limit budget busy");
                quotaHit = true;
                continue;
            }

            try
            {
                var response = await provider.SendAsync(providerRequest, key, model, ct);
                var used = response.TotalTokens > 0 ? response.TotalTokens : estimate;

                _gate.Settle(name, estimate, used);
                _gate.ObserveRateLimit(name, response.RateLimit);
                _gate.ReportSuccess(name);
                await RecordAttemptAsync(name, used, error: null, CancellationToken.None);

                return new AiCompletionResult(name, model, response.Text, response.TotalTokens);
            }
            catch (AiProviderException ex)
            {
                _gate.Settle(name, estimate, ex.Kind == AiProviderErrorKind.RateLimited ? 0 : estimate);
                _gate.ObserveRateLimit(name, ex.RateLimit);

                if (ex.Kind == AiProviderErrorKind.RateLimited)
                {
                    _gate.Block(name, ex.RetryAfter ?? DefaultCooldownAfter429);
                    _gate.ReportRateLimited(name);
                    quotaHit = true;
                }

                await RecordAttemptAsync(name, 0, ex, CancellationToken.None);
                _logger.LogWarning("AI provider {Provider} failed ({Kind}): {Message}. Trying the next provider.", name, ex.Kind, ex.Message);
                reasons.Add($"{name}: {ex.Kind}");
            }
        }

        _logger.LogWarning("No AI provider could serve the request: {Reasons}", string.Join("; ", reasons));
        throw new AiUnavailableException(
            quotaHit ? "AI quota reached" : "AI providers unavailable",
            quotaReached: quotaHit);
    }

    public async Task<AiKeyTestResultDto> TestProviderKeyAsync(string provider, string? apiKeyOverride, string? modelOverride, CancellationToken ct = default)
    {
        var name = AiProviders.Normalize(provider);
        var row = await _db.AiProviderSettings.AsNoTracking().FirstOrDefaultAsync(p => p.Provider == name, ct);

        var key = !string.IsNullOrWhiteSpace(apiKeyOverride)
            ? apiKeyOverride.Trim()
            : AiProviderConfigResolver.ResolveKey(name, _options.Value, row, _protector, _logger);
        if (key is null)
            return new AiKeyTestResultDto("not_configured", "No API key is saved or entered for this provider.");

        var model = !string.IsNullOrWhiteSpace(modelOverride)
            ? modelOverride.Trim()
            : AiProviderConfigResolver.ModelFor(name, row);

        var impl = _providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (impl is null)
            return new AiKeyTestResultDto("error", "No client is registered for this provider.");

        var label = AiProviders.Label(name);
        try
        {
            var response = await impl.SendAsync(
                new AiProviderRequest("Reply with a JSON object: {\"ok\": true}.", "Test.", MaxOutputTokens: 32),
                key,
                model,
                ct);

            await RecordAttemptAsync(name, response.TotalTokens, error: null, CancellationToken.None);
            return new AiKeyTestResultDto("ok", $"Connected to {label} ({model}). The key works.");
        }
        catch (AiProviderException ex)
        {
            await RecordAttemptAsync(name, 0, ex, CancellationToken.None);

            return ex.Kind switch
            {
                AiProviderErrorKind.InvalidKey => new AiKeyTestResultDto("invalid_key", $"{label} rejected this key. Check it and try again."),
                AiProviderErrorKind.RateLimited => new AiKeyTestResultDto("rate_limited", $"The key is accepted but {label} is rate-limiting right now. Try again shortly."),
                _ => new AiKeyTestResultDto("error", ex.Message),
            };
        }
    }

    public async Task<IReadOnlyList<AiReviewTriageResult>> TriageReviewBatchAsync(IReadOnlyList<AiReviewTriagePairRequest> batch, CancellationToken ct = default)
    {
        if (batch.Count == 0)
            return [];

        var (system, user) = AiPrompts.BuildTriage(batch);
        var maxOutput = Math.Min(4096, 90 * batch.Count + 100);

        var completion = await CompleteJsonAsync(new AiJsonRequest(system, user, maxOutput), ct);
        return AiResponseParser.ParseTriageResults(completion.Text, batch, _logger)
            .Select(r => r with { Model = completion.Model })
            .ToList();
    }

    public async Task<IReadOnlyList<RankedAttentionItem>> RankAndPhraseAttentionItemsAsync(IReadOnlyList<AttentionFactItem> facts, CancellationToken ct = default)
    {
        if (facts.Count == 0)
            return [];

        try
        {
            var (system, user) = AiPrompts.BuildAttention(facts);
            var maxOutput = Math.Min(4096, 220 * facts.Count + 200);

            var completion = await CompleteJsonAsync(new AiJsonRequest(system, user, maxOutput), ct);
            return AiResponseParser.ParseAttentionItems(completion.Text, facts, _logger);
        }
        catch (AiUnavailableException ex)
        {
            _logger.LogInformation("Attention ranking uses the rule-based fallback: {Reason}", ex.Message);
            return AiResponseParser.FallbackToRuleBased(facts);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Attention ranking failed. Using the rule-based fallback.");
            return AiResponseParser.FallbackToRuleBased(facts);
        }
    }

    private async Task RecordAttemptAsync(string provider, int tokens, AiProviderException? error, CancellationToken ct)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var today = UtcToday();

            var usage = await _db.AiProviderUsages.FirstOrDefaultAsync(u => u.Provider == provider && u.UsageDate == today, ct);
            if (usage is null)
            {
                usage = new AiProviderUsage { Provider = provider, UsageDate = today };
                _db.AiProviderUsages.Add(usage);
            }
            usage.RequestCount += 1;
            usage.TokenCount += Math.Max(0, tokens);
            usage.UpdatedAt = now;

            if (error is not null)
            {
                var setting = await _db.AiProviderSettings.FirstOrDefaultAsync(p => p.Provider == provider, ct);
                if (setting is null)
                {
                    setting = new AiProviderSetting { Provider = provider };
                    _db.AiProviderSettings.Add(setting);
                }

                setting.LastError = Truncate(error.Message, 500);
                setting.LastErrorAt = now;
                if (error.Kind == AiProviderErrorKind.RateLimited)
                    setting.Last429At = now;
                setting.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Usage bookkeeping must never break the calling run.
            _logger.LogWarning(ex, "Could not record AI usage for {Provider}.", provider);
        }
    }

    private static int EstimateTokens(AiJsonRequest request)
    {
        // Roughly 3.5 characters per token for mixed English and Arabic text, plus the output budget.
        var input = (request.SystemPrompt.Length + request.UserPrompt.Length) / 3.5;
        return (int)Math.Ceiling(input) + request.MaxOutputTokens;
    }

    private static DateOnly UtcToday() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];
}
