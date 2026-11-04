namespace LazyBean.Domain.Interfaces;

public interface IUnitOfWork : IDisposable
{
    ICompetitorRepository Competitors { get; }
    IUserRepository Users { get; }
    ICollectionRunRepository CollectionRuns { get; }
    IRepository<Entities.ProductMatch> ProductMatches { get; }
    IRepository<Entities.ReviewItem> ReviewItems { get; }
    IRepository<Entities.AdvertiserIdentity> AdvertiserIdentities { get; }
    IRepository<Entities.AdCampaign> AdCampaigns { get; }
    IRepository<Entities.Ad> Ads { get; }
    IRepository<Entities.ChangeEvent> ChangeEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
