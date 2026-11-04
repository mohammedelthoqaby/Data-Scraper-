using System.Collections.Concurrent;

namespace LazyBean.Infrastructure.Collection.Http;

// Minimal robots.txt parser: enough to honor "User-agent: *" / "Disallow: /path" rules on
// the simple Shopify/WooCommerce stores this collector targets - not a full RFC 9309 parser.
public class RobotsTxtCache(IHttpClientFactory httpClientFactory)
{
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<string>>> _disallowRulesByHost = new();

    public async Task<bool> IsAllowedAsync(string absoluteUrl, CancellationToken ct)
    {
        var uri = new Uri(absoluteUrl);
        var host = uri.GetLeftPart(UriPartial.Authority);

        var disallowRules = await _disallowRulesByHost.GetOrAdd(host, h => FetchDisallowRulesAsync(h, ct));

        return !disallowRules.Any(rule => uri.AbsolutePath.StartsWith(rule, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<string>> FetchDisallowRulesAsync(string host, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient("Collector");
            var response = await client.GetAsync(new Uri($"{host}/robots.txt"), ct);

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return ParseDisallowRules(body);
        }
        catch
        {
            // robots.txt unreachable - don't let that block the whole collector; treat as "no rules".
            return [];
        }
    }

    private static IReadOnlyList<string> ParseDisallowRules(string robotsTxt)
    {
        var rules = new List<string>();
        var appliesToUs = false;

        foreach (var rawLine in robotsTxt.Split('\n'))
        {
            var line = rawLine.Split('#')[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
            {
                var agent = line["User-agent:".Length..].Trim();
                appliesToUs = agent == "*";
                continue;
            }

            if (!appliesToUs)
            {
                continue;
            }

            if (line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
            {
                var path = line["Disallow:".Length..].Trim();
                if (path.Length > 0)
                {
                    rules.Add(path);
                }
            }
        }

        return rules;
    }
}
