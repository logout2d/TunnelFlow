# TunnelFlow: SNI Sniffing — Solving the Domain Resolution Problem

## The problem and the solution

---

## 1. Essence of the problem

```text
LocalRelay receives a TCP connection from WinpkFilter
    → NAT table provides IP: 142.250.74.46:443
    → SOCKS5 CONNECT 142.250.74.46:443
    → sing-box connects to the VLESS server
    → The VLESS server performs TLS to 142.250.74.46
    → BUT: without SNI, the server does not know which domain is being requested
    → REALITY cannot inject the correct SNI
    → The connection is not established
```

**Root cause**: VLESS REALITY uses SNI (Server Name Indication) from TLS to camouflage
traffic. When sing-box receives a SOCKS5 CONNECT with a bare IP instead of a domain,
it cannot correctly form the TLS handshake on the remote server.

---

## 2. Why SNI sniffing is the right answer

### Comparing approaches

```text
┌──────────────────┬──────────────────────────────────────────────────┐
│   DNS Sniffing   │  • Browser/OS DNS cache — race condition        │
│   (Option 1)     │  • CDN: one domain → many IPs                   │
│                  │  • Round-robin DNS: IP changes between requests │
│    REJECTED      │  • DoH/DoT fully bypass UDP interception        │
│                  │  • No guarantee the DNS query arrives before TCP│
├──────────────────┼──────────────────────────────────────────────────┤
│ Custom Protocol  │  • Requires a sing-box fork or custom inbound   │
│   (Option 3)     │  • Incompatible with the ecosystem              │
│                  │  • Additional attack surface                    │
│    REJECTED      │  • Violates the principle: do not modify        │
│                  │    sing-box                                     │
├──────────────────┼──────────────────────────────────────────────────┤
│  TLS SNI Sniff   │  ✓ The domain is taken from the TLS connection  │
│   (Option 2)     │  ✓ 100% accuracy — it is the exact same domain  │
│                  │    the browser uses                             │
│   CHOSEN ✓       │  ✓ Does not depend on DNS (DoH, cache irrelevant)│
│                  │  ✓ SOCKS5 natively supports domain CONNECT      │
│                  │  ✓ sing-box is not modified                     │
│                  │  ✓ HTTP Host header as a bonus for port 80      │
└──────────────────┴──────────────────────────────────────────────────┘
```

### Key observation

TLS ClientHello is the **first** data packet from the client after the TCP handshake.
LocalRelay can read these bytes, extract the SNI, and **only then**
establish the SOCKS5 connection to the domain. The data is buffered and then
forwarded — the client does not notice the delay.

---

## 3. Updated architecture

### Data flow with SNI sniffing

```text
┌─────────────────────────────────────────────────────────────────────────┐
│                  TunnelFlow Pipeline (with SNI Sniffing)               │
│                                                                        │
│  Browser        WinpkFilter       LocalRelay              sing-box     │
│  ────────       ───────────       ──────────              ────────     │
│     │                │                 │                      │        │
│     │ TCP SYN to     │                 │                      │        │
│     │ 142.250.x:443  │                 │                      │        │
│     ├───────────────►│                 │                      │        │
│     │                │                 │                      │        │
│     │                │ NAT: save dst   │                      │        │
│     │                │ Rewrite → :2070 │                      │        │
│     │                ├────────────────►│                      │        │
│     │                │                 │                      │        │
│     │  ◄──── TCP handshake ───────────►│                      │        │
│     │                │                 │                      │        │
│     │ TLS ClientHello│                 │                      │        │
│     │ (SNI=google.com)                 │                      │        │
│     ├─────────────────────────────────►│                      │        │
│     │                │                 │                      │        │
│     │                │                 │ ┌─────────────────┐  │        │
│     │                │                 │ │ PEEK first bytes │ │        │
│     │                │                 │ │ Parse TLS Hello  │ │        │
│     │                │                 │ │ Extract SNI:     │ │        │
│     │                │                 │ │  "google.com"    │ │        │
│     │                │                 │ │ Buffer the data  │ │        │
│     │                │                 │ └─────────────────┘  │        │
│     │                │                 │                      │        │
│     │                │                 │ SOCKS5 CONNECT       │        │
│     │                │                 │ google.com:443       │        │
│     │                │                 │ (ATYP=0x03 DOMAIN)   │        │
│     │                │                 ├─────────────────────►│        │
│     │                │                 │                      │        │
│     │                │                 │ SOCKS5 OK            │        │
│     │                │                 │◄─────────────────────┤        │
│     │                │                 │                      │        │
│     │                │                 │ Forward buffered     │        │
│     │                │                 │ ClientHello bytes    │        │
│     │                │                 ├─────────────────────►│        │
│     │                │                 │                      │        │
│     │  ◄──────── bidirectional relay ────────────────────────►│        │
│     │                │                 │                      │        │
│     │                │                 │               VLESS REALITY   │
│     │                │                 │               SNI=google.com  │
│     │                │                 │               ──────────────► │
│                                                                        │
└─────────────────────────────────────────────────────────────────────────┘
```

### What changed

```text
BEFORE: Accept TCP → Lookup NAT (IP) → SOCKS5 CONNECT IP:port → Relay
AFTER:  Accept TCP → Peek first bytes → Parse SNI/Host →
        SOCKS5 CONNECT domain:port (ATYP=0x03) → Send buffered bytes → Relay
```

---

## 4. Protocol: how it works at the byte level

### TLS ClientHello — structure (simplified)

