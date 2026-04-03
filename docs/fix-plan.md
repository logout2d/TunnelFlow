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
Status: completed
Scope:
- service chooses mode cleanly
- logs selected mode and TUN prerequisites
- safe fallback when TUN mode is disabled or prerequisites are missing

## Step 16
Phase 3 of the TUN pivot: generate the first minimal per-app process-based route and DNS rules.
Status: completed
Scope:
- one selected app routed to proxy path
- non-selected apps remain direct
- explicit self-exclusion / loop-prevention rules

## Step 17
Phase 4 of the TUN pivot: first real runtime validation with one selected app on Wintun + sing-box TUN.
Status: in progress
Scope:
- validate selected-app proxying
- validate non-selected direct traffic
- validate service/UI lifecycle and observability
Progress:
- first real activation-capable TUN orchestrator slice is now implemented
- service can now select effective TUN mode when Wintun prerequisites are met
- minimal valid TUN inbound address has been added so sing-box no longer fails immediately with `missing interface address`
- sing-box startup readiness is now mode-aware:
  - legacy mode still uses SOCKS port probing
  - TUN mode now uses short process-survival observation instead of invalid SOCKS readiness checks
- service lifecycle is now mode-aware:
  - legacy mode still starts CaptureEngine / LocalRelay / WinpkFilter-backed behavior
  - TUN mode now skips the legacy capture path entirely and only runs Wintun + sing-box TUN
- sing-box log output is now recreated/truncated before each start for clean runtime evidence
- TUN-mode policy mapping now supports all three app rule modes:
  - `Proxy` -> explicit route to `vless-out`
  - `Direct` -> explicit route to `direct`
  - `Block` -> explicit reject rule
- TUN-mode startup logs now include a per-app policy summary:
  - app path
  - rule mode
  - mapped sing-box action/outbound
- TUN-mode DNS hardening now includes explicit DNS reject rules for `Block` apps
- full end-to-end runtime validation is still pending

## Step 18
Phase 5 of the TUN pivot: DNS hardening, loop prevention refinement, and compatibility tuning.
Status: pending
Scope:
- tighten DNS rule behavior
- validate `strict_route` behavior
- refine local/LAN/private bypass handling

## Step 19
Phase 6 of the TUN pivot: de-emphasize legacy transparent-relay / WFP paths after TUN proves stable.
Status: in progress
Scope:
- mark legacy paths clearly
- remove or reduce obsolete mainline assumptions
Progress:
- first Phase 6 step is now in place:
  - service state/status plumbing is TUN-oriented rather than legacy-capture-oriented
  - shared `GetState` / `StatusChanged` contracts now include:
    - selected mode
    - sing-box running state
    - tunnel interface up/down summary
    - active profile id/name
    - proxy/direct/block rule counts
  - new status work is intentionally aligned with a future TUN-only cleanup and does not introduce new primary legacy capture internals
- next narrow Phase 6 UI step is now completed:
  - the UI consumes the richer TUN-oriented status/state fields
  - the main window now shows a compact runtime summary card for:
    - mode
    - engine status
    - tunnel status
    - active profile
    - proxy/direct/block rule counts
  - the UI step keeps runtime behavior unchanged and does not introduce new primary legacy-capture status panels
- next narrow Phase 6 cleanup-preparation step is now completed:
  - Sessions is now mode-aware in the UI
  - in TUN mode it is shown as unavailable rather than as a normal active feature
  - legacy mode keeps the existing Sessions grid behavior unchanged
- next narrow Phase 6 UI polish step is now completed:
  - the sidebar connectivity indicator now explicitly describes service availability
  - wording is now:
    - `Service: On`
    - `Service: Off`
  - this keeps the existing layout and avoids broad UI redesign
- next narrow Phase 6.2 step is now completed:
  - the UI has an offline config fallback path
  - when the service is unavailable, the UI now loads saved config locally from the persisted service config file instead of appearing empty
  - runtime summaries remain explicitly unavailable while saved profiles/rules/active profile stay visible
  - this keeps the product aligned with TUN-first / future TUN-only cleanup and avoids adding new legacy-capture-first status concepts
- next recommended Phase 6.3 step:
  - add narrow service start/restart controls to the UI so offline config visibility can be paired with an explicit way to bring the service back without leaving the app
- next narrow Phase 6.3 step is now completed:
  - the UI now has minimal `Start Service` / `Restart Service` controls
  - the service-control path stays narrow and Windows-appropriate:
    - direct `ServiceController` first
    - elevated one-shot PowerShell fallback on access denied
  - offline config fallback remains intact while recovery controls are available
  - focused UI/view-model validation now passes after a narrow `LogViewModel` test-context safety fix
- next narrow Phase 6.3 polish step is now completed:
  - the UI now handles the "service not installed" case gracefully
  - the service action area shows a short friendly status instead of raw Windows service exception text
  - detailed failure text is kept in the log only
  - the existing control mechanism remains unchanged:
    - `ServiceController` first
    - elevated PowerShell fallback on access denied
- next recommended Phase 6.4 step:
  - add a small explicit reconnect/retry UX around service recovery completion, without broadening into a full service-management surface
- next narrow Phase 6.4 cleanup step is now completed:
  - Sessions has been removed from the visible main navigation and normal user flow
  - deeper Sessions internals remain temporarily in place to keep the cleanup low-risk
- next narrow Phase 6.5 polish step is now completed:
  - reconnect-loop noise in the user-facing UI log has been reduced
  - expected offline/retry behavior is now summarized with friendly service transition messages instead of repeated low-level transport errors
