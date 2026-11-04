using Serilog.Context;

namespace LazyBean.Api.Middleware;

// Reuses an inbound X-Correlation-Id (set by a gateway/caller) or mints one, echoes it back
// on the response, and pushes it into the Serilog LogContext so every log line for this
// request - including the exception middleware's - carries it.
public class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) &&
                             !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