```text
Byte 0:     ContentType     = 0x16 (Handshake)
Byte 1-2:   ProtocolVersion = 0x0301 (TLS 1.0 in the record, even for TLS 1.3)
Byte 3-4:   Length          = record length
Byte 5:     HandshakeType   = 0x01 (ClientHello)
Byte 6-8:   HandshakeLength
Byte 9-10:  ClientVersion
Byte 11-42: Random (32 bytes)
Byte 43:    SessionIdLength
...         SessionId
...         CipherSuites
...         CompressionMethods
...         Extensions        ← SNI is here
```

### SNI Extension

```text
Extension Type = 0x0000 (server_name)
Extension Data:
  ServerNameList Length (2 bytes)
  ServerName Type = 0x00 (host_name)
  HostName Length (2 bytes)
  HostName (UTF-8 string) ← THIS IS WHAT WE NEED: "google.com"
```

### HTTP Host Header

```text
GET / HTTP/1.1\r\n
Host: example.com\r\n    ← THIS IS WHAT WE NEED for HTTP
\r\n
```

### SOCKS5 Domain CONNECT (ATYP=0x03)

```text
BEFORE (IP):
  05 01 00 01 8E FA 4A 2E 01 BB
  ││ ││ ││ ││ ├──────────┤ ├──┤
  ││ ││ ││ ││ 142.250.74.46 443
  ││ ││ ││ └─ ATYP=0x01 (IPv4)
  VER CMD RSV

AFTER (Domain):
  05 01 00 03 0A 67 6F 6F 67 6C 65 2E 63 6F 6D 01 BB
  ││ ││ ││ ││ ││ ├────────────────────────────┤ ├──┤
  ││ ││ ││ ││ ││ g  o  o  g  l  e  .  c  o  m   443
  ││ ││ ││ ││ └─ domain length (10)
  ││ ││ ││ └─ ATYP=0x03 (Domain)
  VER CMD RSV
```

---

## 5. Implementation

### 5.1 SniffResult.cs — protocol analysis result

```csharp
namespace TunnelFlow.Core;

/// <summary>
/// Result of analyzing the first bytes of a TCP connection.
/// Contains the detected domain (if any) and the buffered bytes that were read.
/// </summary>
public sealed class SniffResult
{
    /// <summary>Detected domain (from TLS SNI or HTTP Host)</summary>
    public string? Domain { get; init; }

    /// <summary>Detected protocol</summary>
    public SniffedProtocol Protocol { get; init; }

    /// <summary>Buffered bytes that must be forwarded to sing-box after the SOCKS5 handshake</summary>
    public required byte[] BufferedData { get; init; }

    /// <summary>Number of valid bytes in the buffer</summary>
    public int BufferedLength { get; init; }

    public bool HasDomain => !string.IsNullOrEmpty(Domain);
}

public enum SniffedProtocol
{
    Unknown,
    TLS,
    HTTP
}
```

### 5.2 TlsSniSniffer.cs — TLS ClientHello parser

