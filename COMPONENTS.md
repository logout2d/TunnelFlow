# Components — TunnelFlow

Detailed interfaces, data types, and contracts for each component.

---

## TunnelFlow.Core — shared models

### AppRule

```csharp
public record AppRule
{
    public Guid Id { get; init; }
    public string ExePath { get; init; }        // full path, e.g. C:\Program Files\...
    public string DisplayName { get; init; }    // friendly name shown in UI
    public RuleMode Mode { get; init; }         // Proxy | Direct | Block
    public bool IsEnabled { get; init; }
}

public enum RuleMode { Proxy, Direct, Block }
```

### VlessProfile

```csharp
public record VlessProfile
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public string ServerAddress { get; init; }  // hostname or IP
    public int ServerPort { get; init; }
    public string UserId { get; init; }         // UUID, DPAPI-encrypted at rest
    public string Flow { get; init; }           // e.g. "xtls-rprx-vision" or ""
    public string Network { get; init; }        // "tcp" | "ws" | "grpc"
    public string Security { get; init; }       // "tls" | "reality" | "none"
    public TlsOptions? Tls { get; init; }
    public bool IsActive { get; init; }
}

public record TlsOptions
{
    public string Sni { get; init; }
    public bool AllowInsecure { get; init; }
    public string? Fingerprint { get; init; }   // "chrome" | "firefox" | null
    public string? RealityPublicKey { get; init; }  // when Security == "reality"
    public string? RealityShortId { get; init; }
}
```

### SessionEntry

```csharp
public record SessionEntry
{
    public ulong FlowId { get; init; }          // WinpkFilter flow handle
    public int ProcessId { get; init; }
    public string ProcessPath { get; init; }
    public Protocol Protocol { get; init; }     // Tcp | Udp
    public IPEndPoint OriginalSource { get; init; }
    public IPEndPoint OriginalDestination { get; init; }
    public PolicyDecision Decision { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivityAt { get; set; }
    public SessionState State { get; set; }     // Active | Closing | Closed
}

public enum Protocol { Tcp, Udp }
public enum SessionState { Active, Closing, Closed }
```

### PolicyDecision

```csharp
public record PolicyDecision
{
    public PolicyAction Action { get; init; }   // Proxy | Direct | Block
    public string? Reason { get; init; }        // for logging
}

public enum PolicyAction { Proxy, Direct, Block }
```

---

## TunnelFlow.Core — interfaces

### ICaptureEngine

```csharp
public interface ICaptureEngine : IDisposable
{
    /// Start packet interception. Must be called from elevated context.
    Task StartAsync(CaptureConfig config, CancellationToken ct);

    /// Gracefully stop — drain active sessions, release WinpkFilter handle.
    Task StopAsync(CancellationToken ct);

    /// Live session snapshot for diagnostics.
    IReadOnlyList<SessionEntry> GetActiveSessions();

    /// Raised when a new session is established.
    event EventHandler<SessionEntry> SessionCreated;

    /// Raised when a session ends.
    event EventHandler<SessionEntry> SessionClosed;

    /// Raised on capture errors (driver disconnects, etc.)
    event EventHandler<CaptureError> ErrorOccurred;
}

public record CaptureConfig
{
    public int SocksPort { get; init; }           // sing-box SOCKS5 port, e.g. 2080
    public IPAddress SocksAddress { get; init; }  // 127.0.0.1
    public IReadOnlyList<AppRule> Rules { get; init; }
    public IReadOnlyList<string> ExcludedProcessPaths { get; init; }  // self + singbox
    public IReadOnlyList<IPAddress> ExcludedDestinations { get; init; } // VLESS server IP
}
```

### IPolicyEngine

```csharp
public interface IPolicyEngine
{
    /// Evaluate what to do with a new flow.
    PolicyDecision Evaluate(int pid, string processPath, IPEndPoint destination, Protocol protocol);

    /// Reload rules without restarting capture.
    void UpdateRules(IReadOnlyList<AppRule> rules);
}
```

### ISessionRegistry

```csharp
public interface ISessionRegistry
{
    void Add(SessionEntry entry);
    bool TryGet(ulong flowId, out SessionEntry entry);
    void Remove(ulong flowId);
    void UpdateActivity(ulong flowId);

    /// Clean up UDP sessions that exceeded idle timeout.
    void PurgeExpiredUdp(TimeSpan idleTimeout);

    IReadOnlyList<SessionEntry> GetAll();
}
```

### ISingBoxManager

```csharp
public interface ISingBoxManager : IDisposable
{
    Task StartAsync(VlessProfile profile, SingBoxConfig config, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task RestartAsync(CancellationToken ct);
    SingBoxStatus GetStatus();

    event EventHandler<SingBoxStatus> StatusChanged;
    event EventHandler<string> LogLine;
}

public record SingBoxConfig
{
    public int SocksPort { get; init; }
    public int? DnsPort { get; init; }          // null = use system DNS (MVP)
    public string BinaryPath { get; init; }
    public string ConfigOutputPath { get; init; }
    public TimeSpan RestartDelay { get; init; } // default 3s
    public int MaxRestartAttempts { get; init; } // default 5
}

public enum SingBoxStatus { Stopped, Starting, Running, Crashed, Restarting }
```

---

## TunnelFlow.Capture — process resolution

The core challenge: WinpkFilter operates at packet level (source IP:port) — it does not know which process owns a connection. We resolve this with a two-step lookup:

