using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Competitors.Dtos;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;

namespace LazyBean.Application.Competitors.Commands.UpdateCompetitor;

public class UpdateCompetitorCommandHandler(IUnitOfWork unitOfWork, ICacheService cache)
    : IRequestHandler<UpdateCompetitorCommand, CompetitorDto>
{
    public async Task<CompetitorDto> Handle(UpdateCompetitorCommand request, CancellationToken ct)
    {
        var competitor = await unitOfWork.Competitors.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Competitor), request.Id);

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            competitor.Rename(request.Name);
        }

        if (!string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            competitor.BaseUrl = request.BaseUrl;
        }

        if (request.Enabled.HasValue)
        {
            competitor.SetEnabled(request.Enabled.Value);
        }

        if (request.MinDelayMs.HasValue && request.MinDelayMs.Value >= 0)
        {
            competitor.MinDelayMs = request.MinDelayMs.Value;
        }

        if (request.MaxConcurrency.HasValue && request.MaxConcurrency.Value > 0)
        {
            competitor.MaxConcurrency = request.MaxConcurrency.Value;
        }

        competitor.UpdatedAt = DateTimeOffset.UtcNow;

        unitOfWork.Competitors.Update(competitor);
        await unitOfWork.SaveChangesAsync(ct);

        await cache.RemoveByTagAsync("competitors", ct);

        return new CompetitorDto(
            competitor.Id,
            competitor.Key,
            competitor.Name,
            competitor.BaseUrl,
            competitor.Platform.ToString(),
            competitor.IsReference,
            competitor.Enabled,
            competitor.MinDelayMs,
            competitor.MaxConcurrency);
    }
}
