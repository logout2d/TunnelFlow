# TunnelFlow

TunnelFlow is a Windows per-application proxy client that uses a **TUN-only**
runtime path:

- **Wintun** provides the virtual interface
- **sing-box** runs with a **TUN inbound**
- **VLESS** is the current outbound transport
- selected applications are routed through the tunnel by generated
  process-based rules

The active product path does **not** use:

- a localhost SOCKS or mixed inbound listener
- WinpkFilter or `ndisapi.net`
- `TunnelFlow.Capture`
- the retired packet-rewrite transparent socksifier architecture

## Current release path

1. `TunnelFlow.UI` edits profiles and app rules through IPC.
2. `TunnelFlow.Service` validates TUN prerequisites, activates Wintun, builds
   sing-box config, and starts sing-box in TUN mode.
3. sing-box applies process-based route and DNS rules:
   - `Proxy` apps -> `vless-out`
   - `Direct` apps -> direct
   - `Block` apps -> reject
4. Runtime readiness is based on **process observation**, not localhost port
   probing.

## Requirements

- Windows 10 / 11 x64
- .NET 8 runtime
- Administrator privileges for service/bootstrapper actions
- bundled `sing-box.exe`
- bundled Wintun runtime files

## Repository layout

```text
TunnelFlow/
|- src/
|  |- TunnelFlow.Bootstrapper/  # elevated lifecycle/install helper
|  |- TunnelFlow.Core/          # shared models, IPC contracts, enums
|  |- TunnelFlow.Service/       # Windows service, TUN orchestration, sing-box control
|  |- TunnelFlow.UI/            # WPF desktop client
|  `- TunnelFlow.Tests/         # unit tests
|- third_party/
|  |- singbox/                  # pinned sing-box binary
|  `- wintun/                   # Wintun runtime assets
|- docs/
|  |- project-memory.md
|  |- fix-plan.md
|  |- tunnelflow-wintun-singbox-tun-design.md
|  `- archive/                     # historical/retired notes
`- README.md
```

## Active docs

| Document | Purpose |
|----------|---------|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Active TUN-only system overview |
| [COMPONENTS.md](COMPONENTS.md) | Active components and contracts |
| [DATAFLOW.md](DATAFLOW.md) | Current startup, runtime, and shutdown flow |
| [DECISIONS.md](DECISIONS.md) | Active architectural decisions |
| [RISKS.md](RISKS.md) | Current TUN-only risk register |
| [CURSOR_RULES.md](CURSOR_RULES.md) | Current engineering guardrails for AI/dev work |
| [docs/tunnelflow-wintun-singbox-tun-design.md](docs/tunnelflow-wintun-singbox-tun-design.md) | Detailed pivot/design reference |

## Historical note

Older WinpkFilter / transparent-relay exploration is retained only as
historical context in:

- [docs/archive/PHASE2_PLAN.md](docs/archive/PHASE2_PLAN.md)
- [docs/archive/wfp-tcp-redirect-poc-plan.md](docs/archive/wfp-tcp-redirect-poc-plan.md)
- [docs/archive/README.md](docs/archive/README.md)
- long-form engineering notes in [docs/project-memory.md](docs/project-memory.md)

It is not part of the active release path.

## License

Apache 2.0. See [LICENSE](LICENSE).
