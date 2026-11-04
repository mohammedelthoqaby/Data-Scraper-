using LazyBean.Application.Matching.Dtos;
using LazyBean.Domain.Enums;
using MediatR;

namespace LazyBean.Application.Matching.Queries.GetReviews;

public record GetReviewsQuery(ReviewStatus? Status = null, int Page = 1, int PageSize = 50)
    : IRequest<ReviewQueueResponseDto>;
