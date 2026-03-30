# TunnelFlow: Transparent Per-App TCP Proxy on Windows

## Architecture, Problem Analysis, and Full LocalRelay Implementation

---

## 1. Problem Diagnosis

### Why the Current Scheme Does Not Work

```
Browser → [TCP SYN to google.com:443] → WinpkFilter intercepts
    → rewrites dst to 127.0.0.1:2080
    → sing-box receives raw TCP connection
    → sing-box sees TLS ClientHello but DOES NOT KNOW where to proxy
    → error: destination unknown
```

**Root cause**: sing-box in `mixed` mode (HTTP+SOCKS5) expects a protocol handshake — either `CONNECT host:port` or SOCKS5 negotiation. WinpkFilter performs a redirect at the packet level; the browser is unaware of this and sends data as if it were connecting directly to `google.com:443`.

### Why `mixed` Inbound Cannot Be Used Directly

| Approach | Problem |
|----------|---------|
| mixed inbound | Requires SOCKS5/HTTP handshake from client |
| tproxy inbound | Linux only (netfilter) |
| redirect inbound | Linux only (iptables REDIRECT) |
| tun inbound | Creates a virtual adapter (out of scope) |

---

## 2. Correct Architecture

### Solution: LocalRelay with SOCKS5 Wrapping

An intermediate component — **LocalRelay** — is needed that:

1. Accepts raw TCP from WinpkFilter (redirect)
2. Finds the original destination in the NAT table
3. Opens a SOCKS5 connection to sing-box
4. Performs the SOCKS5 handshake specifying the original destination
5. Proxies data bidirectionally

### Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        TunnelFlow Pipeline                          │
│                                                                     │
│  ┌──────────┐    ┌────────────┐    ┌────────────┐    ┌──────────┐  │
│  │ Browser  │    │ WinpkFilter│    │ LocalRelay │    │ sing-box │  │
│  │ (app)    │    │ NDIS driver│    │ C# .NET 8  │    │ SOCKS5   │  │
│  └────┬─────┘    └─────┬──────┘    └─────┬──────┘    └────┬─────┘  │
│       │                │                 │                │        │
│       │ TCP SYN to     │                 │                │        │
│       │ 142.250.x:443  │                 │                │        │
│       ├───────────────►│                 │                │        │
│       │                │                 │                │        │
│       │                │ 1. Save         │                │        │
│       │                │ dst=142.250:443 │                │        │
│       │                │ in NAT table    │                │        │
│       │                │ by src:srcPort  │                │        │
│       │                │                 │                │        │
│       │                │ 2. Rewrite dst  │                │        │
│       │                │ → 127.0.0.1:    │                │        │
│       │                │   LOCAL_PORT    │                │        │
│       │                ├────────────────►│                │        │
│       │                │                 │                │        │
│       │                │                 │ 3. Accept TCP  │        │
│       │                │                 │ 4. Lookup NAT  │        │
│       │                │                 │    → 142.250   │        │
│       │                │                 │      :443      │        │
│       │                │                 │                │        │
│       │                │                 │ 5. Connect to  │        │
│       │                │                 │ 127.0.0.1:2080 │        │
│       │                │                 ├───────────────►│        │
│       │                │                 │                │        │
│       │                │                 │ 6. SOCKS5      │        │
│       │                │                 │ handshake:     │        │
│       │                │                 │ CONNECT        │        │
│       │                │                 │ 142.250.x:443  │        │
│       │                │                 ├───────────────►│        │
│       │                │                 │                │        │
│       │                │                 │ 7. SOCKS5 OK   │        │
│       │                │                 │◄───────────────┤        │
│       │                │                 │                │        │
│       │  ◄─────────── bidirectional relay ──────────────► │        │
│       │                │                 │                │        │
│       │                │                 │         8. VLESS        │
│       │                │                 │         REALITY ──────► │
│       │                │                 │         to remote       │
│       │                │                 │         server          │
└─────────────────────────────────────────────────────────────────────┘
```

### Ports

| Component | Address | Role |
|-----------|---------|------|
| LocalRelay | `127.0.0.1:2070` | Accepts raw TCP from WinpkFilter |
| sing-box | `127.0.0.1:2080` | SOCKS5 inbound → VLESS REALITY outbound |

**WinpkFilter redirects to port 2070 (LocalRelay), NOT to 2080 (sing-box).**

---

## 3. How to Pass the Original Destination

### Mechanism: Shared NAT Table via Static Singleton

WinpkFilter and LocalRelay live in the same process (or communicate via shared memory). The simplest and most reliable approach is a **static ConcurrentDictionary in a shared C# project**.

### NAT Table Key

```
Key:   "{sourceIP}:{sourcePort}"
Value: IPEndPoint(originalDestIP, originalDestPort)
```

**Why `srcIP:srcPort`**: When WinpkFilter intercepts a packet, it knows:
- Source IP:Port — unique for each TCP connection (ephemeral port)
- Original Destination IP:Port — what we are overwriting

When LocalRelay accepts a connection, the client's `RemoteEndPoint` gives us the same `srcIP:srcPort` — which we use to find the original destination.

### Entry Lifetime

Entries are removed when:
- The TCP connection is closed (FIN/RST intercepted by WinpkFilter)
- A timeout occurs (cleanup every 60 seconds, entries older than 120 seconds)

---

## 4. sing-box Configuration

```json
{
  "log": {
    "level": "info"
  },
  "inbounds": [
    {
      "type": "socks",
      "tag": "socks-in",
      "listen": "127.0.0.1",
      "listen_port": 2080
    }
  ],
  "outbounds": [
    {
      "type": "vless",
      "tag": "vless-out",
      "server": "YOUR_SERVER_IP",
      "server_port": 443,
      "uuid": "YOUR_UUID",
      "flow": "xtls-rprx-vision",
      "tls": {
        "enabled": true,
        "server_name": "www.microsoft.com",
        "utls": {
          "enabled": true,
          "fingerprint": "chrome"
        },
        "reality": {
          "enabled": true,
          "public_key": "YOUR_PUBLIC_KEY",
          "short_id": "YOUR_SHORT_ID"
        }
      }
    }
  ],
  "route": {
    "final": "vless-out"
  }
}
```

**Note**: Use `socks` inbound, not `mixed`. LocalRelay always speaks SOCKS5.

---

## 5. Full LocalRelay Implementation in C# .NET 8

### 5.1 NatTable.cs — Shared NAT Table

```csharp
using System.Collections.Concurrent;
using System.Net;

namespace TunnelFlow.Core;

/// <summary>
/// Thread-safe NAT table mapping srcIP:srcPort → original destination.
/// Shared between WinpkFilter and LocalRelay.
/// </summary>
public sealed class NatTable
{
    public static readonly NatTable Instance = new();

    private readonly ConcurrentDictionary<string, NatEntry> _table = new();

    private NatTable() { }

    /// <summary>
    /// Registers the original destination for a connection.
    /// Called from WinpkFilter BEFORE rewriting dst.
    /// </summary>
    public void Register(IPAddress srcIp, int srcPort, IPEndPoint originalDest)
    {
        var key = MakeKey(srcIp, srcPort);
        var entry = new NatEntry(originalDest, DateTime.UtcNow);
        _table.AddOrUpdate(key, entry, (_, _) => entry);
    }

    /// <summary>
    /// Looks up the original destination by the source endpoint of an incoming connection.
    /// Called from LocalRelay on accept.
    /// </summary>
    public IPEndPoint? Lookup(IPEndPoint clientEndpoint)
    {
        var key = MakeKey(clientEndpoint.Address, clientEndpoint.Port);
        return _table.TryGetValue(key, out var entry) ? entry.OriginalDestination : null;
    }

