using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace LazyBean.Application.Matching.AiMatching;

/// <summary>
/// AI product-matching settings, read from the "Ai:Matching" configuration section.
/// Missing or invalid values fall back to the defaults.
/// </summary>
public sealed record AiMatchingOptions(
    bool Enabled,
    decimal UncertainMinScore,
    decimal UncertainMaxScore,
    int MaxCandidatesPerCompetitor,
    int BatchSize,
    int MaxComparisonsPerRun,
    decimal ConfirmThreshold)
{
    public static AiMatchingOptions From(IConfiguration? configuration)
    {
        var section = configuration?.GetSection("Ai:Matching");

        return new AiMatchingOptions(
            Enabled: ReadBool(section, "Enabled", true),
            UncertainMinScore: ReadDecimal(section, "UncertainMinScore", 0.60m),
            UncertainMaxScore: ReadDecimal(section, "UncertainMaxScore", 0.90m),
            MaxCandidatesPerCompetitor: ReadInt(section, "MaxCandidatesPerCompetitor", 5, 1, 20),
            BatchSize: ReadInt(section, "BatchSize", 5, 1, 10),
            MaxComparisonsPerRun: ReadInt(section, "MaxComparisonsPerRun", 300, 1, 100_000),
            ConfirmThreshold: ReadDecimal(section, "ConfirmThreshold", 0.85m));
    }

    private static bool ReadBool(IConfigurationSection? section, string key, bool fallback) =>
        bool.TryParse(section?[key], out var value) ? value : fallback;

    private static int ReadInt(IConfigurationSection? section, string key, int fallback, int min, int max) =>
        int.TryParse(section?[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;

    private static decimal ReadDecimal(IConfigurationSection? section, string key, decimal fallback) =>
        decimal.TryParse(section?[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0m, 1m)
            : fallback;
}