- next narrow Phase 6.6 UX-consistency step is now completed:
  - App Rules and Profile editing are disabled while the tunnel is running
  - the UI now reflects that runtime uses a started configuration snapshot rather than live editable state
  - small inline hints explain that the tunnel must be stopped before editing
- next narrow Phase 6.7 polish step is now completed:
  - removed the old QUIC / HTTP3 comment text from App Rules
  - cleaned up the header spacing/layout in App Rules and Log
  - kept the change XAML-only with no behavior changes
- next narrow Phase 6.7 header alignment correction is now completed:
  - moved the App Rules and Log header action buttons to a compact left-aligned position beside their titles
  - matched the Clear button sizing to the Add Application button sizing
  - kept the change XAML-only with no behavior changes
- next narrow Phase 6.7 Log cleanup step is now completed:
  - removed the Clear button from the Log view to reduce unnecessary UI noise
  - kept the rest of the Log layout unchanged
- next narrow Phase 6.8 profile selection UX cleanup is now completed:
  - made active profile choice explicit in the Profile view
  - added a top-of-view profile picker and clear active-profile summary
  - moved activation next to profile selection and left the form focused on editing/saving
- next narrow Phase 6.8 profile control-row follow-up is now completed:
  - narrowed the selected-profile control row
  - added `Add New` as an explicit entry point for local unsaved profile creation
  - kept save/activation behavior and active-profile summary semantics unchanged
- next narrow Phase 6.8 Add New selector-visibility bug fix is now completed:
  - kept the improved top profile-selection row visible during new-profile mode
  - fixed the local UI-state bug that hid the selector row after `Add New`
- next narrow Phase 6.8 profile repair pass is now completed:
  - replaced the `User ID` `PasswordBox` with a direct `TextBox` binding
  - removed the fragile `ProfileView.xaml.cs` sync path for `UserId`
  - tightened profile action enablement so:
    - `Save Profile` requires service connection plus valid editable fields
    - `Set Active` is disabled in new-profile mode, offline mode, and already-active state
  - added a small profile hint model:
    - running tunnel -> stop the tunnel to edit
    - offline service -> start the service to save changes
- next narrow Phase 6.8 profile save/validation repair is now completed:
  - added dirty-state tracking to the profile form
  - `Save Profile` is now enabled only when:
    - editing is enabled
    - service is connected
    - the form is valid
    - and there are unsaved changes
  - REALITY profiles now require:
    - `RealityPublicKey`
    - `RealityShortId`
    before save can execute
  - successful saves now reset the dirty baseline so the save action disables again until the next edit
- next narrow Phase 6.8 profile delete flow is now completed:
  - added `Delete Profile` for existing saved profiles only
  - delete is disabled in `Add New` mode and when no existing selected profile is available
  - delete now uses a lightweight confirmation step before removing a profile
  - added the smallest clean backend path for `DeleteProfile` through IPC/service config persistence
  - after successful delete, the selector and active-profile summary now stay consistent with the remaining profiles or fall back to a clean empty state
- next narrow Phase 6.8 profile UI polish pass is now completed:
  - REALITY-only fields are now shown only when `Security == "reality"`
  - a small REALITY helper line explains that public key and short ID are required
  - save-state presentation is now clearer in the UI while keeping the existing dirty/save logic intact
  - the top profile selector row has been compacted slightly without changing the overall layout
- next recommended Phase 6.9 step:
  - perform a second-pass internal cleanup of remaining Sessions-specific UI plumbing only after the TUN-first main path remains stable

## Step 20
Design: elevated helper/bootstrapper component for system lifecycle management.
Status: completed
Scope:
- design only — no code written
- covers service install, repair, uninstall, start, restart, and future update path
- full design spec at `docs/tunnelflow-helper-design.md`
Key decisions:
- new project `TunnelFlow.Helper` — net8.0-windows console exe with `requireAdministrator` manifest
- CLI verbs: install, repair, uninstall, start, stop, restart, status
- 9 structured exit codes (0=Success … 8=AccessDenied)
- UI invokes via `Process.Start` with `UseShellExecute = true` (manifest handles UAC)
- Install layout: Program Files for binaries, ProgramData for runtime data
- Phase 1: build helper in isolation; Phase 2: replace PowerShell fallback in UI; Phase 3: updater
- `sc.exe` child-process approach for service registration in Phase 1 (simpler than System.Configuration.Install)
- Dev fallback: if helper exe not present, fall back to existing PowerShell path

## Step 21
Phase 1 of the Windows bootstrapper approach: add a real bootstrapper project scaffold.
Status: completed
Scope:
- add a Windows-specific elevated console project:
  - `src/TunnelFlow.Bootstrapper`
- define explicit lifecycle verbs:
  - `install`
  - `repair`
  - `uninstall`
  - `start-service`
  - `restart-service`
- add:
  - entrypoint
  - command parsing
  - shared service/install/data path constants
  - explicit exit codes
- keep installer/update logic out of scope for now
Outcome:
- `TunnelFlow.Bootstrapper` is now a real solution project with an embedded elevation manifest
- `start-service` and `restart-service` are implemented narrowly through `ServiceController`
- `install`, `repair`, and `uninstall` are intentionally scaffolded and return `NotImplemented`
- chosen concrete direction is now `Bootstrapper` naming rather than the earlier `Helper` placeholder name
Next recommended step:
- Phase 2: integrate `TunnelFlow.Bootstrapper.exe` into the existing UI service-control path so:
  - installed systems stop depending on PowerShell `runas`
  - future `Install Service` / `Repair Service` UI actions have a real elevated backend
