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
    private IRepository<ChangeEvent>? _changeEvents;
    private IRepository<Product>? _products;

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
    public IRepository<ChangeEvent> ChangeEvents => _changeEvents ??= new Repository<ChangeEvent>(_context);
    public IRepository<Product> Products => _products ??= new Repository<Product>(_context);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

    public void Dispose() => _context.Dispose();
}
