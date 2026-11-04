using System.Text.Json;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Matching.Commands.SubmitMatchDecisions;

public class SubmitMatchDecisionsCommandHandler(
    IUnitOfWork unitOfWork,
    IReadDbContext readContext,
    ICacheService cache)
    : IRequestHandler<SubmitMatchDecisionsCommand, BulkDecisionResultDto>
{
    public async Task<BulkDecisionResultDto> Handle(SubmitMatchDecisionsCommand request, CancellationToken ct)
    {
        var results = new List<DecisionItemSummaryDto>();
        var approvedCount = 0;
        var rejectedCount = 0;

        foreach (var decisionItem in request.Decisions)
        {
            var isApprove = string.Equals(decisionItem.Decision, "approve", StringComparison.OrdinalIgnoreCase);

            // 1. Check if ID is a ReviewItem
            var review = await unitOfWork.ReviewItems.GetByIdAsync(decisionItem.Id, ct);
            if (review is not null)
            {
                review.Status = isApprove ? ReviewStatus.Approved : ReviewStatus.Rejected;
                unitOfWork.ReviewItems.Update(review);

                if (isApprove)
                {
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

                    approvedCount++;
                }
                else
                {
                    rejectedCount++;
                }

                results.Add(new DecisionItemSummaryDto(review.Id, review.Status.ToString().ToLowerInvariant(), decisionItem.Decision.ToLowerInvariant()));
                continue;
            }

            // 2. Check if ID is a ProductMatch
            var match = await unitOfWork.ProductMatches.GetByIdAsync(decisionItem.Id, ct);
            if (match is not null)
            {
                match.Status = isApprove ? MatchStatus.Approved : MatchStatus.Rejected;
                match.Method = MatchMethod.Manual;
                unitOfWork.ProductMatches.Update(match);

                if (isApprove) approvedCount++; else rejectedCount++;
                results.Add(new DecisionItemSummaryDto(match.Id, match.Status.ToString().ToLowerInvariant(), decisionItem.Decision.ToLowerInvariant()));
                continue;
            }

            // Unknown ID: still record as skipped
            results.Add(new DecisionItemSummaryDto(decisionItem.Id, "not_found", decisionItem.Decision.ToLowerInvariant()));
        }

        await unitOfWork.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync("products", ct);

        return new BulkDecisionResultDto(results.Count, approvedCount, rejectedCount, results);
    }
}
