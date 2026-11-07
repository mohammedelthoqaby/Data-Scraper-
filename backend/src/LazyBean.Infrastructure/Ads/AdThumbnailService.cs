using System.Security.Cryptography;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Ads;

// Stores the images the desktop app uploads for ads. Each image is checked (JPEG, size, width, height) before it is stored.
// An upload replaces the images of its ad, so only the latest set is kept.
public sealed class AdThumbnailService(LazyBeanDbContext db, TimeProvider clock, ILogger<AdThumbnailService> logger) : IAdThumbnailService
{
    private const int MaxSourceKeyLength = 500;

    private sealed record DecodedImage(byte[] Data, int Width, int Height, string Sha256);

    public async Task<AdThumbnailUploadResponse> UploadAsync(AdThumbnailUploadRequest request, CancellationToken ct = default)
    {
        var rejected = new List<AdThumbnailRejection>();
        var items = request.Items.Take(AdThumbnailRules.MaxItemsPerRequest).ToList();
        foreach (var extra in request.Items.Skip(AdThumbnailRules.MaxItemsPerRequest))
        {
            rejected.Add(new AdThumbnailRejection(extra.ExternalAdId, "Too many ads in one request."));
        }

        if (!Enum.TryParse<AdPlatform>(request.Platform, true, out var platform))
        {
            rejected.AddRange(items.Select(i => new AdThumbnailRejection(i.ExternalAdId, "The platform is not known.")));
            return new AdThumbnailUploadResponse(0, 0, rejected);
        }

        var identityId = await db.AdvertiserIdentities
            .AsNoTracking()
            .Where(i => i.Platform == platform && i.ExternalId == request.IdentityExternalId)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(ct);
        if (identityId is null)
        {
            rejected.AddRange(items.Select(i => new AdThumbnailRejection(i.ExternalAdId, "No advertiser or page with this ID.")));
            return new AdThumbnailUploadResponse(0, 0, rejected);
        }

        var externalIds = items.Select(i => i.ExternalAdId).Distinct().ToList();
        var ads = await db.Ads
            .Where(a => a.IdentityId == identityId && a.Platform == platform && externalIds.Contains(a.ExternalAdId))
            .ToListAsync(ct);
        var byExternalId = ads
            .GroupBy(a => a.ExternalAdId)
            .ToDictionary(g => g.Key, g => g.First());

        var storedAds = 0;
        var storedImages = 0;
        var now = clock.GetUtcNow();
        foreach (var item in items)
        {
            if (!byExternalId.TryGetValue(item.ExternalAdId, out var ad))
            {
                rejected.Add(new AdThumbnailRejection(item.ExternalAdId, "No ad with this ID for this advertiser."));
                continue;
            }

            var sourceKey = item.SourceKey?.Trim() ?? string.Empty;
            if (sourceKey.Length == 0 || sourceKey.Length > MaxSourceKeyLength)
            {
                rejected.Add(new AdThumbnailRejection(item.ExternalAdId, "The image source is missing or too long."));
                continue;
            }

            var (images, reason) = Decode(item.ImagesBase64);
            if (reason is not null)
            {
                rejected.Add(new AdThumbnailRejection(item.ExternalAdId, reason));
                continue;
            }

            if (images.Count == 0)
            {
                rejected.Add(new AdThumbnailRejection(item.ExternalAdId, "No images were sent."));
                continue;
            }

            // Removed first, in its own statement, so the new images can take the same positions.
            await db.AdThumbnails.Where(t => t.AdId == ad.Id).ExecuteDeleteAsync(ct);
            for (var position = 0; position < images.Count; position++)
            {
                var image = images[position];
                db.AdThumbnails.Add(new AdThumbnail
                {
                    AdId = ad.Id,
                    Position = position,
                    Data = image.Data,
                    ContentType = "image/jpeg",
                    Width = image.Width,
                    Height = image.Height,
                    Sha256 = image.Sha256,
                    UpdatedAt = now,
                });
            }

            ad.ThumbnailCount = images.Count;
            ad.ThumbnailSourceKey = sourceKey;
            await db.SaveChangesAsync(ct);

            storedAds++;
            storedImages += images.Count;
        }

        var totalBytes = await db.AdThumbnails.SumAsync(t => (long)t.Data.Length, ct);
        logger.LogInformation(
            "Ad images stored for {Platform} '{Identity}': {Ads} ad(s), {Images} image(s), {Rejected} rejected. Images stored in total: {TotalMb:F1} MB.",
            platform, request.IdentityExternalId, storedAds, storedImages, rejected.Count, totalBytes / 1048576.0);

        return new AdThumbnailUploadResponse(storedAds, storedImages, rejected);
    }

    public async Task<AdThumbnailImage?> GetImageAsync(Guid adId, int position, CancellationToken ct = default)
    {
        var row = await db.AdThumbnails
            .AsNoTracking()
            .Where(t => t.AdId == adId && t.Position == position)
            .Select(t => new { t.Data, t.ContentType, t.Sha256, t.UpdatedAt, t.CreatedAt })
            .FirstOrDefaultAsync(ct);

        return row is null ? null : new AdThumbnailImage(row.Data, row.ContentType, row.Sha256, row.UpdatedAt ?? row.CreatedAt);
    }

    public async Task<AdThumbnailRetentionResult> ApplyRetentionAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var cutoff = now.AddDays(-AdThumbnailRules.RetentionDays);
        var staleAdIds = await db.Ads
            .Where(a => a.ThumbnailCount > 0 && a.LastSeenAt < cutoff)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var imagesDeleted = 0;
        if (staleAdIds.Count > 0)
        {
            imagesDeleted = await db.AdThumbnails
                .Where(t => staleAdIds.Contains(t.AdId))
                .ExecuteDeleteAsync(ct);

            // A seen-again ad gets its images read again, because its source is cleared here.
            await db.Ads
                .Where(a => staleAdIds.Contains(a.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.ThumbnailCount, 0)
                    .SetProperty(a => a.ThumbnailSourceKey, (string?)null), ct);
        }

        var bytesStored = await db.AdThumbnails.SumAsync(t => (long)t.Data.Length, ct);
        return new AdThumbnailRetentionResult(staleAdIds.Count, imagesDeleted, bytesStored);
    }

    // Every image must pass. The first one that does not is the reason the whole ad is refused.
    private static (List<DecodedImage> Images, string? Reason) Decode(IReadOnlyList<string> base64Images)
    {
        var images = new List<DecodedImage>();
        foreach (var encoded in base64Images.Take(AdThumbnailRules.MaxImagesPerAd))
        {
            byte[] data;
            try
            {
                data = Convert.FromBase64String(encoded);
            }
            catch (FormatException)
            {
                return ([], "An image is not valid base64.");
            }

            var reason = AdThumbnailRules.Reject(data);
            if (reason is not null)
            {
                return ([], reason);
            }

            JpegDimensions.TryRead(data, out var width, out var height);
            images.Add(new DecodedImage(data, width, height, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()));
        }

        return (images, null);
    }
}
