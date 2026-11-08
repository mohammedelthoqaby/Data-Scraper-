namespace LazyBean.Application.Ads.Common;

// Where a Meta ad runs: the platforms it was shown on (facebook, instagram, audience_network, messenger, ...). It is stored on
// the ad as lowercase names joined by commas, so the gallery reads it with the ad. Null when none were recorded (Google ads,
// and Meta ads collected before placements were stored).
public static class AdPlacements
{
    // The column length. A Meta ad has a handful of placements, so the cap is never reached in practice.
    public const int MaxLength = 200;

    private const int MaxNames = 10;

    public static string? Normalize(IEnumerable<string>? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var names = raw
            .Select(name => (name ?? string.Empty).Trim().ToLowerInvariant())
            .Where(name => name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxNames)
            .ToList();

        return names.Count == 0 ? null : string.Join(',', names);
    }

    public static IReadOnlyList<string> Parse(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
