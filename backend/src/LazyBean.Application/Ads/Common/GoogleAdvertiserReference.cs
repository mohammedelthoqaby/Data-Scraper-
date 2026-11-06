using System.Text.RegularExpressions;

namespace LazyBean.Application.Ads.Common;

// A Google Ads Transparency advertiser ID is "AR" followed by exactly 20 digits. Admins may paste the ID itself or
// a link to the advertiser page; both reduce to the same ID.
public static class GoogleAdvertiserReference
{
    public const string InvalidMessage =
        "Enter the Google advertiser ID (AR followed by 20 digits) or paste an Ads Transparency link, " +
        "for example https://adstransparency.google.com/advertiser/AR01064852753525768193.";

    private static readonly Regex BareId = new(
        "^AR[0-9]{20}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AdvertiserLink = new(
        @"adstransparency\.google\.[a-z.]+/advertiser/(AR[0-9]{20})(?![0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? input, out string advertiserId)
    {
        advertiserId = string.Empty;
        var text = input?.Trim() ?? string.Empty;

        string? candidate = null;
        if (BareId.IsMatch(text))
        {
            candidate = text;
        }
        else
        {
            var link = AdvertiserLink.Match(text);
            if (link.Success)
            {
                candidate = link.Groups[1].Value;
            }
        }

        if (candidate is null)
        {
            return false;
        }

        advertiserId = candidate.ToUpperInvariant();
        return true;
    }
}
