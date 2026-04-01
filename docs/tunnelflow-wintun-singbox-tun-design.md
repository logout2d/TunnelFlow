
# TunnelFlow Pivot Design: Wintun + sing-box TUN on Windows

## Status
This document captures the agreed architectural pivot away from packet-level TCP redirection as the main delivery path and toward a Windows TUN-based design using Wintun and sing-box.

## Why we are pivoting
The previous transparent relay path based on packet-level interception and TCP destination rewrite reached a hard architectural limit:

- policy matching worked
- capture decisions worked
- NAT/redirect bookkeeping worked
- relay listener health checks worked
- but redirected TCP flows did not become real accepted sockets in LocalRelay

The core mismatch was:
- packet-level rewrite happens after the application socket has already created its outbound TCP state
- LocalRelay expects a real inbound accepted socket via TcpListener

That made the old packet-rewrite path a poor foundation for the primary product direction.

## New primary direction
Use:

- **Wintun** as the signed Windows TUN driver layer
- **sing-box** with **TUN inbound**
- process-based route and DNS rules to implement **per-application proxying**
- existing **Service + UI** split for privileged vs unprivileged behavior

## Non-goals of this pivot
This pivot does **not** try to:
- keep the old packet-level TCP redirect as the main path
- introduce a custom signed WFP callout driver as the main delivery mechanism
- support every advanced network edge case in Phase 1
- redesign the UI from scratch

## Key architectural decision
The application will no longer try to simulate a local accepted TCP connection by rewriting raw packets.

Instead:

1. TunnelFlow Service starts and controls sing-box with a TUN inbound.
2. Wintun provides the virtual interface.
3. sing-box applies route and DNS rules using Windows process identity fields such as process path/name.
4. Selected applications are routed into the tunnel.
5. Non-selected applications continue using the normal network path.

## Service/UI model
### Service
TunnelFlow.Service remains the privileged/system component and owns:

- sing-box lifecycle
- TUN lifecycle
- route generation
- DNS/rule generation
- configuration persistence
- diagnostics/logging
- IPC server for the UI

### UI
TunnelFlow.UI remains a normal user-mode application and owns:

- profile editing
- rule editing
- status display
- logs/session display
- start/stop commands via IPC
- no direct network adapter or route manipulation

### Result
The user should normally interact with the UI without elevation.
The privileged actions remain in the service.

## Why this direction avoids the driver-signing dead-end
We are avoiding a **custom** redirect driver as the main product path.

The critical assumption of this pivot is:
- use the prebuilt signed Wintun distribution
- do not make a custom kernel redirect driver a requirement for shipping the product

This removes the main blocker that existed in the WFP-driver path.

## New core data flow
### High-level flow
1. UI sends desired profile/rule state to the Service.
2. Service generates a sing-box config with:
   - TUN inbound
   - selected outbound
   - route rules
   - DNS rules
3. Service starts sing-box.
4. Wintun interface becomes active.
5. Process-based rules decide which app traffic goes into the proxy path.
6. Selected traffic goes through sing-box to the configured outbound/proxy.
7. Non-selected traffic remains direct.

## Design principles
1. **Prefer stable signed components over custom kernel work**
2. **Keep the existing Service/UI split**
3. **Push as much logic as possible into config generation instead of low-level interception**
4. **Make the first milestone small and observable**
5. **Keep rollback easy**

## Process-based routing strategy
The planned primary selectors are:

- process path
- process name
- possibly path regex only if needed later

The current product concept is already centered on per-application rules, so this aligns naturally with the existing model.

## DNS strategy
DNS must be treated as a first-class part of the design.

Goals:
- selected applications should resolve consistently through the intended path
- avoid DNS leaks where possible
- keep local/LAN behavior usable

Expected design points:
- generate DNS rules alongside route rules
- define safe bypasses for local/private destinations
- test Windows-specific strict routing behavior carefully

## Loop prevention strategy
This is mandatory.

The Service-generated sing-box config must ensure:
- sing-box itself does not route into its own tunnel path
- control/service traffic does not loop
- local service endpoints and local management paths are excluded appropriately

This area must be validated early because loop errors will look like total connectivity failure.

## Compatibility expectations
### Good candidates
This approach should be strongest for:
- browsers
- chat apps
- launchers
- CLI tools
- many games using standard Windows networking
- typical desktop applications using normal TCP/UDP networking via the Windows stack

### Watch-outs
Need explicit validation for:
- apps with unusual DNS behavior
- apps sensitive to interface changes
- apps that strongly prefer IPv6
- apps that use unusual local discovery patterns
- dev tools/VM tools that may dislike strict routing
- anti-cheat / low-level networking / custom drivers

## Main technical risks
### 1. Route/rule generation correctness
The biggest product risk becomes config/rule correctness, not packet interception.

### 2. DNS leak / DNS mismatch
If DNS rules are wrong, app routing may appear broken even when transport routing is fine.

### 3. Tunnel loop / self-proxying
If service or sing-box traffic is not excluded correctly, the system may deadlock its own connectivity.

### 4. Windows compatibility edge cases
VirtualBox-like tools, dev environments, some VPN software, and other network-sensitive tools may react poorly to aggressive tunnel settings.

### 5. Version drift
Both sing-box and TUN-related behavior can shift across versions.
Version pinning is recommended for early phases.

## What we are preserving from the current codebase
Preserve as much as possible:

