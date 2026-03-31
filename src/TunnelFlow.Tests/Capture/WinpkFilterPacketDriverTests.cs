using TunnelFlow.Capture.Interop;

namespace TunnelFlow.Tests.Capture;

public class WinpkFilterPacketDriverTests
{
    [Fact]
    public void GetReinjectPath_UsesAdapter_ForNonRedirectedOutboundPackets()
    {
        var result = WinpkFilterPacketDriver.GetReinjectPath(
            isOutbound: true,
            redirectToLocalRelay: false);

        Assert.Equal(WinpkFilterPacketDriver.ReinjectPathAdapter, result);
    }

    [Fact]
    public void GetReinjectPath_UsesMstcp_ForRedirectedOutboundRelayPackets()
    {
        var result = WinpkFilterPacketDriver.GetReinjectPath(
            isOutbound: true,
            redirectToLocalRelay: true);

        Assert.Equal(WinpkFilterPacketDriver.ReinjectPathMstcp, result);
    }

    [Fact]
    public void GetReinjectPath_KeepsInboundPacketsOnMstcp()
    {
        var result = WinpkFilterPacketDriver.GetReinjectPath(
            isOutbound: false,
            redirectToLocalRelay: false);

        Assert.Equal(WinpkFilterPacketDriver.ReinjectPathMstcp, result);
    }
}
