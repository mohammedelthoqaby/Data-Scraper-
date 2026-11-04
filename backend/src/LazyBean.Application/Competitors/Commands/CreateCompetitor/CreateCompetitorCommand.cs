using LazyBean.Application.Competitors.Dtos;
using LazyBean.Domain.Enums;
using MediatR;

namespace LazyBean.Application.Competitors.Commands.CreateCompetitor;

public record CreateCompetitorCommand(
    string Key,
    string Name,
    string BaseUrl,
    CompetitorPlatform Platform,
    bool IsReference) : IRequest<CompetitorDto>;
