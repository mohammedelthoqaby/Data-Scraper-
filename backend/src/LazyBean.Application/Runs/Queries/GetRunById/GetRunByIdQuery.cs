using LazyBean.Application.Runs.Dtos;
using MediatR;

namespace LazyBean.Application.Runs.Queries.GetRunById;

public record GetRunByIdQuery(Guid Id) : IRequest<RunDto>;
