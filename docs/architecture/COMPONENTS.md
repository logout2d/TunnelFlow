# Components

Active components and contracts for the current TUN-only release path.

## Runtime projects

### TunnelFlow.UI
- WPF desktop client
- edits profiles and app rules
- sends IPC commands to the service
- shows runtime state, warnings, logs, and profile/subscription UI

### TunnelFlow.Service
- validates TUN prerequisites
- owns lifecycle state machine
- activates/stops Wintun through `ITunOrchestrator`
- generates sing-box config
- starts/stops sing-box through `ISingBoxManager`
- pushes structured state/status over named-pipe IPC
- persists config and logs under `%ProgramData%\TunnelFlow\`

### TunnelFlow.Bootstrapper
- elevated install/repair/start/restart helper
- keeps service lifecycle management out of the normal UI process

### TunnelFlow.Core
- shared models (`AppRule`, `VlessProfile`, enums)
- shared IPC message/response contracts
- shared runtime warning and lifecycle payload types

## Key active models

### AppRule
- identifies an executable path
- mode is `Proxy`, `Direct`, or `Block`
- enabled rules feed sing-box route and DNS generation

### VlessProfile
- stores the current remote profile data
- includes current subscription metadata when applicable
- remains the active proxy profile model for the release path

### StatePayload / StatusPayload
Active runtime state sent from service to UI includes:
- lifecycle state
- sing-box status
- selected mode
- tunnel interface up/down
- active profile id/name
- runtime warning evidence
- active owner session id
- rule counts

## Active runtime interfaces

### ISingBoxManager
- start sing-box
- stop sing-box
- restart sing-box
- expose status and log events

### ITunOrchestrator
- start the TUN interface
- stop the TUN interface
- expose TUN prerequisite information used by service mode selection

## Explicitly retired from the active release surface

These are no longer active release components:
- `TunnelFlow.Capture`
- WinpkFilter / `ndisapi.net`
- session-registry-driven transparent relay architecture
- localhost SOCKS inbound as the app-traffic transport seam

Historical notes about those components remain only in engineering history docs.
