# Fix plan

## Current stage
Environment prepared for Codex-guided debugging and patching.

## Active design reference
- Primary implementation direction:
  - `docs/tunnelflow-wintun-singbox-tun-design.md`
- Historical diagnostic/R&D reference:
  - `docs/wfp-tcp-redirect-poc-plan.md`

## Step 1
Add repository instructions and project memory for Codex.
Status: in progress

## Step 2
Patch SOCKS5 domain reply parsing and improve ProtocolSniffer robustness.
Status: completed

## Step 3
Add focused tests for SOCKS5/domain and fragmented TLS sniffing behavior.
Status: completed

## Step 4
Patch SingBoxConfigBuilder to honor profile.Network (tcp/ws/grpc as supported by the project).
Status: completed

## Step 5
Add end-to-end diagnostic logging across capture, policy, driver, relay, and SOCKS to localize the runtime failure point, including redirect rewrite/reinject, relay listener self-check diagnostics, and explicit post-decision redirect application logs for rewrite execution and reinject path.
Status: completed

## Step 6
Replace loopback relay addressing with a non-loopback local IPv4 host address so the existing NDIS rewrite/response rewrite model can function.
Status: completed

## Step 7
Reinject redirected outbound relay packets through MSTCP/local stack instead of the generic outbound adapter path.
Status: completed

## Step 8
Phase 0 foundation: add WFP TCP redirect documentation, skeleton abstractions/models, config feature flag, DI wiring, and a no-op provider without changing runtime behavior.
Status: completed

## Step 9
Phase 1 preparation: teach LocalRelay to check connection-level original-destination metadata first and fall back to the existing NAT lookup path.
Status: completed

## Step 10
Phase 2 provider skeleton: add the WFP redirect provider lifecycle/startup path without enabling real redirect behavior yet.
Status: completed

## Step 11
Close out the WFP redirect exploration as a diagnostic/R&D path rather than the main delivery architecture.
Status: completed
Outcome:
- packet-level TCP redirect to LocalRelay is not a viable primary product path
- WFP redirect exploration produced useful diagnostics, scaffolding, and architecture knowledge
- that path is now retained only as reference / experimental material, not as the main plan

## Step 12
Phase 0 of the TUN pivot: align docs/plans and declare Wintun + sing-box TUN as the primary direction.
Status: completed

## Step 13
Phase 0.5 of the TUN pivot: add persisted TUN-mode scaffolding and a no-op service-side TUN orchestration seam.
Status: completed
Scope:
- add a mode switch or equivalent persisted setting for the TUN path
- add a no-op service-side TUN orchestration abstraction and DI wiring
- keep runtime behavior unchanged while the TUN path remains a stub

## Step 14
Phase 1 of the TUN pivot: add sing-box config generation support for TUN mode.
Status: completed
Scope:
- add sing-box config-builder support for TUN inbound skeleton
- keep old behavior unchanged unless the new mode is enabled

## Step 15
Phase 2 of the TUN pivot: wire service-controlled TUN lifecycle selection without full runtime cutover.
Status: pending
Scope:
- service chooses mode cleanly
- logs selected mode and TUN prerequisites
- safe fallback when TUN mode is disabled or prerequisites are missing

## Step 16
Phase 3 of the TUN pivot: generate the first minimal per-app process-based route and DNS rules.
Status: pending
Scope:
- one selected app routed to proxy path
- non-selected apps remain direct
- explicit self-exclusion / loop-prevention rules

## Step 17
Phase 4 of the TUN pivot: first real runtime validation with one selected app on Wintun + sing-box TUN.
Status: pending
Scope:
- validate selected-app proxying
- validate non-selected direct traffic
- validate service/UI lifecycle and observability

## Step 18
Phase 5 of the TUN pivot: DNS hardening, loop prevention refinement, and compatibility tuning.
Status: pending
Scope:
- tighten DNS rule behavior
- validate `strict_route` behavior
- refine local/LAN/private bypass handling

## Step 19
Phase 6 of the TUN pivot: de-emphasize legacy transparent-relay / WFP paths after TUN proves stable.
Status: pending
Scope:
- mark legacy paths clearly
- remove or reduce obsolete mainline assumptions
