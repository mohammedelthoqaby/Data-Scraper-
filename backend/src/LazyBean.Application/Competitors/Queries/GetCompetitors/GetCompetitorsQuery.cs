using LazyBean.Application.Competitors.Dtos;
using MediatR;

namespace LazyBean.Application.Competitors.Queries.GetCompetitors;

public record GetCompetitorsQuery : IRequest<IReadOnlyList<CompetitorDto>>;
