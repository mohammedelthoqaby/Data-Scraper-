using LazyBean.Application.Ai.Dtos;

namespace LazyBean.Application.Common.Interfaces;

public interface IAiClient
{
    Task<AiTestResultDto> TestConnectionAsync(string? apiKeyOverride, string? modelOverride, CancellationToken ct = default);
    Task<IReadOnlyList<AiReviewTriageResult>> TriageReviewBatchAsync(IReadOnlyList<AiReviewTriagePairRequest> batch, CancellationToken ct = default);
    Task<IReadOnlyList<RankedAttentionItem>> RankAndPhraseAttentionItemsAsync(IReadOnlyList<AttentionFactItem> facts, CancellationToken ct = default);
}
