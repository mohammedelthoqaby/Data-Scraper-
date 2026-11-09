using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Queries.GetAdsSummary;

public record GetAdsSummaryQuery : IRequest<AdsSummaryDto>;
