using System.Text;

namespace TunnelFlow.Tests.Capture;

public static class TlsTestHelper
{
    public static byte[] BuildClientHello(string sni)
    {
        byte[] sniBytes = Encoding.ASCII.GetBytes(sni);

        int sniExtLen = 2 + 1 + 2 + sniBytes.Length;
        int sniExtTotalLen = 2 + 2 + sniExtLen;
        int extensionsLen = sniExtTotalLen;
        int clientHelloBodyLen = 2 + 32 + 1 + 0 + 2 + 2 + 1 + 1 + 2 + extensionsLen;
        int handshakeLen = 1 + 3 + clientHelloBodyLen;
        int recordLen = 5 + handshakeLen;

        var data = new byte[recordLen];
        int pos = 0;

        data[pos++] = 0x16;
        data[pos++] = 0x03;
        data[pos++] = 0x01;
        data[pos++] = (byte)(handshakeLen >> 8);
        data[pos++] = (byte)(handshakeLen & 0xFF);

        data[pos++] = 0x01;
        data[pos++] = 0x00;
        data[pos++] = (byte)(clientHelloBodyLen >> 8);
        data[pos++] = (byte)(clientHelloBodyLen & 0xFF);

        data[pos++] = 0x03;
        data[pos++] = 0x03;
        pos += 32;
        data[pos++] = 0x00;

        data[pos++] = 0x00;
        data[pos++] = 0x02;
        data[pos++] = 0x13;
        data[pos++] = 0x01;

        data[pos++] = 0x01;
        data[pos++] = 0x00;

        data[pos++] = (byte)(extensionsLen >> 8);
        data[pos++] = (byte)(extensionsLen & 0xFF);

        data[pos++] = 0x00;
        data[pos++] = 0x00;
        data[pos++] = (byte)(sniExtLen >> 8);
        data[pos++] = (byte)(sniExtLen & 0xFF);

        int listLen = 1 + 2 + sniBytes.Length;
        data[pos++] = (byte)(listLen >> 8);
        data[pos++] = (byte)(listLen & 0xFF);
        data[pos++] = 0x00;
        data[pos++] = (byte)(sniBytes.Length >> 8);
        data[pos++] = (byte)(sniBytes.Length & 0xFF);
        sniBytes.CopyTo(data.AsSpan(pos));

        return data;
    }
}
