using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace LazyBean.Infrastructure.Ai.Providers;

/// <summary>Shared HTTP plumbing for the provider clients: timeouts, error classification, header parsing, sanitizing.</summary>
internal static class AiHttp
{
    private static readonly Regex DurationPattern = new(
        @"^\s*(?:(?<h>\d+)h)?(?:(?<m>\d+)m(?!s))?(?:(?<s>\d+(?:\.\d+)?)s)?(?:(?<ms>\d+(?:\.\d+)?)ms)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        TimeSpan timeout,
        string provider,
        string? secret,
        CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiProviderException(
                AiProviderErrorKind.Timeout,
                $"{provider} request timed out after {timeout.TotalSeconds:0}s.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException(
                AiProviderErrorKind.Network,
                Sanitize($"{provider} network error: {ex.Message}", secret));
        }
    }

    public static AiRateLimitInfo? ReadRateLimit(HttpResponseMessage response)
    {
        var remainingRequests = ReadInt(response, "x-ratelimit-remaining-requests");
        var resetRequests = ParseDuration(ReadString(response, "x-ratelimit-reset-requests"));
        var remainingTokens = ReadInt(response, "x-ratelimit-remaining-tokens");
        var resetTokens = ParseDuration(ReadString(response, "x-ratelimit-reset-tokens"));

        if (remainingRequests is null && resetRequests is null && remainingTokens is null && resetTokens is null)
            return null;

        return new AiRateLimitInfo(remainingRequests, resetRequests, remainingTokens, resetTokens);
    }

    public static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }

    /// <summary>Maps a non-success HTTP status to a classified, sanitized exception.</summary>
    public static AiProviderException ToException(
        HttpStatusCode status,
        string body,
        string provider,
        string? secret,
        TimeSpan? retryAfter,
        AiRateLimitInfo? rateLimit)
    {
        var code = (int)status;
        var excerpt = Sanitize(body, secret);

        if (status == HttpStatusCode.TooManyRequests)
        {
            return new AiProviderException(
                AiProviderErrorKind.RateLimited,
                $"{provider} rate limit or quota reached (HTTP 429).",
                code,
                retryAfter ?? rateLimit?.ResetRequests ?? rateLimit?.ResetTokens,
                rateLimit);
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new AiProviderException(
                AiProviderErrorKind.InvalidKey,
                $"{provider} rejected the API key (HTTP {code}).",
                code);
        }

        if (code >= 500 || status == HttpStatusCode.RequestTimeout)
        {
            return new AiProviderException(
                AiProviderErrorKind.ServerError,
                $"{provider} server error (HTTP {code}).",
                code,
                retryAfter,
                rateLimit);
        }

        return new AiProviderException(
            AiProviderErrorKind.BadRequest,
            $"{provider} request failed (HTTP {code}): {excerpt}",
            code);
    }

    /// <summary>Parses durations such as "2m59.56s", "7.66s", "120ms" or "1h2m3s".</summary>
    public static TimeSpan? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = DurationPattern.Match(text);
        if (!match.Success || !match.Value.Trim().Any(char.IsDigit))
            return null;

        double Group(string name) =>
            match.Groups[name].Success
                ? double.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture)
                : 0;

        var total = TimeSpan.FromHours(Group("h"))
            + TimeSpan.FromMinutes(Group("m"))
            + TimeSpan.FromSeconds(Group("s"))
            + TimeSpan.FromMilliseconds(Group("ms"));

        return total;
    }

    /// <summary>Removes the key and any query-string or bearer secrets from text that may be logged or shown.</summary>
    public static string Sanitize(string? raw, string? secret)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Unknown error";

        var text = raw;
        if (!string.IsNullOrWhiteSpace(secret))
            text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);

        text = Regex.Replace(text, @"key=[^&\s""']+", "key=***", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"Bearer\s+\S+", "Bearer ***", RegexOptions.IgnoreCase);

        return text.Length > 300 ? text[..300] + "..." : text;
    }

    private static string? ReadString(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
            return values.FirstOrDefault();
        if (response.Content.Headers.TryGetValues(name, out var contentValues))
            return contentValues.FirstOrDefault();
        return null;
    }

    private static int? ReadInt(HttpResponseMessage response, string name)
    {
        var text = ReadString(response, name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
