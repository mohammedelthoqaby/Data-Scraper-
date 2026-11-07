using System.Text.RegularExpressions;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Collector.App.Runtime;

namespace LazyBean.Collector.App.Jobs;

// Finds a Facebook page's numeric ID in the HTML of its public page. No login: the public page embeds the ID
// (userID, page_id, entity_id, fb://page/...). When several IDs appear, the one that appears most often wins.
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

// Gets a public Facebook page's HTML, without logging in. Behind an interface so the lookup can be tested offline.
public interface IFacebookPageSource
{
    Task<string?> GetPageHtmlAsync(string pageName, CancellationToken ct);
}

public sealed class FacebookPageSource(HttpClient http) : IFacebookPageSource
{
    // The same identity the collector uses for every other request.
    public const string UserAgent = "LazyBeanBot/1.0 (+https://lazy-bean.runasp.net)";

    public async Task<string?> GetPageHtmlAsync(string pageName, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.facebook.com/{Uri.EscapeDataString(pageName)}");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        using var response = await http.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
    }
}

// Resolves a Facebook page name to its numeric ID. Fails the job when no ID is found, so nothing is added.
public sealed class MetaPageResolveJobHandler(IFacebookPageSource pages, ActivityLog log, CollectorState state)
{
    public async Task<JobOutcome> HandleAsync(CollectorJobDto job, CancellationToken ct)
    {
        var name = job.PageSlug ?? throw new InvalidOperationException("The lookup has no Facebook page name.");
        state.UpdateJob($"Looking up the Facebook page {name}", 0, 1);
        log.Info($"Looking up the Facebook page {name} for its page ID.");

        var html = await pages.GetPageHtmlAsync(name, ct);
        var pageId = html is null ? null : PageIdParser.Find(html);
        if (pageId is null)
        {
            throw new InvalidOperationException(
                $"No page ID was found on the public page for {name}. Check that the page is public, or paste its numeric ID.");
        }

        return new JobOutcome($"Found page ID {pageId} for {name}.", [new CollectorPageReport(pageId, "Resolved", 0, null)]);
    }
}
