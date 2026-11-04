using System.Security.Cryptography;
using System.Text;

namespace LazyBean.Application.Ads.Common;

public static class CreativeHashHelper
{
    public static string Compute(string? title, string? body, string? snapshotUrl = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.Append(title.Trim().ToLowerInvariant());
        }
        sb.Append('|');
        if (!string.IsNullOrWhiteSpace(body))
        {
            sb.Append(body.Trim().ToLowerInvariant());
        }
        if (!string.IsNullOrWhiteSpace(snapshotUrl))
        {
            sb.Append('|');
            sb.Append(snapshotUrl.Trim().ToLowerInvariant());
        }

        var inputBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hashBytes = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }
}
