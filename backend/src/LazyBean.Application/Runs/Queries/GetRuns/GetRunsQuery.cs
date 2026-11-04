using LazyBean.Application.Runs.Dtos;
using MediatR;

namespace LazyBean.Application.Runs.Queries.GetRuns;

public record GetRunsQuery : IRequest<IReadOnlyList<RunDto>>;
