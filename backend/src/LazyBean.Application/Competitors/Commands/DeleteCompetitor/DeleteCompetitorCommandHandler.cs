using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;

namespace LazyBean.Application.Competitors.Commands.DeleteCompetitor;

public class DeleteCompetitorCommandHandler(IUnitOfWork unitOfWork, ICacheService cache)
    : IRequestHandler<DeleteCompetitorCommand>
{
    public async Task Handle(DeleteCompetitorCommand request, CancellationToken ct)
    {
        var competitor = await unitOfWork.Competitors.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Competitor), request.Id);

        unitOfWork.Competitors.Remove(competitor);
        await unitOfWork.SaveChangesAsync(ct);

        await cache.RemoveByTagAsync("competitors", ct);
    }
}
