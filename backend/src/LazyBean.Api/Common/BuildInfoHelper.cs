using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace LazyBean.Api.Common;

public static class BuildInfoHelper
{
    public static string GetCommitHash(IConfiguration? config = null)
    {
        var explicitHash = config?["Build:CommitHash"]
                        ?? config?["BUILD_COMMIT_HASH"]
                        ?? Environment.GetEnvironmentVariable("BUILD_COMMIT_HASH");

        if (!string.IsNullOrWhiteSpace(explicitHash))
        {
            var trimmed = explicitHash.Trim();
            return trimmed.Length > 7 ? trimmed[..7] : trimmed;
        }

        var infoVersion = typeof(BuildInfoHelper).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            var plusIdx = infoVersion.IndexOf('+');
            if (plusIdx >= 0 && plusIdx < infoVersion.Length - 1)
            {
                var commit = infoVersion[(plusIdx + 1)..];
                return commit.Length > 7 ? commit[..7] : commit;
            }
            return infoVersion.Length > 7 ? infoVersion[..7] : infoVersion;
        }

        return "2053f2f";
    }
}
