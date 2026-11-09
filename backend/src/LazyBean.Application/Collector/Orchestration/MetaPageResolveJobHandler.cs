using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Collector.Ports;

namespace LazyBean.Application.Collector.Orchestration;

// Resolves a Facebook page name to its numeric ID. Fails the job when no ID is found, so nothing is added.
public sealed class MetaPageResolveJobHandler(IFacebookPageIdLookup pages, ICollectionLog log, ICollectionProgress state)
{
    public async Task<JobOutcome> HandleAsync(CollectorJobDto job, CancellationToken ct)
    {
        var name = job.PageSlug ?? throw new InvalidOperationException("The lookup has no Facebook page name.");
        state.UpdateJob($"Looking up the Facebook page {name}", 0, 1);
        log.Info($"Looking up the Facebook page {name} for its page ID.");

        var pageId = await pages.FindPageIdAsync(name, ct);
        if (pageId is null)
        {
            throw new InvalidOperationException(
                $"No page ID was found on the public page for {name}. Check that the page is public, or paste its numeric ID.");
        }

        return new JobOutcome($"Found page ID {pageId} for {name}.", [new CollectorPageReport(pageId, "Resolved", 0, null)]);
    }
}