```csharp
using System.Text;

namespace TunnelFlow.Core;

/// <summary>
/// Extracts SNI (Server Name Indication) from a TLS ClientHello.
/// 
/// Parses the minimum necessary part of the TLS record:
/// - Record Header (5 bytes)
/// - Handshake Header (4 bytes) 
/// - ClientHello fields up to Extensions
/// - Looks for extension type 0x0000 (server_name)
/// - Extracts the hostname
/// 
/// Does not fully validate TLS — only what is needed for SNI.
/// Works with TLS 1.0/1.1/1.2/1.3 (SNI is the same in all versions).
/// </summary>
public static class TlsSniSniffer
{
    // TLS constants
    private const byte ContentTypeHandshake = 0x16;
    private const byte HandshakeTypeClientHello = 0x01;
    private const ushort ExtensionServerName = 0x0000;
    private const byte ServerNameTypeHostname = 0x00;

    /// <summary>
    /// Checks whether the first bytes look like a TLS record.
    /// Fast check before full parsing.
    /// </summary>
    public static bool LooksLikeTls(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
            return false;

        // Byte 0: ContentType = 0x16 (Handshake)
        if (data[0] != ContentTypeHandshake)
            return false;

        // Byte 1-2: Version >= 0x0301 (TLS 1.0+)
        // Some implementations send 0x0301 even for TLS 1.3
        if (data[1] != 0x03 || data[2] < 0x01)
            return false;

        // Byte 5: HandshakeType = 0x01 (ClientHello)
        if (data[5] != HandshakeTypeClientHello)
            return false;

        return true;
    }

    /// <summary>
    /// Extracts the SNI hostname from a TLS ClientHello.
    /// Returns null if SNI is not found or the data is invalid.
    /// 
    /// Does not throw exceptions — returns null on any parsing error.
    /// </summary>
    public static string? ExtractSni(ReadOnlySpan<byte> data)
    {
        try
        {
            return ExtractSniInternal(data);
        }
        catch
        {
            // Any parsing error means SNI was not found
            return null;
        }
    }

    private static string? ExtractSniInternal(ReadOnlySpan<byte> data)
    {
        // Minimum ClientHello: 5 (record) + 4 (handshake) + 2 (version) 
        // + 32 (random) + 1 (session_id_len) = 44 bytes
        if (data.Length < 44)
            return null;

        int pos = 0;

        // === TLS Record Header (5 bytes) ===
        // ContentType(1) + Version(2) + Length(2)
        if (data[pos] != ContentTypeHandshake)
            return null;
        pos += 5; // skip record header

        // === Handshake Header (4 bytes) ===
        // HandshakeType(1) + Length(3)
        if (data[pos] != HandshakeTypeClientHello)
            return null;
        pos += 4; // skip handshake header

        // === ClientHello Body ===
        // ClientVersion (2 bytes)
        pos += 2;

        // Random (32 bytes)
        pos += 32;

        // Session ID
        if (pos >= data.Length)
            return null;
        int sessionIdLen = data[pos];
        pos += 1 + sessionIdLen;

        // Cipher Suites
        if (pos + 2 > data.Length)
            return null;
        int cipherSuitesLen = ReadUInt16(data, pos);
        pos += 2 + cipherSuitesLen;

        // Compression Methods
        if (pos >= data.Length)
            return null;
        int compressionLen = data[pos];
        pos += 1 + compressionLen;

        // === Extensions ===
        if (pos + 2 > data.Length)
            return null;
        int extensionsLen = ReadUInt16(data, pos);
        pos += 2;

        int extensionsEnd = pos + extensionsLen;
        if (extensionsEnd > data.Length)
            extensionsEnd = data.Length; // Read whatever is available

        // Iterate over extensions, looking for SNI (type 0x0000)
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

        return null; // SNI extension not found
    }

    /// <summary>
    /// Parses the contents of the SNI extension.
    /// 
    /// Format:
    ///   ServerNameList Length (2)
    ///   ServerName Type (1) = 0x00 for host_name
    ///   HostName Length (2)
    ///   HostName (UTF-8)
    /// </summary>
    private static string? ParseSniExtension(ReadOnlySpan<byte> extData)
    {
        if (extData.Length < 5)
            return null;

        int pos = 0;

        // Server Name List Length
        int listLen = ReadUInt16(extData, pos);
        pos += 2;

        int listEnd = pos + listLen;
        if (listEnd > extData.Length)
            listEnd = extData.Length;

        // There may be multiple server names; take the first host_name
        while (pos + 3 <= listEnd)
        {
            byte nameType = extData[pos];
            int nameLen = ReadUInt16(extData, pos + 1);
            pos += 3;

            if (pos + nameLen > listEnd)
                return null;

            if (nameType == ServerNameTypeHostname && nameLen > 0)
            {
                string hostname = Encoding.ASCII.GetString(
                    extData.Slice(pos, nameLen));

                // Basic validation
                if (IsValidHostname(hostname))
                    return hostname;
            }

            pos += nameLen;
        }

        return null;
    }

    /// <summary>
    /// Basic hostname validation — protection against garbage input.
    /// </summary>
    private static bool IsValidHostname(string hostname)
    {
        if (string.IsNullOrEmpty(hostname) || hostname.Length > 253)
            return false;

        // Must contain at least one dot (except localhost)
        // and consist of valid characters
        foreach (char c in hostname)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-')
                return false;
        }

        // Must not start or end with dot/hyphen
        if (hostname[0] == '.' || hostname[0] == '-')
            return false;
        if (hostname[^1] == '-')
            return false;

        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }
}
```

### 5.3 HttpHostSniffer.cs — HTTP Host parser

```csharp
using System.Text;

namespace TunnelFlow.Core;

/// <summary>
/// Extracts the Host header from an HTTP/1.x request.
/// Used as a fallback for plain HTTP (port 80) connections.
/// </summary>
public static class HttpHostSniffer
{
    private static readonly byte[] HostHeaderBytes =
        "host:"u8.ToArray();

    /// <summary>
    /// Checks whether the data looks like an HTTP request.
    /// </summary>
    public static bool LooksLikeHttp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
            return false;

        // HTTP methods start with known strings
        return StartsWithAscii(data, "GET ") ||
               StartsWithAscii(data, "POST ") ||
               StartsWithAscii(data, "PUT ") ||
               StartsWithAscii(data, "DELETE ") ||
               StartsWithAscii(data, "HEAD ") ||
               StartsWithAscii(data, "OPTIONS ") ||
               StartsWithAscii(data, "PATCH ") ||
               StartsWithAscii(data, "CONNECT ");
    }

    /// <summary>
    /// Extracts the Host header value from an HTTP request.
    /// Returns only the hostname (without the port).
    /// </summary>
    public static string? ExtractHost(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
            return null;

        // Look for "Host:" header (case-insensitive)
        // HTTP headers end with \r\n\r\n
        for (int i = 0; i < data.Length - 6; i++)
        {
            // Look for start of line (after \r\n or start of data after the first line)
            if (i > 0 && !(data[i - 1] == '\n'))
                continue;

            // Check "Host:" (case-insensitive)
            if (!MatchesHostHeader(data, i))
                continue;

            // Found "Host:", extract the value
            int valueStart = i + 5; // after "Host:"

            // Skip spaces
            while (valueStart < data.Length && data[valueStart] == ' ')
                valueStart++;

            // Read until \r or \n
            int valueEnd = valueStart;
            while (valueEnd < data.Length && data[valueEnd] != '\r' && data[valueEnd] != '\n')
                valueEnd++;

            if (valueEnd <= valueStart)
                return null;

            string hostValue = Encoding.ASCII.GetString(
                data.Slice(valueStart, valueEnd - valueStart));

            // Remove port if present (Host: example.com:8080 → example.com)
            int colonIdx = hostValue.LastIndexOf(':');
            if (colonIdx > 0)
            {
                // Check that everything after the colon is digits (port, not IPv6)
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

        // Case-insensitive "Host:"
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
```

### 5.4 ProtocolSniffer.cs — unified facade

