namespace LazyBean.Application.Collection.Contracts;

public record IngestBatchResult(
    int ProductsSaved,
    int VariantsSaved,
    int PriceObservationsRecorded,
    int ChangeEventsRecorded);

public record CompleteCompetitorResult(
    string Status,
    int TotalProducts,
    int TotalPages,
    string? Message);

public record IngestProductsBatchRequest(
    int Page,
    int? TotalPages,
    IReadOnlyList<RawProduct> Products);

public record CompleteCompetitorRunRequest(
    string Status,
    int TotalPages,
    int TotalProducts,
    string? Error);

public interface ICompetitorProductIngestionService
{
    Task<IngestBatchResult> IngestBatchAsync(
        Guid runId,
        string competitorSlug,
        int pageNumber,
        int? totalPages,
        IReadOnlyList<RawProduct> products,
        CancellationToken ct = default);

    Task<CompleteCompetitorResult> CompleteCompetitorAsync(
        Guid runId,
        string competitorSlug,
        string status,
        int totalPages,
        int totalProducts,
        string? error = null,
        CancellationToken ct = default);
}
