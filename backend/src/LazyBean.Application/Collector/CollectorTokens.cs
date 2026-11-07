using System.Security.Cryptography;
using System.Text;

namespace LazyBean.Application.Collector;

// Device tokens are 256-bit random values, so a plain SHA-256 is enough to store them: nothing
// about them can be brute-forced from the hash, and the lookup stays a single indexed comparison.
public static class CollectorTokens
{
    private const int TokenBytes = 32;

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string Hash(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
