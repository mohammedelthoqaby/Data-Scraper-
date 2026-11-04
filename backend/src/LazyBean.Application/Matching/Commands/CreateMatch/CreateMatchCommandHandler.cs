using FluentValidation.Results;
using LazyBean.Application.Common.Exceptions;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Matching.Commands.CreateMatch;

public class CreateMatchCommandHandler(
    IUnitOfWork unitOfWork,
    IReadDbContext readContext,
    ICacheService cache)
    : IRequestHandler<CreateMatchCommand, MatchResultDto>
{
    public async Task<MatchResultDto> Handle(CreateMatchCommand request, CancellationToken ct)
    {
        var compVarId = request.CompetitorVariantId;
        var refVarId = request.ReferenceVariantId;

        if (compVarId is null && request.CompetitorProductId.HasValue)
        {
            var v = await readContext.Query<Variant>()
                .FirstOrDefaultAsync(v => v.ProductId == request.CompetitorProductId.Value, ct);
            compVarId = v?.Id;
        }

        if (refVarId is null && request.ReferenceProductId.HasValue)
        {
            var v = await readContext.Query<Variant>()
                .FirstOrDefaultAsync(v => v.ProductId == request.ReferenceProductId.Value, ct);
            refVarId = v?.Id;
        }

        if (compVarId is null || refVarId is null)
        {
            throw new ValidationException([new ValidationFailure("Ids", "Both competitor and reference variant or product IDs must be supplied.")]);
        }

        var existing = await readContext.Query<ProductMatch>()
            .FirstOrDefaultAsync(m => m.CompetitorVariantId == compVarId.Value && m.ReferenceVariantId == refVarId.Value, ct);

        if (existing is not null)
        {
            existing.Status = request.Approve ? MatchStatus.Approved : MatchStatus.Rejected;
            existing.Method = MatchMethod.Manual;
            existing.Score = 1.0m;
            unitOfWork.ProductMatches.Update(existing);
            await unitOfWork.SaveChangesAsync(ct);
            await cache.RemoveByTagAsync("products", ct);

            return new MatchResultDto(existing.Id, existing.CompetitorVariantId, existing.ReferenceVariantId, existing.Status.ToString().ToLowerInvariant());
        }

        var newMatch = new ProductMatch
        {
            CompetitorVariantId = compVarId.Value,
            ReferenceVariantId = refVarId.Value,
            Score = 1.0m,
            Method = MatchMethod.Manual,
            Status = request.Approve ? MatchStatus.Approved : MatchStatus.Rejected
        };

        await unitOfWork.ProductMatches.AddAsync(newMatch, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync("products", ct);

        return new MatchResultDto(newMatch.Id, newMatch.CompetitorVariantId, newMatch.ReferenceVariantId, newMatch.Status.ToString().ToLowerInvariant());
    }
}
