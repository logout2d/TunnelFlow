# TunnelFlow AI / Dev Rules

This file describes the **current** engineering constraints for TunnelFlow.

## Product direction

TunnelFlow's active release path is **TUN-only**.

Assume the mainline runtime is:
- Wintun
- sing-box `tun` inbound
- service-controlled lifecycle
- process-based route and DNS rules

Do **not** treat these as active product paths unless a task explicitly says so:
- localhost SOCKS listener readiness
- WinpkFilter / `ndisapi.net`
- `TunnelFlow.Capture`
- transparent packet-rewrite socksifier architecture

## Non-negotiable rules

1. Preserve the TUN-only release path.
2. Do not add fallback to localhost SOCKS readiness or mixed inbound assumptions.
3. UI stays unprivileged; privileged actions go through the service/bootstrapper.
4. Runtime health must stay honest:
   - local state can be shown
   - warning evidence can be shown conservatively
   - do not invent a "connected" or "healthy" state
5. Favor small, reviewable patches over refactors.

## Active architecture touch points

- `src/TunnelFlow.UI/`
- `src/TunnelFlow.Service/`
- `src/TunnelFlow.Bootstrapper/`
- `src/TunnelFlow.Core/`
- `src/TunnelFlow.Tests/`

Current runtime seams to preserve:
- service lifecycle state machine
- owner lease / heartbeat for UI-owned sessions
- sing-box TUN config generation
- process-observation startup readiness
- service.log and singbox.log diagnostics

## Editing guidance

- Read the relevant service/UI code before changing behavior.
- Keep diffs scoped.
- Prefer deterministic tests.
- Update `docs/project-memory.md` and `docs/fix-plan.md` after each meaningful task.
- When cleaning docs, keep historical material clearly marked as historical rather than leaving it in the active release narrative.

## Validation guidance

Prefer the narrowest validation that proves the change:
- unit tests for service/config/view-model logic first
- broader app/runtime checks only when the task needs them

## Historical note

Older root docs and R&D material may still reference WinpkFilter-era work. Treat
that as historical context only unless the task explicitly asks for archaeology.
