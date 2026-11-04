using LazyBean.Application.Collection.Contracts;
using LazyBean.Domain.Entities;

namespace LazyBean.IntegrationTests.Collection;

// Maps a URL substring to a canned response - lets adapter tests exercise pagination
// and JSON mapping without any real HTTP, retry, or robots.txt logic involved.
internal class FakeCollectorHttpClient : ICollectorHttpClient
{
    private readonly List<(string UrlContains, CollectedResponse Response)> _responses = [];
    public readonly List<string> RequestedUrls = [];

    public FakeCollectorHttpClient When(string urlContains, string body, IReadOnlyDictionary<string, string>? headers = null)
    {
        _responses.Add((urlContains, new CollectedResponse(200, body, true, Headers: headers)));
        return this;
    }

    public Task<CollectedResponse> GetAsync(Competitor competitor, string url, CancellationToken ct)
    {
        RequestedUrls.Add(url);
        var match = _responses.FirstOrDefault(r => url.Contains(r.UrlContains));
        return Task.FromResult(match.Response ?? new CollectedResponse(404, string.Empty, false));
    }
}
