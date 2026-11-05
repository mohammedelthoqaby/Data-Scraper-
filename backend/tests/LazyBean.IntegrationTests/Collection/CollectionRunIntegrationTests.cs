using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Collection.Adapters;
using LazyBean.Infrastructure.Collection.Evidence;
using LazyBean.Infrastructure.Collection.Http;
using LazyBean.Infrastructure.Collection.Orchestration;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LazyBean.IntegrationTests.Collection;

// A full run - real Postgres (the dedicated "lazybean_test" database, never dev), real
// CollectorHttpClient/robots/retry plumbing, real ShopifyAdapter, real
// normalization/validation - with only the network layer faked (FixtureHttpMessageHandler).
// Proves the whole chain writes Products, Variants, PriceObservations and ChangeEvents,
// not just that each piece works in isolation.
[Collection("Database")]
public class CollectionRunIntegrationTests(TestDatabaseFixture testDb)
{
    [Fact]
    public async Task ExecuteAsync_FullRun_WritesProductsVariantsObservationsAndChangeEvents()
    {
        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<LazyBeanDbContext>(o => o.UseNpgsql(testDb.ConnectionString))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<LazyBeanDbContext>>();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        // Within the dedicated test database only (never the dev database): this test's
        // orchestrator run processes every Enabled competitor in it, so a clean slate is
        // needed for a deterministic "exactly one competitor" result - other tests sharing
        // this same test database (e.g. UniqueKeyConflictMappingTests) leave rows behind.
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE competitors, products, variants, price_observations, collection_runs, " +
            "competitor_runs, change_events, review_items, fetched_resources");

        var competitor = new Competitor
        {
            Key = $"it-test-{Guid.NewGuid():N}",
            Name = "Integration Test Shopify Store",
            BaseUrl = "https://it-test.example.com",
            Platform = CompetitorPlatform.Shopify,
            Enabled = true,
            MinDelayMs = 0
        };
        db.Competitors.Add(competitor);

        var run = new CollectionRun { Trigger = RunTrigger.Manual, Status = CollectionRunStatus.Queued };
        db.CollectionRuns.Add(run);
        await db.SaveChangesAsync();

        var handler = new FixtureHttpMessageHandler()
            .When("products.json?limit=250&page=1", 200, Fixtures.Read("shopify_coffeezonemisr_page1.json"))
            .When("products.json?limit=250&page=2", 200, Fixtures.Read("shopify_empty_page.json"));

