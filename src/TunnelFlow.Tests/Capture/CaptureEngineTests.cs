using System.Net;
using TunnelFlow.Capture;

namespace TunnelFlow.Tests.Capture;

public class CaptureEngineTests
{
    [Fact]
    public void SelectRelayListenAddress_PrefersFirstUsableNonLoopbackIpv4()
    {
        var result = CaptureEngine.SelectRelayListenAddress(
        [
            IPAddress.IPv6Loopback,
            IPAddress.Loopback,
            IPAddress.Parse("169.254.10.20"),
            IPAddress.Parse("192.168.50.10"),
            IPAddress.Parse("10.0.0.20")
        ]);

        Assert.Equal(IPAddress.Parse("192.168.50.10"), result);
    }

    [Fact]
    public void SelectRelayListenAddress_ThrowsWhenNoUsableIpv4Exists()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CaptureEngine.SelectRelayListenAddress(
            [
                IPAddress.IPv6Loopback,
                IPAddress.Loopback,
                IPAddress.Parse("169.254.10.20")
            ]));

        Assert.Contains("No non-loopback IPv4 address found", ex.Message);
    }
}
