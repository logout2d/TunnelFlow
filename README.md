# TunnelFlow

TunnelFlow is a Windows desktop client for working with VLESS profiles and
tunneling selected applications through a virtual adapter.

It is aimed at users who want a simple Windows client for routing selected apps
through a VLESS tunnel without carrying forward legacy relay complexity.

It is built around a release-focused **TUN-only** runtime path:

- **Wintun** provides the virtual interface
- **sing-box** runs with a **TUN inbound**
- **VLESS** is the active outbound transport
- selected applications are handled through generated process-based rules

TunnelFlow is meant to keep the workflow simple:

- manage VLESS profiles in a clear desktop UI
- pick which applications should use the tunnel
- keep the runtime service-managed and consistent
- avoid retired localhost-SOCKS or packet-capture release assumptions

## Downloads

- `TunnelFlow-win-x64-v0.1.0.zip`
  - Main Windows x64 package without a bundled `sing-box.exe`
- `TunnelFlow-win-x64-with-core-v0.1.0.zip`
  - Convenience Windows x64 package with a bundled `sing-box.exe` for easier
    first-time setup

### Which package should I choose?

- Choose the standard package if you already manage your own compatible
  `sing-box.exe`.
- Choose the with-core package if you want a simpler first setup with the
  sing-box core already included.

Bundled third-party components remain separate components under their own
licenses.

## Features

- Simple VLESS profile workflow for creating, editing, selecting, and activating
  profiles
- Per-application tunneling rules with explicit `Proxy`, `Direct`, and `Block`
  behavior
- TUN-only runtime path built around Wintun and sing-box
- Service-managed lifecycle for start, stop, restart, install, and repair flows
- Honest runtime state:
  - local runtime state is shown clearly
  - conservative warning evidence can be shown when runtime problems are
    detected
  - the app does not invent a fake "healthy" or "connected" state
- Compact Windows desktop UI with dedicated views for:
  - App Rules
  - Profile management
  - Logs
  - About

## How It Works

1. `TunnelFlow.UI` lets you manage VLESS profiles and application rules.
2. `TunnelFlow.Service` validates prerequisites, activates Wintun, builds the
   sing-box configuration, and starts sing-box in TUN mode.
3. sing-box applies generated process-based route and DNS rules:
   - `Proxy` apps -> `vless-out`
   - `Direct` apps -> direct
   - `Block` apps -> reject
4. Runtime readiness in the active release path is based on **process
   observation**, not localhost port probing.

The active product path does **not** use:

- a localhost SOCKS or mixed inbound listener
- WinpkFilter or `ndisapi.net`
- `TunnelFlow.Capture`
- the retired packet-capture / transparent-relay / local-SOCKS architecture

## Requirements

- Windows 10 / 11 x64
- .NET 8 runtime
- Administrator privileges for service/bootstrapper actions
- bundled `sing-box.exe`
- bundled Wintun runtime files

## Repository Layout

```text
TunnelFlow/
|- src/
|  |- TunnelFlow.Bootstrapper/  # elevated lifecycle/install helper
|  |- TunnelFlow.Core/          # shared models, IPC contracts, enums
|  |- TunnelFlow.Service/       # Windows service, TUN orchestration, sing-box control
|  |- TunnelFlow.UI/            # WPF desktop client
|  `- TunnelFlow.Tests/         # unit and focused integration tests
|- third_party/
|  |- singbox/                  # pinned sing-box binary
|  `- wintun/                   # Wintun runtime assets
|- assets/
|  |- icons/                    # app/window/About icons
|  `- donations/                # donation visuals such as QR images
|- docs/
|  |- architecture/             # active architecture/reference docs
|  |- engineering/              # engineering workflow rules
|  |- archive/                  # historical notes kept out of the active surface
|  |- project-memory.md
|  |- fix-plan.md
|  `- tunnelflow-wintun-singbox-tun-design.md
|- AGENTS.md
`- README.md
```

## Active Documentation

| Document | Purpose |
|----------|---------|
| [docs/architecture/ARCHITECTURE.md](docs/architecture/ARCHITECTURE.md) | Active TUN-only system overview |
| [docs/architecture/COMPONENTS.md](docs/architecture/COMPONENTS.md) | Active components and contracts |
| [docs/architecture/DATAFLOW.md](docs/architecture/DATAFLOW.md) | Current startup, runtime, and shutdown flow |
| [docs/architecture/DECISIONS.md](docs/architecture/DECISIONS.md) | Active architectural decisions |
| [docs/architecture/RISKS.md](docs/architecture/RISKS.md) | Current TUN-only risk register |
| [docs/engineering/AI_DEV_RULES.md](docs/engineering/AI_DEV_RULES.md) | Current engineering guardrails for AI/dev work |
| [docs/tunnelflow-wintun-singbox-tun-design.md](docs/tunnelflow-wintun-singbox-tun-design.md) | Detailed TUN pivot and runtime design reference |

## Historical Note

Older WinpkFilter / transparent-relay exploration is retained only as historical
context in:

- [docs/archive/README.md](docs/archive/README.md)
- long-form engineering notes in [docs/project-memory.md](docs/project-memory.md)
- implementation history recorded in [docs/fix-plan.md](docs/fix-plan.md)

It is not part of the active release path.

## Support / Donations

If TunnelFlow is useful to you and you want to support continued work, you can
use the placeholders below until final payment details are published.

- Wallet address BTC: bc1quha9k5dauxp5r4k9hdg9lymhnjktu0fwhxre82
  <details>
  <summary>Show BTC QR</summary>

  ![Donation QR placeholder](assets/donations/qr-btc.png)

  </details>

- Wallet address USDT (ERC20): 0x8a3e373c37b7FA4c783a44d42804f72Cc4e73b11
  <details>
  <summary>Show USDT (ERC20) QR</summary>

  ![Donation QR placeholder](assets/donations/qr-usdt-erc20.png)

  </details>

- Wallet address TON: UQDTP7B-iZKdb1sGfXPUeRXCYr6wgGGPuJ1cBClhfYRJHHIe
  <details>
  <summary>Show TON QR</summary>

  ![Donation QR placeholder](assets/donations/qr-ton.png)

  </details>

## License

Apache 2.0. See [LICENSE](LICENSE).
