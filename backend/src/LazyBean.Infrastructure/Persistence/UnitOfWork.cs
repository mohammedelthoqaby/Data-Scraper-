using LazyBean.Domain.Entities;
using LazyBean.Domain.Interfaces;
using LazyBean.Infrastructure.Persistence.Repositories;

namespace LazyBean.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly LazyBeanDbContext _context;
    private ICompetitorRepository? _competitors;
    private IUserRepository? _users;
    private ICollectionRunRepository? _collectionRuns;
    private IRepository<ProductMatch>? _productMatches;
    private IRepository<ReviewItem>? _reviewItems;
    private IRepository<AdvertiserIdentity>? _advertiserIdentities;
    private IRepository<AdCampaign>? _adCampaigns;
    private IRepository<Ad>? _ads;
    private IRepository<AdIngestBatch>? _adIngestBatches;
    private IRepository<ChangeEvent>? _changeEvents;
    private IRepository<Product>? _products;
    private IRepository<AiSetting>? _aiSettings;
    private IRepository<AiReviewTriageCache>? _aiReviewTriageCaches;
    private IRepository<AiMatchDecision>? _aiMatchDecisions;
    private IRepository<Alert>? _alerts;
    private IRepository<AlertHistory>? _alertHistories;
    private IRepository<AlertSetting>? _alertSettings;
    private IRepository<CollectorDevice>? _collectorDevices;
    private IRepository<CollectorJob>? _collectorJobs;

    public UnitOfWork(LazyBeanDbContext context)
    {
        _context = context;
    }

    public ICompetitorRepository Competitors => _competitors ??= new CompetitorRepository(_context);
    public IUserRepository Users => _users ??= new UserRepository(_context);
    public ICollectionRunRepository CollectionRuns => _collectionRuns ??= new CollectionRunRepository(_context);
    public IRepository<ProductMatch> ProductMatches => _productMatches ??= new Repository<ProductMatch>(_context);
    public IRepository<ReviewItem> ReviewItems => _reviewItems ??= new Repository<ReviewItem>(_context);
    public IRepository<AdvertiserIdentity> AdvertiserIdentities => _advertiserIdentities ??= new Repository<AdvertiserIdentity>(_context);
    public IRepository<AdCampaign> AdCampaigns => _adCampaigns ??= new Repository<AdCampaign>(_context);
    public IRepository<Ad> Ads => _ads ??= new Repository<Ad>(_context);
    public IRepository<AdIngestBatch> AdIngestBatches => _adIngestBatches ??= new Repository<AdIngestBatch>(_context);
    public IRepository<ChangeEvent> ChangeEvents => _changeEvents ??= new Repository<ChangeEvent>(_context);
    public IRepository<Product> Products => _products ??= new Repository<Product>(_context);
    public IRepository<AiSetting> AiSettings => _aiSettings ??= new Repository<AiSetting>(_context);
    public IRepository<AiReviewTriageCache> AiReviewTriageCaches => _aiReviewTriageCaches ??= new Repository<AiReviewTriageCache>(_context);
    public IRepository<AiMatchDecision> AiMatchDecisions => _aiMatchDecisions ??= new Repository<AiMatchDecision>(_context);
    public IRepository<Alert> Alerts => _alerts ??= new Repository<Alert>(_context);
    public IRepository<AlertHistory> AlertHistories => _alertHistories ??= new Repository<AlertHistory>(_context);
    public IRepository<AlertSetting> AlertSettings => _alertSettings ??= new Repository<AlertSetting>(_context);
    public IRepository<CollectorDevice> CollectorDevices => _collectorDevices ??= new Repository<CollectorDevice>(_context);
    public IRepository<CollectorJob> CollectorJobs => _collectorJobs ??= new Repository<CollectorJob>(_context);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

    public void ClearTracker() => _context.ChangeTracker.Clear();

    public void Dispose() => _context.Dispose();
}
