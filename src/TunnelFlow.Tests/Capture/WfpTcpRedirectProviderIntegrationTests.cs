using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TunnelFlow.Capture.TcpRedirect;
using TunnelFlow.Capture.TcpRedirect.Interop;
using TunnelFlow.Capture.TransparentProxy;

namespace TunnelFlow.Tests.Capture;

public class WfpTcpRedirectProviderIntegrationTests
{
    [Fact]
    public async Task RealAcceptedConnection_UsesRedirectMetadataPath_ForOriginalDestination()
    {
        int relayPort = GetFreeTcpPort();
        int socksPort = GetFreeTcpPort();
        var relayEndpoint = new IPEndPoint(IPAddress.Loopback, relayPort);
        var socksEndpoint = new IPEndPoint(IPAddress.Loopback, socksPort);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var store = new InMemoryOriginalDestinationStore();
        var nativeSession = new WfpNativeSession(
            new WfpNativeInterop(),
            NullLogger<WfpNativeSession>.Instance);
        var provider = new WfpTcpRedirectProvider(
            store,
            nativeSession,
            NullLogger<WfpTcpRedirectProvider>.Instance);
        await provider.StartAsync(new WfpRedirectConfig
        {
            UseWfpTcpRedirect = true
        }, cts.Token);

        var socksTask = RunFakeSocksServerAsync(socksEndpoint, cts.Token);

        await using var relay = new LocalRelay(
            relayEndpoint,
            socksEndpoint,
            _ => new IPEndPoint(IPAddress.Parse("198.51.100.25"), 9090),
            key =>
            {
                return provider.TryGetOriginalDestination(key, out var record)
                    ? record.OriginalDestination
                    : null;
            },
            NullLogger<LocalRelay>.Instance);

        try
        {
            await relay.StartAsync(cts.Token);

            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var clientEndpoint = (IPEndPoint)client.LocalEndPoint!;

            provider.RecordRedirect(new ConnectionRedirectRecord
            {
                LookupKey = ConnectionLookupKey.From(clientEndpoint),
                OriginalDestination = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 8081),
                RelayEndpoint = relayEndpoint
            });

            await client.ConnectAsync(relayEndpoint, cts.Token);
            using var clientStream = new NetworkStream(client, ownsSocket: false);
            byte[] requestBytes = Encoding.ASCII.GetBytes(
                "GET / HTTP/1.1\r\nHost: redirect.example.com\r\nConnection: close\r\n\r\n");
            await clientStream.WriteAsync(requestBytes, cts.Token);
            client.Shutdown(SocketShutdown.Send);

            var observed = await socksTask;
            var stats = provider.GetStats();

            Assert.Equal("redirect.example.com", observed.Host);
            Assert.Equal(8081, observed.Port);
            Assert.Equal(nameof(WfpTcpRedirectProvider), stats.ActiveProviderName);
            Assert.True(stats.RedirectRegistrationCount >= 1);
            Assert.True(stats.LookupHitCount >= 1);
            Assert.Equal(1, stats.ActiveRecordCount);
        }
        finally
        {
            await provider.StopAsync(cts.Token);
        }
    }

    private static async Task<SocksObservation> RunFakeSocksServerAsync(
        IPEndPoint socksEndpoint,
        CancellationToken ct)
    {
        var listener = new TcpListener(socksEndpoint);
        listener.Start();

        try
        {
            using var server = await listener.AcceptTcpClientAsync(ct);
            using var stream = server.GetStream();

            var greeting = new byte[3];
            await Socks5Connector.ReadExactAsync(stream, greeting, ct);
            await stream.WriteAsync(new byte[] { 0x05, 0x00 }, ct);

            var header = new byte[4];
            await Socks5Connector.ReadExactAsync(stream, header, ct);

            string host;
            int port;

            if (header[3] == 0x03)
            {
                var domainLength = new byte[1];
                await Socks5Connector.ReadExactAsync(stream, domainLength, ct);

                var domainAndPort = new byte[domainLength[0] + 2];
                await Socks5Connector.ReadExactAsync(stream, domainAndPort, ct);

                host = Encoding.ASCII.GetString(domainAndPort, 0, domainLength[0]);
                port = (domainAndPort[^2] << 8) | domainAndPort[^1];
            }
            else if (header[3] == 0x01)
            {
                var addressAndPort = new byte[6];
                await Socks5Connector.ReadExactAsync(stream, addressAndPort, ct);

                host = new IPAddress(addressAndPort.AsSpan(0, 4)).ToString();
                port = (addressAndPort[4] << 8) | addressAndPort[5];
            }
            else
            {
                throw new InvalidOperationException($"Unexpected SOCKS address type: {header[3]}");
            }

            await stream.WriteAsync(new byte[] { 0x05, 0x00, 0x00, 0x01, 127, 0, 0, 1, 0x1F, 0x90 }, ct);

            var buffer = new byte[256];
            _ = await stream.ReadAsync(buffer, ct);

            return new SocksObservation(host, port);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed record SocksObservation(string Host, int Port);
}
