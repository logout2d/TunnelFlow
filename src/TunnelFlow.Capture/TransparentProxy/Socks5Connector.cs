using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

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
    private const byte AddrTypeDomain = 0x03;
    private const byte AddrTypeIPv6 = 0x04;
    private const byte ReplySuccess = 0x00;

    public static async Task<NetworkStream> ConnectByDomainAsync(
        IPEndPoint socksServer,
        string domain,
        int port,
        CancellationToken ct = default,
        ILogger? logger = null)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        string targetContext = $"{domain}:{port}";

        try
        {
            logger?.LogInformation("SOCKS connect-start socks={SocksServer} target={Target} mode=domain", socksServer, targetContext);
            await socket.ConnectAsync(socksServer, ct);
            var stream = new NetworkStream(socket, ownsSocket: true);

            logger?.LogInformation("SOCKS tcp-connected socks={SocksServer} target={Target}", socksServer, targetContext);
            await NegotiateAuthAsync(stream, ct, logger, targetContext);
            await SendDomainConnectRequestAsync(stream, domain, port, ct, logger, targetContext);
            await ReadConnectResponseAsync(stream, ct, logger, targetContext);
            logger?.LogInformation("SOCKS connect-established target={Target}", targetContext);

            return stream;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SOCKS connect-failed target={Target}", targetContext);
            socket.Dispose();
            throw;
        }
    }

    public static async Task<NetworkStream> ConnectByIpAsync(
        IPEndPoint socksServer,
        IPEndPoint destination,
        CancellationToken ct = default,
        ILogger? logger = null)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        string targetContext = destination.ToString();

        try
        {
            logger?.LogInformation("SOCKS connect-start socks={SocksServer} target={Target} mode=ip", socksServer, targetContext);
            await socket.ConnectAsync(socksServer, ct);
            var stream = new NetworkStream(socket, ownsSocket: true);

            logger?.LogInformation("SOCKS tcp-connected socks={SocksServer} target={Target}", socksServer, targetContext);
            await NegotiateAuthAsync(stream, ct, logger, targetContext);
            await SendIpConnectRequestAsync(stream, destination, ct, logger, targetContext);
            await ReadConnectResponseAsync(stream, ct, logger, targetContext);
            logger?.LogInformation("SOCKS connect-established target={Target}", targetContext);

            return stream;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SOCKS connect-failed target={Target}", targetContext);
            socket.Dispose();
            throw;
        }
    }

    private static async Task NegotiateAuthAsync(
        NetworkStream stream,
        CancellationToken ct,
        ILogger? logger,
        string targetContext)
    {
        byte[] greeting = [Version, 0x01, NoAuth];
        await stream.WriteAsync(greeting, ct);

        var response = new byte[2];
        await ReadExactAsync(stream, response, ct);

        if (response[0] != Version)
            throw new Socks5Exception($"Unexpected SOCKS version: {response[0]}");

        if (response[1] != NoAuth)
            throw new Socks5Exception($"SOCKS5 server rejected no-auth, returned method: {response[1]}");

        logger?.LogInformation("SOCKS auth-ok target={Target} method=no-auth", targetContext);
    }

    private static async Task SendDomainConnectRequestAsync(
        NetworkStream stream,
        string domain,
        int port,
        CancellationToken ct,
        ILogger? logger,
        string targetContext)
    {
        byte[] domainBytes = Encoding.ASCII.GetBytes(domain);
        if (domainBytes.Length > 255)
            throw new Socks5Exception($"Domain too long: {domain} ({domainBytes.Length} bytes)");

        var request = new byte[4 + 1 + domainBytes.Length + 2];
        request[0] = Version;
        request[1] = CmdConnect;
        request[2] = 0x00;
        request[3] = AddrTypeDomain;
        request[4] = (byte)domainBytes.Length;
        domainBytes.CopyTo(request.AsSpan(5));

        BinaryPrimitives.WriteUInt16BigEndian(
            request.AsSpan(5 + domainBytes.Length),
            (ushort)port);

        await stream.WriteAsync(request, ct);
        logger?.LogInformation("SOCKS connect-request target={Target} atyp=domain", targetContext);
    }

    private static async Task SendIpConnectRequestAsync(
        NetworkStream stream,
        IPEndPoint destination,
        CancellationToken ct,
        ILogger? logger,
        string targetContext)
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
        logger?.LogInformation(
            "SOCKS connect-request target={Target} atyp={AddressType}",
            targetContext,
            isIpv6 ? "ipv6" : "ipv4");
    }

    private static async Task ReadConnectResponseAsync(
        NetworkStream stream,
        CancellationToken ct,
        ILogger? logger,
        string targetContext)
    {
        var header = new byte[4];
        await ReadExactAsync(stream, header, ct);

        if (header[0] != Version)
            throw new Socks5Exception($"Unexpected SOCKS version in response: {header[0]}");

        if (header[1] != ReplySuccess)
            throw new Socks5Exception($"SOCKS5 CONNECT failed with code: {header[1]} ({GetErrorMessage(header[1])})");

        int initialRemainingLength = GetConnectResponseInitialRemainingLength(header[3]);
        var remaining = new byte[initialRemainingLength];
        await ReadExactAsync(stream, remaining, ct);

        if (header[3] == AddrTypeDomain)
        {
            int domainLength = remaining[0];
            var domainAndPort = new byte[domainLength + 2];
            await ReadExactAsync(stream, domainAndPort, ct);
        }

        logger?.LogInformation(
            "SOCKS connect-reply target={Target} atyp={AddressType}",
            targetContext,
            GetAddressTypeName(header[3]));
    }

    internal static int GetConnectResponseInitialRemainingLength(byte addrType) => addrType switch
    {
        AddrTypeIPv4 => 4 + 2,
        AddrTypeDomain => 1,
        AddrTypeIPv6 => 16 + 2,
        _ => throw new Socks5Exception($"Unknown address type in response: {addrType}")
    };

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

    internal static byte[] BuildDomainConnectRequest(string domain, int port)
    {
        byte[] domainBytes = Encoding.ASCII.GetBytes(domain);
        var request = new byte[4 + 1 + domainBytes.Length + 2];
        request[0] = Version;
        request[1] = CmdConnect;
        request[2] = 0x00;
        request[3] = AddrTypeDomain;
        request[4] = (byte)domainBytes.Length;
        domainBytes.CopyTo(request.AsSpan(5));
        BinaryPrimitives.WriteUInt16BigEndian(
            request.AsSpan(5 + domainBytes.Length),
            (ushort)port);
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

    private static string GetAddressTypeName(byte code) => code switch
    {
        AddrTypeIPv4 => "ipv4",
        AddrTypeDomain => "domain",
        AddrTypeIPv6 => "ipv6",
        _ => $"unknown:{code}"
    };
}

public class Socks5Exception : Exception
{
    public Socks5Exception(string message) : base(message) { }
}
