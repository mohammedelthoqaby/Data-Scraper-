using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Domain.Entities;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LazyBean.Infrastructure.Collection.Evidence;

// Writes the raw response body gzip'd to EvidenceStorage:Path (a volume in production,
// a local folder in dev) and records one FetchedResource row pointing at it.
//
// Takes a DbContext factory, not an injected DbContext: this is called concurrently by
// every competitor's CollectorHttpClient during a run (competitors run in parallel), and
// a single DbContext instance can't be used from multiple threads at once.
public class GzipEvidenceStore(IDbContextFactory<LazyBeanDbContext> dbContextFactory, IConfiguration configuration)
    : IEvidenceStore
{
    public async Task SaveAsync(
        Guid runId, Guid competitorId, string url, int httpStatus, string rawBody, CancellationToken ct)
    {
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();

        var storageRoot = configuration["EvidenceStorage:Path"] ?? "data/evidence";
        Directory.CreateDirectory(storageRoot);
        PurgeOldEvidence(storageRoot, 14);
        var storagePath = Path.Combine(storageRoot, $"{contentHash}.json.gz");

        if (!File.Exists(storagePath))
        {
            await using var fileStream = File.Create(storagePath);
            await using var gzip = new GZipStream(fileStream, CompressionLevel.Optimal);
            await using var writer = new StreamWriter(gzip, Encoding.UTF8);
            await writer.WriteAsync(rawBody);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        dbContext.FetchedResources.Add(new FetchedResource
        {
            RunId = runId,
            CompetitorId = competitorId,
            Url = url,
            HttpStatus = httpStatus,
            FetchedAt = DateTimeOffset.UtcNow,
            ContentHash = contentHash,
            StoragePath = storagePath
        });

        await dbContext.SaveChangesAsync(ct);
    }

    private static void PurgeOldEvidence(string storageRoot, int maxAgeDays)
    {
        try
        {
            if (!Directory.Exists(storageRoot)) return;
            var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
            foreach (var file in Directory.EnumerateFiles(storageRoot, "*.json.gz"))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTimeUtc < cutoff)
                {
                    try { fileInfo.Delete(); } catch { }
                }
            }
        }
        catch
        {
            // Ignore purge failures
        }
    }
}
