using System.Security.Cryptography;

namespace LazyBean.Api.Common;

// The desktop installer the server offers at /downloads. It is hashed once, at startup, so Settings >
// Collector shows the SHA-256 of the file this process actually serves. Replacing the file needs a restart.
public sealed class CollectorAppPackage
{
    public const string FileName = "LazyBeanCollector.exe";
    public const string DownloadPath = "/downloads/" + FileName;

    public CollectorAppPackage(IWebHostEnvironment environment, ILogger<CollectorAppPackage> logger)
    {
        var path = Path.Combine(environment.WebRootPath ?? string.Empty, "downloads", FileName);
        if (!File.Exists(path))
        {
            logger.LogInformation("No desktop installer at {Path}. Settings > Collector will report it as not published.", path);
            return;
        }

        var file = new FileInfo(path);
        using var stream = File.OpenRead(path);
        Available = true;
        DownloadUrl = DownloadPath;
        SizeBytes = file.Length;
        Sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        logger.LogInformation("Desktop installer {File} is {Size} bytes; SHA-256 {Sha256}.", FileName, SizeBytes, Sha256);
    }

    public bool Available { get; }

    public string? DownloadUrl { get; }

    public long? SizeBytes { get; }

    public string? Sha256 { get; }
}