### Step 1 — Port → PID

```csharp
// TCP: call GetExtendedTcpTable(TCP_TABLE_OWNER_PID_ALL)
// UDP: call GetExtendedUdpTable(UDP_TABLE_OWNER_PID)
// Returns: local port → PID mapping
// Must be called when a new flow appears (not cached — ports are reused quickly)
```

### Step 2 — PID → exe path

```csharp
// OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, pid)
// QueryFullProcessImageName → full path string
// Cache: PID → path with TTL 5s (processes don't change their path)
// On cache miss or stale: re-query
```

### Race condition window

There is an inherent race: a packet arrives, we look up the port, the process has already exited and another process grabbed the same port. Mitigation:
- Compare process start time from `SYSTEM_PROCESS_INFORMATION` against the connection's creation time.
- On ambiguous cases: apply `Direct` policy (safe default — don't tunnel unknown process).
- Log the ambiguity for diagnostics.

---

## TunnelFlow.Service — Named Pipe IPC

### Protocol

Line-delimited JSON over `\\.\pipe\TunnelFlowService`.

Each message: `{ "type": "...", "id": "uuid", "payload": { ... } }\n`

### Commands (UI → Service)

```jsonc
// Get current state
{ "type": "GetState", "id": "..." }

// Add or update an app rule
{ "type": "UpsertRule", "id": "...", "payload": { /* AppRule */ } }

// Delete a rule
{ "type": "DeleteRule", "id": "...", "payload": { "ruleId": "uuid" } }

// Save a VLESS profile
{ "type": "UpsertProfile", "id": "...", "payload": { /* VlessProfile */ } }

// Activate a profile (starts/restarts sing-box)
{ "type": "ActivateProfile", "id": "...", "payload": { "profileId": "uuid" } }

// Start / stop tunneling
{ "type": "StartCapture", "id": "..." }
{ "type": "StopCapture", "id": "..." }

// Get active sessions (diagnostic)
{ "type": "GetSessions", "id": "..." }

// Export diagnostic bundle
{ "type": "ExportDiagnostics", "id": "...", "payload": { "outputPath": "..." } }
```

### Responses (Service → UI)

```jsonc
// Success response
{ "type": "Ok", "id": "...", "payload": { /* depends on command */ } }

// Error response
{ "type": "Error", "id": "...", "payload": { "code": "...", "message": "..." } }
```

### Push events (Service → UI, no request id)

```jsonc
{ "type": "StatusChanged", "payload": { "captureRunning": true, "singboxStatus": "Running" } }
{ "type": "SessionCreated", "payload": { /* SessionEntry */ } }
{ "type": "SessionClosed",  "payload": { "flowId": 12345 } }
{ "type": "LogLine",        "payload": { "source": "singbox|service|capture", "level": "Info", "message": "..." } }
{ "type": "SingBoxCrashed", "payload": { "attempt": 2, "retryingIn": 3 } }
```

---

## sing-box config generation

The Service generates a `config.json` for sing-box on each profile activation. Template:

```jsonc
{
  "log": { "level": "info", "output": "PATH_TO_LOG" },
  "inbounds": [
    {
      "type": "socks",
      "tag": "socks-in",
      "listen": "127.0.0.1",
      "listen_port": SOCKS_PORT,
      "sniff": false
    }
  ],
  "outbounds": [
    {
      "type": "vless",
      "tag": "vless-out",
      "server": "SERVER_ADDRESS",
      "server_port": SERVER_PORT,
      "uuid": "USER_ID",
      "flow": "",
      "tls": {
        "enabled": true,
        "server_name": "SNI",
        "utls": { "enabled": true, "fingerprint": "FINGERPRINT" }
      },
      "transport": { /* ws or grpc if applicable */ }
    },
    { "type": "direct", "tag": "direct" },
    { "type": "block",  "tag": "block"  }
  ],
  "route": {
    "rules": [
      { "outbound": "direct" }  // default: all traffic that reaches sing-box goes out via VLESS
    ],
    "final": "vless-out"
  }
}
```

Note: routing decisions (proxy vs direct vs block) are made by TunnelFlow.Capture before traffic reaches sing-box. sing-box only sees traffic that has already been decided as "proxy". This keeps the config simple.

---

## UDP specifics

### Session table

UDP is connectionless. We maintain associations manually:

```
Key:   (src_ip, src_port, dst_ip, dst_port, protocol=UDP)
Value: SessionEntry with LastActivityAt timestamp
```

### Idle timeout: 30 seconds (configurable)

Purge job runs every 10 seconds via `ISessionRegistry.PurgeExpiredUdp`.

### QUIC/HTTP3 blocking rule

For tunneled processes, UDP port 443 outbound is **blocked** (PolicyAction.Block).

Rationale: sing-box SOCKS5 inbound does not support UDP ASSOCIATE for QUIC in a reliable cross-platform way, and browsers will not automatically fall back to TCP unless UDP 443 is unreachable. Blocking forces TCP+TLS fallback.

This rule is applied in PolicyEngine as a hard override, regardless of AppRule settings:

```csharp
// In IPolicyEngine.Evaluate():
if (protocol == Protocol.Udp && destination.Port == 443 && baseDecision.Action == PolicyAction.Proxy)
    return new PolicyDecision(PolicyAction.Block, "QUIC block — forces TCP fallback");
```

This must be documented in the UI as "QUIC/HTTP3 is disabled for tunneled apps (by design)".
