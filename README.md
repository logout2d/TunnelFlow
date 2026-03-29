# TunnelFlow

**Transparent per-app proxy for Windows** — selective tunneling of chosen applications through VLESS via WinpkFilter + sing-box.

> No virtual adapter. No system-wide VPN. Just the apps you choose, routed through your VLESS server.

---

## How it works

TunnelFlow intercepts network traffic at the packet level using WinpkFilter (NDIS driver), identifies packets by process, and redirects traffic from selected applications to a local sing-box instance acting as SOCKS5 → VLESS transport.

```
Selected App
    │  TCP/UDP packets
    ▼
WinpkFilter (NDIS driver)
    │  per-process intercept + redirect
    ▼
sing-box (local SOCKS5 inbound)
    │  VLESS outbound
    ▼
VLESS Server (remote)
```

All other system traffic flows directly, unaffected.

---

## Status

🚧 **Pre-alpha** — specification and architecture phase.

| Phase | Status |
|-------|--------|
| Concept & spec | ✅ Done |
| Technical specification | ✅ Done |
| Architecture docs | ✅ Done |
| Prototype (TCP) | 🔲 Planned |
| UDP + Session registry | 🔲 Planned |
| DNS policy layer | 🔲 Planned |
| Production hardening | 🔲 Planned |

---

## Requirements

- Windows 10 / 11 (x64)
- .NET 8 Runtime
- Administrator privileges (service layer only)
- WinpkFilter driver (bundled)
- sing-box binary (bundled)

---

## Project structure

```
TunnelFlow/
├── docs/                    # Architecture, specs, decisions
│   ├── ARCHITECTURE.md
│   ├── COMPONENTS.md
│   ├── DATAFLOW.md
│   ├── DECISIONS.md
│   └── RISKS.md
├── src/
│   ├── TunnelFlow.UI/       # WPF .NET 8 — user interface
│   ├── TunnelFlow.Service/  # Windows Service — orchestration
│   ├── TunnelFlow.Capture/  # WinpkFilter wrapper — packet interception
│   ├── TunnelFlow.Core/     # Shared models, interfaces, contracts
│   └── TunnelFlow.Tests/    # Unit + integration tests
├── third_party/
│   ├── winpkfilter/         # WinpkFilter SDK (driver + managed wrapper)
│   └── singbox/             # sing-box binary (platform-specific)
├── .cursor/
│   └── rules                # Cursor AI context rules
├── CURSOR_RULES.md          # Cursor prompting guide for this project
└── README.md
```

---

## Documentation

| Document | Purpose |
|----------|---------|
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | System overview, component roles, boundaries |
| [COMPONENTS.md](docs/COMPONENTS.md) | Detailed component specs, interfaces, data types |
| [DATAFLOW.md](docs/DATAFLOW.md) | TCP/UDP/DNS traffic flows, session lifecycle |
| [DECISIONS.md](docs/DECISIONS.md) | Architecture Decision Records (ADR) |
| [RISKS.md](docs/RISKS.md) | Known risks, mitigations, open questions |
| [CURSOR_RULES.md](CURSOR_RULES.md) | Rules and context for AI-assisted development |

---

## License

Apache 2.0 — see [LICENSE](LICENSE).
