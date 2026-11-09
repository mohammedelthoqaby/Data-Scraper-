using System.Drawing;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collector.App.UI;

namespace LazyBean.Collector.App.Tests;

public sealed class TrayTests
{
    [Fact]
    public void StatusLine_ShowsTheBatchPlace_WhileWorking()
    {
        Assert.Equal("Working 2/3", TrayStatus.Describe(ConnectionState.Connected, working: true, new BatchProgress(2, 3)));
        Assert.Equal("Working", TrayStatus.Describe(ConnectionState.Connected, working: true, batch: null));
    }

    [Theory]
    [InlineData(ConnectionState.Connected, "Connected")]
    [InlineData(ConnectionState.Waiting, "Waiting for the server")]
    [InlineData(ConnectionState.NotPaired, "Not paired")]
    [InlineData(ConnectionState.PairingRequired, "Pairing required")]
    public void StatusLine_ShowsTheConnection_WhenIdle(ConnectionState connection, string expected) =>
        Assert.Equal(expected, TrayStatus.Describe(connection, working: false, batch: null));

    [Fact]
    public void WorkingBadge_KeepsTheIcon_AndPaintsAGreenCorner()
    {
        var size = new Size(16, 16);
        using var icon = AppIcons.Load(size);
        using var badged = TrayIcons.WithWorkingBadge(icon, size);
        Assert.NotEqual(IntPtr.Zero, badged.Handle);

        using var bitmap = badged.ToBitmap();
        var corner = bitmap.GetPixel(14, 14);
        Assert.True(corner.G > corner.R && corner.G > corner.B, $"Expected a green corner, got {corner}.");
    }
}
