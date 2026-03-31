using System.Text;

namespace TunnelFlow.Capture.TransparentProxy;

public static class HttpHostSniffer
{
    public static bool LooksLikeHttp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
            return false;

        return StartsWithAscii(data, "GET ") ||
               StartsWithAscii(data, "POST ") ||
               StartsWithAscii(data, "PUT ") ||
               StartsWithAscii(data, "DELETE ") ||
               StartsWithAscii(data, "HEAD ") ||
               StartsWithAscii(data, "OPTIONS ") ||
               StartsWithAscii(data, "PATCH ") ||
               StartsWithAscii(data, "CONNECT ");
    }

    public static string? ExtractHost(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
            return null;

        for (int i = 0; i < data.Length - 6; i++)
        {
            if (i > 0 && data[i - 1] != '\n')
                continue;

            if (!MatchesHostHeader(data, i))
                continue;

            int valueStart = i + 5;
            while (valueStart < data.Length && data[valueStart] == ' ')
                valueStart++;

            int valueEnd = valueStart;
            while (valueEnd < data.Length && data[valueEnd] != '\r' && data[valueEnd] != '\n')
                valueEnd++;

            if (valueEnd <= valueStart)
                return null;

            string hostValue = Encoding.ASCII.GetString(data.Slice(valueStart, valueEnd - valueStart));
            int colonIdx = hostValue.LastIndexOf(':');
            if (colonIdx > 0)
            {
                bool isPort = true;
                for (int j = colonIdx + 1; j < hostValue.Length; j++)
                {
                    if (!char.IsDigit(hostValue[j]))
                    {
                        isPort = false;
                        break;
                    }
                }

                if (isPort)
                    hostValue = hostValue[..colonIdx];
            }

            return hostValue.Trim().ToLowerInvariant();
        }

        return null;
    }

    private static bool MatchesHostHeader(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 5 > data.Length)
            return false;

        return (data[offset] == 'H' || data[offset] == 'h') &&
               (data[offset + 1] == 'o' || data[offset + 1] == 'O') &&
               (data[offset + 2] == 's' || data[offset + 2] == 'S') &&
               (data[offset + 3] == 't' || data[offset + 3] == 'T') &&
               data[offset + 4] == ':';
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> data, string prefix)
    {
        if (data.Length < prefix.Length)
            return false;

        for (int i = 0; i < prefix.Length; i++)
        {
            if (data[i] != (byte)prefix[i])
                return false;
        }

        return true;
    }
}
