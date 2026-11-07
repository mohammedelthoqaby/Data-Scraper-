namespace LazyBean.Collector.App.Runtime;

// The one-line status the tray menu and its tooltip show: what the app is doing, in words the user can act on.
public static class TrayStatus
{
    public static string Describe(ConnectionState connection, bool working, BatchProgress? batch)
    {
        if (working)
        {
            return batch is { Total: > 0 } ? $"Working {batch.Position}/{batch.Total}" : "Working";
        }

        return connection switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Waiting => "Waiting for the server",
            ConnectionState.NotPaired => "Not paired",
            ConnectionState.PairingRequired => "Pairing required",
            ConnectionState.Stopped => "Stopped",
            _ => "Starting…",
        };
    }
}