```csharp
using System.Net.Sockets;

namespace TunnelFlow.Core;

/// <summary>
/// Reads the first bytes of a TCP connection and determines the protocol + domain.
/// 
/// Strategy:
/// 1. Read the first N bytes (peek, do not consume semantically)
/// 2. Try to recognize TLS → extract SNI
/// 3. Try to recognize HTTP → extract Host
/// 4. If nothing is found → fall back to IP from the NAT table
/// 
/// The read bytes are saved in a buffer — they must be forwarded 
/// to sing-box after the SOCKS5 connection is established.
/// </summary>
public static class ProtocolSniffer
{
    // ClientHello is usually 200-600 bytes, but can be up to ~16 KB 
    // with a large list of cipher suites.
    // 4096 covers 99.9% of real ClientHello messages.
    private const int PeekBufferSize = 4096;

    // Timeout for reading the first bytes.
    // If the client sends no data in time — fall back to IP.
    private static readonly TimeSpan PeekTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Reads the first bytes from the TCP stream and tries to determine the protocol/domain.
    /// 
    /// IMPORTANT: the read bytes are NOT pushed back into the stream. 
    /// They are stored in SniffResult.BufferedData and must be sent 
    /// to sing-box after the SOCKS5 handshake.
    /// </summary>
    public static async Task<SniffResult> SniffAsync(
        NetworkStream stream,
        CancellationToken ct = default)
    {
        var buffer = new byte[PeekBufferSize];
        int totalRead = 0;

        try
        {
            // Read with timeout — the client may not send data immediately
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(PeekTimeout);

            // First read — get whatever is available
            totalRead = await stream.ReadAsync(
                buffer.AsMemory(0, PeekBufferSize),
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

            // For TLS: ClientHello may arrive in several TCP segments.
            // If we got the start of the TLS record but not the whole thing — read more.
            if (totalRead >= 5 && buffer[0] == 0x16)
            {
                int recordLength = (buffer[3] << 8) | buffer[4];
                int expectedTotal = recordLength + 5; // 5 bytes record header

                // Read the rest if needed (but no more than the buffer)
                while (totalRead < expectedTotal && totalRead < PeekBufferSize)
                {
                    if (!stream.DataAvailable)
                    {
                        // Wait a bit — data may still be in flight
                        await Task.Delay(10, timeoutCts.Token);
                        if (!stream.DataAvailable)
                            break;
                    }

                    int read = await stream.ReadAsync(
                        buffer.AsMemory(totalRead, PeekBufferSize - totalRead),
                        timeoutCts.Token);

                    if (read == 0)
                        break;

                    totalRead += read;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Peek timeout — not an error, continue with what we have
        }

        var dataSpan = buffer.AsSpan(0, totalRead);

        // Try TLS
        if (TlsSniSniffer.LooksLikeTls(dataSpan))
        {
            string? sni = TlsSniSniffer.ExtractSni(dataSpan);
            return new SniffResult
            {
                Domain = sni,
                Protocol = SniffedProtocol.TLS,
                BufferedData = buffer,
                BufferedLength = totalRead
            };
        }

        // Try HTTP
        if (HttpHostSniffer.LooksLikeHttp(dataSpan))
        {
            string? host = HttpHostSniffer.ExtractHost(dataSpan);
            return new SniffResult
            {
                Domain = host,
                Protocol = SniffedProtocol.HTTP,
                BufferedData = buffer,
                BufferedLength = totalRead
            };
        }

        // Unknown protocol — fall back to IP
        return new SniffResult
        {
            Protocol = SniffedProtocol.Unknown,
            BufferedData = buffer,
            BufferedLength = totalRead
        };
    }
}
```

### 5.5 Socks5Connector.cs — updated with Domain CONNECT support

