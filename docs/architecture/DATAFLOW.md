# Data Flow

This file describes the **active TUN-only** data flow for TunnelFlow.

## Startup flow

1. `TunnelFlow.UI` sends `StartCapture` through named-pipe IPC.
2. `TunnelFlow.Service` verifies:
   - active profile exists
   - sing-box binary exists
   - TUN prerequisites are satisfied
3. Service activates Wintun through `ITunOrchestrator`.
4. Service generates a sing-box config with:
   - `tun-in`
   - VLESS outbound
   - process-based route rules
   - DNS rules for proxy/block app cases
5. Service starts sing-box.
6. sing-box readiness is confirmed by process observation.
7. Service publishes runtime state/status to the UI.

There is no localhost SOCKS or mixed inbound readiness step in the active path.

## Runtime traffic flow

```text
Selected App
  -> Windows networking stack
  -> Wintun interface
  -> sing-box tun-in
  -> route/DNS rules
  -> vless-out
  -> remote server
```

### Rule behavior
- `Proxy` app rules route traffic to `vless-out`
- `Direct` app rules route traffic to `direct`
- `Block` app rules reject traffic and DNS for those apps
- unlisted apps stay on the normal direct path

## Shutdown flow

Tunnel shutdown is ordered as:

1. lifecycle -> `Stopping`
2. stop sing-box
3. stop Wintun / TUN orchestrator
4. lifecycle -> `Stopped`

This prevents TUN teardown while sing-box is still active.

## Owner lease flow

When the UI starts the tunnel:
- the UI session becomes the active owner
- the UI sends periodic heartbeats
- the service tracks lease freshness
- if heartbeats stop long enough, the service performs the normal controlled stop

This protects against UI crash/kill without adding a broader multi-owner system.

## Runtime evidence flow

Service-side observability is split into:
- structured state/status payloads for lifecycle and local runtime facts
- sing-box/service logs for diagnostics
- conservative warning evidence derived from strong or repeated log signals

The UI does **not** infer a "healthy" state from this data.

## Historical note

Older WinpkFilter / localhost-SOCKS flow documents are retired from the active
release path. Keep them as historical reference only.
