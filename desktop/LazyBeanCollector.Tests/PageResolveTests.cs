using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Collector.Orchestration;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Collector.Ports;
using LazyBean.Infrastructure.External.PageResolution;
using LazyBean.Collector.App.Runtime;

namespace LazyBean.Collector.App.Tests;

// Looking up a Facebook page's numeric ID from its public page, without logging in.
public sealed class PageResolveTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "lazybean-resolve-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_logs))
        {
            Directory.Delete(_logs, recursive: true);
        }
    }

    [Fact]
    public void PageIdParser_ReadsTheIdFromTheEmbeddedUserId()
    {
        const string html = "<script>{\"userID\":\"61565792458339\",\"name\":\"Lazy Bean\"}</script><meta content=\"fb://profile/61565792458339\">";

        Assert.Equal("61565792458339", PageIdParser.Find(html));
    }

    [Fact]
    public void PageIdParser_ReadsPageIdFromTheAppLink_AndAcceptsANumericValue()
    {
        Assert.Equal("433792133147901", PageIdParser.Find("<a href=\"fb://page/433792133147901\"></a>"));
        Assert.Equal("433792133147901", PageIdParser.Find("{\"page_id\":433792133147901}"));
    }

    [Fact]
    public void PageIdParser_PrefersTheIdThatAppearsMost()
    {
        const string html = "{\"userID\":\"111111111\"} {\"pageID\":\"222222222\"} {\"pageID\":\"222222222\"}";

        Assert.Equal("222222222", PageIdParser.Find(html));
    }

    [Fact]
    public void PageIdParser_ReturnsNullWhenNoIdIsPresent()
    {
        Assert.Null(PageIdParser.Find("<html><body>Log in to continue</body></html>"));
        Assert.Null(PageIdParser.Find(string.Empty));
    }

    [Fact]
    public async Task Handler_ReturnsThePageIdAsAResolvedPage()
    {
        var source = new FakePageSource("{\"userID\":\"61565792458339\"} fb://profile/61565792458339");
        var handler = new MetaPageResolveJobHandler(source, new ActivityLog(_logs), new CollectorState());

        var outcome = await handler.HandleAsync(Job("lazybeanonline"), CancellationToken.None);

        Assert.Equal("lazybeanonline", source.RequestedName);
        Assert.Contains("61565792458339", outcome.Summary);
        Assert.Equal("61565792458339", Assert.Single(outcome.Pages!).PageId);
        Assert.Equal("Resolved", Assert.Single(outcome.Pages!).Status);
    }

    [Fact]
    public async Task Handler_FailsTheJob_WhenThePageCannotBeRead()
    {
        var handler = new MetaPageResolveJobHandler(new FakePageSource(null), new ActivityLog(_logs), new CollectorState());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(Job("lazybeanonline"), CancellationToken.None));

        Assert.Contains("lazybeanonline", error.Message);
    }

    [Fact]
    public async Task Handler_FailsTheJob_WhenItHasNoPageName()
    {
        var handler = new MetaPageResolveJobHandler(new FakePageSource("{\"userID\":\"1234567\"}"), new ActivityLog(_logs), new CollectorState());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(Job(null), CancellationToken.None));
    }

    private static CollectorJobDto Job(string? pageSlug) =>
        new(Guid.NewGuid(), "ResolveMetaPage", null, 1, null, 0, 0, pageSlug);

    // The page's HTML, read by the lookup; the ID is found from it the way the lookup finds it.
    private sealed class FakePageSource(string? html) : IFacebookPageIdLookup
    {
        public string? RequestedName { get; private set; }

        public Task<string?> FindPageIdAsync(string pageName, CancellationToken ct)
        {
            RequestedName = pageName;
            return Task.FromResult(html is null ? null : PageIdParser.Find(html));
        }
    }
}