```csharp
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TunnelFlow.Core;

/// <summary>
/// SOCKS5 client with support for three CONNECT modes:
/// - IPv4   (ATYP=0x01)
/// - IPv6   (ATYP=0x04)  
/// - Domain (ATYP=0x03) ← NEW: for SNI-aware proxying
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

    /// <summary>
    /// SOCKS5 CONNECT by domain (ATYP=0x03).
    /// sing-box receives the domain and uses it for SNI in VLESS REALITY.
    /// </summary>
    public static async Task<NetworkStream> ConnectByDomainAsync(
        IPEndPoint socksServer,
        string domain,
        int port,
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
            await SendDomainConnectRequestAsync(stream, domain, port, ct);
            await ReadConnectResponseAsync(stream, ct);

            return stream;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// SOCKS5 CONNECT by IP (ATYP=0x01/0x04).
    /// Fallback when the domain is unknown.
    /// </summary>
    public static async Task<NetworkStream> ConnectByIpAsync(
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
            await SendIpConnectRequestAsync(stream, destination, ct);
            await ReadConnectResponseAsync(stream, ct);

            return stream;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // ──────────────── Auth ────────────────

    private static async Task NegotiateAuthAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] greeting = [Version, 0x01, NoAuth];
        await stream.WriteAsync(greeting, ct);

        var response = new byte[2];
        await ReadExactAsync(stream, response, ct);

        if (response[0] != Version)
            throw new Socks5Exception($"Unexpected SOCKS version: {response[0]}");

        if (response[1] != NoAuth)
            throw new Socks5Exception($"SOCKS5 auth rejected, method: {response[1]}");
    }

    // ──────────────── Domain CONNECT (ATYP=0x03) ────────────────

    /// <summary>
    /// Format: VER(1) CMD(1) RSV(1) ATYP(1)=0x03 LEN(1) DOMAIN(N) PORT(2)
    /// 
    /// The key difference from IP: the address is a length-prefixed ASCII string.
    /// sing-box will receive the domain and perform DNS + TLS with the correct SNI.
    /// </summary>
    private static async Task SendDomainConnectRequestAsync(
        NetworkStream stream, string domain, int port, CancellationToken ct)
    {
        byte[] domainBytes = Encoding.ASCII.GetBytes(domain);

        if (domainBytes.Length > 255)
            throw new Socks5Exception($"Domain too long: {domain} ({domainBytes.Length} bytes)");

        // VER(1) + CMD(1) + RSV(1) + ATYP(1) + LEN(1) + DOMAIN(N) + PORT(2)
        var request = new byte[4 + 1 + domainBytes.Length + 2];
        request[0] = Version;
        request[1] = CmdConnect;
        request[2] = 0x00; // RSV
        request[3] = AddrTypeDomain;
        request[4] = (byte)domainBytes.Length;

        domainBytes.CopyTo(request.AsSpan(5));

        BinaryPrimitives.WriteUInt16BigEndian(
            request.AsSpan(5 + domainBytes.Length),
            (ushort)port);

        await stream.WriteAsync(request, ct);
    }

    // ──────────────── IP CONNECT (ATYP=0x01/0x04) ────────────────

    private static async Task SendIpConnectRequestAsync(
        NetworkStream stream, IPEndPoint destination, CancellationToken ct)
    {
        byte[] addrBytes = destination.Address.GetAddressBytes();
        bool isIpv6 = destination.Address.AddressFamily == AddressFamily.InterNetworkV6;
        byte addrType = isIpv6 ? AddrTypeIPv6 : AddrTypeIPv4;

        var request = new byte[4 + addrBytes.Length + 2];
        request[0] = Version;
        request[1] = CmdConnect;
        request[2] = 0x00;
        request[3] = addrType;

        addrBytes.CopyTo(request.AsSpan(4));

        BinaryPrimitives.WriteUInt16BigEndian(
            request.AsSpan(4 + addrBytes.Length),
            (ushort)destination.Port);

        await stream.WriteAsync(request, ct);
    }

    // ──────────────── Response ────────────────

    private static async Task ReadConnectResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await ReadExactAsync(stream, header, ct);

        if (header[0] != Version)
            throw new Socks5Exception($"Unexpected SOCKS version: {header[0]}");

        if (header[1] != ReplySuccess)
            throw new Socks5Exception(
                $"SOCKS5 CONNECT failed: code {header[1]} ({GetErrorMessage(header[1])})");

        int addrLen = header[3] switch
        {
            AddrTypeIPv4 => 4,
            AddrTypeIPv6 => 16,
            AddrTypeDomain => throw new Socks5Exception("Unexpected domain in response"),
            _ => throw new Socks5Exception($"Unknown addr type: {header[3]}")
        };

        var remaining = new byte[addrLen + 2];
        await ReadExactAsync(stream, remaining, ct);
    }

    // ──────────────── Utils ────────────────

    private static async Task ReadExactAsync(
        NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset), ct);

            if (read == 0)
                throw new Socks5Exception("Connection closed during handshake");

            offset += read;
        }
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
```

### 5.6 LocalRelay.cs — updated with SNI sniffing

