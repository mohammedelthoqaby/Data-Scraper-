using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Competitors.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Competitors.Queries.GetCompetitorById;

public class GetCompetitorByIdQueryHandler(IReadDbContext readContext, ICacheService cache)
    : IRequestHandler<GetCompetitorByIdQuery, CompetitorDto>
{
    public async Task<CompetitorDto> Handle(GetCompetitorByIdQuery request, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            $"competitors:{request.Id}",
            async token => await readContext.Query<Competitor>()
                .Where(c => c.Id == request.Id)
                .Select(c => new CompetitorDto(
                    c.Id, c.Key, c.Name, c.BaseUrl, c.Platform.ToString(), c.IsReference, c.Enabled, c.MinDelayMs, c.MaxConcurrency))
                .SingleOrDefaultAsync(token)
                ?? throw new NotFoundException(nameof(Competitor), request.Id),
            TimeSpan.FromMinutes(5),
            tags: ["competitors"],
            ct);
    }
}
