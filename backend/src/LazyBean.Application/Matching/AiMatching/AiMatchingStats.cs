namespace LazyBean.Application.Matching.AiMatching;

public sealed record AiMatchProviderUsage(int Requests, long Tokens);

/// <summary>Outcome of one AI matching pass. Shown as the ai_matching phase message and in run progress.</summary>
public sealed record AiMatchingStats(
    string? NotRunReason,
    int UncertainCandidates,
    int MissedCandidates,
    int Evaluated,
    int CacheHits,
    int GuardrailBlocked,
    int SentToAi,
    int Confirmed,
    int ToReview,
    int Rejected,
    int SkippedManual,
    int SkippedQuota,
    int Malformed,
    IReadOnlyDictionary<string, AiMatchProviderUsage> Providers,
    string? StopReason)
{
    public int AiRequests => Providers.Values.Sum(p => p.Requests);

    public long AiTokens => Providers.Values.Sum(p => p.Tokens);

    public static AiMatchingStats NotRun(string reason) => new(
        reason, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        new Dictionary<string, AiMatchProviderUsage>(), null);

    public string Summary
    {
        get
        {
            if (NotRunReason is not null)
                return NotRunReason;

            var providers = Providers.Count == 0
                ? "no AI calls"
                : string.Join(", ", Providers.Select(p => $"{p.Key} {p.Value.Requests} req / {p.Value.Tokens} tok"));

            var body = $"{Evaluated} evaluated: {Confirmed} AI confirmed, {ToReview} to review, {Rejected} rejected, " +
                       $"{GuardrailBlocked} blocked by guardrails, {CacheHits} from cache, {SkippedManual} skipped (manual), " +
                       $"{SkippedQuota} skipped for quota; providers: {providers}";

            return StopReason is null
                ? body
                : $"{StopReason}, {SkippedQuota} items left for next run. {body}";
        }
    }
}
