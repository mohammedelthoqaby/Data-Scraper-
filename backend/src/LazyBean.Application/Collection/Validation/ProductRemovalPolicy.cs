namespace LazyBean.Application.Collection.Validation;

// Pure: decides whether a competitor's product count this run is trustworthy enough to
// mark no-longer-seen products Removed, or whether it looks like a broken adapter
// (count dropped too far, too fast) - in which case nothing gets marked Removed this run.
public static class ProductRemovalPolicy
{
    private const decimal MaxDropRatio = 0.30m;

    public static bool IsHealthy(int currentProductCount, IReadOnlyList<int> recentCompletedRunProductCounts)
    {
        if (recentCompletedRunProductCounts.Count == 0)
        {
            return true; // no history yet - nothing to compare against, trust the first run.
        }

        var average = recentCompletedRunProductCounts.Average();
        if (average <= 0)
        {
            return true;
        }

        var dropRatio = (average - currentProductCount) / average;
        return dropRatio <= (double)MaxDropRatio;
    }
}
