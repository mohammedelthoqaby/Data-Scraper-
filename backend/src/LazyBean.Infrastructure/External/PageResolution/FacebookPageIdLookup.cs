using System.Text.RegularExpressions;
using LazyBean.Application.Collector.Ports;
using LazyBean.Infrastructure.External.Http;

namespace LazyBean.Infrastructure.External.PageResolution;

// Gets a public Facebook page's HTML, without logging in, and reads its numeric ID. No login: the public page embeds the ID.
public sealed class FacebookPageIdLookup(IHttpClientFactory httpClients) : IFacebookPageIdLookup
{
    public const string ClientName = "FacebookPages";

    public async Task<string?> FindPageIdAsync(string pageName, CancellationToken ct)
    {
        var html = await GetPageHtmlAsync(pageName, ct);
        return html is null ? null : PageIdParser.Find(html);
    }

    private async Task<string?> GetPageHtmlAsync(string pageName, CancellationToken ct)
    {
        using var http = httpClients.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.facebook.com/{Uri.EscapeDataString(pageName)}");
        request.Headers.TryAddWithoutValidation("User-Agent", ExternalHttpPolicy.CollectorUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        using var response = await http.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
    }
}

// Finds a Facebook page's numeric ID in the HTML of its public page (userID, page_id, entity_id, fb://page/...). When several
// IDs appear, the one that appears most often wins.
public static partial class PageIdParser
{
    public static string? Find(string html)
    {
        var counts = new Dictionary<string, int>();
        var order = new List<string>();
        foreach (Match match in IdPattern().Matches(html))
        {
            var id = match.Groups["id"].Value;
            if (!counts.ContainsKey(id))
            {
                counts[id] = 0;
                order.Add(id);
            }

            counts[id]++;
        }

        return order.Count == 0
            ? null
            : order.OrderByDescending(id => counts[id]).ThenBy(id => order.IndexOf(id)).First();
    }

    [GeneratedRegex("\"(?:pageID|page_id|entity_id|delegate_page_id|userID)\":\"?(?<id>\\d{5,})\"?|fb://(?:page|profile)/(?<id>\\d{5,})")]
    private static partial Regex IdPattern();
}
