using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LazyBean.Infrastructure.Ai;

/// <summary>
/// Reads and writes AI settings. Keys are encrypted before they reach the database, and
/// every DTO carries only the masked form (for example "gsk_••••1234").
/// </summary>
public sealed class AiSettingsStore : IAiSettingsStore
{
    private const int MaxModelNameLength = 100;

    private readonly LazyBeanDbContext _db;
    private readonly IAiKeyProtector _protector;
    private readonly IOptions<AiOptions> _options;
    private readonly ILogger<AiSettingsStore> _logger;

    public AiSettingsStore(
        LazyBeanDbContext db,
        IAiKeyProtector protector,
        IOptions<AiOptions> options,
        ILogger<AiSettingsStore> logger)
    {
        _db = db;
        _protector = protector;
        _options = options;
        _logger = logger;
    }

    public async Task<AiSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var setting = await _db.AiSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AiSetting();
        var rows = await _db.AiProviderSettings.AsNoTracking().ToListAsync(ct);
        var today = UtcToday();
        var usage = await _db.AiProviderUsages.AsNoTracking()
            .Where(u => u.UsageDate == today)
            .ToListAsync(ct);

        var providers = AiProviders.All
            .Select(name => BuildProviderStatus(name, rows.FirstOrDefault(r => r.Provider == name), usage, today))
            .ToList();

