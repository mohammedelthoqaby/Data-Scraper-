using System.Text.Json;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Persistence;

// Dev/ops convenience: creates one bootstrap login if the users table is empty, from
// Admin:Username / Admin:Password (set via user-secrets or environment - never appsettings.json).
// Syncs/creates bootstrap admin login from Admin:Username / Admin:Password
// (set via user-secrets or environment - never appsettings.json).
// If the user exists and the stored hash doesn't verify against Admin:Password,
// re-hashes and updates the user so environment variables stay the source of truth.
public static class DbInitializer
{
    public static async Task SeedAdminUserAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        var context = provider.GetRequiredService<LazyBeanDbContext>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var passwordHasher = provider.GetRequiredService<IPasswordHasher>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        var username = configuration["Admin:Username"];
        var password = configuration["Admin:Password"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            if (!await context.Users.AnyAsync(ct))
            {
                logger.LogWarning(
                    "No users exist and Admin:Username/Admin:Password are not configured - " +
                    "set them via user-secrets to bootstrap the first login.");
            }
            return;
        }

        var adminUser = await context.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (adminUser is null)
        {
            context.Users.Add(new User { Username = username, PasswordHash = passwordHasher.Hash(password) });
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Seeded bootstrap admin user {Username}.", username);
        }
        else if (!passwordHasher.Verify(adminUser.PasswordHash, password))
        {
            adminUser.PasswordHash = passwordHasher.Hash(password);
            await context.SaveChangesAsync(ct);
            logger.LogInformation("admin credential synced from configuration");
        }
    }

    private sealed record SeedCompetitorItem(
        string Key,
        string Name,
        string BaseUrl,
        string Platform,
        bool IsReference,
        int MinDelayMs,
        List<string>? MetaPageIds = null,
        string? CollectionHost = null,
        List<string>? PendingMetaPageIds = null);

    public static async Task SeedCompetitorsAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
        var env = scope.ServiceProvider.GetService<IHostEnvironment>();
        var contentRoot = env?.ContentRootPath ?? Directory.GetCurrentDirectory();

        var possiblePaths = new[]
        {
            Path.Combine(contentRoot, "seed", "competitors.json"),
            Path.Combine(contentRoot, "backend", "src", "LazyBean.Api", "seed", "competitors.json"),
            Path.Combine(AppContext.BaseDirectory, "seed", "competitors.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "seed", "competitors.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "seed", "competitors.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "seed", "competitors.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "backend", "src", "LazyBean.Api", "seed", "competitors.json")
        };

        var filePath = possiblePaths.FirstOrDefault(File.Exists);
        if (filePath is null)
        {
            logger.LogWarning("Competitor seed file not found in any standard path. Skipping competitor seed.");
            return;
        }

        var json = await File.ReadAllTextAsync(filePath, ct);
        var items = JsonSerializer.Deserialize<List<SeedCompetitorItem>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (items is null || items.Count == 0)
        {
            return;
        }

        // Clean up legacy duplicate 'coffee-zone-misr' if present, in favor of 'coffee-zone'
        var duplicate = await context.Competitors.FirstOrDefaultAsync(c => c.Key == "coffee-zone-misr", ct);
        if (duplicate is not null)
        {
            var dupProducts = await context.Products.Where(p => p.CompetitorId == duplicate.Id).ToListAsync(ct);
            var dupProductIds = dupProducts.Select(p => p.Id).ToList();
            var dupVariants = await context.Variants.Where(v => dupProductIds.Contains(v.ProductId)).ToListAsync(ct);
            var dupVariantIds = dupVariants.Select(v => v.Id).ToList();

            if (dupVariantIds.Count > 0)
            {
                var matches = await context.ProductMatches
                    .Where(m => dupVariantIds.Contains(m.CompetitorVariantId) || dupVariantIds.Contains(m.ReferenceVariantId))
                    .ToListAsync(ct);
                context.ProductMatches.RemoveRange(matches);

                var observations = await context.PriceObservations
                    .Where(o => dupVariantIds.Contains(o.VariantId))
                    .ToListAsync(ct);
                context.PriceObservations.RemoveRange(observations);

                var changeEvents = await context.ChangeEvents
                    .Where(e => e.VariantId.HasValue && dupVariantIds.Contains(e.VariantId.Value))
                    .ToListAsync(ct);
                context.ChangeEvents.RemoveRange(changeEvents);

                context.Variants.RemoveRange(dupVariants);
            }

            var compOffers = await context.Offers
                .Where(o => o.CompetitorId == duplicate.Id || (o.ProductId.HasValue && dupProductIds.Contains(o.ProductId.Value)))
                .ToListAsync(ct);
            context.Offers.RemoveRange(compOffers);

            var compRuns = await context.CompetitorRuns.Where(cr => cr.CompetitorId == duplicate.Id).ToListAsync(ct);
            context.CompetitorRuns.RemoveRange(compRuns);

            var fetchedResources = await context.FetchedResources.Where(fr => fr.CompetitorId == duplicate.Id).ToListAsync(ct);
            context.FetchedResources.RemoveRange(fetchedResources);

            var identities = await context.AdvertiserIdentities.Where(ai => ai.CompetitorId == duplicate.Id).ToListAsync(ct);
            context.AdvertiserIdentities.RemoveRange(identities);

            var ads = await context.Ads.Where(a => a.CompetitorId == duplicate.Id).ToListAsync(ct);
            context.Ads.RemoveRange(ads);

            var adCampaigns = await context.AdCampaigns.Where(ac => ac.CompetitorId == duplicate.Id).ToListAsync(ct);
            context.AdCampaigns.RemoveRange(adCampaigns);

            context.Products.RemoveRange(dupProducts);
            context.Competitors.Remove(duplicate);

            await context.SaveChangesAsync(ct);
            logger.LogInformation("Removed duplicate legacy competitor coffee-zone-misr ({Id}) and all associated records via code path.", duplicate.Id);
        }

        var existingCompetitors = await context.Competitors.ToListAsync(ct);
        var existingByKey = existingCompetitors.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var updated = 0;

        foreach (var item in items)
        {
            if (!Enum.TryParse<CompetitorPlatform>(item.Platform, ignoreCase: true, out var platform))
            {
                logger.LogWarning("Unknown competitor platform {Platform} for key {Key}. Skipping.", item.Platform, item.Key);
                continue;
            }

            var targetHost = !string.IsNullOrWhiteSpace(item.CollectionHost) ? item.CollectionHost.Trim().ToLowerInvariant() : "server";

            if (existingByKey.TryGetValue(item.Key, out var existing))
            {
                var changed = false;
                if (existing.Name != item.Name) { existing.Name = item.Name; changed = true; }
                if (existing.BaseUrl != item.BaseUrl) { existing.BaseUrl = item.BaseUrl; changed = true; }
                if (existing.Platform != platform) { existing.Platform = platform; changed = true; }
                if (existing.IsReference != item.IsReference) { existing.IsReference = item.IsReference; changed = true; }
                if (existing.MinDelayMs != item.MinDelayMs) { existing.MinDelayMs = item.MinDelayMs; changed = true; }
                if (existing.CollectionHost != targetHost) { existing.CollectionHost = targetHost; changed = true; }

                if (changed) updated++;
            }
            else
            {
                context.Competitors.Add(new Competitor
                {
                    Key = item.Key,
                    Name = item.Name,
                    BaseUrl = item.BaseUrl,
                    Platform = platform,
                    IsReference = item.IsReference,
                    Enabled = true,
                    MinDelayMs = item.MinDelayMs > 0 ? item.MinDelayMs : 3000,
                    MaxConcurrency = 1,
                    CollectionHost = targetHost
                });
                added++;
            }
        }

        if (added > 0 || updated > 0)
        {
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Seeded competitors from {Path}: {Added} added, {Updated} updated.", filePath, added, updated);
        }

        // Seed Meta advertiser identities idempotently with status PendingApproval
        var existingIdentities = await context.AdvertiserIdentities
            .Where(i => i.Platform == AdPlatform.Meta)
            .ToListAsync(ct);
        var existingMetaById = existingIdentities
            .ToDictionary(i => i.ExternalId, StringComparer.OrdinalIgnoreCase);

        var allCompetitors = await context.Competitors.ToListAsync(ct);
        var allCompetitorsByKey = allCompetitors.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

        var identitiesAdded = 0;
        foreach (var item in items)
        {
            if (item.MetaPageIds == null || item.MetaPageIds.Count == 0)
                continue;

            if (!allCompetitorsByKey.TryGetValue(item.Key, out var comp))
                continue;

            foreach (var pageId in item.MetaPageIds)
            {
                var cleanPageId = pageId.Trim();
                if (string.IsNullOrWhiteSpace(cleanPageId))
                    continue;

                if (existingMetaById.TryGetValue(cleanPageId, out var existing))
                {
                    if (existing.CompetitorId != comp.Id)
                    {
                        existing.CompetitorId = comp.Id;
                        identitiesAdded++;
                    }
                    if (existing.Status == AdvertiserIdentityStatus.Pending)
                    {
                        existing.Status = AdvertiserIdentityStatus.Approved;
                        identitiesAdded++;
                    }
                }
                else
                {
                    var newIdentity = new AdvertiserIdentity
                    {
                        CompetitorId = comp.Id,
                        Platform = AdPlatform.Meta,
                        ExternalId = cleanPageId,
                        Name = comp.Name,
                        Status = AdvertiserIdentityStatus.Approved,
                        LibraryUrl = $"https://www.facebook.com/ads/library/?active_status=all&ad_type=all&country=EG&view_all_page_id={cleanPageId}",
                        Notes = "Seeded from competitors.json"
                    };
                    context.AdvertiserIdentities.Add(newIdentity);
                    existingMetaById[cleanPageId] = newIdentity;
                    identitiesAdded++;
                }
            }
        }

        // Our own Meta pages: seeded as Pending, so an admin approves them before they are collected.
        // An identity that already exists keeps its status.
        foreach (var item in items)
        {
            if (item.PendingMetaPageIds == null || item.PendingMetaPageIds.Count == 0)
                continue;

            if (!allCompetitorsByKey.TryGetValue(item.Key, out var comp))
                continue;

            foreach (var pageId in item.PendingMetaPageIds)
            {
                var cleanPageId = pageId.Trim();
                if (string.IsNullOrWhiteSpace(cleanPageId) || existingMetaById.ContainsKey(cleanPageId))
                    continue;

                var pending = new AdvertiserIdentity
                {
                    CompetitorId = comp.Id,
                    Platform = AdPlatform.Meta,
                    ExternalId = cleanPageId,
                    Name = comp.Name,
                    Status = AdvertiserIdentityStatus.Pending,
                    LibraryUrl = $"https://www.facebook.com/ads/library/?active_status=all&ad_type=all&country=EG&view_all_page_id={cleanPageId}",
                    Notes = "Seeded from competitors.json; awaiting approval"
                };
                context.AdvertiserIdentities.Add(pending);
                existingMetaById[cleanPageId] = pending;
                identitiesAdded++;
            }
        }

        if (identitiesAdded > 0)
        {
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Seeded or approved {Count} Meta advertiser identities.", identitiesAdded);
        }

        await FixSeedMatchesAndCategoriesAsync(context, logger, ct);
    }

    private static async Task FixSeedMatchesAndCategoriesAsync(LazyBeanDbContext context, ILogger logger, CancellationToken ct)
    {
        // 1. Reclassify L'or capsules from CapsulesOther to CapsulesNespressoOriginal
        var lorCaps = await context.Products
            .Where(p => p.CanonicalCategory == CanonicalCategory.CapsulesOther &&
                        (p.Title.Contains("L'OR") || p.Title.Contains("L'or") || p.Title.Contains("Lor") || p.Title.Contains("لور")) &&
                        (p.Title.Contains("Capsule") || p.Title.Contains("capsule") || p.Title.Contains("كبسول") || p.Title.Contains("Pod") || p.Title.Contains("pod")))
            .ToListAsync(ct);

        if (lorCaps.Count > 0)
        {
            foreach (var p in lorCaps)
            {
                p.CanonicalCategory = CanonicalCategory.CapsulesNespressoOriginal;
            }
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Reclassified {Count} L'or capsule products to CapsulesNespressoOriginal.", lorCaps.Count);
        }

        // 2. Ensure product match exists for L'or Ristretto 11
        var compVarId = Guid.Parse("16e6d6af-3de2-4ffa-8c22-6dddbe3f3dd9");
        var refVarId = Guid.Parse("7a740c7a-1f40-4835-8df4-5ff37610f693");

        var compVar = await context.Variants.FirstOrDefaultAsync(v => v.Id == compVarId, ct);
        var refVar = await context.Variants.FirstOrDefaultAsync(v => v.Id == refVarId, ct);

        if (compVar != null && refVar != null)
        {
            var match = await context.ProductMatches.FirstOrDefaultAsync(m => m.CompetitorVariantId == compVarId && m.ReferenceVariantId == refVarId, ct);
            if (match == null)
            {
                context.ProductMatches.Add(new ProductMatch
                {
                    CompetitorVariantId = compVarId,
                    ReferenceVariantId = refVarId,
                    Score = 0.98m,
                    Method = MatchMethod.Manual,
                    Status = MatchStatus.Approved,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await context.SaveChangesAsync(ct);
                logger.LogInformation("Seeded approved product match for L'or Ristretto 11 between competitor variant {CompId} and Lazy Bean variant {RefId}.", compVarId, refVarId);
            }
            else if (match.Status != MatchStatus.Approved && match.Status != MatchStatus.Auto)
            {
                match.Status = MatchStatus.Approved;
                await context.SaveChangesAsync(ct);
            }
        }
    }

    public static Task SeedReferenceCompetitorAsync(IServiceProvider services, CancellationToken ct = default)
        => SeedCompetitorsAsync(services, ct);

    public static async Task RecoverInterruptedRunsAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
        var matchingQueue = scope.ServiceProvider.GetService<LazyBean.Application.Matching.IMatchingQueue>();
        var runQueue = scope.ServiceProvider.GetService<LazyBean.Application.Collection.Orchestration.IRunQueue>();
        var config = scope.ServiceProvider.GetService<IConfiguration>();
        var autoResume = config?.GetValue("Collection:AutoResumeInterruptedRuns", true) ?? true;

        // Find runs that were running or interrupted during server shutdown
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var candidateRuns = await context.CollectionRuns
            .Where(r => r.Status == CollectionRunStatus.Running || 
                        (r.Status == CollectionRunStatus.Interrupted && r.CreatedAt >= cutoff) ||
                        (r.PhasesJson != null && r.PhasesJson.Contains("\"Running\"")))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var runsToResumeMatching = new List<Guid>();
        Guid? runToResumeCollection = null;

        if (candidateRuns.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            var isFirstCandidate = true;

            foreach (var run in candidateRuns)
            {
                var matchingWasInterrupted = false;

                if (!string.IsNullOrWhiteSpace(run.PhasesJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(run.PhasesJson);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            var updatedList = new List<Dictionary<string, object?>>();
                            foreach (var el in doc.RootElement.EnumerateArray())
                            {
                                var dict = new Dictionary<string, object?>();
                                foreach (var prop in el.EnumerateObject())
                                {
                                    dict[prop.Name] = prop.Value.ValueKind switch
                                    {
                                        JsonValueKind.String => prop.Value.GetString(),
                                        JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? i : (object?)prop.Value.GetDouble(),
                                        JsonValueKind.True => true,
                                        JsonValueKind.False => false,
                                        JsonValueKind.Array when prop.Name == "completedCategories" => prop.Value.EnumerateArray().Select(x => x.GetString()).Where(x => x != null).ToList(),
                                        _ => null
                                    };
                                }

                                if (string.Equals(dict.GetValueOrDefault("status")?.ToString(), "Running", StringComparison.OrdinalIgnoreCase))
                                {
                                    dict["status"] = "interrupted";
                                    dict["message"] = "Interrupted by server restart";
                                    var phaseName = dict.GetValueOrDefault("name")?.ToString();
                                    if (string.Equals(phaseName, "matching", StringComparison.OrdinalIgnoreCase))
                                    {
                                        matchingWasInterrupted = true;
                                    }
                                }

                                updatedList.Add(dict);
                            }

                            run.PhasesJson = JsonSerializer.Serialize(updatedList);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to parse/update PhasesJson for run {RunId} during recovery.", run.Id);
                    }
                }

                var hasUnfinishedCompetitors = await context.CompetitorRuns
                    .AnyAsync(cr => cr.RunId == run.Id && (cr.Status == CompetitorRunStatus.Running || cr.Status == CompetitorRunStatus.Queued || cr.Status == CompetitorRunStatus.Interrupted), ct);

                if (matchingWasInterrupted && !hasUnfinishedCompetitors && matchingQueue != null)
                {
                    run.Status = CollectionRunStatus.Running;
                    runsToResumeMatching.Add(run.Id);
                    logger.LogInformation("Orphaned matching phase found for CollectionRun {RunId}. Enqueued to resume matching.", run.Id);
                }
                else if (run.Status == CollectionRunStatus.Running)
                {
                    run.Status = CollectionRunStatus.Interrupted;
                    run.FinishedAt = now;
                    logger.LogWarning("Marked interrupted CollectionRun {RunId} as Interrupted (reason: server restart).", run.Id);

                    if (isFirstCandidate && hasUnfinishedCompetitors && autoResume && runQueue != null)
                    {
                        runToResumeCollection = run.Id;
                        logger.LogInformation("Auto-resuming interrupted CollectionRun {RunId} on startup.", run.Id);
                    }
                }
                else if (isFirstCandidate && run.Status == CollectionRunStatus.Interrupted && hasUnfinishedCompetitors && autoResume && runQueue != null)
                {
                    runToResumeCollection = run.Id;
                    logger.LogInformation("Auto-resuming previously interrupted CollectionRun {RunId} on startup.", run.Id);
                }

                isFirstCandidate = false;
            }
        }

        var runningCompetitorRuns = await context.CompetitorRuns
            .Where(cr => cr.Status == CompetitorRunStatus.Running)
            .ToListAsync(ct);

        if (runningCompetitorRuns.Count > 0)
        {
            foreach (var cr in runningCompetitorRuns)
            {
                cr.Status = CompetitorRunStatus.Interrupted;
                cr.Errors += 1;
                cr.Coverage = RunCoverage.Partial;
                logger.LogWarning(
                    "Marked interrupted CompetitorRun {CompetitorRunId} (Run {RunId}, Competitor {CompetitorId}) as Interrupted (reason: server restart).",
                    cr.Id, cr.RunId, cr.CompetitorId);
            }
        }

        if (candidateRuns.Count > 0 || runningCompetitorRuns.Count > 0)
        {
            await context.SaveChangesAsync(ct);
        }

        if (runToResumeCollection.HasValue && runQueue != null)
        {
            logger.LogInformation("Enqueuing resumed CollectionRun {RunId} to IRunQueue.", runToResumeCollection.Value);
            await runQueue.EnqueueAsync(runToResumeCollection.Value, CancellationToken.None);
        }

        if (matchingQueue != null)
        {
            foreach (var runId in runsToResumeMatching)
            {
                await matchingQueue.EnqueueAsync(new LazyBean.Application.Matching.MatchingJob(runId), CancellationToken.None);
            }
        }
    }
}