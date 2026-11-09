using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace LazyBean.Application.Ads.Common;

// The optional AI-read, off by default (AI Configuration). For an unlinked Google image ad whose preview is stored, the
// vision model names the product and its price. Every attempt counts against the daily cap, and one ingest call reads
// at most MaxReadsPerBatch, so the call stays short. An ad that could not be read is tried again on a later run.
public sealed class CreativeVisionService(
    IUnitOfWork unitOfWork,
    ICreativeImageReader reader,
    TimeProvider clock,
    ILogger<CreativeVisionService> logger)
{
    public const int MaxReadsPerBatch = 10;
    public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(60);

    // Returns the number of attempts made in this call.
    public async Task<int> ApplyAsync(IReadOnlyList<Ad> ads, IReadOnlyList<LinkableProduct> catalog, CancellationToken ct = default)
    {
        var setting = (await unitOfWork.AiSettings.GetAllAsync(ct)).FirstOrDefault();
        if (setting is null || !setting.CreativeVisionEnabled)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (setting.CreativeVisionUsageDate != today)
        {
            setting.CreativeVisionUsageDate = today;
            setting.CreativeVisionUsedToday = 0;
        }

        var remaining = setting.CreativeVisionDailyCap - setting.CreativeVisionUsedToday;
        if (remaining <= 0)
        {
            return 0;
        }

        var candidates = ads
            .Where(a => a.Platform == AdPlatform.Google &&
                        a.ProductId is null &&
                        a.CreativeFormat == GoogleCreativeFormat.Image &&
                        !string.IsNullOrWhiteSpace(a.PreviewUrl) &&
                        a.AiReadAt is null)
            .Take(Math.Min(remaining, MaxReadsPerBatch))
            .ToList();

        // The reads share the ingest request, which the desktop app waits for. They get their own time budget, so a slow
        // model cannot make the ingest time out after the ads were saved. Reads not done in time run on a later pass.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeBudget);

        var attempts = 0;
        foreach (var ad in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (budget.IsCancellationRequested)
            {
                logger.LogInformation("AI-read stopped at its time budget; the remaining creatives are read on a later run.");
                break;
            }

            attempts++;
            setting.CreativeVisionUsedToday++;

            try
            {
                var reading = await reader.ReadAsync(ad.PreviewUrl!, budget.Token) ?? new CreativeImageReading(null, null);
                ad.AiReadName = reading.ProductName;
                ad.AiReadPrice = reading.Price;
                ad.AiReadAt = now;
                AdDerivedFields.Apply(ad, catalog);
                unitOfWork.Ads.Update(ad);
            }
            catch (InvalidOperationException ex)
            {
                // The feature cannot run (no key, or AI is not configured): stop, and do not count the rest.
                logger.LogWarning("AI-read stopped: {Reason}", ex.Message);
                setting.CreativeVisionUsedToday--;
                attempts--;
                break;
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // The time budget ran out during this read. The creative is read on a later run; the attempt still counts.
                logger.LogInformation("AI-read of Google creative {ExternalAdId} was cut off by the time budget.", ad.ExternalAdId);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "AI-read of Google creative {ExternalAdId} failed. It will be tried again on a later run.", ad.ExternalAdId);
            }
        }

        unitOfWork.AiSettings.Update(setting);
        if (attempts > 0)
        {
            logger.LogInformation(
                "AI-read: {Attempts} Google creative preview(s) read; {Left} of {Cap} left for today.",
                attempts,
                Math.Max(0, setting.CreativeVisionDailyCap - setting.CreativeVisionUsedToday),
                setting.CreativeVisionDailyCap);
        }

        return attempts;
    }
}