- TunnelFlow.Service as orchestration root
- UI, ViewModels, IPC, ConfigStore
- profile model and rule model
- sing-box generation infrastructure where reusable
- diagnostics/logging patterns
- tests for config persistence and service-level behavior

## What becomes legacy / de-emphasized
The following path is no longer the main delivery architecture:

- packet-level TCP redirect to LocalRelay as the primary product mechanism
- WinpkFilter-based transparent relay as the main path

It may remain temporarily for:
- experiments
- diagnostics
- regression reference
- eventual removal after the TUN path proves itself

## Proposed phased plan

## Phase 0 - Pivot documentation and scaffolding
### Goal
Record the pivot clearly and keep the repository coherent.

### Deliverables
- this design doc in repo
- updates to project memory
- updates to fix plan
- clear declaration that TUN is now the main path

### Acceptance
- docs committed
- no code behavior change yet

---

## Phase 1 - sing-box TUN config generation skeleton
### Goal
Teach config generation and persisted config to express the new TUN mode.

### Deliverables
- config model additions for TUN mode
- sing-box builder support for TUN inbound skeleton
- feature flag / mode selection
- no runtime cutover yet

### Acceptance
- build green
- config tests for TUN config structure
- old behavior unchanged unless new mode enabled

---

## Phase 2 - Service-controlled TUN lifecycle skeleton
### Goal
Wire the service to choose and start the TUN-oriented sing-box mode in a controlled way.

### Deliverables
- lifecycle hooks in service
- logging for selected mode
- safe fallback when TUN mode disabled
- no final routing behavior assumptions yet

### Acceptance
- service build/tests green
- structured logs show mode selection cleanly

---

## Phase 3 - Minimal per-app route rule generation
### Goal
Generate the first narrow process-based route rules for one selected app.

### Deliverables
- one app rule -> sing-box route rule mapping
- one bypass/direct default strategy
- explicit self-exclusion rules

### Acceptance
- config snapshot/tests prove one selected app gets proxied
- config for non-selected apps stays direct by design

---

## Phase 4 - First real runtime TUN validation
### Goal
Prove one real selected application can use the tunnel path while non-selected traffic remains direct.

### Validation target
- one known application
- one known remote site/service
- logs prove mode active
- behavior is observable and repeatable

### Acceptance
- selected app works through TUN path
- non-selected app remains direct
- service/UI still usable

---

## Phase 5 - DNS hardening and compatibility tuning
### Goal
Reduce leaks and weird routing failures.

### Deliverables
- DNS rule generation refinement
- private/LAN/local bypass policy
- strict route tuning
- compatibility notes

### Acceptance
- selected test apps behave consistently
- common DNS failures understood or mitigated

---

## Phase 6 - Remove dependency on transparent relay main path
### Goal
Make TUN the clear primary architecture.

### Deliverables
- de-emphasize legacy transparent relay code
- simplify service flow where possible
- update docs and roadmap

### Acceptance
- new path is the documented mainline
- old path clearly marked legacy/spike

## Proposed file plan

## New / changed docs
- `docs/tunnelflow-wintun-singbox-tun-design.md`
- `docs/project-memory.md`
- `docs/fix-plan.md`

## Likely managed code touch points
### Service/config layer
- `src/TunnelFlow.Service/Config/TunnelFlowConfig.cs`
- `src/TunnelFlow.Service/Config/ConfigStore.cs`
- `src/TunnelFlow.Service/Program.cs`
- `src/TunnelFlow.Service/Services/OrchestratorService.cs`

### sing-box generation layer
- `src/TunnelFlow.Service/SingBox/SingBoxConfigBuilder.cs`
- possibly new config helper files for TUN-specific generation

### Core models
- `src/TunnelFlow.Core/...` models for mode selection / profile extensions if needed

### Tests
- `src/TunnelFlow.Tests/Service/...`
- new focused tests for TUN config generation and mode selection

## Suggested feature-flag strategy
Add a mode switch like:
- `TransparentRelay`
- `TunMode`

Early phases should keep:
- old path available as fallback/reference
- new path off by default until validated

## Logging requirements for the TUN path
Minimum required logs:

- selected network mode
- sing-box config mode summary
- TUN enabled/disabled
- selected route-rule count
- selected DNS-rule count
- per-app rule summary
- start/stop success/failure
- safe error if Wintun/sing-box assets are missing

## Validation strategy
### Build-level
- narrow config builder tests
- config store tests
- orchestration tests where possible

### Runtime-level
For the first runtime validation:
1. start service
2. enable TUN mode
3. enable one app rule
4. open one site/app action
5. confirm logs and behavior
6. confirm non-selected app still works direct

## Rollback strategy
If the TUN path misbehaves:
- disable TUN mode feature flag
- fall back to existing path
- retain logs and config snapshots
- do not partially remove the legacy path until the TUN path is proven

## Open questions to answer in later phases
1. How should UI expose the mode switch?
2. Should TUN mode be global with per-app proxy rules, or per-profile?
3. How should LAN/private bypass rules be surfaced to users?
4. How should IPv6 be handled in early releases?
5. How should DNS strictness be tuned by default?
6. How much of the transparent relay code should be removed vs retained as legacy?

## Immediate next step
Implement only **Phase 1**:
- TUN mode config scaffolding in persisted config and sing-box config generation
- no runtime cutover yet
- build/test only
