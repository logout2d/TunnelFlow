using System.Net.Sockets;

namespace TunnelFlow.Capture.TransparentProxy;

public static class ProtocolSniffer
{
    private const int PeekBufferSize = 4096;
    private static readonly TimeSpan PeekTimeout = TimeSpan.FromSeconds(5);

    public static async Task<SniffResult> SniffAsync(NetworkStream stream, CancellationToken ct = default)
    {
        var buffer = new byte[PeekBufferSize];
        int totalRead = 0;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(PeekTimeout);

            totalRead = await stream.ReadAsync(buffer.AsMemory(0, PeekBufferSize), timeoutCts.Token);
            if (totalRead == 0)
            {
                return new SniffResult
                {
                    Protocol = SniffedProtocol.Unknown,
                    BufferedData = buffer,
                    BufferedLength = 0
                };
            }

            if (totalRead >= 5 && buffer[0] == 0x16)
            {
                int recordLength = (buffer[3] << 8) | buffer[4];
                int expectedTotal = recordLength + 5;

                while (totalRead < expectedTotal && totalRead < PeekBufferSize)
                {
                    if (!stream.DataAvailable)
                    {
                        await Task.Delay(10, timeoutCts.Token);
                        if (!stream.DataAvailable)
                            break;
                    }

                    int read = await stream.ReadAsync(
                        buffer.AsMemory(totalRead, PeekBufferSize - totalRead), timeoutCts.Token);
                    if (read == 0)
                        break;

                    totalRead += read;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // ignore timeout and keep buffered bytes
        }

        if (TlsSniSniffer.LooksLikeTls(buffer.AsSpan(0, totalRead)))
        {
            return new SniffResult
            {
                Domain = TlsSniSniffer.ExtractSni(buffer.AsSpan(0, totalRead)),
                Protocol = SniffedProtocol.TLS,
                BufferedData = buffer,
                BufferedLength = totalRead
            };
        }

        if (HttpHostSniffer.LooksLikeHttp(buffer.AsSpan(0, totalRead)))
        {
            return new SniffResult
            {
                Domain = HttpHostSniffer.ExtractHost(buffer.AsSpan(0, totalRead)),
                Protocol = SniffedProtocol.HTTP,
                BufferedData = buffer,
                BufferedLength = totalRead
            };
        }

        return new SniffResult
        {
            Protocol = SniffedProtocol.Unknown,
            BufferedData = buffer,
            BufferedLength = totalRead
        };
    }
}
