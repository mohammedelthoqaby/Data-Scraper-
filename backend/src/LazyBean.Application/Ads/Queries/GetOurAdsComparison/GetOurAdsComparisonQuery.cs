using LazyBean.Application.Ads.Comparison;
using LazyBean.Application.Common.Interfaces;
using MediatR;

namespace LazyBean.Application.Ads.Queries.GetOurAdsComparison;

public sealed record GetOurAdsComparisonQuery : IRequest<IReadOnlyList<OwnAdComparisonRow>>;

public sealed class GetOurAdsComparisonQueryHandler(IReadDbContext db)
    : IRequestHandler<GetOurAdsComparisonQuery, IReadOnlyList<OwnAdComparisonRow>>
{
    public Task<IReadOnlyList<OwnAdComparisonRow>> Handle(GetOurAdsComparisonQuery request, CancellationToken ct) =>
        OurAdsComparisonLoader.BuildAsync(db, ct);
}
