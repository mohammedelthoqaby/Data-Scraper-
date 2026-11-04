using MediatR;

namespace LazyBean.Application.Matching.Commands.CreateMatch;

public record MatchResultDto(
    Guid Id,
    Guid CompetitorVariantId,
    Guid ReferenceVariantId,
    string Status);

public record CreateMatchCommand(
    Guid? CompetitorVariantId = null,
    Guid? ReferenceVariantId = null,
    Guid? CompetitorProductId = null,
    Guid? ReferenceProductId = null,
    bool Approve = true,
    string? Notes = null) : IRequest<MatchResultDto>;
