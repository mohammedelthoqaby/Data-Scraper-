namespace LazyBean.Application.Collection.Orchestration;

// Runs one CollectionRun end to end: fetch -> normalize -> validate -> persist ->
// removal/coverage rules -> change events -> cache invalidation. Invoked by the
// BackgroundService that drains IRunQueue, one run at a time.
public interface ICollectionRunExecutor
{
    Task ExecuteAsync(Guid runId, CancellationToken ct);
}
