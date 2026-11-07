namespace LazyBean.Application.Collector;

// Claim types set on a device-token principal. Policies check these; the cookie principal never has them.
public static class CollectorClaims
{
    public const string Scope = "collector_scope";
    public const string DeviceId = "collector_device_id";
}
