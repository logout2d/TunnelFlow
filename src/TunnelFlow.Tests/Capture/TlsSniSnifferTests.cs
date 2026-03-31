using System.Text;
using TunnelFlow.Capture.TransparentProxy;

namespace TunnelFlow.Tests.Capture;

public class TlsSniSnifferTests
{
    [Fact]
    public void ExtractSni_ValidClientHello_ReturnsDomain()
    {
        var hello = TlsTestHelper.BuildClientHello("www.google.com");
        var sni = TlsSniSniffer.ExtractSni(hello);
        Assert.Equal("www.google.com", sni);
    }

    [Fact]
    public void ExtractSni_TruncatedData_ReturnsNull()
    {
        var hello = TlsTestHelper.BuildClientHello("www.google.com");
        var sni = TlsSniSniffer.ExtractSni(hello.AsSpan(0, 20));
        Assert.Null(sni);
    }

    [Fact]
    public void LooksLikeTls_HttpData_ReturnsFalse()
    {
        var http = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n");
        Assert.False(TlsSniSniffer.LooksLikeTls(http));
    }

    [Fact]
    public void ExtractHost_ValidHttpRequest_ReturnsHost()
    {
        var http = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n");
        Assert.Equal("example.com", HttpHostSniffer.ExtractHost(http));
    }
}
