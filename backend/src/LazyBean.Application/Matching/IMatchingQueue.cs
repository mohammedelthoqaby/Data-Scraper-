namespace LazyBean.Application.Matching;

public record MatchingJob(
    Guid? RunId = null,
    TaskCompletionSource<MatchingResult>? Completion = null);

public interface IMatchingQueue
{
    ValueTask EnqueueAsync(MatchingJob job, CancellationToken ct = default);
    IAsyncEnumerable<MatchingJob> ReadAllAsync(CancellationToken ct);
}
