using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace TunnelFlow.Capture.TransparentProxy;

/// <summary>
/// Performs RFC 1928 SOCKS5 handshake (no-auth + CONNECT) against sing-box.
/// Returns a relay-ready <see cref="NetworkStream"/>.
/// </summary>
public static class Socks5Connector
{
    private const byte Version = 0x05;
    private const byte NoAuth = 0x00;
    private const byte CmdConnect = 0x01;
    private const byte AddrTypeIPv4 = 0x01;
    private const byte AddrTypeIPv6 = 0x04;
    private const byte ReplySuccess = 0x00;

    public static async Task<NetworkStream> ConnectAsync(
        IPEndPoint socksServer,
        IPEndPoint destination,
        CancellationToken ct = default)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        try
        {
            await socket.ConnectAsync(socksServer, ct);
            var stream = new NetworkStream(socket, ownsSocket: true);

            await NegotiateAuthAsync(stream, ct);
            await SendConnectRequestAsync(stream, destination, ct);
            await ReadConnectResponseAsync(stream, ct);

            return stream;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task NegotiateAuthAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] greeting = [Version, 0x01, NoAuth];
        await stream.WriteAsync(greeting, ct);

        var response = new byte[2];
        await ReadExactAsync(stream, response, ct);

        if (response[0] != Version)
            throw new Socks5Exception($"Unexpected SOCKS version: {response[0]}");

        if (response[1] != NoAuth)
            throw new Socks5Exception($"SOCKS5 server rejected no-auth, returned method: {response[1]}");
    }

    private static async Task SendConnectRequestAsync(
        NetworkStream stream, IPEndPoint destination, CancellationToken ct)
    {
        byte[] destAddrBytes = destination.Address.GetAddressBytes();
        bool isIpv6 = destination.Address.AddressFamily == AddressFamily.InterNetworkV6;
        byte addrType = isIpv6 ? AddrTypeIPv6 : AddrTypeIPv4;

        var request = new byte[4 + destAddrBytes.Length + 2];
        request[0] = Version;
        request[1] = CmdConnect;
        request[2] = 0x00;
        request[3] = addrType;

        destAddrBytes.CopyTo(request.AsSpan(4));

        BinaryPrimitives.WriteUInt16BigEndian(
            request.AsSpan(4 + destAddrBytes.Length),
            (ushort)destination.Port);

        await stream.WriteAsync(request, ct);
    }

    private static async Task ReadConnectResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await ReadExactAsync(stream, header, ct);

        if (header[0] != Version)
            throw new Socks5Exception($"Unexpected SOCKS version in response: {header[0]}");

        if (header[1] != ReplySuccess)
            throw new Socks5Exception($"SOCKS5 CONNECT failed with code: {header[1]} ({GetErrorMessage(header[1])})");

        int addrLen = header[3] switch
        {
            AddrTypeIPv4 => 4,
            AddrTypeIPv6 => 16,
            _ => throw new Socks5Exception($"Unknown address type in response: {header[3]}")
        };

        var remaining = new byte[addrLen + 2];
        await ReadExactAsync(stream, remaining, ct);
    }

    internal static async Task ReadExactAsync(
        NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset), ct);

            if (read == 0)
                throw new Socks5Exception("SOCKS5 connection closed unexpectedly during handshake");

            offset += read;
        }
    }

    /// <summary>Builds the raw SOCKS5 CONNECT request bytes for testing.</summary>
    internal static byte[] BuildConnectRequest(IPEndPoint destination)
    {
        byte[] destAddrBytes = destination.Address.GetAddressBytes();
        bool isIpv6 = destination.Address.AddressFamily == AddressFamily.InterNetworkV6;
        byte addrType = isIpv6 ? AddrTypeIPv6 : AddrTypeIPv4;

        var request = new byte[4 + destAddrBytes.Length + 2];
        request[0] = Version;
        request[1] = CmdConnect;
        request[2] = 0x00;
        request[3] = addrType;
        destAddrBytes.CopyTo(request.AsSpan(4));
        BinaryPrimitives.WriteUInt16BigEndian(
            request.AsSpan(4 + destAddrBytes.Length),
            (ushort)destination.Port);
        return request;
    }

    private static string GetErrorMessage(byte code) => code switch
    {
        0x01 => "general SOCKS server failure",
        0x02 => "connection not allowed by ruleset",
        0x03 => "network unreachable",
        0x04 => "host unreachable",
        0x05 => "connection refused",
        0x06 => "TTL expired",
        0x07 => "command not supported",
        0x08 => "address type not supported",
        _ => "unknown error"
    };
}

public class Socks5Exception : Exception
{
    public Socks5Exception(string message) : base(message) { }
}
