using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LazyBean.Api.Middleware;

public class MemoryMonitoringMiddleware(RequestDelegate next, ILogger<MemoryMonitoringMiddleware> logger)
{
    private const long HighAllocThresholdBytes = 50 * 1024 * 1024; // 50 MB

    public async Task InvokeAsync(HttpContext context)
    {
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();

        try
        {
            await next(context);
        }
        finally
        {
            var allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            if (allocDelta > HighAllocThresholdBytes)
            {
                var wsMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
                var allocMb = allocDelta / (1024.0 * 1024.0);
                logger.LogWarning(
                    "High memory allocation: {Method} {Path} allocated {AllocMb:F1} MB (Current WorkingSet: {WsMb:F1} MB)",
                    context.Request.Method,
                    context.Request.Path,
                    allocMb,
                    wsMb);
            }
        }
    }
}