        var evidenceDir = Path.Combine(Path.GetTempPath(), "lazybean-it-evidence-" + Guid.NewGuid());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("EvidenceStorage:Path", evidenceDir)])
            .Build();

        var httpClientFactory = new FakeHttpClientFactory(handler);
        var robotsCache = new RobotsTxtCache(httpClientFactory);
        var evidenceStore = new GzipEvidenceStore(dbContextFactory, configuration);
        var collectorHttp = new CollectorHttpClient(
            httpClientFactory, robotsCache, evidenceStore, NullLogger<CollectorHttpClient>.Instance);
        var adapterResolver = new SingleAdapterResolver(new ShopifyAdapter(collectorHttp));
        var cache = new NoOpCacheService();

        var executor = new CollectionRunExecutor(
            dbContextFactory, adapterResolver, cache, NullLogger<CollectionRunExecutor>.Instance);

        await executor.ExecuteAsync(run.Id, CancellationToken.None);

        // AsNoTracking: `db` already has `run` tracked from the Add() above, and EF Core's
        // identity resolution would otherwise hand back that stale in-memory instance
        // instead of the row the executor (using its own DbContext instances) wrote.
        var finishedRun = await db.CollectionRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(CollectionRunStatus.Completed, finishedRun.Status);
        Assert.NotNull(finishedRun.FinishedAt);

        var competitorRun = await db.CompetitorRuns.AsNoTracking().SingleAsync(cr => cr.RunId == run.Id);
        Assert.Equal(CompetitorRunStatus.Completed, competitorRun.Status);
        Assert.Equal(RunCoverage.Complete, competitorRun.Coverage);
        Assert.Equal(30, competitorRun.Products);
        // Pages counts pages with content (1): the empty page-2 that stops pagination is
        // still fetched and recorded as evidence (see evidenceRows below) but isn't a "page."
        Assert.Equal(1, competitorRun.Pages);

        var products = await db.Products.Where(p => p.CompetitorId == competitor.Id).ToListAsync();
        Assert.Equal(30, products.Count);
        Assert.All(products, p => Assert.Equal(ProductStatus.Active, p.Status));

        var variants = await db.Variants.Where(v => products.Select(p => p.Id).Contains(v.ProductId)).ToListAsync();
        Assert.Equal(30, variants.Count); // one variant each in this fixture

        var observations = await db.PriceObservations
            .Where(o => variants.Select(v => v.Id).Contains(o.VariantId) && o.RunId == run.Id)
            .ToListAsync();
        Assert.Equal(30, observations.Count);

        // Every product is new on this first run -> one ProductAdded ChangeEvent each.
        var changeEvents = await db.ChangeEvents
            .Where(e => variants.Select(v => (Guid?)v.Id).Contains(e.VariantId) && e.RunId == run.Id)
            .ToListAsync();
        Assert.Equal(30, changeEvents.Count);
        Assert.All(changeEvents, e => Assert.Equal(ChangeEventType.ProductAdded, e.Type));

        var evidenceRows = await db.FetchedResources.Where(f => f.RunId == run.Id).ToListAsync();
        Assert.Equal(2, evidenceRows.Count); // the two listing pages
        Assert.All(evidenceRows, f => Assert.True(File.Exists(f.StoragePath)));

        if (Directory.Exists(evidenceDir))
        {
            Directory.Delete(evidenceDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MultiPageFixture_AllProductsStayPresentAcrossTwoRuns_NoneBecomeMissingOnce()
    {
        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<LazyBeanDbContext>(o => o.UseNpgsql(testDb.ConnectionString))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<LazyBeanDbContext>>();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE competitors, products, variants, price_observations, collection_runs, " +
            "competitor_runs, change_events, review_items, fetched_resources");

        var competitor = new Competitor
        {
            Key = $"it-multipage-{Guid.NewGuid():N}",
            Name = "MultiPage Competitor",
            BaseUrl = "https://multipage.example.com",
            Platform = CompetitorPlatform.Shopify,
            MinDelayMs = 0,
            Enabled = true
        };
        db.Competitors.Add(competitor);
        await db.SaveChangesAsync();

        var page1Json = @"{""products"": [
            {""id"": 101, ""title"": ""Coffee Beans 1kg"", ""handle"": ""coffee-beans-1kg"", ""product_type"": ""Coffee Beans"", ""vendor"": ""Lavazza"", ""tags"": [""beans"", ""espresso""], ""variants"": [{""id"": 1001, ""title"": ""1kg"", ""price"": ""500.00"", ""available"": true}]},
            {""id"": 102, ""title"": ""Ground Coffee 250g"", ""handle"": ""ground-coffee-250g"", ""product_type"": ""Ground Coffee"", ""vendor"": ""Illy"", ""tags"": [""ground""], ""variants"": [{""id"": 1002, ""title"": ""250g"", ""price"": ""200.00"", ""available"": true}]}
        ]}";

        var page2Json = @"{""products"": [
            {""id"": 103, ""title"": ""Nespresso Original Capsules 10 Pods"", ""handle"": ""nespresso-capsules"", ""product_type"": ""Capsules"", ""vendor"": ""Nespresso"", ""tags"": [""capsules""], ""variants"": [{""id"": 1003, ""title"": ""10 Pods"", ""price"": ""150.00"", ""available"": true}]},
            {""id"": 104, ""title"": ""Turkish Coffee 200g"", ""handle"": ""turkish-coffee"", ""product_type"": ""Turkish Coffee"", ""vendor"": ""Abu Auf"", ""tags"": [""turkish""], ""variants"": [{""id"": 1004, ""title"": ""200g"", ""price"": ""90.00"", ""available"": true}]}
        ]}";

        var page3Json = @"{""products"": []}";

        var handler = new FixtureHttpMessageHandler()
            .When("products.json?limit=250&page=1", 200, page1Json)
            .When("products.json?limit=250&page=2", 200, page2Json)
            .When("products.json?limit=250&page=3", 200, page3Json);

        var evidenceDir = Path.Combine(Path.GetTempPath(), "lazybean-it-mp-" + Guid.NewGuid());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("EvidenceStorage:Path", evidenceDir)])
            .Build();

        var httpClientFactory = new FakeHttpClientFactory(handler);
        var robotsCache = new RobotsTxtCache(httpClientFactory);
        var evidenceStore = new GzipEvidenceStore(dbContextFactory, configuration);
        var collectorHttp = new CollectorHttpClient(httpClientFactory, robotsCache, evidenceStore, NullLogger<CollectorHttpClient>.Instance);
        var adapterResolver = new SingleAdapterResolver(new ShopifyAdapter(collectorHttp));
        var cache = new NoOpCacheService();
        var executor = new CollectionRunExecutor(dbContextFactory, adapterResolver, cache, NullLogger<CollectionRunExecutor>.Instance);

        // --- RUN 1 ---
        var run1 = new CollectionRun { Trigger = RunTrigger.Manual, Status = CollectionRunStatus.Queued };
        db.CollectionRuns.Add(run1);
        await db.SaveChangesAsync();

        await executor.ExecuteAsync(run1.Id, CancellationToken.None);

        var prodsAfterRun1 = await db.Products.AsNoTracking().Where(p => p.CompetitorId == competitor.Id).ToListAsync();
        Assert.Equal(4, prodsAfterRun1.Count);
        Assert.All(prodsAfterRun1, p => Assert.Equal(ProductStatus.Active, p.Status));

        // Also assert raw source fields and classification were stored!
        var beans = prodsAfterRun1.Single(p => p.ExternalId == "101");
        Assert.Equal("Coffee Beans", beans.ProductType);
        Assert.Equal("Lavazza", beans.Vendor);
        Assert.Equal(CanonicalCategory.CoffeeBeans, beans.CanonicalCategory);
        Assert.NotNull(beans.ClassificationConfidence);
        Assert.NotNull(beans.MatchedRule);

        // --- RUN 2 ---
        var run2 = new CollectionRun { Trigger = RunTrigger.Manual, Status = CollectionRunStatus.Queued };
        db.CollectionRuns.Add(run2);
        await db.SaveChangesAsync();

        await executor.ExecuteAsync(run2.Id, CancellationToken.None);

        var prodsAfterRun2 = await db.Products.AsNoTracking().Where(p => p.CompetitorId == competitor.Id).ToListAsync();
        Assert.Equal(4, prodsAfterRun2.Count);
        Assert.All(prodsAfterRun2, p => Assert.Equal(ProductStatus.Active, p.Status));
        Assert.DoesNotContain(prodsAfterRun2, p => p.Status == ProductStatus.MissingOnce);
        Assert.DoesNotContain(prodsAfterRun2, p => p.Status == ProductStatus.Removed);

        var removedEvents = await db.ChangeEvents.Where(e => e.RunId == run2.Id && e.Type == ChangeEventType.ProductRemoved).ToListAsync();
        Assert.Empty(removedEvents);

        if (Directory.Exists(evidenceDir))
        {
            Directory.Delete(evidenceDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MissingProduct_RequiresTwoConsecutiveMissesForRemoval()
    {
        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<LazyBeanDbContext>(o => o.UseNpgsql(testDb.ConnectionString))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<LazyBeanDbContext>>();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE competitors, products, variants, price_observations, collection_runs, " +
            "competitor_runs, change_events, review_items, fetched_resources");

        var competitor = new Competitor
        {
            Key = $"it-missing-{Guid.NewGuid():N}",
            Name = "Missing Test Competitor",
            BaseUrl = "https://missing-test.example.com",
            Platform = CompetitorPlatform.Shopify,
            MinDelayMs = 0,
            Enabled = true
        };
        db.Competitors.Add(competitor);

        // Seed an existing active product with a variant
        var product = new Product
        {
            CompetitorId = competitor.Id,
            ExternalId = "prod-to-disappear",
            Title = "Disappearing Coffee 250g",
            NormalizedTitle = "disappearing coffee 250g",
            CanonicalUrl = "https://missing-test.example.com/products/disappearing",
            Status = ProductStatus.Active
        };
        db.Products.Add(product);
        var variant = new Variant
        {
            ProductId = product.Id,
            ExternalId = "var-to-disappear",
            Title = "Default Title"
        };
        db.Variants.Add(variant);
        await db.SaveChangesAsync();

        // Adapter returns empty product list on page 1
        var handler = new FixtureHttpMessageHandler()
            .When("products.json?limit=250&page=1", 200, Fixtures.Read("shopify_empty_page.json"));

        var evidenceDir = Path.Combine(Path.GetTempPath(), "lazybean-it-missing-" + Guid.NewGuid());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("EvidenceStorage:Path", evidenceDir)])
            .Build();

        var httpClientFactory = new FakeHttpClientFactory(handler);
        var robotsCache = new RobotsTxtCache(httpClientFactory);
        var evidenceStore = new GzipEvidenceStore(dbContextFactory, configuration);
        var collectorHttp = new CollectorHttpClient(httpClientFactory, robotsCache, evidenceStore, NullLogger<CollectorHttpClient>.Instance);
        var adapterResolver = new SingleAdapterResolver(new ShopifyAdapter(collectorHttp));
        var cache = new NoOpCacheService();
        var executor = new CollectionRunExecutor(dbContextFactory, adapterResolver, cache, NullLogger<CollectionRunExecutor>.Instance);

        // --- RUN 1: First miss ---
        var run1 = new CollectionRun { Trigger = RunTrigger.Manual, Status = CollectionRunStatus.Queued };
        db.CollectionRuns.Add(run1);
        await db.SaveChangesAsync();

        await executor.ExecuteAsync(run1.Id, CancellationToken.None);

        var pAfterRun1 = await db.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id);
        Assert.Equal(ProductStatus.MissingOnce, pAfterRun1.Status);

        var eventsRun1 = await db.ChangeEvents.Where(e => e.RunId == run1.Id && e.Type == ChangeEventType.ProductRemoved).ToListAsync();
        Assert.Empty(eventsRun1); // No ProductRemoved event on first miss!

        // --- RUN 2: Second consecutive miss ---
        var run2 = new CollectionRun { Trigger = RunTrigger.Manual, Status = CollectionRunStatus.Queued };
        db.CollectionRuns.Add(run2);
        await db.SaveChangesAsync();

        await executor.ExecuteAsync(run2.Id, CancellationToken.None);

        var pAfterRun2 = await db.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id);
        Assert.Equal(ProductStatus.Removed, pAfterRun2.Status);

        var eventsRun2 = await db.ChangeEvents.Where(e => e.RunId == run2.Id && e.Type == ChangeEventType.ProductRemoved).ToListAsync();
        Assert.Single(eventsRun2); // ProductRemoved event emitted on second miss!

        if (Directory.Exists(evidenceDir))
        {
            Directory.Delete(evidenceDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenOneCompetitorFailsAndOneSucceeds_MarksRunPartial()
    {
        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<LazyBeanDbContext>(o => o.UseNpgsql(testDb.ConnectionString))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<LazyBeanDbContext>>();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE competitors, products, variants, price_observations, collection_runs, " +
            "competitor_runs, change_events, review_items, fetched_resources");

        var compSuccess = new Competitor
        {
            Key = $"it-success-{Guid.NewGuid():N}",
            Name = "Success Competitor",
            BaseUrl = "https://success.example.com",
            Platform = CompetitorPlatform.Shopify,
            Enabled = true,
            MinDelayMs = 0
        };
        var compFailing = new Competitor
        {
            Key = $"it-failing-{Guid.NewGuid():N}",
            Name = "Failing Competitor",
            BaseUrl = "https://failing.example.com",
            Platform = CompetitorPlatform.Shopify,
            Enabled = true,
            MinDelayMs = 0
        };
        db.Competitors.AddRange(compSuccess, compFailing);
        await db.SaveChangesAsync();

        var successJson = "{\"products\":[{\"id\":999,\"title\":\"Test Item 1\",\"variants\":[{\"id\":1999,\"price\":\"100.00\",\"available\":true}]}]}";

        var handler = new FixtureHttpMessageHandler();
        handler.When("https://success.example.com/robots.txt", 200, "User-agent: *\nAllow: /");
        handler.When("https://success.example.com/products.json?limit=250&page=1", 200, successJson);
        handler.When("https://success.example.com/products.json?limit=250&page=2", 200, "{\"products\":[]}");

        handler.When("https://failing.example.com/robots.txt", 200, "User-agent: *\nAllow: /");
        handler.When("https://failing.example.com/products.json?limit=250&page=1", 500, "Internal Server Error");

        var evidenceDir = Path.Combine(Path.GetTempPath(), "lazybean-it-partial-" + Guid.NewGuid());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("EvidenceStorage:Path", evidenceDir)])
            .Build();

        var httpClientFactory = new FakeHttpClientFactory(handler);
        var robotsCache = new RobotsTxtCache(httpClientFactory);
        var evidenceStore = new GzipEvidenceStore(dbContextFactory, configuration);
        var collectorHttp = new CollectorHttpClient(httpClientFactory, robotsCache, evidenceStore, NullLogger<CollectorHttpClient>.Instance);
        var baseAdapter = new ShopifyAdapter(collectorHttp);
        var adapterResolver = new FailingCompetitorAdapterResolver(baseAdapter, compFailing.Id);
        var cache = new NoOpCacheService();
        var executor = new CollectionRunExecutor(dbContextFactory, adapterResolver, cache, NullLogger<CollectionRunExecutor>.Instance);

        var run = new CollectionRun { Trigger = RunTrigger.Manual, Status = CollectionRunStatus.Queued };
        db.CollectionRuns.Add(run);
        await db.SaveChangesAsync();

        await executor.ExecuteAsync(run.Id, CancellationToken.None);

        var finishedRun = await db.CollectionRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(CollectionRunStatus.Partial, finishedRun.Status);

        var compRuns = await db.CompetitorRuns.Where(cr => cr.RunId == run.Id).ToListAsync();
        Assert.Contains(compRuns, cr => cr.CompetitorId == compSuccess.Id && cr.Status == CompetitorRunStatus.Completed);
        Assert.Contains(compRuns, cr => cr.CompetitorId == compFailing.Id && cr.Status == CompetitorRunStatus.Failed);

        if (Directory.Exists(evidenceDir))
        {
            Directory.Delete(evidenceDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReferenceCompetitor_RunsLinkCheckSampleAndFlagsBrokenLinks()
    {
        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<LazyBeanDbContext>(o => o.UseNpgsql(testDb.ConnectionString))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<LazyBeanDbContext>>();

        await using var db = await dbContextFactory.CreateDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE competitors, products, variants, price_observations, collection_runs, " +
            "competitor_runs, change_events, review_items, fetched_resources");

        var competitor = new Competitor
        {
            Key = $"it-ref-{Guid.NewGuid():N}",
            Name = "Lazy Bean Reference",
            BaseUrl = "https://api.example.com/api",
            Platform = CompetitorPlatform.LazzyBeanApi,
            IsReference = true,
            Enabled = true,
            MinDelayMs = 0
        };
        db.Competitors.Add(competitor);

        var run = new CollectionRun { Trigger = RunTrigger.Manual, Status = CollectionRunStatus.Queued };
        db.CollectionRuns.Add(run);
        await db.SaveChangesAsync();

        var products = new List<RawProduct>
        {
            new(
                "prod-ok-1",
                "Colombian Supremo Coffee Beans 1kg",
                "https://lazybean.co/single-product/prod-ok-1",
                "Coffee",
                [new("var-1", "Default", 350m, null, "EGP", true, "SKU-1")],
                WeightGrams: 1000),
            new(
                "prod-broken-2",
                "Discontinued Blend Coffee Beans 1kg",
                "https://lazybean.co/single-product/prod-broken-2",
                "Coffee",
                [new("var-2", "Default", 400m, null, "EGP", true, "SKU-2")],
                WeightGrams: 1000)
        };

        var handler = new FixtureHttpMessageHandler()
            .When("products/prod-ok-1?lang=en-US", 200, "{\"data\":{\"id\":\"prod-ok-1\"}}")
            .When("products/prod-broken-2?lang=en-US", 404, "{\"message\":\"Product not found\"}");

        var evidenceDir = Path.Combine(Path.GetTempPath(), "lazybean-it-evidence-" + Guid.NewGuid());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("EvidenceStorage:Path", evidenceDir),
                new("Reference:StorefrontBaseUrl", "https://lazybean.co")
            ])
            .Build();

        var httpClientFactory = new FakeHttpClientFactory(handler);
        var robotsCache = new RobotsTxtCache(httpClientFactory);
        var evidenceStore = new GzipEvidenceStore(dbContextFactory, configuration);
        var collectorHttp = new CollectorHttpClient(
            httpClientFactory, robotsCache, evidenceStore, NullLogger<CollectorHttpClient>.Instance);
        var adapterResolver = new SingleAdapterResolver(new FakeReferenceAdapter(products));
        var cache = new NoOpCacheService();

        var executor = new CollectionRunExecutor(
            dbContextFactory, adapterResolver, cache, new LazyBean.Application.Collection.Classification.ProductClassifier(), collectorHttp, configuration, NullLogger<CollectionRunExecutor>.Instance);

        await executor.ExecuteAsync(run.Id, CancellationToken.None);

        var compRun = await db.CompetitorRuns.AsNoTracking().SingleAsync(cr => cr.RunId == run.Id);
        Assert.Equal(2, compRun.Products);
        Assert.Equal(1, compRun.Errors);

        var reviewItems = await db.ReviewItems.AsNoTracking().ToListAsync();
        var brokenItem = Assert.Single(reviewItems, r => r.Kind == ReviewItemKind.BrokenReferenceLink);
        Assert.Contains("prod-broken-2", brokenItem.PayloadJson);

        if (Directory.Exists(evidenceDir))
        {
            Directory.Delete(evidenceDir, recursive: true);
        }
    }

    private sealed class FakeReferenceAdapter(IReadOnlyList<RawProduct> products) : Application.Collection.Contracts.ICompetitorAdapter
    {
        public CompetitorPlatform Platform => CompetitorPlatform.LazzyBeanApi;

        public async IAsyncEnumerable<RawProduct> FetchAsync(
            Competitor competitor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var p in products)
            {
                yield return p;
            }
        }
    }

    private sealed class SingleAdapterResolver(Application.Collection.Contracts.ICompetitorAdapter adapter)
        : ICompetitorAdapterResolver
    {
        public Application.Collection.Contracts.ICompetitorAdapter Resolve(CompetitorPlatform platform) => adapter;
    }

    private sealed class FailingCompetitorAdapterResolver(
        Application.Collection.Contracts.ICompetitorAdapter inner,
        Guid failingCompetitorId) : ICompetitorAdapterResolver
    {
        public Application.Collection.Contracts.ICompetitorAdapter Resolve(CompetitorPlatform platform) =>
            new FailingAdapter(inner, failingCompetitorId);
    }

    private sealed class FailingAdapter(
        Application.Collection.Contracts.ICompetitorAdapter inner,
        Guid failingId) : Application.Collection.Contracts.ICompetitorAdapter
    {
        public CompetitorPlatform Platform => inner.Platform;

        public async IAsyncEnumerable<RawProduct> FetchAsync(
            Competitor competitor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            if (competitor.Id == failingId)
            {
                throw new InvalidOperationException("Simulated competitor collection crash.");
            }

            await foreach (var p in inner.FetchAsync(competitor, ct))
            {
                yield return p;
            }
        }
    }
}
