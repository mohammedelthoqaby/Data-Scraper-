using System.Text.Json;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Matching.Commands.SubmitMatchDecision;

public class SubmitMatchDecisionCommandHandler(
    IUnitOfWork unitOfWork,
    IReadDbContext readContext,
    ICacheService cache)
    : IRequestHandler<SubmitMatchDecisionCommand, DecisionResultDto>
{
    public async Task<DecisionResultDto> Handle(SubmitMatchDecisionCommand request, CancellationToken ct)
    {
        var isApprove = string.Equals(request.Decision, "approve", StringComparison.OrdinalIgnoreCase);

        // Check if ID is a ReviewItem
        var review = await unitOfWork.ReviewItems.GetByIdAsync(request.Id, ct);
        if (review is not null)
        {
            review.Status = isApprove ? ReviewStatus.Approved : ReviewStatus.Rejected;
            unitOfWork.ReviewItems.Update(review);

            if (isApprove)
            {
                // Parse payload and create or update ProductMatch
                try
                {
                    using var doc = JsonDocument.Parse(review.PayloadJson);
                    var compVarId = doc.RootElement.GetProperty("CompetitorVariantId").GetGuid();
                    var refVarId = doc.RootElement.GetProperty("ReferenceVariantId").GetGuid();
                    var score = doc.RootElement.TryGetProperty("Score", out var s) ? s.GetDecimal() : 1.0m;

                    var existingMatch = await readContext.Query<ProductMatch>()
                        .FirstOrDefaultAsync(m => m.CompetitorVariantId == compVarId && m.ReferenceVariantId == refVarId, ct);

                    if (existingMatch is not null)
                    {
                        existingMatch.Status = MatchStatus.Approved;
                        existingMatch.Method = MatchMethod.Manual;
                        unitOfWork.ProductMatches.Update(existingMatch);
                    }
                    else
                    {
                        var newMatch = new ProductMatch
                        {
                            CompetitorVariantId = compVarId,
                            ReferenceVariantId = refVarId,
                            Score = score,
                            Method = MatchMethod.Manual,
                            Status = MatchStatus.Approved
                        };
                        await unitOfWork.ProductMatches.AddAsync(newMatch, ct);
                    }
                }
                catch
                {
                }
            }

            await unitOfWork.SaveChangesAsync(ct);
            await cache.RemoveByTagAsync("products", ct);

            return new DecisionResultDto(review.Id, review.Status.ToString().ToLowerInvariant(), request.Decision.ToLowerInvariant());
        }

        // Check if ID is a ProductMatch
        var match = await unitOfWork.ProductMatches.GetByIdAsync(request.Id, ct);
        if (match is not null)
        {
            match.Status = isApprove ? MatchStatus.Approved : MatchStatus.Rejected;
            match.Method = MatchMethod.Manual;
            unitOfWork.ProductMatches.Update(match);

            await unitOfWork.SaveChangesAsync(ct);
            await cache.RemoveByTagAsync("products", ct);

            return new DecisionResultDto(match.Id, match.Status.ToString().ToLowerInvariant(), request.Decision.ToLowerInvariant());
        }

        throw new NotFoundException(nameof(ReviewItem), request.Id);
    }
}
