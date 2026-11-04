using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
                var token = signValues.ToString();
                hasValidSign = VerifyNetlifySignature(token, _expectedSecret, DateTimeOffset.UtcNow);
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

    public static bool VerifyNetlifySignature(string? jwsToken, string? secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(jwsToken) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var parts = jwsToken.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var headerPart = parts[0];
        var payloadPart = parts[1];
        var signaturePart = parts[2];

        try
        {
            // 1. Verify HMAC-SHA256 signature
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            var signingInput = Encoding.UTF8.GetBytes($"{headerPart}.{payloadPart}");

            using var hmac = new HMACSHA256(keyBytes);
            var expectedSig = hmac.ComputeHash(signingInput);
            var providedSig = Base64UrlDecode(signaturePart);

            if (expectedSig.Length != providedSig.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedSig, providedSig))
            {
                return false;
            }

            // 2. Decode payload JSON
            var payloadBytes = Base64UrlDecode(payloadPart);
            using var jsonDoc = JsonDocument.Parse(payloadBytes);
            var root = jsonDoc.RootElement;

            // 3. Verify issuer is "netlify"
            if (!root.TryGetProperty("iss", out var issProp) || issProp.GetString() != "netlify")
            {
                return false;
            }

            // 4. Verify expiration timestamp (exp)
            if (!root.TryGetProperty("exp", out var expProp))
            {
                return false;
            }

            long expEpochSeconds;
            if (expProp.ValueKind == JsonValueKind.Number)
            {
                expEpochSeconds = expProp.GetInt64();
            }
            else if (expProp.ValueKind == JsonValueKind.String && long.TryParse(expProp.GetString(), out var parsedExp))
            {
                expEpochSeconds = parsedExp;
            }
            else
            {
                return false;
            }

            var expTime = DateTimeOffset.FromUnixTimeSeconds(expEpochSeconds);
            // Allow up to 30 seconds of clock skew
            if (now > expTime.AddSeconds(30))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string CreateNetlifyToken(string secret, string iss = "netlify", DateTimeOffset? exp = null)
    {
        var headerJson = """{"alg":"HS256","typ":"JWT"}""";
        var expSeconds = (exp ?? DateTimeOffset.UtcNow.AddMinutes(5)).ToUnixTimeSeconds();
        var payloadJson = $$"""{"iss":"{{iss}}","exp":{{expSeconds}}}""";

        var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signingInput = Encoding.UTF8.GetBytes($"{headerBase64}.{payloadBase64}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = hmac.ComputeHash(signingInput);
        var sigBase64 = Base64UrlEncode(sig);

        return $"{headerBase64}.{payloadBase64}.{sigBase64}";
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