```csharp
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace TunnelFlow.Core;

/// <summary>
/// LocalRelay v2 — with SNI sniffing.
/// 
/// New flow for each connection:
/// 1. Accept TCP from WinpkFilter
/// 2. Lookup NAT → get the original IP:port
/// 3. Peek the first bytes → sniff the protocol
/// 4. If TLS → extract SNI domain
/// 5. If HTTP → extract Host header
/// 6. SOCKS5 CONNECT with domain (ATYP=0x03) or IP (ATYP=0x01) as fallback
/// 7. Send the buffered bytes to sing-box
/// 8. Bidirectional relay
/// </summary>
public sealed class LocalRelay : IAsyncDisposable
{
    private readonly IPEndPoint _listenEndpoint;
    private readonly IPEndPoint _socksEndpoint;
    private readonly ILogger<LocalRelay> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _connectionThrottle;
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private Task? _cleanupLoop;
    private int _activeConnections;

    // Counters for monitoring
    private long _totalConnections;
    private long _sniResolved;
    private long _httpHostResolved;
    private long _ipFallback;

    private const int BufferSize = 65536;
    private const int MaxConcurrentConnections = 4096;
    private const int NatCleanupIntervalSec = 60;
    private const int NatEntryMaxAgeSec = 120;

    public int ActiveConnections => _activeConnections;
    public long TotalConnections => _totalConnections;
    public long SniResolved => _sniResolved;
    public long HttpHostResolved => _httpHostResolved;
    public long IpFallback => _ipFallback;

    public LocalRelay(
        int listenPort,
        int socksPort,
        ILogger<LocalRelay> logger)
    {
        _listenEndpoint = new IPEndPoint(IPAddress.Loopback, listenPort);
        _socksEndpoint = new IPEndPoint(IPAddress.Loopback, socksPort);
        _logger = logger;
        _connectionThrottle = new SemaphoreSlim(
            MaxConcurrentConnections, MaxConcurrentConnections);
    }

    public void Start()
    {
        _listener = new TcpListener(_listenEndpoint);
        _listener.Server.NoDelay = true;
        _listener.Server.SetSocketOption(
            SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start(backlog: 512);

        _logger.LogInformation(
            "LocalRelay v2 (SNI-aware) started on {Endpoint} → SOCKS5 {Socks}",
            _listenEndpoint, _socksEndpoint);

        _acceptLoop = AcceptLoopAsync(_cts.Token);
        _cleanupLoop = CleanupLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                _ = HandleConnectionAsync(client, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting connection");
                await Task.Delay(100, ct);
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        if (!await _connectionThrottle.WaitAsync(TimeSpan.FromSeconds(5), ct))
        {
            _logger.LogWarning("Connection throttled, dropping");
            client.Dispose();
            return;
        }

        Interlocked.Increment(ref _activeConnections);
        Interlocked.Increment(ref _totalConnections);
        IPEndPoint? clientEndpoint = null;

        try
        {
            clientEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
            if (clientEndpoint == null)
            {
                _logger.LogWarning("Could not determine client endpoint");
                return;
            }

            // 1. Lookup original destination (IP:port) from the NAT table
            var originalDest = NatTable.Instance.Lookup(clientEndpoint);
            if (originalDest == null)
            {
                _logger.LogWarning("No NAT entry for {Client}", clientEndpoint);
                return;
            }

            using var clientStream = client.GetStream();

            // 2. Sniff the first bytes — determine protocol and domain
            var sniffResult = await ProtocolSniffer.SniffAsync(clientStream, ct);

            // 3. Connect to sing-box through SOCKS5
            NetworkStream socksStream;

            if (sniffResult.HasDomain)
            {
                // Domain found — SOCKS5 CONNECT with domain (ATYP=0x03)
                socksStream = await Socks5Connector.ConnectByDomainAsync(
                    _socksEndpoint,
                    sniffResult.Domain!,
                    originalDest.Port,
                    ct);

                if (sniffResult.Protocol == SniffedProtocol.TLS)
                {
                    Interlocked.Increment(ref _sniResolved);
                    _logger.LogDebug(
                        "SNI: {Client} → {Domain}:{Port} (TLS SNI)",
                        clientEndpoint, sniffResult.Domain, originalDest.Port);
                }
                else
                {
                    Interlocked.Increment(ref _httpHostResolved);
                    _logger.LogDebug(
                        "Host: {Client} → {Domain}:{Port} (HTTP Host)",
                        clientEndpoint, sniffResult.Domain, originalDest.Port);
                }
            }
            else
            {
                // Domain not found — fallback to IP
                Interlocked.Increment(ref _ipFallback);
                socksStream = await Socks5Connector.ConnectByIpAsync(
                    _socksEndpoint, originalDest, ct);

                _logger.LogDebug(
                    "IP fallback: {Client} → {Destination} (proto={Protocol})",
                    clientEndpoint, originalDest, sniffResult.Protocol);
            }

            await using (socksStream)
            {
                // 4. Send the buffered data (ClientHello / HTTP request)
                if (sniffResult.BufferedLength > 0)
                {
                    await socksStream.WriteAsync(
                        sniffResult.BufferedData.AsMemory(0, sniffResult.BufferedLength),
                        ct);
                }

                // 5. Bidirectional relay for the remaining data
                await RelayAsync(clientStream, socksStream, ct);
            }
        }
        catch (Socks5Exception ex)
        {
            _logger.LogWarning("SOCKS5 error for {Client}: {Error}",
                clientEndpoint, ex.Message);
        }
        catch (IOException) { }
        catch (SocketException) { }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {Client}", clientEndpoint);
        }
        finally
        {
            client.Dispose();
            if (clientEndpoint != null)
                NatTable.Instance.Remove(clientEndpoint.Address, clientEndpoint.Port);
            Interlocked.Decrement(ref _activeConnections);
            _connectionThrottle.Release();
        }
    }

    /// <summary>
    /// Bidirectional relay — copying data in both directions.
    /// </summary>
    private static async Task RelayAsync(
        NetworkStream clientStream,
        NetworkStream socksStream,
        CancellationToken ct)
    {
        using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var c2s = CopyAsync(clientStream, socksStream, relayCts);
        var s2c = CopyAsync(socksStream, clientStream, relayCts);

        await Task.WhenAny(c2s, s2c);
        await relayCts.CancelAsync();

        try { await c2s; } catch { }
        try { await s2c; } catch { }
    }

    private static async Task CopyAsync(
        NetworkStream src, NetworkStream dst,
        CancellationTokenSource relayCts)
    {
        var buffer = new byte[BufferSize];
        try
        {
            while (!relayCts.Token.IsCancellationRequested)
            {
                int n = await src.ReadAsync(buffer, relayCts.Token);
                if (n == 0) break;
                await dst.WriteAsync(buffer.AsMemory(0, n), relayCts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            await relayCts.CancelAsync();
        }
    }

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(NatCleanupIntervalSec));

        while (await timer.WaitForNextTickAsync(ct))
        {
            var removed = NatTable.Instance.Cleanup(
                TimeSpan.FromSeconds(NatEntryMaxAgeSec));

            // Periodic statistics
            _logger.LogInformation(
                "Stats: active={Active} total={Total} sni={Sni} http={Http} " +
                "ip_fallback={Fallback} nat_entries={Nat}",
                ActiveConnections, TotalConnections,
                SniResolved, HttpHostResolved, IpFallback,
                NatTable.Instance.Count);

            if (removed > 0)
                _logger.LogDebug("NAT cleanup: {Removed} stale entries", removed);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener?.Stop();

        if (_acceptLoop != null)
            try { await _acceptLoop; } catch { }
        if (_cleanupLoop != null)
            try { await _cleanupLoop; } catch { }

        _cts.Dispose();
        _connectionThrottle.Dispose();
        _listener?.Dispose();

        _logger.LogInformation(
            "LocalRelay stopped. Final stats: total={Total} sni={Sni} " +
            "http={Http} ip_fallback={Fallback}",
            TotalConnections, SniResolved, HttpHostResolved, IpFallback);
    }
}
```

