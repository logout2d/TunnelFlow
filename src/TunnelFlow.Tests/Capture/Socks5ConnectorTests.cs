using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using TunnelFlow.Capture.TransparentProxy;

namespace TunnelFlow.Tests.Capture;

public class Socks5ConnectorTests
{
    [Theory]
    [InlineData(0x01, 6)]
    [InlineData(0x03, 1)]
    [InlineData(0x04, 18)]
    public void GetConnectResponseInitialRemainingLength_SupportsKnownAddressTypes(byte addrType, int expected)
    {
        var length = Socks5Connector.GetConnectResponseInitialRemainingLength(addrType);
        Assert.Equal(expected, length);
    }

    [Fact]
    public async Task ConnectByDomainAsync_AcceptsDomainReplyAddressType()
    {
        const string requestDomain = "www.example.com";
        const string replyDomain = "proxy.reply.local";
        const int port = 443;

        var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 0);
        listener.Start();

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var server = await listener.AcceptTcpClientAsync();
                using var stream = server.GetStream();

                var greeting = new byte[3];
                await stream.ReadExactlyAsync(greeting);
                Assert.Equal(new byte[] { 0x05, 0x01, 0x00 }, greeting);
                await stream.WriteAsync(new byte[] { 0x05, 0x00 });

                var request = new byte[4 + 1 + requestDomain.Length + 2];
                await stream.ReadExactlyAsync(request);
                Assert.Equal(Socks5Connector.BuildDomainConnectRequest(requestDomain, port), request);

                var replyDomainBytes = System.Text.Encoding.ASCII.GetBytes(replyDomain);
                var response = new byte[4 + 1 + replyDomainBytes.Length + 2];
                response[0] = 0x05;
                response[1] = 0x00;
                response[2] = 0x00;
                response[3] = 0x03;
                response[4] = (byte)replyDomainBytes.Length;
                replyDomainBytes.CopyTo(response.AsSpan(5));
                BinaryPrimitives.WriteUInt16BigEndian(
                    response.AsSpan(5 + replyDomainBytes.Length),
                    port);

                await stream.WriteAsync(response);
            });

            await using var socksStream = await Socks5Connector.ConnectByDomainAsync(
                (IPEndPoint)listener.LocalEndpoint,
                requestDomain,
                port);

            Assert.True(socksStream.CanRead);
            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }
}
