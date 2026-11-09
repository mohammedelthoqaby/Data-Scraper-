using LazyBean.Application.Ai.Dtos;

namespace LazyBean.Application.Common.Interfaces;

/// <summary>
/// The AI abstraction used across the app. Implementations route each call to the
/// primary provider and fall back to the other one on quota, rate-limit, timeout or
/// server errors. Throws <see cref="LazyBean.Application.Ai.AiUnavailableException"/>
/// when no provider can serve the call.
/// </summary>
public interface IAiClient
{
    /// <summary>Generic JSON-mode completion, reusable by any feature (triage, attention, product matching).</summary>
    Task<AiCompletionResult> CompleteJsonAsync(AiJsonRequest request, CancellationToken ct = default);

    /// <summary>Tiny real request against one provider. Never returns the key.</summary>
    Task<AiKeyTestResultDto> TestProviderKeyAsync(string provider, string? apiKeyOverride, string? modelOverride, CancellationToken ct = default);

    Task<IReadOnlyList<AiReviewTriageResult>> TriageReviewBatchAsync(IReadOnlyList<AiReviewTriagePairRequest> batch, CancellationToken ct = default);

    Task<IReadOnlyList<RankedAttentionItem>> RankAndPhraseAttentionItemsAsync(IReadOnlyList<AttentionFactItem> facts, CancellationToken ct = default);
}
