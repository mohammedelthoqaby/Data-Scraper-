using LazyBean.Domain.Enums;
using MediatR;

namespace LazyBean.Application.Runs.Commands.StartRun;

public record StartRunResultDto(Guid RunId, string State, bool AlreadyActive);

public record StartRunCommand(RunTrigger Trigger, IReadOnlyList<Guid>? CompetitorIds = null) : IRequest<StartRunResultDto>;
