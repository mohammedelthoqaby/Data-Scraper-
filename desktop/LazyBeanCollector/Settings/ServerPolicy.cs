using System.Net;

namespace LazyBean.Collector.App.Settings;

// Which server this PC talks to. The packaged app talks to the production server by default. A local address saved
// by a development run is shown to the user as a problem (a banner, and the pairing form starts at production); it is
// never used silently for a release build.
public static class ServerPolicy
{
    public const string ProductionServerUrl = CollectorSettings.DefaultServerUrl;

    // True for the packaged app. Debug builds are the developers' own builds.
    public static bool IsReleaseBuild
    {
        get
        {
#if DEBUG
            return false;
#else
            return true;
#endif
        }
    }

    // localhost, *.localhost, and any loopback address (127.0.0.0/8, ::1).
    public static bool IsLocalAddress(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.Trim('[', ']').ToLowerInvariant();
        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    // A release build saved to a local address needs the user's decision (the window shows a banner for it).
    public static bool NeedsProductionServer(string? serverUrl, bool releaseBuild) =>
        releaseBuild && IsLocalAddress(serverUrl);

    // The server the pairing form starts with: the saved one, except that a local address in a release build gives way
    // to production.
    public static string PairingDefault(string? savedServerUrl, bool releaseBuild) =>
        string.IsNullOrWhiteSpace(savedServerUrl) || NeedsProductionServer(savedServerUrl, releaseBuild)
            ? ProductionServerUrl
            : savedServerUrl.Trim();
}