    /// <summary>
    /// Removes an entry. Called when a connection closes.
    /// </summary>
    public void Remove(IPAddress srcIp, int srcPort)
    {
        var key = MakeKey(srcIp, srcPort);
        _table.TryRemove(key, out _);
    }

    /// <summary>
    /// Removes entries older than maxAge. Called periodically.
    /// </summary>
    public int Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var removed = 0;

        foreach (var kvp in _table)
        {
            if (kvp.Value.CreatedAt < cutoff)
            {
                if (_table.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }

        return removed;
    }

    public int Count => _table.Count;

    private static string MakeKey(IPAddress ip, int port) => $"{ip}:{port}";

    private sealed record NatEntry(IPEndPoint OriginalDestination, DateTime CreatedAt);
}
```

### 5.2 Socks5Connector.cs — SOCKS5 Client Handshake

```csharp
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace TunnelFlow.Core;

/// <summary>
/// Performs the SOCKS5 handshake with sing-box to establish a proxied connection.
/// RFC 1928 — minimal implementation (no-auth + CONNECT).
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
    /// Connects to the SOCKS5 server and performs CONNECT to the specified destination.
    /// Returns a connected and relay-ready NetworkStream.
    /// </summary>
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
            AddrTypeDomain => throw new Socks5Exception("Unexpected domain address in CONNECT response"),
            _ => throw new Socks5Exception($"Unknown address type: {header[3]}")
        };

        var remaining = new byte[addrLen + 2];
        await ReadExactAsync(stream, remaining, ct);
    }

    private static async Task ReadExactAsync(
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

### 5.3 LocalRelay.cs — Main Component

```csharp
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace TunnelFlow.Core;

/// <summary>
/// LocalRelay — bridge between WinpkFilter (raw redirect) and sing-box (SOCKS5).
///
/// Listens for TCP on localPort, for each incoming connection:
/// 1. Determines the original destination from the NAT table
/// 2. Establishes a SOCKS5 connection to sing-box
/// 3. Relays data bidirectionally
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

    private const int BufferSize = 65536;
    private const int MaxConcurrentConnections = 4096;
    private const int NatCleanupIntervalSec = 60;
    private const int NatEntryMaxAgeSec = 120;

    public int ActiveConnections => _activeConnections;

    public LocalRelay(
        int listenPort,
        int socksPort,
        ILogger<LocalRelay> logger)
    {
        _listenEndpoint = new IPEndPoint(IPAddress.Loopback, listenPort);
        _socksEndpoint = new IPEndPoint(IPAddress.Loopback, socksPort);
        _logger = logger;
        _connectionThrottle = new SemaphoreSlim(MaxConcurrentConnections, MaxConcurrentConnections);
    }

    public void Start()
    {
        _listener = new TcpListener(_listenEndpoint);
        _listener.Server.NoDelay = true;
        _listener.Server.SetSocketOption(
            SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start(backlog: 512);

        _logger.LogInformation(
            "LocalRelay started on {Endpoint}, forwarding to SOCKS5 {Socks}",
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
        IPEndPoint? clientEndpoint = null;

        try
        {
            clientEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
            if (clientEndpoint == null)
            {
                _logger.LogWarning("Could not determine client endpoint");
                return;
            }

            var originalDest = NatTable.Instance.Lookup(clientEndpoint);
            if (originalDest == null)
            {
                _logger.LogWarning(
                    "No NAT entry for {Client}, dropping connection",
                    clientEndpoint);
                return;
            }

            _logger.LogDebug(
                "Relaying {Client} → {Destination} via SOCKS5",
                clientEndpoint, originalDest);

            using var clientStream = client.GetStream();
            await using var socksStream = await Socks5Connector.ConnectAsync(
                _socksEndpoint, originalDest, ct);

            await RelayAsync(clientStream, socksStream, ct);
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
            _logger.LogError(ex, "Unexpected error handling {Client}", clientEndpoint);
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

    private static async Task RelayAsync(
        NetworkStream clientStream,
        NetworkStream socksStream,
        CancellationToken ct)
    {
        using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var clientToSocks = CopyDirectionAsync(clientStream, socksStream, relayCts);
        var socksToClient = CopyDirectionAsync(socksStream, clientStream, relayCts);

        await Task.WhenAny(clientToSocks, socksToClient);
        await relayCts.CancelAsync();

        try { await clientToSocks; } catch { }
        try { await socksToClient; } catch { }
    }

    private static async Task CopyDirectionAsync(
        NetworkStream source,
        NetworkStream destination,
        CancellationTokenSource relayCts)
    {
        var buffer = new byte[BufferSize];
        try
        {
            while (!relayCts.Token.IsCancellationRequested)
            {
                int bytesRead = await source.ReadAsync(buffer, relayCts.Token);
                if (bytesRead == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), relayCts.Token);
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

            if (removed > 0)
            {
                _logger.LogDebug(
                    "NAT cleanup: removed {Count} stale entries, {Remaining} remaining",
                    removed, NatTable.Instance.Count);
            }
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

        _logger.LogInformation("LocalRelay stopped");
    }
}
```

### 5.4 Integration with WinpkFilter

```csharp
using System.Net;

namespace TunnelFlow.Core;

/// <summary>
/// Example call from the WinpkFilter packet processing callback.
/// Shows how and when to register NAT entries.
/// </summary>
public static class WinpkFilterIntegration
{
    /// <summary>
    /// Called when a TCP SYN packet from a tracked process is intercepted.
    /// BEFORE modifying the dst address in the packet.
    /// </summary>
    public static void OnTcpPacketIntercepted(
        IPAddress srcIp,
        int srcPort,
        IPAddress originalDstIp,
        int originalDstPort,
        int relayPort = 2070)
    {
        // 1. Save original destination to NAT table
        NatTable.Instance.Register(
            srcIp,
            srcPort,
            new IPEndPoint(originalDstIp, originalDstPort));

        // 2. WinpkFilter then rewrites dst to LocalRelay
        //    newDstIp   = 127.0.0.1
        //    newDstPort = relayPort (2070)
        //    This part is done in native WinpkFilter code
    }

    /// <summary>
    /// Called when a TCP FIN/RST is intercepted (optional, for fast cleanup).
    /// </summary>
    public static void OnTcpConnectionClosed(IPAddress srcIp, int srcPort)
    {
        NatTable.Instance.Remove(srcIp, srcPort);
    }
}
```

### 5.5 Program.cs — Startup

```csharp
using Microsoft.Extensions.Logging;
using TunnelFlow.Core;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

var logger = loggerFactory.CreateLogger<LocalRelay>();

var relay = new LocalRelay(
    listenPort: 2070,   // WinpkFilter redirects here
    socksPort: 2080,    // sing-box SOCKS5 inbound
    logger: logger);

relay.Start();

Console.WriteLine("TunnelFlow LocalRelay running. Press Ctrl+C to stop.");
Console.WriteLine($"Listening: 127.0.0.1:2070 → SOCKS5 127.0.0.1:2080");

var shutdown = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.SetResult();
};

await shutdown.Task;
await relay.DisposeAsync();
```

---

## 6. Edge Cases

### 6.1 UDP Traffic (DNS)

DNS requests go over UDP. WinpkFilter intercepts TCP, but DNS needs separate handling:

```json
// Add to sing-box config:
{
  "dns": {
    "servers": [
      {
        "tag": "remote-dns",
        "type": "https",
        "server": "1.1.1.1",
        "detour": "vless-out"
      }
    ]
  }
}
```

### 6.2 Loopback Exclusion

WinpkFilter **must not** intercept packets with dst `127.0.0.1` — otherwise an infinite loop is created (LocalRelay → sing-box → redirect → LocalRelay → ...).

```csharp
// In packet filter callback:
if (originalDstIp.Equals(IPAddress.Loopback))
    return; // Pass through, do not intercept
```

### 6.3 sing-box as a Managed Process

LocalRelay **must not** be intercepted by WinpkFilter. Two options:

1. **PID exclusion**: WinpkFilter filters by process PID. sing-box.exe and TunnelFlow.exe itself are added to the exclude list.
2. **Destination exclusion**: Packets to the IP addresses of sing-box outbound servers are not intercepted.

### 6.4 IPv6

The current implementation supports IPv6 in SOCKS5 (`AddrTypeIPv6`). Make sure WinpkFilter also intercepts IPv6 packets and the NAT table handles IPv6 addresses correctly.

---

## 7. Integration Checklist

```
[ ] 1. sing-box: change inbound from "mixed" to "socks" (port 2080)
[ ] 2. WinpkFilter: redirect dst to 127.0.0.1:2070 (not 2080!)
[ ] 3. WinpkFilter: BEFORE rewriting dst — call NatTable.Register()
[ ] 4. WinpkFilter: exclude loopback dst (127.0.0.0/8)
[ ] 5. WinpkFilter: exclude PID of sing-box.exe and TunnelFlow.exe
[ ] 6. Start sing-box with SOCKS5 inbound
[ ] 7. Start LocalRelay (port 2070 → SOCKS5 2080)
[ ] 8. Start WinpkFilter with filtered target processes
[ ] 9. Verify: browser → WinpkFilter → LocalRelay → sing-box → VLESS → internet
```

---

## 8. Debugging and Diagnostics

### Verify LocalRelay is accepting connections

```bash
netstat -an | findstr 2070
```

### Verify SOCKS5 handshake manually

```bash
# curl via sing-box SOCKS5 directly (should work)
curl --socks5 127.0.0.1:2080 https://ifconfig.me

# If it works — sing-box is configured correctly
```

### Verify NAT table

Add a monitoring endpoint:

```csharp
_logger.LogInformation(
    "Status: {Active} active connections, {NatEntries} NAT entries",
    ActiveConnections, NatTable.Instance.Count);
```

### Common Errors

| Symptom | Cause | Fix |
|---------|-------|-----|
| "No NAT entry for client" | WinpkFilter not calling Register() | Check WinpkFilter integration |
| "SOCKS5 CONNECT failed code 5" | sing-box cannot connect to destination | Check VLESS outbound |
| Infinite loop / hang | Intercepting loopback traffic | Exclude 127.0.0.0/8 in WinpkFilter |
| Connection timeout | sing-box PID being intercepted | Add sing-box.exe to exclude list |
| "Connection refused on 2080" | sing-box not running | Start sing-box before relay |

---

## 9. Performance

### Expected Benchmarks

| Metric | Value |
|--------|-------|
| Latency overhead (relay hop) | ~0.1–0.3ms (loopback) |
| Throughput per connection | Limited by sing-box / VLESS |
| Max concurrent connections | 4096 (configurable) |
| Memory per connection | ~130 KB (2 × 64 KB buffer) |
| Memory for 1000 connections | ~130 MB |

### Optimizations

- `NoDelay = true` on all sockets — disables Nagle's algorithm
- 64 KB buffers — matches TCP window size
- `SemaphoreSlim` throttling — prevents resource exhaustion
- Cleanup loop — prevents NAT entry leaks

---

## 10. Alternative Approaches (for reference)

| Approach | Pros | Cons |
|----------|------|------|
| **LocalRelay + SOCKS5** (chosen) | Simple, reliable, sing-box unmodified | Additional hop |
| TUN adapter + sing-box tun | Native support | Requires virtual adapter (out of scope) |
| WFP callout driver | Kernel level, fast | Complex development, signing required |
| LSP/Winsock Layered Provider | Works at Winsock level | Deprecated in Windows, unreliable |
| HTTP CONNECT relay | Simpler than SOCKS5 | Not all traffic types supported |
| sing-box with custom protocol | No relay | Requires fork of sing-box |

**LocalRelay + SOCKS5** is the optimal balance of simplicity, reliability, and compatibility.
