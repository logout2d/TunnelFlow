# AGENTS.md

## Project goal
Bring TunnelFlow to a working state with reliable transparent proxying for desktop apps and browsers on Windows.

## Current priority
Fix the transparent relay chain so browser/application traffic reaches end websites reliably, not only a limited subset.

## Mandatory workflow
- Work only in the current feature branch.
- Make small, reviewable commits.
- Prefer minimal, high-confidence fixes over refactors.
- Read relevant code first, then propose a narrow patch.
- After each code change, run the narrowest relevant validation first.
- Do not change unrelated files.
- Do not edit generated bin/obj outputs on purpose.
- If a hypothesis is uncertain, add focused logging or tests before broad changes.

## Architecture constraints
- Preserve the current intended flow:
  packet capture -> local relay -> SOCKS5 CONNECT -> sing-box
- Do not replace the architecture unless explicitly instructed.
- Domain-preserving behavior is critical.
- Avoid silent fallback to IP-based routing when hostname-based routing is expected.

## Testing rules
- Add or update focused tests for parser/protocol/config-builder fixes when feasible.
- Prefer deterministic unit tests before broad integration changes.
- Record manual test steps and outcomes in docs/project-memory.md.

## Documentation workflow
Read before editing:
- docs/project-memory.md
- docs/fix-plan.md

Update after editing:
- docs/project-memory.md with findings, decisions, validation, and open questions
- docs/fix-plan.md with current step/status

## Coding rules
- Keep diffs scoped.
- Avoid cosmetic renames.
- Avoid broad refactors unless required.
- Preserve existing public contracts unless a fix requires change.
- Be explicit in logs around fallback behavior and failure modes.

## Reporting format
At the end of each task, report:
1. what changed
2. why it changed
3. risks / open questions
4. exact validation performed