# Architecture — TunnelFlow

Version 0.2 · Based on spec v0.1 + decisions from concept phase

---

## Design principles

1. **Strict separation of concerns** — the capture layer knows nothing about VLESS; the transport layer knows nothing about processes.
2. **Fail closed by default** — if the service crashes or sing-box dies, tunneled apps lose connectivity (not bypass the tunnel silently).
3. **Self-exclusion is mandatory** — TunnelFlow's own binaries and sing-box are always on the deny list. Circular interception is a crash, not a feature.
4. **No virtual adapter** — we are a transparent per-app socksifier, not a VPN client.

---

## Component map

```
┌─────────────────────────────────────────────────────────────┐
│  User space (normal process)                                │
│                                                             │
│  ┌──────────────┐    Named Pipe IPC    ┌─────────────────┐ │
│  │  TunnelFlow  │◄────────────────────►│ TunnelFlow      │ │
│  │  .UI (WPF)   │                      │ .Service        │ │
│  └──────────────┘                      │ (Windows Svc)   │ │
│                                        └────────┬────────┘ │
│                                                 │           │
│                              spawn + manage     │           │
│                         ┌───────────────────────┤           │
│                         │                       │           │
│               ┌─────────▼──────┐    ┌──────────▼────────┐ │
│               │ TunnelFlow     │    │   sing-box         │ │
│               │ .Capture       │    │   (child process)  │ │
│               │ (WinpkFilter   │    │                    │ │
│               │  wrapper)      │    │  SOCKS5 inbound    │ │
│               └─────────┬──────┘    │  VLESS outbound    │ │
│                         │           └──────────┬─────────┘ │
└─────────────────────────┼──────────────────────┼───────────┘
                          │                      │
┌─────────────────────────▼──────────────────────┼───────────┐
│  Kernel space                                  │           │
│                                                │           │
│  ┌──────────────────────────────────────────┐  │           │
│  │  WinpkFilter NDIS driver                 │  │           │
│  │  - intercepts packets by process PID     │  │           │
│  │  - redirects matching flows to loopback  │  │           │
│  └──────────────────────────────────────────┘  │           │
└────────────────────────────────────────────────┼───────────┘
                                                 │
                                          VLESS tunnel
                                                 │
                                         Remote VLESS server
```

---

## Component responsibilities

### TunnelFlow.UI (WPF, .NET 8)
- Renders configuration UI: app list, VLESS profiles, mode selector, log viewer.
- Communicates with Service via Named Pipe (JSON protocol).
- Runs as normal user process; does NOT require elevation.
- Does NOT touch WinpkFilter or sing-box directly.

### TunnelFlow.Service (Windows Service, .NET 8)
- Runs elevated (LocalSystem or dedicated service account).
- Owns lifecycle of Capture engine and sing-box process.
- Generates sing-box JSON config from stored profiles.
- Implements health-check watchdog for sing-box (restart on crash).
- Exposes Named Pipe server for UI communication.
- Persists configuration to `%ProgramData%\TunnelFlow\`.

### TunnelFlow.Capture (.NET 8 class library)
- Wraps WinpkFilter managed API.
- Resolves active TCP/UDP connections to process paths via `GetExtendedTcpTable` / `GetExtendedUdpTable` → `QueryFullProcessImageName`.
- Maintains Session Registry (see COMPONENTS.md).
- Applies Policy Engine rules: proxy / direct / block per process.
- Redirects matching flows to `127.0.0.1:SOCKS_PORT`.
- Hard-coded self-exclusion: own PIDs + sing-box PID + loopback + VLESS server IP.

### TunnelFlow.Core (.NET 8 class library)
- Shared models: `AppRule`, `VlessProfile`, `SessionEntry`, `PolicyDecision`.
- Shared interfaces: `ICaptureEngine`, `IPolicyEngine`, `ISessionRegistry`.
- IPC message types (Named Pipe protocol).
- No dependencies on WinpkFilter or sing-box.

### sing-box (child process, managed binary)
- Bundled binary in `third_party/singbox/`.
- Started by Service with auto-generated `config.json`.
- Listens on `127.0.0.1:SOCKS_PORT` (SOCKS5, no auth).
- Routes all inbound through VLESS outbound.
- DNS policy: forward DNS queries through tunnel (production) or system resolver (MVP, documented limitation).

---

## IPC contract (Named Pipe)

Pipe name: `\\.\pipe\TunnelFlowService`

Transport: line-delimited JSON (newline `\n` terminated).

Direction: bidirectional. UI sends commands; Service sends responses and push events.

See COMPONENTS.md for full message schema.

---

## Process isolation model

| Process | Elevation | Can crash without data loss |
|---------|-----------|---------------------------|
| UI | No | Yes — Service keeps running |
| Service | Yes (LocalSystem) | No — capture stops, sing-box stops |
| Capture (in-process with Service) | Yes | No |
| sing-box | Yes (spawned by Service) | Watchdog restarts within 3s |

---

## Network flow summary

See [DATAFLOW.md](DATAFLOW.md) for full detail.

Short version:
- **Matching app TCP** → WinpkFilter intercepts → redirect to `127.0.0.1:2080` → sing-box SOCKS5 → VLESS out.
- **Matching app UDP** → same redirect path; Session Registry tracks UDP associations with 30s idle timeout.
- **Matching app UDP 443** → blocked (forces QUIC→TCP fallback in browsers).
- **Non-matching app** → WinpkFilter passes through unmodified.
- **TunnelFlow/sing-box own traffic** → hard deny-list, always pass-through.

---

## Configuration storage

```
%ProgramData%\TunnelFlow\
├── config.json          # App rules, profiles, settings
├── singbox_last.json    # Last generated sing-box config (debug)
└── logs\
    ├── service.log
    └── singbox.log
```

Config is written by Service only. UI reads/writes via IPC commands.
Credentials (VLESS UUID, server address) stored in Windows DPAPI-encrypted fields within `config.json`.
