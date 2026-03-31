using System.Net;
using System.Net.Sockets;
using TunnelFlow.Capture.TransparentProxy;

namespace TunnelFlow.Tests.Capture;

public class ProtocolSnifferTests
{
    [Fact]
    public async Task SniffAsync_FragmentedTlsClientHello_PreservesHostname()
    {
        const string domain = "fragmented.example.com";
        byte[] hello = TlsTestHelper.BuildClientHello(domain);

        var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 0);
        listener.Start();

        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            var connectTask = client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            using var server = await listener.AcceptTcpClientAsync();
            await connectTask;

            using NetworkStream clientStream = client.GetStream();
            using NetworkStream serverStream = server.GetStream();

            var sniffTask = ProtocolSniffer.SniffAsync(clientStream);

            await serverStream.WriteAsync(hello.AsMemory(0, 3));
            await Task.Delay(50);
            await serverStream.WriteAsync(hello.AsMemory(3, hello.Length - 3));

            var result = await sniffTask;

            Assert.Equal(SniffedProtocol.TLS, result.Protocol);
            Assert.Equal(domain, result.Domain);
            Assert.Equal(hello.Length, result.BufferedLength);
        }
        finally
        {
            listener.Stop();
        }
    }
}
