using MediatR;

namespace LazyBean.Application.Runs.Commands.CancelRun;

public record CancelRunResultDto(Guid RunId, string State);

public record CancelRunCommand(Guid RunId) : IRequest<CancelRunResultDto>;
