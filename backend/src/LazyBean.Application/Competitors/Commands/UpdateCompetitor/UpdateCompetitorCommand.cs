using LazyBean.Application.Competitors.Dtos;
using MediatR;

namespace LazyBean.Application.Competitors.Commands.UpdateCompetitor;

public record UpdateCompetitorCommand(
    Guid Id,
    string? Name = null,
    string? BaseUrl = null,
    bool? Enabled = null,
    int? MinDelayMs = null,
    int? MaxConcurrency = null) : IRequest<CompetitorDto>;
