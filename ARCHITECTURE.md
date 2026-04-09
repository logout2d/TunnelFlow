# Architecture

This file describes the **active release architecture** for TunnelFlow.

TunnelFlow now ships a **TUN-only** runtime path:

- `TunnelFlow.UI` is the unprivileged desktop client
- `TunnelFlow.Service` is the privileged orchestration layer
- `TunnelFlow.Bootstrapper` handles elevated install/start/repair actions
- Wintun provides the virtual interface
- sing-box runs with a `tun` inbound and VLESS outbound

The active product path does **not** include a localhost SOCKS listener or any
WinpkFilter/`ndisapi.net` capture layer.

## Component overview

### TunnelFlow.UI
- WPF client
- edits profiles and app rules
- connects to the service through named-pipe IPC
- shows runtime state, warnings, logs, and profile/subscription state

### TunnelFlow.Service
- validates TUN prerequisites
- activates/stops Wintun through `ITunOrchestrator`
- generates sing-box config for the active profile
- starts/stops sing-box and observes startup by short process-survival checks
- owns lifecycle state, runtime warning evidence, and owner lease tracking
- persists config under `%ProgramData%\TunnelFlow\`

### TunnelFlow.Bootstrapper
- elevated helper for install/repair/start/restart actions
- keeps service management out of the normal UI process

### Wintun + sing-box
- Wintun provides the tunnel interface
- sing-box receives tunnel traffic through `tun-in`
- route and DNS rules are generated from app rules
- selected apps are proxied, direct apps stay direct, blocked apps are rejected

## Active runtime flow

1. UI sends config and lifecycle commands to the service.
2. Service validates TUN prerequisites and loads the active profile.
3. Service activates Wintun.
4. Service writes `singbox_last.json` and starts sing-box in TUN mode.
5. Startup readiness is confirmed by process observation, not port probing.
6. Service publishes structured state/status payloads back to the UI.

## Shutdown flow

Tunnel stop is ordered deliberately:

1. lifecycle becomes `Stopping`
2. sing-box is stopped first
3. TUN orchestrator is stopped second
4. lifecycle becomes `Stopped`

This keeps TUN teardown aligned with the current release-hardening model.

## Runtime artifacts

TunnelFlow stores active runtime artifacts under:

```text
%ProgramData%\TunnelFlow\
|- config.json
|- singbox_last.json
`- logs\
   |- singbox.log
   `- service.log
```

## Historical note

Older WinpkFilter / transparent-relay material is no longer the active product
architecture. Historical design context remains in:

- `docs/tunnelflow-wintun-singbox-tun-design.md`
- `docs/wfp-tcp-redirect-poc-plan.md`
- `docs/project-memory.md`
