using System.Net.Sockets;

namespace TunnelFlow.Capture.TransparentProxy;

public static class ProtocolSniffer
{
    private const int PeekBufferSize = 4096;
    private const int InitialDetectionBytes = 16;
    private static readonly TimeSpan PeekTimeout = TimeSpan.FromSeconds(5);

    public static async Task<SniffResult> SniffAsync(NetworkStream stream, CancellationToken ct = default)
    {
        var buffer = new byte[PeekBufferSize];
        int totalRead = 0;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(PeekTimeout);

            totalRead = await ReadUntilAsync(
                stream,
                buffer,
                totalRead,
                Math.Min(InitialDetectionBytes, PeekBufferSize),
                timeoutCts.Token);

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
                int expectedTotal = Math.Min(recordLength + 5, PeekBufferSize);
                totalRead = await ReadUntilAsync(
                    stream,
                    buffer,
                    totalRead,
                    expectedTotal,
                    timeoutCts.Token);
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

    private static async Task<int> ReadUntilAsync(
        NetworkStream stream,
        byte[] buffer,
        int totalRead,
        int targetLength,
        CancellationToken ct)
    {
        while (totalRead < targetLength && totalRead < buffer.Length)
        {
            if (!stream.DataAvailable)
            {
                await Task.Delay(10, ct);
                continue;
            }

            int read = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0)
                break;

            totalRead += read;
        }

        return totalRead;
    }
}
