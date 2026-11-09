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
    IRepository<Entities.AdIngestBatch> AdIngestBatches { get; }
    IRepository<Entities.ChangeEvent> ChangeEvents { get; }
    IRepository<Entities.Product> Products { get; }
    IRepository<Entities.AiSetting> AiSettings { get; }
    IRepository<Entities.AiReviewTriageCache> AiReviewTriageCaches { get; }
    IRepository<Entities.AiMatchDecision> AiMatchDecisions { get; }
    IRepository<Entities.Alert> Alerts { get; }
    IRepository<Entities.AlertHistory> AlertHistories { get; }
    IRepository<Entities.AlertSetting> AlertSettings { get; }
    IRepository<Entities.CollectorDevice> CollectorDevices { get; }
    IRepository<Entities.CollectorJob> CollectorJobs { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    void ClearTracker();
}
