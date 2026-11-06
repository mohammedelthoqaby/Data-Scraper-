using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Runtime;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;

namespace LazyBean.Collector.App.Jobs;

// Collects one WooCommerce store and sends it to the server in batches, then completes the competitor.
// Products are never marked Removed here: the server decides that from a complete, healthy run only.
public sealed class StoreJobHandler(
    ICollectorApi api,
    ICompetitorAdapter adapter,
    ActivityLog log,
    CollectorState state,
    int batchSize = 50)
{
    public async Task<JobOutcome> HandleAsync(CollectorJobDto job, CancellationToken ct)
    {
        var target = job.Competitor ?? throw new InvalidOperationException("The job names no competitor.");
        var runId = job.RunId ?? throw new InvalidOperationException("The job belongs to no run.");

        if (adapter.Platform != CompetitorPlatform.WooCommerce ||
            !string.Equals(target.Platform, nameof(CompetitorPlatform.WooCommerce), StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"This app cannot collect {target.Name} ({target.Platform}) yet.");
        }

        var competitor = new Competitor
        {
            Id = target.Id,
            Key = target.Slug,
            Name = target.Name,
            BaseUrl = target.BaseUrl,
            Platform = CompetitorPlatform.WooCommerce,
            CollectionHost = "desktop",
            Enabled = true,
            MinDelayMs = Math.Max(500, target.MinDelayMs),
            MaxConcurrency = Math.Max(1, target.MaxConcurrency),
        };

        log.Info($"Collecting {target.Name} from {target.BaseUrl}.");
        using var runScope = CollectionRunContext.Begin(runId);

        var batch = new List<RawProduct>(batchSize);
        var total = 0;
        var batchNumber = 0;
        state.UpdateJob("Reading the catalog…", 0, null);

        try
        {
            await foreach (var raw in adapter.FetchAsync(competitor, ct))
            {
                total++;
                batch.Add(raw);
                if (batch.Count >= batchSize)
                {
                    batchNumber++;
                    await SendBatchAsync(runId, target.Slug, batchNumber, batch, ct);
                    batch.Clear();
                    state.UpdateJob($"{total} products read", total, null);
                }
            }

            if (batch.Count > 0)
            {
                batchNumber++;
                await SendBatchAsync(runId, target.Slug, batchNumber, batch, ct);
                batch.Clear();
            }

            if (CollectionRunContext.IsDegraded)
            {
                // The adapter stopped early (timeout, blocked page). Report the run as failed so the server
                // keeps the previous catalog instead of treating the missing products as removed.
                var reason = CollectionRunContext.DegradedReason ?? "the catalog was only partly read";
                await api.CompleteCompetitorAsync(
                    runId,
                    target.Slug,
                    new CompleteCompetitorRunRequest("Failed", Math.Max(1, batchNumber), total, $"Degraded: {reason}"),
                    ct);
                log.Warn($"{target.Name}: incomplete ({reason}). Products were not changed.");
                return new JobOutcome($"{target.Name}: incomplete ({reason}). Products were not changed.");
            }

            var totalPages = CollectionRunContext.TotalPages ?? Math.Max(1, batchNumber);
            await api.CompleteCompetitorAsync(
                runId,
                target.Slug,
                new CompleteCompetitorRunRequest("Completed", totalPages, total, null),
                ct);

            log.Info($"{target.Name}: {total} products sent in {batchNumber} batch(es).");
            return new JobOutcome($"{target.Name}: {total} products.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not CollectorApiException)
        {
            await ReportFailureAsync(runId, target, batchNumber, total, ex.Message);
            throw;
        }
        catch (CollectorApiException ex) when (ex.Kind is not ApiErrorKind.Conflict and not ApiErrorKind.Unauthorized)
        {
            await ReportFailureAsync(runId, target, batchNumber, total, ex.Message);
            throw;
        }
    }

    private Task SendBatchAsync(Guid runId, string slug, int batchNumber, List<RawProduct> batch, CancellationToken ct) =>
        api.IngestProductsAsync(runId, slug, new IngestProductsBatchRequest(batchNumber, null, batch.ToList()), ct);

    private async Task ReportFailureAsync(Guid runId, CollectorJobCompetitorDto target, int batchNumber, int total, string message)
    {
        log.Error($"{target.Name} failed: {message}");
        try
        {
            await api.CompleteCompetitorAsync(
                runId,
                target.Slug,
                new CompleteCompetitorRunRequest("Failed", Math.Max(1, batchNumber), total, message),
                CancellationToken.None);
        }
        catch (CollectorApiException)
        {
            // The server is unreachable; the job itself is failed by the runner, and the run times out.
        }
    }

}
