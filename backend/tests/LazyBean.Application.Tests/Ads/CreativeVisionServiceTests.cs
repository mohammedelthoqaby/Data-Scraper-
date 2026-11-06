using LazyBean.Application.Ads.Common;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// The optional AI-read: off by default, capped per day, only for unlinked Google image ads with a stored preview.
public class CreativeVisionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private const string Preview = "https://tpc.googlesyndication.com/archive/simgad/123";

    [Fact]
    public async Task WhenDisabled_NothingIsRead()
    {
        var (db, sut, reader) = Setup(enabled: false);
        var ads = AddAds(db, 3);

        var attempts = await sut.ApplyAsync(ads, Catalog(), CancellationToken.None);

        Assert.Equal(0, attempts);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task WhenEnabled_ReadsNoMoreThanTheDailyCap()
    {
        var (db, sut, reader) = Setup(enabled: true, cap: 2);
        var ads = AddAds(db, 3);

        var attempts = await sut.ApplyAsync(ads, Catalog(), CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(2, reader.Calls);
        Assert.Equal(2, ads.Count(a => a.AiReadAt is not null));
        Assert.Equal(2, (await db.AiSettings.SingleAsync()).CreativeVisionUsedToday);
    }

    [Fact]
    public async Task WhenTheCapIsAlreadyUsedToday_NothingIsRead()
    {
        var (db, sut, reader) = Setup(enabled: true, cap: 2, usedToday: 2, usageDate: Today());
        var ads = AddAds(db, 2);

        var attempts = await sut.ApplyAsync(ads, Catalog(), CancellationToken.None);

        Assert.Equal(0, attempts);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task OnANewDay_TheCountStartsAgain()
    {
        var (db, sut, reader) = Setup(enabled: true, cap: 2, usedToday: 2, usageDate: Today().AddDays(-1));
        var ads = AddAds(db, 2);

        var attempts = await sut.ApplyAsync(ads, Catalog(), CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(2, reader.Calls);
        Assert.Equal(Today(), (await db.AiSettings.SingleAsync()).CreativeVisionUsageDate);
    }

    [Fact]
    public async Task OnlyUnlinkedGoogleImagesWithAPreviewAreRead()
    {
        var (db, sut, reader) = Setup(enabled: true, cap: 20);
        var linked = NewAd(db, "linked", AdPlatform.Google, GoogleCreativeFormat.Image, Preview, productId: Guid.NewGuid());
        var video = NewAd(db, "video", AdPlatform.Google, GoogleCreativeFormat.Video, Preview);
        var text = NewAd(db, "text", AdPlatform.Google, GoogleCreativeFormat.Text, null);
        var noPreview = NewAd(db, "noPreview", AdPlatform.Google, GoogleCreativeFormat.Image, null);
        var meta = NewAd(db, "meta", AdPlatform.Meta, "Image", Preview);
        var eligible = NewAd(db, "eligible", AdPlatform.Google, GoogleCreativeFormat.Image, Preview);
        await db.SaveChangesAsync();

        var attempts = await sut.ApplyAsync(new[] { linked, video, text, noPreview, meta, eligible }, Catalog(), CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.Equal(new[] { Preview }, reader.Urls);
        Assert.NotNull(eligible.AiReadAt);
        Assert.Null(linked.AiReadAt);
        Assert.Null(meta.AiReadAt);
    }

    [Fact]
    public async Task AReadThatNamesAProduct_LinksTheAdAndIsMarkedAiRead()
    {
        var productId = Guid.NewGuid();
        var (db, sut, _) = Setup(enabled: true, cap: 5, reading: new CreativeImageReading("Lazy Bean", 245m));
        var ad = NewAd(db, "named", AdPlatform.Google, GoogleCreativeFormat.Image, Preview);
        await db.SaveChangesAsync();

        await sut.ApplyAsync(new[] { ad }, new[] { new LinkableProduct(productId, "Lazy Bean", null, null) }, CancellationToken.None);

        Assert.Equal(productId, ad.ProductId);
        Assert.Equal("ai-read", ad.LinkMethod);
        Assert.Equal("Lazy Bean", ad.AiReadName);
        Assert.Equal(245m, ad.AiReadPrice);
        Assert.Equal(Now, ad.AiReadAt);
        Assert.Null(ad.LinkReason);
    }

    [Fact]
    public async Task AReadThatNamesNothing_IsNotReadAgain()
    {
        var (db, sut, _) = Setup(enabled: true, cap: 5, reading: new CreativeImageReading(null, null));
        var ad = NewAd(db, "blank", AdPlatform.Google, GoogleCreativeFormat.Image, Preview);
        await db.SaveChangesAsync();

        await sut.ApplyAsync(new[] { ad }, Catalog(), CancellationToken.None);

        Assert.NotNull(ad.AiReadAt);
        Assert.Null(ad.AiReadName);
        Assert.Equal(AdLinkReasons.NoProductInfo, ad.LinkReason);
    }

    [Fact]
    public async Task AFailedRead_CountsTowardTheCap_AndIsTriedAgainLater()
    {
        var (db, sut, _) = Setup(enabled: true, cap: 5, failWith: new HttpRequestException("Gemini returned HTTP 503."));
        var ad = NewAd(db, "flaky", AdPlatform.Google, GoogleCreativeFormat.Image, Preview);
        await db.SaveChangesAsync();

        var attempts = await sut.ApplyAsync(new[] { ad }, Catalog(), CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.Null(ad.AiReadAt);
        Assert.Equal(1, (await db.AiSettings.SingleAsync()).CreativeVisionUsedToday);
    }

    [Fact]
    public async Task WhenTheFeatureCannotRun_TheRestAreNotCounted()
    {
        var (db, sut, _) = Setup(enabled: true, cap: 5, failWith: new InvalidOperationException("No Gemini API key is saved."));
        var ads = AddAds(db, 3);

        var attempts = await sut.ApplyAsync(ads, Catalog(), CancellationToken.None);

        Assert.Equal(0, attempts);
        Assert.Equal(0, (await db.AiSettings.SingleAsync()).CreativeVisionUsedToday);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(Now.UtcDateTime);

    private static IReadOnlyList<LinkableProduct> Catalog() => new[] { new LinkableProduct(Guid.NewGuid(), "Espresso Gold", null, null) };

    private static (LazyBeanDbContext Db, CreativeVisionService Sut, FakeReader Reader) Setup(
        bool enabled,
        int cap = 20,
        int usedToday = 0,
        DateOnly? usageDate = null,
        CreativeImageReading? reading = null,
        Exception? failWith = null)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new LazyBeanDbContext(options);
        db.AiSettings.Add(new AiSetting
        {
            CreativeVisionEnabled = enabled,
            CreativeVisionDailyCap = cap,
            CreativeVisionUsedToday = usedToday,
            CreativeVisionUsageDate = usageDate ?? Today(),
        });
        db.SaveChanges();
        // The service loads its own copy of the settings, as it does in a request. A copy still tracked here would clash.
        db.ChangeTracker.Clear();

        var reader = new FakeReader(reading ?? new CreativeImageReading(null, null), failWith);
        var sut = new CreativeVisionService(new LazyBean.Infrastructure.Persistence.UnitOfWork(db), reader, new FixedClock(Now), NullLogger<CreativeVisionService>.Instance);
        return (db, sut, reader);
    }

    private static List<Ad> AddAds(LazyBeanDbContext db, int count)
    {
        var ads = new List<Ad>();
        for (var i = 0; i < count; i++)
        {
            ads.Add(NewAd(db, $"ad{i}", AdPlatform.Google, GoogleCreativeFormat.Image, Preview));
        }

        db.SaveChanges();
        return ads;
    }

    private static Ad NewAd(LazyBeanDbContext db, string externalId, AdPlatform platform, string? format, string? preview, Guid? productId = null)
    {
        var ad = new Ad
        {
            ExternalAdId = externalId,
            Platform = platform,
            CreativeFormat = format,
            PreviewUrl = preview,
            ProductId = productId,
            Status = AdStatus.Active,
            FirstSeenAt = Now,
            LastSeenAt = Now,
            CreativeHash = externalId,
        };
        db.Ads.Add(ad);
        return ad;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeReader(CreativeImageReading reading, Exception? failWith) : ICreativeImageReader
    {
        public int Calls { get; private set; }
        public List<string> Urls { get; } = new();

        public Task<CreativeImageReading?> ReadAsync(string imageUrl, CancellationToken ct = default)
        {
            Calls++;
            Urls.Add(imageUrl);
            if (failWith is not null)
            {
                throw failWith;
            }

            return Task.FromResult<CreativeImageReading?>(reading);
        }
    }
}
