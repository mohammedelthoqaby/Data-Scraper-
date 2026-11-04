using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace LazyBean.Api.Middleware;

public class NetlifyForwardedHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly string? _expectedSecret = configuration["Security:NetlifyProxySecret"] ?? configuration["Netlify:Secret"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!string.IsNullOrWhiteSpace(_expectedSecret))
        {
            var hasValidSign = false;
            if (context.Request.Headers.TryGetValue("x-nf-sign", out var signValues))
            {
                var sign = signValues.ToString();
                var signBytes = Encoding.UTF8.GetBytes(sign);
                var secretBytes = Encoding.UTF8.GetBytes(_expectedSecret);
                if (signBytes.Length == secretBytes.Length &&
                    CryptographicOperations.FixedTimeEquals(signBytes, secretBytes))
                {
                    hasValidSign = true;
                }
            }

            if (hasValidSign)
            {
                // Trusted request from Netlify proxy: extract real client IP from X-Forwarded-For
                if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var fwdValues))
                {
                    var clientIp = fwdValues.ToString().Split(',')[0].Trim();
                    if (IPAddress.TryParse(clientIp, out var parsedIp))
                    {
                        context.Connection.RemoteIpAddress = parsedIp;
                    }
                }
                if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var protoValues))
                {
                    context.Request.Scheme = protoValues.ToString();
                }
            }
            else
            {
                // Untrusted: strip forwarded headers so connection IP is used
                context.Request.Headers.Remove("X-Forwarded-For");
                context.Request.Headers.Remove("X-Forwarded-Proto");
            }
        }

        await next(context);
    }
}
