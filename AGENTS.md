# AGENTS.md

## Project goal
Keep TunnelFlow stable and release-ready as a Windows desktop client for VLESS
profiles and per-app tunneling through the active TUN-only path.

## Current priority
Preserve the current TUN-only release path, keep packaging and runtime behavior
reliable, and avoid reintroducing retired relay or localhost-SOCKS assumptions.

## Mandatory workflow
- Work only in the current feature branch.
- Make small, reviewable commits.
- Prefer minimal, high-confidence fixes over refactors.
- Read relevant code first, then propose a narrow patch.
- After each code change, run the narrowest relevant validation first.
- Do not change unrelated files.
- Do not edit generated `bin/` or `obj/` outputs on purpose.
- If a hypothesis is uncertain, add focused logging or tests before broad changes.

## Architecture constraints
- Preserve the active release flow:

  UI -> Service -> Wintun -> sing-box `tun` inbound -> VLESS outbound

- Do not reintroduce:
  - localhost SOCKS readiness
  - WinpkFilter / `ndisapi.net`
  - `TunnelFlow.Capture`
  - retired packet-capture / local-relay architecture

- UI stays unprivileged.
- Elevated actions belong in the service/bootstrapper path.
- Runtime health must stay honest:
  - local runtime state can be shown
  - conservative warning evidence can be shown
  - do not invent a proven "healthy" or "connected" state

## Testing rules
- Add or update focused tests for service, config, view-model, bootstrapper, and
  packaging fixes when feasible.
- Prefer deterministic unit tests before broad integration changes.
- Record manual test steps and outcomes in `docs/project-memory.md`.

## Documentation workflow
Read before editing:
- `docs/project-memory.md`
- `docs/fix-plan.md`

Update after editing:
- `docs/project-memory.md` with findings, decisions, validation, and open questions
- `docs/fix-plan.md` with the current step and status

## Coding rules
- Keep diffs scoped.
- Avoid cosmetic renames.
- Avoid broad refactors unless required.
- Preserve existing public contracts unless a fix truly requires change.
- Be explicit in logs around fallback behavior and failure modes.
- When cleaning docs, keep historical material clearly marked as historical rather
  than leaving it in the active release narrative.

## Reporting format
At the end of each task, report:
1. what changed
2. why it changed
3. risks / open questions
4. exact validation performed