# Data Flow — TunnelFlow

Traffic lifecycle for TCP, UDP, and DNS scenarios.

---

## TCP flow

```
App                WinpkFilter        Capture Engine         sing-box         VLESS Server
 │                     │                    │                    │                  │
 │──SYN──────────────►│                    │                    │                  │
 │                     │  new flow event    │                    │                  │
 │                     │───────────────────►│                    │                  │
 │                     │         resolve PID│                    │                  │
 │                     │         GetExtTcpTable                  │                  │
 │                     │         QueryFullProcessImageName       │                  │
 │                     │         PolicyEngine.Evaluate()         │                  │
 │                     │◄──────────────────│                    │                  │
 │                     │  decision=Proxy   │                    │                  │
 │                     │  redirect to      │                    │                  │
 │                     │  127.0.0.1:2080   │                    │                  │
 │◄────────────────────│                   │                    │                  │
 │  [transparent]      │                   │                    │                  │
 │                     │                   │  SessionRegistry   │                  │
 │                     │                   │  .Add(entry)       │                  │
 │                     │                   │                    │                  │
 │──TCP data──────────────────────────────────────────────────►│                  │
 │                     │                   │  Registry          │                  │
 │                     │                   │  .UpdateActivity() │                  │
 │                     │                   │                    │──VLESS framed──►│
 │                     │                   │                    │                  │
 │◄─────────────────────────────────────────────────────────────│◄────response────│
 │                     │                   │                    │                  │
 │──FIN───────────────────────────────────────────────────────►│                  │
 │                     │  flow end event   │                    │                  │
 │                     │───────────────────►│                    │                  │
 │                     │                   │  Registry          │                  │
 │                     │                   │  .Remove(flowId)   │                  │
```

### Decision=Direct

WinpkFilter passes packet through unmodified. No entry in Session Registry.

### Decision=Block

WinpkFilter drops the packet. App receives connection refused / timeout depending on protocol.

---

## UDP flow

UDP has no connection concept. TunnelFlow synthesizes sessions.

```
App                WinpkFilter        Capture Engine         sing-box         VLESS Server
 │                     │                    │                    │                  │
 │──UDP datagram──────►│                    │                    │                  │
 │                     │  new flow event    │                    │                  │
 │                     │───────────────────►│                    │                  │
 │                     │         resolve PID (GetExtUdpTable)    │                  │
 │                     │         PolicyEngine.Evaluate()         │                  │
 │                     │         CHECK: dst port 443? → Block    │                  │
 │                     │                   │                    │                  │
 │                     │  decision=Proxy   │                    │                  │
 │                     │  redirect to SOCKS│                    │                  │
 │                     │                   │  Registry.Add()    │                  │
 │                     │                   │  (with TTL 30s)    │                  │
 │──UDP data──────────────────────────────────────────────────►│                  │
 │                     │                   │  Registry          │                  │
 │                     │                   │  .UpdateActivity() │                  │
 │                     │                   │                    │──VLESS/UDP─────►│
 │◄────────────────────────────────────────────────────────────│◄────response────│
 │                     │                   │                    │                  │
 │  (no more packets)  │                   │                    │                  │
 │                     │                   │  after 30s idle:   │                  │
 │                     │                   │  PurgeExpired()    │                  │
 │                     │                   │  Registry.Remove() │                  │
```

### QUIC/HTTP3 special case

```
App (Chrome)       WinpkFilter        Capture Engine
 │                     │                    │
 │──UDP dst:443───────►│                    │
 │                     │  new flow event    │
 │                     │───────────────────►│
 │                     │         PolicyEngine:
 │                     │         protocol=UDP, port=443, decision→Block
 │                     │◄──────────────────│
 │                     │  DROP packet      │
 │   ←connection refused / timeout         │
 │                     │                   │
 │  Chrome detects UDP 443 unreachable     │
 │  falls back to TCP+TLS (HTTP/1.1, H2)   │
 │──TCP SYN───────────►│                   │
 │  [normal TCP proxy flow continues...]   │
```

---

## DNS flow (MVP limitation)

In MVP, DNS is **not** intercepted. System resolver is used for all apps, including tunneled ones.

```
Tunneled App
    │
    │── DNS query ──► Windows system resolver (DNS client service)
    │                     │
    │                     │── UDP port 53 ──► DNS server (e.g. 8.8.8.8)
    │                     │                  [NOT tunneled — goes direct]
    │                     │
    │◄── DNS response ────│
    │
    │── TCP connection to resolved IP ──► [tunneled normally]
```

**Risk**: DNS queries for tunneled apps go outside the tunnel. An observer on the network can see which domains the tunneled apps are resolving, even if the data flows are hidden.

**Mitigation (MVP)**: Document this limitation prominently in UI ("DNS queries are not tunneled in this version").

**Production plan** (Phase 3): Intercept DNS queries from tunneled processes and forward them through sing-box using a local DNS inbound on `127.0.0.1:DNSPORT`.

```jsonc
// Future sing-box config addition:
{
  "type": "dns",
  "tag": "dns-in",
  "listen": "127.0.0.1",
  "listen_port": 5353
}
```

---

## Service startup flow

```
Windows starts TunnelFlow.Service
    │
    ├── Read config from %ProgramData%\TunnelFlow\config.json
    ├── Start Named Pipe server (listen for UI connections)
    ├── If active profile exists:
    │       ├── Generate sing-box config.json
    │       ├── Start sing-box process
    │       ├── Wait for sing-box SOCKS5 port to be responsive (max 5s)
    │       ├── Initialize WinpkFilter driver
    │       ├── Apply rules to CaptureEngine
    │       └── Start packet interception
    └── Push StatusChanged event to connected UI
```

## Service shutdown flow

```
Stop requested (SCM, UI, or system shutdown)
    │
    ├── Signal CaptureEngine.StopAsync()
    │       ├── Drain in-flight sessions (max 2s grace)
    │       ├── Release WinpkFilter handle
    │       └── Clear Session Registry
    ├── Signal sing-box process to stop (SIGTERM / taskkill)
    │       └── Wait max 3s, then force kill
    └── Close Named Pipe server
```

---

## sing-box crash recovery

```
sing-box process exits unexpectedly
    │
    ISingBoxManager watchdog detects exit
    │
    ├── attempt <= MaxRestartAttempts (5)?
    │       ├── Yes:
    │       │       ├── Wait RestartDelay (3s)
    │       │       ├── Restart sing-box
    │       │       ├── Push SingBoxCrashed event to UI
    │       │       └── Resume (capture stays active during restart window)
    │       └── No:
    │               ├── Stop CaptureEngine (fail-closed)
    │               ├── Push StatusChanged(captureRunning=false) to UI
    │               └── Log critical error
```

Note: during the restart window (3s), tunneled app packets will be redirected to a SOCKS5 port that isn't responding. Apps will experience connection errors. This is intentional (fail-closed). A future version may implement a temporary block policy during restart.
