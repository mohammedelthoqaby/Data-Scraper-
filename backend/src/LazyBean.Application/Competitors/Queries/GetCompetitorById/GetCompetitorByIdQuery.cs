using LazyBean.Application.Competitors.Dtos;
using MediatR;

namespace LazyBean.Application.Competitors.Queries.GetCompetitorById;

public record GetCompetitorByIdQuery(Guid Id) : IRequest<CompetitorDto>;
