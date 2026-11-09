using LazyBean.Infrastructure.Ai.Providers;
using LazyBean.Infrastructure.Ai.RateLimiting;
using Xunit;
using static LazyBean.Application.Tests.Ai.AiTestSupport;

namespace LazyBean.Application.Tests.Ai;

/// <summary>Token-bucket behaviour for the free-tier limits (requests and estimated tokens).</summary>
public class AiRateGateTests
{
    [Fact]
    public async Task TryAcquire_WhenRequestBudgetIsSpent_ReturnsFalseWhenNoWaitIsAllowed()
    {
        var gate = new AiRateGate(CreateOptions(groqRequestsPerMinute: 2));

        Assert.True(await gate.TryAcquireAsync("groq", 10, TimeSpan.Zero, CancellationToken.None));
        Assert.True(await gate.TryAcquireAsync("groq", 10, TimeSpan.Zero, CancellationToken.None));
        Assert.False(await gate.TryAcquireAsync("groq", 10, TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task TryAcquire_WhenTokenBudgetIsInsufficient_ReturnsFalse()
    {
        // 100 tokens per minute: the first 80-token call fits, the second does not until the bucket refills.
        var gate = new AiRateGate(CreateOptions(groqTokensPerMinute: 100));

        Assert.True(await gate.TryAcquireAsync("groq", 80, TimeSpan.Zero, CancellationToken.None));
        Assert.False(await gate.TryAcquireAsync("groq", 80, TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task Settle_RefundsTheDifferenceBetweenEstimateAndActualUsage()
    {
        var gate = new AiRateGate(CreateOptions(groqTokensPerMinute: 100, groqRequestsPerMinute: 60));

        Assert.True(await gate.TryAcquireAsync("groq", 100, TimeSpan.Zero, CancellationToken.None));
        Assert.False(await gate.TryAcquireAsync("groq", 50, TimeSpan.Zero, CancellationToken.None));

        // The call really used only 10 tokens, so 90 come back and the next 80-token call fits.
        gate.Settle("groq", estimatedTokens: 100, actualTokens: 10);

        Assert.True(await gate.TryAcquireAsync("groq", 80, TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public void Block_KeepsTheProviderUnavailableForTheCooldown()
    {
        var gate = new AiRateGate(CreateOptions());

        gate.Block("groq", TimeSpan.FromMinutes(5));

        Assert.True(gate.IsBlocked("groq", out var remaining));
        Assert.InRange(remaining.TotalMinutes, 4.9, 5.0);
        Assert.False(gate.IsBlocked("gemini", out _));
    }

    [Fact]
    public void ObserveRateLimit_WhenRequestWindowIsExhausted_BlocksUntilReset()
    {
        var gate = new AiRateGate(CreateOptions());

        gate.ObserveRateLimit("groq", new AiRateLimitInfo(
            RemainingRequests: 0,
            ResetRequests: TimeSpan.FromSeconds(120),
            RemainingTokens: null,
            ResetTokens: null));

        Assert.True(gate.IsBlocked("groq", out var remaining));
        Assert.True(remaining > TimeSpan.FromSeconds(100));
    }

    [Fact]
    public void ObserveRateLimit_WhenRemainingTokensAreLow_CapsTheTokenBucket()
    {
        var gate = new AiRateGate(CreateOptions(groqTokensPerMinute: 8000));

        gate.ObserveRateLimit("groq", new AiRateLimitInfo(null, null, RemainingTokens: 0, ResetTokens: TimeSpan.FromSeconds(30)));

        Assert.True(gate.IsBlocked("groq", out _));
    }
}
