using MediatR;

namespace LazyBean.Application.Competitors.Commands.DeleteCompetitor;

public record DeleteCompetitorCommand(Guid Id) : IRequest;