---

## 6. Decision matrix: what happens for each traffic type

```text
┌──────────────────┬────────────┬───────────────┬───────────────────────┐
│ Traffic type     │ Sniff      │ Domain from   │ SOCKS5 CONNECT        │
├──────────────────┼────────────┼───────────────┼───────────────────────┤
│ HTTPS (:443)     │ TLS        │ SNI           │ CONNECT domain:443    │
│                  │            │ "google.com"  │ ATYP=0x03             │
├──────────────────┼────────────┼───────────────┼───────────────────────┤
│ HTTP (:80)       │ HTTP       │ Host header   │ CONNECT domain:80     │
│                  │            │ "example.com" │ ATYP=0x03             │
├──────────────────┼────────────┼───────────────┼───────────────────────┤
│ TLS without SNI  │ TLS        │ null          │ CONNECT ip:port       │
│ (rare, legacy)   │            │               │ ATYP=0x01 (fallback)  │
├──────────────────┼────────────┼───────────────┼───────────────────────┤
│ Other TCP        │ Unknown    │ null          │ CONNECT ip:port       │
│ (SSH, SMTP, etc) │            │               │ ATYP=0x01 (fallback)  │
├──────────────────┼────────────┼───────────────┼───────────────────────┤
│ TLS 1.3 + ECH    │ TLS        │ outer SNI *   │ CONNECT domain:443    │
│                  │            │               │ ATYP=0x03             │
└──────────────────┴────────────┴───────────────┴───────────────────────┘

* ECH (Encrypted Client Hello) encrypts the "inner" SNI,
  but the "outer" SNI is still present in ClientHello
  and is sufficient for our routing task.
```

---

## 7. Order of operations — timeline of a single connection

```text
Time →
─────────────────────────────────────────────────────────────────────

[0ms]   Browser: TCP SYN to 142.250.74.46:443

[0ms]   WinpkFilter:
        • Intercepts the SYN packet
        • NatTable.Register("10.0.0.5:49152", 142.250.74.46:443)
        • Rewrites dst → 127.0.0.1:2070
        • Recalculates TCP checksum
        • Lets the packet pass

[~0ms]  TCP handshake: Browser ↔ LocalRelay (on loopback, <0.1ms)

[~1ms]  Browser sends TLS ClientHello (first data packet)

[~1ms]  LocalRelay:
        1. Accept TCP → clientEndpoint = 10.0.0.5:49152
        2. NatTable.Lookup → 142.250.74.46:443
        3. stream.ReadAsync → reads ClientHello (~300 bytes)
        4. TlsSniSniffer.ExtractSni → "www.google.com"

[~1ms]  LocalRelay → sing-box:
        5. TCP connect to 127.0.0.1:2080 (loopback, <0.1ms)
        6. SOCKS5 auth: [05 01 00] → [05 00]
        7. SOCKS5 CONNECT: [05 01 00 03 0E 77 77 77 2E 67 6F 6F ...]
           (ATYP=0x03, "www.google.com":443)
        8. sing-box: CONNECT OK [05 00 00 01 ...]

[~2ms]  LocalRelay:
        9. Sends the buffered ClientHello to the sing-box stream
        10. Starts bidirectional relay

[~50ms] sing-box → VLESS REALITY:
        • Connects to the remote server
        • TLS handshake with SNI = "www.google.com"
        • REALITY camouflage works correctly

[~100ms] Full path established:
         Browser ↔ LocalRelay ↔ sing-box ↔ VLESS ↔ Internet

─────────────────────────────────────────────────────────────────────
Overhead from SNI sniffing: ~1ms (one read on loopback)
```

---

## 8. Edge cases and safeguards

### 8.1 The client sends no data (timeout)

Some protocols wait for the server to speak first.
ProtocolSniffer has a 5-second timeout — if no data arrives,
the connection is established by IP as a fallback.

### 8.2 Fragmented ClientHello

TLS ClientHello may arrive in several TCP segments.
ProtocolSniffer determines the expected length from the TLS record header
and reads the missing bytes.

### 8.3 TLS without SNI

Very rare, but possible in legacy clients. Fallback to IP.
VLESS REALITY may not work in this case, but that is a problem
with the client itself, not the relay.

### 8.4 HTTP/2 and HTTP/3

HTTP/2 over TLS (h2) — SNI is extracted from TLS ClientHello, so it works.
HTTP/2 without TLS (h2c) — starts with "PRI * HTTP/2.0" instead of a normal
HTTP method. Recognition can be added if needed.
HTTP/3 — this is QUIC (UDP); WinpkFilter does not intercept it.

### 8.5 Encrypted Client Hello (ECH)

ECH encrypts the "inner" SNI, but the "outer" SNI remains visible
in ClientHello. For routing purposes, the outer SNI is sufficient.

---

## 9. Testing the SNI parser

### Unit test for TlsSniSniffer

