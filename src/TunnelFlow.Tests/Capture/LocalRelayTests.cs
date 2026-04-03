using System.Net;
using TunnelFlow.Capture.TransparentProxy;

namespace TunnelFlow.Tests.Capture;

public class LocalRelayTests
{
    [Fact]
    public void Socks5ConnectRequest_IPv4_BytesAreCorrect()
    {
        var dest = new IPEndPoint(IPAddress.Parse("142.250.74.110"), 443);
        var request = Socks5Connector.BuildConnectRequest(dest);

        Assert.Equal(10, request.Length);
        Assert.Equal(0x05, request[0]); // VER
        Assert.Equal(0x01, request[1]); // CMD = CONNECT
        Assert.Equal(0x00, request[2]); // RSV
        Assert.Equal(0x01, request[3]); // ATYP = IPv4

        byte[] expectedAddr = dest.Address.GetAddressBytes();
        Assert.Equal(expectedAddr[0], request[4]);
        Assert.Equal(expectedAddr[1], request[5]);
        Assert.Equal(expectedAddr[2], request[6]);
        Assert.Equal(expectedAddr[3], request[7]);

        ushort port = (ushort)((request[8] << 8) | request[9]);
        Assert.Equal(443, port);
    }

    [Fact]
    public void Socks5ConnectRequest_IPv6_BytesAreCorrect()
    {
        var dest = new IPEndPoint(IPAddress.Parse("::1"), 8080);
        var request = Socks5Connector.BuildConnectRequest(dest);

        Assert.Equal(22, request.Length); // 4 header + 16 addr + 2 port
        Assert.Equal(0x04, request[3]);   // ATYP = IPv6

        ushort port = (ushort)((request[20] << 8) | request[21]);
        Assert.Equal(8080, port);
    }

    [Fact]
    public void Socks5ConnectRequest_HeaderIsCorrect()
    {
        var dest = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 80);
        var request = Socks5Connector.BuildConnectRequest(dest);

        Assert.Equal(0x05, request[0]); // VER
        Assert.Equal(0x01, request[1]); // CMD = CONNECT
        Assert.Equal(0x00, request[2]); // RSV
        Assert.Equal(0x01, request[3]); // ATYP = IPv4
    }

    [Fact]
    public void NatLookupMiss_ReturnsNull()
    {
        IReadOnlyDictionary<string, IPEndPoint> emptyNat =
            new Dictionary<string, IPEndPoint>();

        string key = "192.168.1.5:54321";
        bool found = emptyNat.TryGetValue(key, out _);

        Assert.False(found);
    }

    [Fact]
    public void NatLookupHit_ReturnsOriginalDestination()
    {
        var originalDest = new IPEndPoint(IPAddress.Parse("142.250.74.110"), 443);
        IReadOnlyDictionary<string, IPEndPoint> natTable =
            new Dictionary<string, IPEndPoint>
            {
                ["192.168.1.5:54321"] = originalDest
            };

        Assert.True(natTable.TryGetValue("192.168.1.5:54321", out var result));
        Assert.Equal(originalDest, result);
    }

    [Fact]
    public void ResolveOriginalDestination_ReturnsNatDestinationWhenLookupMatches()
    {
        var clientEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.5"), 54321);
        var natDestination = new IPEndPoint(IPAddress.Parse("198.51.100.25"), 443);

        var result = LocalRelay.ResolveOriginalDestination(
            clientEndpoint,
            key => key == "192.168.1.5:54321" ? natDestination : null,
            out var source);

        Assert.Equal(natDestination, result);
        Assert.Equal("nat", source);
    }

    [Fact]
    public void ResolveOriginalDestination_ReturnsMissWhenNoLookupMatches()
    {
        var clientEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.5"), 54321);

        var result = LocalRelay.ResolveOriginalDestination(
            clientEndpoint,
            _ => null,
            out var source);

        Assert.Null(result);
        Assert.Equal("miss", source);
    }
}
