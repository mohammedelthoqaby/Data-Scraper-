namespace LazyBean.IntegrationTests.Collection;

// A real HttpMessageHandler (so the real HttpClient/HttpClientFactory pipeline runs end to
// end) that serves canned bodies by URL substring instead of touching the network.
// robots.txt isn't mapped, so it 404s - RobotsTxtCache treats that as "no rules."
internal class FixtureHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(string UrlContains, int Status, string Body, Dictionary<string, string> Headers)> _responses = [];

    public FixtureHttpMessageHandler When(string urlContains, int status, string body, Dictionary<string, string>? headers = null) =>
        Add(urlContains, status, body, headers);

    private FixtureHttpMessageHandler Add(string urlContains, int status, string body, Dictionary<string, string>? headers)
    {
        _responses.Add((urlContains, status, body, headers ?? []));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        var match = _responses.FirstOrDefault(r => url.Contains(r.UrlContains));

        if (match.Body is null)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }

        var response = new HttpResponseMessage((System.Net.HttpStatusCode)match.Status)
        {
            Content = new StringContent(match.Body)
        };

        foreach (var (key, value) in match.Headers)
        {
            response.Headers.TryAddWithoutValidation(key, value);
        }

        return Task.FromResult(response);
    }
}

internal class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