```csharp
using System.Text;

namespace TunnelFlow.Tests;

/// <summary>
/// Generator of test TLS ClientHello packets for unit tests.
/// Minimal valid ClientHello with the given SNI.
/// </summary>
public static class TlsTestHelper
{
    /// <summary>
    /// Creates a minimal TLS ClientHello with the specified SNI.
    /// </summary>
    public static byte[] BuildClientHello(string sni)
    {
        byte[] sniBytes = Encoding.ASCII.GetBytes(sni);

        // SNI Extension
        // type(2) + ext_len(2) + list_len(2) + name_type(1) + name_len(2) + name(N)
        int sniExtLen = 2 + 1 + 2 + sniBytes.Length; // list contents
        int sniExtTotalLen = 2 + 2 + sniExtLen;      // type + len + contents

        // Extensions block
        int extensionsLen = sniExtTotalLen;

        // ClientHello body (after handshake header):
        // version(2) + random(32) + session_id_len(1) + session_id(0)
        // + cipher_suites_len(2) + cipher_suite(2) 
        // + compression_len(1) + compression(1)
        // + extensions_len(2) + extensions
        int clientHelloBodyLen = 2 + 32 + 1 + 0 + 2 + 2 + 1 + 1 + 2 + extensionsLen;

        // Handshake: type(1) + length(3) + body
        int handshakeLen = 1 + 3 + clientHelloBodyLen;

        // TLS Record: type(1) + version(2) + length(2) + handshake
        int recordLen = 5 + handshakeLen;

        var data = new byte[recordLen];
        int pos = 0;

        // TLS Record Header
        data[pos++] = 0x16; // ContentType: Handshake
        data[pos++] = 0x03; // Version: TLS 1.0 (record layer)
        data[pos++] = 0x01;
        data[pos++] = (byte)(handshakeLen >> 8);
        data[pos++] = (byte)(handshakeLen & 0xFF);

        // Handshake Header
        data[pos++] = 0x01; // ClientHello
        data[pos++] = 0x00;
        data[pos++] = (byte)(clientHelloBodyLen >> 8);
        data[pos++] = (byte)(clientHelloBodyLen & 0xFF);

        // ClientVersion
        data[pos++] = 0x03;
        data[pos++] = 0x03; // TLS 1.2

        // Random (32 bytes of zeros)
        pos += 32;

        // Session ID (length = 0)
        data[pos++] = 0x00;

        // Cipher Suites: 1 suite (TLS_AES_128_GCM_SHA256)
        data[pos++] = 0x00;
        data[pos++] = 0x02;
        data[pos++] = 0x13;
        data[pos++] = 0x01;

        // Compression: 1 method (null)
        data[pos++] = 0x01;
        data[pos++] = 0x00;

        // Extensions length
        data[pos++] = (byte)(extensionsLen >> 8);
        data[pos++] = (byte)(extensionsLen & 0xFF);

        // SNI Extension
        data[pos++] = 0x00; // type: server_name
        data[pos++] = 0x00;
        data[pos++] = (byte)(sniExtLen >> 8); // extension data length
        data[pos++] = (byte)(sniExtLen & 0xFF);

        // Server Name List
        int listLen = 1 + 2 + sniBytes.Length;
        data[pos++] = (byte)(listLen >> 8);
        data[pos++] = (byte)(listLen & 0xFF);

        data[pos++] = 0x00; // name type: host_name

        data[pos++] = (byte)(sniBytes.Length >> 8);
        data[pos++] = (byte)(sniBytes.Length & 0xFF);

        sniBytes.CopyTo(data.AsSpan(pos));
        pos += sniBytes.Length;

        return data;
    }
}

// Example usage in tests:
//
// [Fact]
// public void ExtractSni_ValidClientHello_ReturnsDomain()
// {
//     var hello = TlsTestHelper.BuildClientHello("www.google.com");
//     var sni = TlsSniSniffer.ExtractSni(hello);
//     Assert.Equal("www.google.com", sni);
// }
//
// [Fact]
// public void ExtractSni_TruncatedData_ReturnsNull()
// {
//     var hello = TlsTestHelper.BuildClientHello("www.google.com");
//     var sni = TlsSniSniffer.ExtractSni(hello.AsSpan(0, 20));
//     Assert.Null(sni);
// }
//
// [Fact]
// public void LooksLikeTls_HttpData_ReturnsFalse()
// {
//     var http = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n");
//     Assert.False(TlsSniSniffer.LooksLikeTls(http));
// }
//
// [Fact]
// public void ExtractHost_ValidHttpRequest_ReturnsHost()
// {
//     var http = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n");
//     Assert.Equal("example.com", HttpHostSniffer.ExtractHost(http));
// }
```

---

## 10. Project file structure

```text
TunnelFlow/
├── TunnelFlow.Core/
│   ├── NatTable.cs               // NAT table (unchanged)
│   ├── SniffResult.cs            // Sniffing result
│   ├── TlsSniSniffer.cs          // TLS ClientHello → SNI
│   ├── HttpHostSniffer.cs        // HTTP request → Host
│   ├── ProtocolSniffer.cs        // Facade: reads bytes + determines protocol
│   ├── Socks5Connector.cs        // SOCKS5 client (IP + Domain CONNECT)
│   ├── LocalRelay.cs             // Main component (updated)
│   └── WinpkFilterIntegration.cs // Integration with WinpkFilter
├── TunnelFlow.App/
│   └── Program.cs                // Entry point
└── TunnelFlow.Tests/
    └── TlsTestHelper.cs          // Test ClientHello generator
```

---

## 11. Update checklist

```text
[ ] 1. Add SniffResult.cs, TlsSniSniffer.cs, HttpHostSniffer.cs, ProtocolSniffer.cs
[ ] 2. Update Socks5Connector.cs — add ConnectByDomainAsync
[ ] 3. Update LocalRelay.cs — add sniffing between accept and SOCKS5 connect
[ ] 4. sing-box: make sure the socks inbound accepts domain CONNECT (by default — yes)
[ ] 5. Write unit tests for TlsSniSniffer with different ClientHello messages
[ ] 6. Integration test: curl → LocalRelay → sing-box → verify that SNI is correct
[ ] 7. Monitoring: verify that sni_resolved >> ip_fallback (otherwise the parser is broken)
```
