using System.Text;

namespace TunnelFlow.Capture.TransparentProxy;

public static class TlsSniSniffer
{
    private const byte ContentTypeHandshake = 0x16;
    private const byte HandshakeTypeClientHello = 0x01;
    private const ushort ExtensionServerName = 0x0000;
    private const byte ServerNameTypeHostname = 0x00;

    public static bool LooksLikeTls(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
            return false;

        if (data[0] != ContentTypeHandshake)
            return false;

        if (data[1] != 0x03 || data[2] < 0x01)
            return false;

        if (data[5] != HandshakeTypeClientHello)
            return false;

        return true;
    }

    public static string? ExtractSni(ReadOnlySpan<byte> data)
    {
        try
        {
            return ExtractSniInternal(data);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractSniInternal(ReadOnlySpan<byte> data)
    {
        if (data.Length < 44)
            return null;

        int pos = 0;

        if (data[pos] != ContentTypeHandshake)
            return null;
        pos += 5;

        if (data[pos] != HandshakeTypeClientHello)
            return null;
        pos += 4;

        pos += 2;
        pos += 32;

        if (pos >= data.Length)
            return null;
        int sessionIdLen = data[pos];
        pos += 1 + sessionIdLen;

        if (pos + 2 > data.Length)
            return null;
        int cipherSuitesLen = ReadUInt16(data, pos);
        pos += 2 + cipherSuitesLen;

        if (pos >= data.Length)
            return null;
        int compressionLen = data[pos];
        pos += 1 + compressionLen;

        if (pos + 2 > data.Length)
            return null;
        int extensionsLen = ReadUInt16(data, pos);
        pos += 2;

        int extensionsEnd = pos + extensionsLen;
        if (extensionsEnd > data.Length)
            extensionsEnd = data.Length;

        while (pos + 4 <= extensionsEnd)
        {
            ushort extType = ReadUInt16(data, pos);
            int extLen = ReadUInt16(data, pos + 2);
            pos += 4;

            if (extType == ExtensionServerName && extLen > 0)
            {
                return ParseSniExtension(data.Slice(pos, Math.Min(extLen, extensionsEnd - pos)));
            }

            pos += extLen;
        }

        return null;
    }

    private static string? ParseSniExtension(ReadOnlySpan<byte> extData)
    {
        if (extData.Length < 5)
            return null;

        int pos = 0;
        int listLen = ReadUInt16(extData, pos);
        pos += 2;

        int listEnd = pos + listLen;
        if (listEnd > extData.Length)
            listEnd = extData.Length;

        while (pos + 3 <= listEnd)
        {
            byte nameType = extData[pos];
            int nameLen = ReadUInt16(extData, pos + 1);
            pos += 3;

            if (pos + nameLen > listEnd)
                return null;

            if (nameType == ServerNameTypeHostname && nameLen > 0)
            {
                string hostname = Encoding.ASCII.GetString(extData.Slice(pos, nameLen));
                if (IsValidHostname(hostname))
                    return hostname;
            }

            pos += nameLen;
        }

        return null;
    }

    private static bool IsValidHostname(string hostname)
    {
        if (string.IsNullOrEmpty(hostname) || hostname.Length > 253)
            return false;

        foreach (char c in hostname)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-')
                return false;
        }

        if (hostname[0] == '.' || hostname[0] == '-')
            return false;
        if (hostname[^1] == '-')
            return false;

        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);
}
