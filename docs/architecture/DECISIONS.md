# Decisions

This file records the active architectural decisions for the current release
path.

## ADR-001: TUN-only release path

**Status**: Accepted

TunnelFlow's active product/runtime path is TUN-only:
- Wintun for the virtual interface
- sing-box `tun` inbound
- service-controlled orchestration
- process-based route and DNS generation

The retired WinpkFilter / packet-rewrite / localhost-SOCKS approach is not part
of the active release path.

## ADR-002: Service owns privileged networking lifecycle

**Status**: Accepted

`TunnelFlow.Service` remains the privileged orchestrator for:
- TUN activation
- sing-box start/stop
- runtime state publishing
- owner lease enforcement
- persistent logging

The UI remains an unprivileged client.

## ADR-003: sing-box startup readiness uses process observation

**Status**: Accepted

The active TUN-only path confirms startup through short process-survival
observation rather than localhost port probing.

Rationale:
- the release path no longer depends on a localhost SOCKS listener
- process observation matches the real TUN startup model
- it avoids carrying dormant SOCKS assumptions into the release path

## ADR-004: No fake healthy/connectivity state

**Status**: Accepted

The UI may show:
- local runtime state
- conservative warning evidence

It must not imply proven end-to-end connectivity unless that signal truly
exists.

## ADR-005: Owner lease protects against orphaned active tunnels

**Status**: Accepted

When the UI owns the active tunnel session, it must maintain a heartbeat-based
lease. If the owner disappears long enough, the service performs the normal
controlled stop path.

## Historical note

Earlier ADRs centered on WinpkFilter / `ndisapi.net` / localhost SOCKS were part
of the retired architecture exploration and are no longer active release
constraints.
