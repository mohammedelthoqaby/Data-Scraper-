using System.Reflection;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Common;
using LazyBean.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Infrastructure.Persistence;

public class LazyBeanDbContext(DbContextOptions<LazyBeanDbContext> options) : DbContext(options), IReadDbContext, IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<AiSetting> AiSettings => Set<AiSetting>();
    public DbSet<AiProviderSetting> AiProviderSettings => Set<AiProviderSetting>();
    public DbSet<AiProviderUsage> AiProviderUsages => Set<AiProviderUsage>();
    public DbSet<AiReviewTriageCache> AiReviewTriageCaches => Set<AiReviewTriageCache>();
    public DbSet<AiMatchDecision> AiMatchDecisions => Set<AiMatchDecision>();
    public DbSet<Competitor> Competitors => Set<Competitor>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Variant> Variants => Set<Variant>();
    public DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();
    public DbSet<CollectionRun> CollectionRuns => Set<CollectionRun>();
    public DbSet<CompetitorRun> CompetitorRuns => Set<CompetitorRun>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<ChangeEvent> ChangeEvents => Set<ChangeEvent>();
    public DbSet<ProductMatch> ProductMatches => Set<ProductMatch>();
    public DbSet<ReviewItem> ReviewItems => Set<ReviewItem>();
    public DbSet<FetchedResource> FetchedResources => Set<FetchedResource>();
    public DbSet<AdvertiserIdentity> AdvertiserIdentities => Set<AdvertiserIdentity>();
    public DbSet<AdCampaign> AdCampaigns => Set<AdCampaign>();
    public DbSet<Ad> Ads => Set<Ad>();
    public DbSet<AdIngestBatch> AdIngestBatches => Set<AdIngestBatch>();
    public DbSet<AdThumbnail> AdThumbnails => Set<AdThumbnail>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertHistory> AlertHistories => Set<AlertHistory>();
    public DbSet<AlertSetting> AlertSettings => Set<AlertSetting>();
    public DbSet<CollectorDevice> CollectorDevices => Set<CollectorDevice>();
    public DbSet<CollectorJob> CollectorJobs => Set<CollectorJob>();

    public IQueryable<T> Query<T>() where T : BaseEntity => Set<T>().AsNoTracking();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(builder);
    }
}
