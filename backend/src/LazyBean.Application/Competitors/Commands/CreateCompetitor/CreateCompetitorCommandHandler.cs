using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Competitors.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Interfaces;
using MediatR;

namespace LazyBean.Application.Competitors.Commands.CreateCompetitor;

public class CreateCompetitorCommandHandler(IUnitOfWork unitOfWork, ICacheService cache)
    : IRequestHandler<CreateCompetitorCommand, CompetitorDto>
{
    public async Task<CompetitorDto> Handle(CreateCompetitorCommand request, CancellationToken ct)
    {
        var competitor = new Competitor
        {
            Key = request.Key,
            Name = request.Name,
            BaseUrl = request.BaseUrl,
            Platform = request.Platform,
            IsReference = request.IsReference,
            Enabled = true
        };

        await unitOfWork.Competitors.AddAsync(competitor, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await cache.RemoveByTagAsync("competitors", ct);

        return new CompetitorDto(
            competitor.Id, competitor.Key, competitor.Name, competitor.BaseUrl,
            competitor.Platform.ToString(), competitor.IsReference, competitor.Enabled);
    }
}
