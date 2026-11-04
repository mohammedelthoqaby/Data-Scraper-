using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Queries.GetMetaIntegrationStatus;

public record GetMetaIntegrationStatusQuery : IRequest<MetaIntegrationStatusDto>;
