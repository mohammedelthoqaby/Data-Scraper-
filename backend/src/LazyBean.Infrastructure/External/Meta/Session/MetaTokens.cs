using System.Text;
using System.Text.RegularExpressions;

namespace LazyBean.Infrastructure.External.Meta;

// Pure helpers from meta_ads_collector.client: token mining from the facebook.com homepage HTML,
// the jazoest checksum and the base-36 request counter. Kept static so they can be unit-tested
// against the exact regexes the Python library uses.
public static class MetaTokens
{
    private static readonly Regex[] LsdPatterns =
    [
        new(@"""LSD"",\[\],\{""token"":""([^""]+)""\}"),
        new(@"\[""LSD"",\[\],\{""token"":""([^""]+)"""),
        new(@"""lsd"":""([^""]+)"""),
        new(@"name=""lsd"" value=""([^""]+)"""),
    ];

    private static readonly Regex[] RevPatterns =
    [
        new(@"""__spin_r"":(\d+)"),
        new(@"""server_revision"":(\d+)"),
        new(@"""revision"":(\d+)"),
        new(@"\{""__spin_r"":(\d+)"),
    ];

    private static readonly Regex SpinT = new(@"""__spin_t"":(\d+)");
    private static readonly Regex SpinB = new(@"""__spin_b"":""([^""]+)""");
    private static readonly Regex Hsi = new(@"""__hsi"":""(\d+)""");
    private static readonly Regex HsiAlt = new(@"""hsi"":""(\d+)""");
    private static readonly Regex Dtsg = new(@"""DTSGInitialData"",\[\],\{""token"":""([^""]+)""");
    private static readonly Regex Dyn = new(@"""__dyn"":""([^""]+)""");
    private static readonly Regex Csr = new(@"""__csr"":""([^""]+)""");
    private static readonly Regex Hs = new(@"""__hs"":""([^""]+)""");
    private static readonly Regex Hsdp = new(@"""__hsdp"":""([^""]+)""");
    private static readonly Regex Hblp = new(@"""__hblp"":""([^""]+)""");
    private static readonly Regex CometReq = new(@"""__comet_req"":(\d+)");
    private static readonly Regex Jazoest = new(@"""jazoest[""\s:]+(\d+)");
    private static readonly Regex VersionV = new(@"""v""\s*:\s*""([a-f0-9]{4,10})""");
    private static readonly Regex AsbdId = new(@"""asbd_id""\s*:\s*""?(\d+)""?");
    private static readonly Regex AsbdIdAlt = new(@"x-asbd-id[""\s:]+(\d+)");

    public static readonly Regex ServerRevision = new(@"""server_revision"":(\d+)");

    public static IReadOnlyDictionary<string, string> ExtractTokens(string html)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pattern in LsdPatterns)
        {
            var match = pattern.Match(html);
            if (match.Success)
            {
                tokens["lsd"] = match.Groups[1].Value;
                break;
            }
        }

        foreach (var pattern in RevPatterns)
        {
            var match = pattern.Match(html);
            if (match.Success)
            {
                tokens["__rev"] = match.Groups[1].Value;
                tokens["__spin_r"] = match.Groups[1].Value;
                break;
            }
        }

        Capture(tokens, "__spin_t", SpinT, html);
        Capture(tokens, "__spin_b", SpinB, html);
        CaptureFirst(tokens, "__hsi", html, Hsi, HsiAlt);
        Capture(tokens, "fb_dtsg", Dtsg, html);
        Capture(tokens, "__dyn", Dyn, html);
        Capture(tokens, "__csr", Csr, html);
        Capture(tokens, "__hs", Hs, html);
        Capture(tokens, "__hsdp", Hsdp, html);
        Capture(tokens, "__hblp", Hblp, html);
        Capture(tokens, "__comet_req", CometReq, html);
        Capture(tokens, "jazoest", Jazoest, html);
        Capture(tokens, "v", VersionV, html);
        CaptureFirst(tokens, "x-asbd-id", html, AsbdId, AsbdIdAlt);

        return tokens;
    }

    public static string CalculateJazoest(string lsd)
    {
        if (string.IsNullOrEmpty(lsd))
        {
            return "2893";
        }

        var total = 0;
        foreach (var c in lsd)
        {
            total += c;
        }

        return (2 + total).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string EncodeRequestId(int counter)
    {
        if (counter < 10)
        {
            return counter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var result = new StringBuilder();
        var value = counter;
        while (value > 0)
        {
            result.Insert(0, digits[value % 36]);
            value /= 36;
        }

        return result.ToString();
    }

    private static void Capture(Dictionary<string, string> tokens, string name, Regex pattern, string html)
    {
        var match = pattern.Match(html);
        if (match.Success)
        {
            tokens[name] = match.Groups[1].Value;
        }
    }

    private static void CaptureFirst(Dictionary<string, string> tokens, string name, string html, Regex primary, Regex fallback)
    {
        var match = primary.Match(html);
        if (!match.Success)
        {
            match = fallback.Match(html);
        }

        if (match.Success)
        {
            tokens[name] = match.Groups[1].Value;
        }
    }
}
