namespace LazyBean.Application.Collection.Orchestration;

// Ambient run id for the duration of one competitor's fetch, so ICollectorHttpClient can
// tag the evidence it saves without ICompetitorAdapter.FetchAsync needing a runId
// parameter (its contract is competitor + CancellationToken only).
public static class CollectionRunContext
{
    private static readonly AsyncLocal<Guid?> RunId = new();
    private static readonly AsyncLocal<PageCounter?> Pages = new();
    private static readonly AsyncLocal<DegradedState?> Degraded = new();

    public static Guid? CurrentRunId => RunId.Value;

    // Adapters call IncrementPageCount() once per top-level listing page they fetch (not
    // per sub-request, e.g. WooCommerce variation detail lookups don't count) so the
    // executor can record CompetitorRun.Pages without the adapter contract exposing it.
    public static int CurrentPageCount => Pages.Value?.Count ?? 0;
    public static int? TotalPages => Pages.Value?.Total;
    public static string? StatusMessage => Pages.Value?.Message;

    public static bool IsDegraded => Degraded.Value?.IsDegraded ?? false;
    public static string? DegradedReason => Degraded.Value?.Reason;

    public static void IncrementPageCount()
    {
        if (Pages.Value is { } counter)
        {
            counter.Count++;
        }
    }

    public static void SetTotalPages(int total)
    {
        if (Pages.Value is { } counter)
        {
            counter.Total = total;
        }
    }

    public static void SetStatusMessage(string message)
    {
        if (Pages.Value is { } counter)
        {
            counter.Message = message;
        }
    }

    public static void MarkDegraded(string reason)
    {
        if (Degraded.Value is { } state)
        {
            state.IsDegraded = true;
            state.Reason = reason;
        }
    }

    // One scope per competitor's FetchAsync call - AsyncLocal isolates it from sibling
    // competitors running concurrently in the same CollectionRun.
    public static IDisposable Begin(Guid runId)
    {
        var previousRunId = RunId.Value;
        var previousPages = Pages.Value;
        var previousDegraded = Degraded.Value;
        RunId.Value = runId;
        Pages.Value = new PageCounter();
        Degraded.Value = new DegradedState();
        return new Scope(previousRunId, previousPages, previousDegraded);
    }

    private sealed class PageCounter
    {
        public int Count;
        public int? Total;
        public string? Message;
    }

    private sealed class DegradedState
    {
        public bool IsDegraded;
        public string? Reason;
    }

    private sealed class Scope(Guid? previousRunId, PageCounter? previousPages, DegradedState? previousDegraded) : IDisposable
    {
        public void Dispose()
        {
            RunId.Value = previousRunId;
            Pages.Value = previousPages;
            Degraded.Value = previousDegraded;
        }
    }
}