        return new AiSettingsDto(
            setting.IsEnabled,
            setting.PrimaryProvider,
            setting.DailyRequestCap,
            setting.TriageRunCap,
            providers,
            setting.CreativeVisionEnabled,
            setting.CreativeVisionDailyCap,
            setting.CreativeVisionUsedToday);
    }

    public async Task<AiSettingsDto> UpdateAsync(UpdateAiSettingsRequestDto request, CancellationToken ct = default)
    {
        var setting = await _db.AiSettings.FirstOrDefaultAsync(ct);
        if (setting is null)
        {
            setting = new AiSetting();
            _db.AiSettings.Add(setting);
        }

        if (request.IsEnabled.HasValue)
            setting.IsEnabled = request.IsEnabled.Value;

        if (!string.IsNullOrWhiteSpace(request.PrimaryProvider))
        {
            if (!AiProviders.IsKnown(request.PrimaryProvider))
                throw new ArgumentException("Primary provider must be 'groq' or 'gemini'.", nameof(request));
            setting.PrimaryProvider = AiProviders.Normalize(request.PrimaryProvider);
        }

        if (request.DailyRequestCap is { } cap)
            setting.DailyRequestCap = Math.Clamp(cap, 1, 100_000);

        if (request.TriageRunCap is { } triageCap)
            setting.TriageRunCap = Math.Clamp(triageCap, 1, 1000);

        if (request.CreativeVisionEnabled.HasValue)
            setting.CreativeVisionEnabled = request.CreativeVisionEnabled.Value;

        if (request.CreativeVisionDailyCap is > 0)
            setting.CreativeVisionDailyCap = Math.Clamp(request.CreativeVisionDailyCap.Value, 1, 500);

        setting.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("AI settings updated (enabled: {Enabled}, primary: {Primary}).", setting.IsEnabled, setting.PrimaryProvider);
        return await GetAsync(ct);
    }

    public async Task<AiSettingsDto> SaveProviderAsync(string provider, SaveAiProviderRequestDto request, CancellationToken ct = default)
    {
        var row = await GetOrCreateProviderRowAsync(provider, ct);
        var now = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            var clean = request.ApiKey.Trim();
            row.EncryptedApiKey = _protector.EncryptKey(clean);
            row.KeyPrefix = clean.Length >= 12 ? clean[..4] : null;
            row.KeyLastFour = clean.Length >= 4 ? clean[^4..] : null;

            // A new key invalidates the previous test and error history.
            row.LastTestedAt = null;
            row.LastTestStatus = null;
            row.LastTestMessage = null;
            row.LastError = null;
            row.LastErrorAt = null;
            row.Last429At = null;
        }

        if (request.ModelName is not null)
        {
            var model = request.ModelName.Trim();
            if (model.Length > MaxModelNameLength || model.Any(char.IsWhiteSpace))
                throw new ArgumentException("Model name must be a single identifier without spaces (max 100 characters).", nameof(request));
            row.ModelName = model.Length == 0 ? null : model;
        }

        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("AI provider {Provider} settings saved. The key is stored encrypted.", provider);
        return await GetAsync(ct);
    }

    public async Task<AiSettingsDto> RemoveProviderKeyAsync(string provider, CancellationToken ct = default)
    {
        var row = await GetOrCreateProviderRowAsync(provider, ct);
        row.EncryptedApiKey = null;
        row.KeyPrefix = null;
        row.KeyLastFour = null;
        row.LastTestedAt = null;
        row.LastTestStatus = null;
        row.LastTestMessage = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Saved AI key for {Provider} removed.", provider);
        return await GetAsync(ct);
    }

    public async Task RecordTestResultAsync(string provider, AiKeyTestResultDto result, CancellationToken ct = default)
    {
        var row = await GetOrCreateProviderRowAsync(provider, ct);
        var now = DateTimeOffset.UtcNow;

        row.LastTestedAt = now;
        row.LastTestStatus = result.Status;
        row.LastTestMessage = Truncate(result.Message, 500);
        if (result.Status == "rate_limited")
            row.Last429At = now;
        row.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
    }

    private AiProviderStatusDto BuildProviderStatus(
        string name,
        AiProviderSetting? row,
        IReadOnlyList<AiProviderUsage> usage,
        DateOnly today)
    {
        var source = AiProviderConfigResolver.KeySource(_options.Value, name, row);
        var masked = source switch
        {
            // The environment key is masked from the value itself, so the saved mask is never needed.
            AiProviderConfigResolver.SourceEnvironment => _protector.MaskKey(_options.Value.ForProvider(name).ApiKey),
            AiProviderConfigResolver.SourceDatabase => AiKeyProtector.BuildMask(row?.KeyPrefix, row?.KeyLastFour),
            _ => null,
        };

        var todayUsage = usage.FirstOrDefault(u => u.Provider == name);
        var limits = _options.Value.ForProvider(name);

        return new AiProviderStatusDto(
            Provider: name,
            Label: AiProviders.Label(name),
            IsConfigured: source != AiProviderConfigResolver.SourceNone,
            KeySource: source,
            MaskedApiKey: string.IsNullOrEmpty(masked) ? null : masked,
            ModelName: AiProviderConfigResolver.ModelFor(name, row),
            DefaultModel: AiProviders.DefaultModel(name),
            SuggestedModels: AiProviders.SuggestedModels(name),
            LastTestedAt: row?.LastTestedAt,
            LastTestStatus: row?.LastTestStatus,
            LastTestMessage: row?.LastTestMessage,
            Usage: new AiProviderUsageDto(
                UsageDate: today,
                RequestsToday: todayUsage?.RequestCount ?? 0,
                TokensToday: todayUsage?.TokenCount ?? 0,
                DailyRequestLimit: limits.RequestsPerDay,
                LastError: row?.LastError,
                LastErrorAt: row?.LastErrorAt,
                Last429At: row?.Last429At));
    }

    private async Task<AiProviderSetting> GetOrCreateProviderRowAsync(string provider, CancellationToken ct)
    {
        if (!AiProviders.IsKnown(provider))
            throw new ArgumentException($"Unknown AI provider '{provider}'.", nameof(provider));

        var name = AiProviders.Normalize(provider);
        var row = await _db.AiProviderSettings.FirstOrDefaultAsync(p => p.Provider == name, ct);
        if (row is null)
        {
            row = new AiProviderSetting { Provider = name };
            _db.AiProviderSettings.Add(row);
        }
        return row;
    }

    private static DateOnly UtcToday() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];
}
