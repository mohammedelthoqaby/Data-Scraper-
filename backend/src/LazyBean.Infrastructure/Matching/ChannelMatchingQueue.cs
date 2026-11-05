using System.Threading.Channels;
using LazyBean.Application.Matching;

namespace LazyBean.Infrastructure.Matching;

public class ChannelMatchingQueue : IMatchingQueue
{
    private readonly Channel<MatchingJob> _channel = Channel.CreateUnbounded<MatchingJob>(new UnboundedChannelOptions
    {
        SingleReader = true
    });

    public ValueTask EnqueueAsync(MatchingJob job, CancellationToken ct = default)
    {
        return _channel.Writer.WriteAsync(job, ct);
    }

    public IAsyncEnumerable<MatchingJob> ReadAllAsync(CancellationToken ct)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}
