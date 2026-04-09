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
- next TUN-only cleanup Phase 3 step is now completed:
  - removed the remaining service-side runtime planning/fallback branch that still orchestrated around legacy capture startup
  - `OrchestratorService` now fails closed when TUN mode is disabled or prerequisites are unmet instead of falling back to legacy mode
  - orchestration is now aligned around the intended TUN-only runtime path:
    - validate TUN selection
    - start `ITunOrchestrator`
    - build TUN sing-box config
    - start sing-box in TUN mode
  - the compiled legacy capture stack, `ndisapi.net`, and session IPC/contracts remain intentionally in place for later dedicated removal phases
- next TUN-only cleanup Phase 4 step is now completed:
  - removed the direct `TunnelFlow.Service -> TunnelFlow.Capture` project/build dependency
  - removed capture-specific DI wiring from the service host startup
  - removed `OrchestratorService` dependencies on `ICaptureEngine` and `IPolicyEngine`
  - kept `GetSessions` IPC as an empty compatibility stub for now
  - the remaining capture project, `ndisapi.net`, and capture/session contracts remain intentionally in place for later cleanup phases
- next TUN-only cleanup Phase 5 step is now completed:
  - removed `TunnelFlow.Capture` from the active solution/build graph
  - removed the remaining dead session IPC/service plumbing from the active service path
  - removed the old capture/session contract files from active `TunnelFlow.Core` compilation
  - removed the capture test slice from active test compilation
  - `third_party/ndisapi.net` is no longer part of the active build graph
  - physical capture/vendor files are still present on disk for a later deletion-only pruning pass
- next TUN-only cleanup Phase 6 step is now completed:
  - physically removed:
    - `src/TunnelFlow.Capture/`
    - `src/TunnelFlow.Tests/Capture/`
    - `third_party/ndisapi.net/`
  - physically removed the now-dead core capture/session contract source files
  - removed stale repo metadata tied to the deleted legacy stack:
    - `.gitmodules`
    - obsolete `.gitignore` entries
    - old capture build logs
  - historical legacy docs remain intentionally in place as reference material, but they are no longer part of the active build/runtime path

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

## Step 22
Phase 1.1 of the Windows bootstrapper approach: make install and repair real.
Status: completed
Scope:
- keep the bootstrapper Windows-specific and narrow
- implement:
  - `install`
  - `repair`
- keep:
  - `uninstall` stubbed for now
  - no UI integration yet
  - no updater logic yet
Outcome:
- `install` now:
  - resolves and verifies the intended `TunnelFlow.Service.exe`
  - creates the `TunnelFlow` Windows service via `sc.exe`
  - sets startup mode to `auto`
  - starts the service
- `repair` now:
  - creates the service if missing
  - otherwise refreshes service config via `sc.exe config`
  - ensures expected `binPath` and startup mode
  - restarts the service
- exit-code handling remains explicit and aligned with the existing bootstrapper model
Next recommended step:
- Phase 2: replace the current UI PowerShell fallback with bootstrapper invocation and add a minimal `Install Service` / `Repair Service` path in the existing service-recovery UX

## Step 23
Phase 1.2 of the Windows bootstrapper approach: improve default dev-path service resolution.
Status: completed
Scope:
- keep explicit `--service-exe` behavior unchanged
- improve default service exe lookup for current repository/dev layout
- improve operator-facing resolution failure messaging
Outcome:
- bootstrapper default lookup now tries:
  - sibling `TunnelFlow.Service.exe`
  - repo-relative `src\TunnelFlow.Service\bin\Debug\net8.0-windows\TunnelFlow.Service.exe`
  - repo-relative `src\TunnelFlow.Service\bin\Release\net8.0-windows\TunnelFlow.Service.exe`
- missing-resolution errors now list checked candidate paths and suggest `--service-exe <path>`
Next recommended step:
- Phase 2: integrate `TunnelFlow.Bootstrapper` into the UI service-control path while keeping the current explicit Windows lifecycle surface narrow

## Step 24
Phase 2 of the Windows bootstrapper approach: narrow UI integration for service lifecycle actions.
Status: completed
Scope:
- integrate the existing sidebar service-action flow with `TunnelFlow.Bootstrapper`
- support at minimum:
  - `install`
  - `repair`
  - `restart-service`
- keep the UI change narrow and preserve friendly status/log behavior
Outcome:
- UI service action now selects among:
  - `Install Service`
  - `Repair Service`
  - `Restart Service`
- bootstrapper verbs now invoked from UI:
  - `install`
  - `repair`
  - `restart-service`
- `start-service` / `restart-service` retain the old direct service-control fallback if the bootstrapper executable is missing
- not-installed state no longer disables the service action; it now offers install directly
- focused UI/view-model validation is now green after a narrow test-setup repair:
  - `Profile.SaveCommand` and `Profile.ActivateCommand` were already correctly gated by service connection and existing-profile selection
  - the affected `MainViewModel` test now sets up those preconditions explicitly
Next recommended step:
- Phase 2.1: add a narrow explicit `Repair Service` / `Install Service` failure mapping layer so more bootstrapper exit codes can be surfaced as friendlier UI statuses without broadening into a full service-management surface

## Step 25
Phase 2.2 of the Windows bootstrapper approach: implement uninstall.
Status: completed
Scope:
- keep the lifecycle step narrow and Windows-specific
- implement only:
  - stop service if present
  - delete Windows service registration
- preserve ProgramData/config/logs
Outcome:
- `uninstall` now:
  - returns `NotInstalled` cleanly when the service is missing
  - stops the service if needed
  - deletes the `TunnelFlow` service via `sc.exe delete`
  - waits for the registration to disappear
  - preserves `C:\ProgramData\TunnelFlow`
Next recommended step:
- Phase 2.3: improve UI-facing bootstrapper exit-code mapping for install/repair/uninstall so more lifecycle failures become short friendly statuses without expanding into a full management UI

## Step 26
Phase 2.3 of the Windows bootstrapper approach: narrow UI integration for uninstall.
Status: completed
Scope:
- add a small destructive uninstall action to the existing sidebar service-control area
- require confirmation
- keep the UI change minimal and avoid a separate management page
Outcome:
- installed state now shows a secondary `Uninstall Service` action
- uninstall uses bootstrapper `uninstall`
- confirmation is required before execution
- successful uninstall transitions the UI cleanly to:
  - not installed
  - disconnected/offline runtime summaries
  - `Install Service` as the primary remaining service action
- focused `MainViewModel` validation is green
Next recommended step:
- Phase 2.4: improve bootstrapper exit-code to UI-status mapping for uninstall/install/repair so common failure cases become more specific than the current generic `Service action failed`

## Step 27
Phase 2.4 of the Windows bootstrapper approach: fix service install-state detection in UI startup/offline flow.
Status: completed
Scope:
- add an explicit installed/not-installed probe to the service-control layer
- use that probe during startup and disconnected/offline UI state
- keep the change narrow and avoid redesigning the sidebar
Outcome:
- cold startup/offline state no longer assumes the service is installed
- UI now correctly shows:
  - not installed -> `Install Service`, uninstall hidden
  - installed but disconnected -> `Repair Service`, uninstall visible
  - connected -> `Restart Service`, uninstall visible
- focused `MainViewModel` validation is green
Next recommended step:
- Phase 2.5: improve UI-facing bootstrapper failure mapping so install/repair/uninstall common errors become specific short statuses instead of the current generic `Service action failed`

## Step 28
Phase 2.4.1 of the Windows bootstrapper approach: fix the service-action reconnect race in UI state.
Status: completed
Scope:
- keep the lifecycle actions unchanged:
  - `Install Service`
  - `Repair Service`
  - `Restart Service`
- prevent stale post-action waiting text from overwriting a newer connected state
- keep the fix narrow to `MainViewModel` state handling and focused tests
Outcome:
- successful reconnect now always wins over stale `Waiting for service connection...` UI state
- `RequestServiceActionAsync()` now sets the waiting status only if:
  - the UI is still disconnected
  - and the pending action still matches the original request
- focused `MainViewModel` validation is green, including a race-specific reconnect test
Next recommended step:
- Phase 2.5: improve UI-facing bootstrapper failure mapping so install/repair/uninstall common errors become specific short statuses instead of the current generic `Service action failed`

## Step 29
Phase 2.5 of the Windows bootstrapper approach: narrow service-management polish in the UI.
Status: completed
Scope:
- improve lifecycle failure/status mapping without changing bootstrapper architecture
- keep detailed failure text in the log, with short friendly UI statuses
- disable disruptive repair/uninstall actions while the tunnel is running
Outcome:
- lifecycle status mapping is now more specific:
  - `Administrator approval required`
  - `Service bootstrapper not available`
  - `Install timed out`
  - `Repair timed out`
  - `Uninstall timed out`
  - `Install failed`
  - `Repair failed`
  - `Uninstall failed`
- raw exception text remains log-only
- when the tunnel is active:
  - `Repair Service` is not executable
  - `Uninstall Service` is hidden / not executable
- focused `MainViewModel` validation is green
Next recommended step:
- Phase 2.6: add one narrow UI hint or tooltip for disabled service actions during active tunnel runtime so the user understands why repair/uninstall are unavailable without expanding into a larger management surface

## Step 30
Phase 2.5.1 of the Windows bootstrapper approach: fix active-tunnel service-action consistency.
Status: completed
Scope:
- keep service lifecycle availability internally consistent while the tunnel is active
- avoid special-casing connected restart when other disruptive actions are already blocked
- keep the change narrow to `MainViewModel` command gating and focused tests
Outcome:
- active tunnel now disables all disruptive service lifecycle actions consistently:
  - `Restart Service`
  - `Repair Service`
  - `Uninstall Service`
- focused `MainViewModel` validation is green
Next recommended step:
- Phase 2.6: add one narrow UI hint or tooltip for disabled service actions during active tunnel runtime so the user understands why service actions are unavailable without expanding into a larger management surface

## Step 31
Phase 2.5.2 of the Windows bootstrapper approach: polish uninstall visibility during active tunnel runtime.
Status: completed
Scope:
- keep uninstall consistent with the primary service action in the sidebar
- preserve the active-tunnel rule that disruptive service actions are unavailable
- keep the change narrow to UI visibility/enabled-state behavior
Outcome:
- when the service is installed and the tunnel is active:
  - `Uninstall Service` remains visible
  - `Uninstall Service` is disabled
- focused `MainViewModel` validation is green
Next recommended step:
- Phase 2.6: add one narrow UI hint or tooltip for disabled service actions during active tunnel runtime so the user understands why service actions are unavailable without expanding into a larger management surface

## Step 32
Phase 1 of direct URL config import: add a narrow single-profile import path in the Profile UI.
Status: completed
Scope:
- add a small direct URL import surface in the existing Profile view
- support HTTP/HTTPS fetch only
- support one practical direct format:
  - fetched `vless://...` single-profile content
- reuse the existing profile persistence/selection flow
- keep subscription refresh and background sync out of scope
Outcome:
- Profile UI now accepts an HTTP/HTTPS direct URL and imports one `vless://` profile
- imported data is mapped into the existing `VlessProfile` model and persisted via the existing `UpsertProfile` path
- imported profile is selected in the existing profile flow with a short success/failure result
- focused importer/profile validation is green
Next recommended step:
- Phase 1.1 of direct URL import: add one more small compatibility slice for realistic direct imports, such as better handling for fetched content that contains a single profile plus surrounding whitespace/comments, while still avoiding subscription semantics

## Step 33
Phase 1.1 of direct URL config import: support direct pasted `vless://` input in the same import field.
Status: completed
Scope:
- keep the existing HTTP/HTTPS fetch path working
- expand the same import field/button to also accept direct pasted `vless://...` URIs
- keep parsing logic centralized in the existing import service
- update friendly validation messaging to match the broader supported input
Outcome:
- the Profile import field now supports:
  - `http://` / `https://` URLs that fetch remote content
  - direct pasted `vless://...` URIs
- direct `vless://` input is now parsed locally without a remote fetch
- the importer still reuses the existing single-profile VLESS mapping into the current `VlessProfile` model
- validation/help text now says the field accepts either an HTTP/HTTPS URL or a direct `vless://` URI
- focused importer/profile validation is green
Next recommended step:
- Phase 1.2 of direct URL import: add one more narrow compatibility slice for fetched content that includes a single supported profile plus surrounding comments/metadata lines, while still avoiding full subscription semantics

## Step 34
Phase 1 of subscription URL import: add narrow multi-profile import over HTTP/HTTPS.
Status: completed
Scope:
- keep the existing import UI surface
- support subscription-style HTTP/HTTPS content that yields multiple supported profiles
- reuse the current VLESS parsing/mapping path
- keep background refresh, scheduled sync, and subscription management UI out of scope
Outcome:
- the existing Profile import field can now import multiple profiles from a subscription URL
- supported fetched content in this phase:
  - plain text with multiple URI-style lines
  - base64-encoded subscription content that decodes into multiple lines
- supported imported profile format remains explicit:
  - `vless://...`
- import now handles partial success cleanly:
  - supported VLESS profiles are imported
  - unsupported entries are skipped
  - UI shows a short batch summary such as:
    - `Imported 2 profiles.`
    - `Imported 2 profiles; skipped 1 unsupported entry.`
- focused importer/profile validation is green
Note:
- a later dedicated TUN-only cleanup phase must still remove legacy capture / WinpkFilter-era paths from the final product once the remaining TUN-first product surface is complete
Next recommended step:
- Phase 1.1 of subscription import: add one more narrow compatibility slice for common subscription payload variations, such as extra comments/metadata wrappers around otherwise supported multi-profile content, while still avoiding background refresh semantics

## Step 35
Phase 0 of TUN-only cleanup: audit and inventory remaining legacy capture / WinpkFilter-era surface.
Status: completed
Scope:
- audit only
- no code removal yet
- no runtime/TUN behavior changes
- identify what is still:
  - required now
  - user-invisible / likely removable later
  - uncertain and needs deeper verification
Outcome:
- current build/runtime still depends on legacy capture code because:
  - `TunnelFlow.Service` still references `TunnelFlow.Capture`
  - `TunnelFlow.Capture` still references `third_party/ndisapi.net`
  - `ndisapi.dll` is still copied as part of the capture project
  - the service still retains a real legacy runtime branch through:
    - `ICaptureEngine`
    - `IPacketDriver`
    - `LocalRelay`
    - `ITcpRedirectProvider`
- already user-invisible / likely removable later:
  - Sessions UI/view-model leftovers
  - WFP experimental redirect scaffolding
  - `UseWfpTcpRedirect` legacy flag
  - stub/fallback capture placeholders
- uncertain / verify later:
  - session IPC/contracts
  - capture-era tests that may be archived rather than simply deleted
  - legacy-named config/settings that should eventually be renamed rather than only removed
Phased cleanup recommendation:
1. Remove remaining hidden Sessions plumbing from UI/state flow.
2. Remove WFP experimental redirect path and its config flag.
3. Remove the legacy transparent-relay runtime branch from the service and make runtime fully TUN-only.
4. Remove `TunnelFlow.Capture`, `ndisapi.net`, and `ndisapi.dll` from build/runtime.
5. Rename/prune final legacy-facing config, status, tests, and docs.
Important release-direction note:
- a later dedicated TUN-only cleanup phase must remove legacy capture / WinpkFilter-era paths from the final product before final release
Next recommended step:
- Phase 1 of TUN-only cleanup: remove the already user-invisible Sessions leftovers and other low-risk UI/state plumbing first, before touching service/runtime dependencies

## Step 36
Phase 1 of TUN-only cleanup: remove hidden Sessions leftovers.
Status: completed
Scope:
- remove only the remaining dead Sessions UI/state plumbing
- keep service/runtime capture/session contracts intact for now
- avoid touching `TunnelFlow.Capture` / `ndisapi.net` in this step
Outcome:
- removed:
  - `SessionsViewModel`
  - `SessionsView.xaml`
  - `SessionsView.xaml.cs`
  - the `SessionsViewModel -> SessionsView` app data template
  - hidden Sessions sidebar/button residue
  - `MainViewModel` session event handling and Sessions property/construction
- kept intentionally:
  - service-side session IPC/contracts and capture-session runtime plumbing
  - these were not proven unused in this same narrow step and still belong to the later legacy-runtime cleanup phases
- focused validation is green:
  - test-project build passed
  - focused `MainViewModelTests` passed with class-name filter after the exact fully-qualified filter returned no matches in this environment
Next recommended step:
- Phase 2 of TUN-only cleanup: remove the WFP experimental redirect path and the `UseWfpTcpRedirect` legacy flag before moving on to the higher-risk service/runtime branch cleanup

## Step 37
Phase 2 of TUN-only cleanup: remove WFP experimental redirect leftovers and legacy flag surface.
Status: completed
Scope:
- remove only the dead WFP experimental redirect branch and its config/plumbing surface
- keep the main legacy capture stack in place for now
- avoid touching `TunnelFlow.Capture` project existence and `ndisapi.net` in this step
Outcome:
- removed:
  - `src/TunnelFlow.Capture/TcpRedirect/`
  - `native/TunnelFlow.WfpRedirectChannel/`
  - `native/TunnelFlow.WfpRedirectDriver/`
  - WFP-specific capture tests
  - `UseWfpTcpRedirect` from service config persistence
  - service DI/start/stop plumbing for the redirect provider
- simplified the remaining legacy relay path back to its existing NAT-table lookup only
- current TUN behavior remains unchanged
- current legacy capture stack still exists, but without the experimental WFP redirect branch
Next recommended step:
- Phase 3 of TUN-only cleanup: remove the remaining legacy transparent-relay runtime branch from the service and make the runtime path TUN-only before the later final capture-project/`ndisapi.net` removal phase

## Step 41
Repository hygiene cleanup: remove tracked generated/build artifacts from the repository state.
Status: completed
Scope:
- identify tracked build/generated junk still living in Git after the TUN-only cleanup work
- remove only safe generated artifacts such as tracked `bin/` and `obj/` outputs
- keep source files and real runtime assets intact
Outcome:
- removed tracked build output from:
  - `src/TunnelFlow.Core/bin/` and `src/TunnelFlow.Core/obj/`
  - `src/TunnelFlow.Service/bin/` and `src/TunnelFlow.Service/obj/`
  - `src/TunnelFlow.Tests/bin/` and `src/TunnelFlow.Tests/obj/`
  - `src/TunnelFlow.UI/bin/` and `src/TunnelFlow.UI/obj/`
- used `git rm --cached` so local build output remains available to the developer while leaving the repository clean
- `.gitignore` did not need changes in this step because it already covered the intended build/generated junk via the existing `bin/`, `obj/`, and `*.log` patterns
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~OrchestratorServiceTests|FullyQualifiedName~ConfigStoreTests|FullyQualifiedName~MainViewModelTests" --logger "console;verbosity=minimal"`

## Step 42
UI polish: improve default main window size and obvious vertical alignment inconsistencies.
Status: completed
Scope:
- XAML-only polish
- no runtime/service/TUN changes
- no layout redesign
Outcome:
- main window default size increased from `960x620` to `1180x760`
- main window minimum size increased from `720x480` to `960x640`
- normalized visible control heights/alignment in:
  - `AppRulesView`
  - `ProfileView`
  - `LogView`
- kept the current TUN-first layout and visual structure intact
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`

## Step 43
UI correction: tighten main window size and profile row widths to the preferred screenshot-driven layout.
Status: completed
Scope:
- very small XAML-only correction
- no behavior changes
- no layout redesign
Outcome:
- main window default size changed from `1180x760` to `1090x990`
- `Selected profile` selector width reduced from `220` to `214`
- import URL text box width increased from `340` to `386` so the import action aligns more cleanly with the profile action row below
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`

## Step 44
UI rollback-and-correct pass: restore sane window sizing and compact Profile row heights.
Status: completed
Scope:
- surgical XAML correction only
- no behavior changes
- no layout redesign
Outcome:
- main window default size changed from `1090x990` to `972x640`
- main window minimum size changed from `960x640` to `720x480`
- `Selected profile` selector width changed from `214` to `208`
- import URL text box width changed from `386` to `388`
- removed the Profile `MinHeight` / `VerticalContentAlignment` tweaks that had made controls feel globally taller
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`

## Step 45
Final UI correction pass: adjust default window size, App Rules mode width, and Profile import alignment.
Status: completed
Scope:
- very small XAML-only correction
- no behavior changes
- no layout redesign
Outcome:
- main window default size changed from `972x640` to `1088x990`
- minimum size remained `720x480`
- App Rules mode selector column width changed from `120` to `108`
- Profile import URL text box width changed from `388` to `360` to bring the `Import URL` button into clean right-edge alignment with the `Delete` button below
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`

## Step 46
Subscription import Phase 2: retain saved subscription source and add manual update.
Status: completed
Scope:
- narrow extension of the existing direct/subscription import flow
- manual update only
- no background refresh or scheduler
- no runtime/TUN changes
Outcome:
- added saved subscription metadata on imported profiles:
  - `SubscriptionSourceUrl`
  - `SubscriptionProfileKey`
- preserved that metadata through service config persistence and offline UI config loading
- added a small `Update subscription` UI path for selected imported subscription profiles
- update behavior now:
  - fetches the same source URL again
  - updates matching imported profiles in place
  - adds new profiles from the source
  - reports concise mixed-result summaries
- intentionally not implemented in this phase:
  - automatic refresh
  - scheduled sync
  - removal of profiles that disappeared from the remote subscription
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.DirectUrlProfileImportServiceTests|FullyQualifiedName~TunnelFlow.Tests.UI.ProfileViewModelTests" --logger "console;verbosity=minimal"`

## Step 52
Runtime connectivity warning evidence: add a coarse UI warning model with anti-noise suppression.
Status: completed
Scope:
- narrow service/UI state addition only
- no runtime/TUN changes
- no healthy/connectivity semantics
Outcome:
- added shared optional runtime warning state to service/UI payloads:
  - `AuthenticationFailure`
  - `ConnectionProblem`
- implemented a conservative service-side classifier over existing sing-box log lines
- removed `Mode` from the runtime panel UI and added one optional `Warning` row
- added anti-noise suppression so weak close/reset/forcibly-closed noise is cleared and suppressed after later successful outbound VLESS activity
- intentionally kept strong auth and strong connection evidence classified
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.OrchestratorServiceTests|FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --logger "console;verbosity=minimal"`

## Step 53
Runtime connectivity warning evidence Phase 3: boundary-based reset behavior.
Status: completed
Scope:
- narrow warning-lifetime refinement only
- no runtime/TUN changes
- no healthy/connectivity semantics
Outcome:
- added explicit safe reset boundaries for structured warning evidence:
  - start attempt
  - stop
  - sing-box restarting
  - sing-box stopped
  - sing-box crashed
  - UI service-unavailable/offline boundary
- kept strong warning evidence within a single active runtime session
- avoided timer-based aging or a broader health engine
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.OrchestratorServiceTests|FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --logger "console;verbosity=minimal"`

## Step 54
Runtime connectivity warning evidence: stop clearing weak warnings from bare outbound VLESS connection-start lines.
Status: completed
Scope:
- narrow classifier refinement only
- no runtime/TUN changes
- no healthy/connectivity semantics
Outcome:
- removed the optimistic success heuristic that treated bare `outbound/vless ... connection to ...` lines as enough to clear weak warning evidence
- weak `Connection problem` evidence now remains until an explicit reset boundary occurs
- strong auth and strong connection-failure classification remain unchanged
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.OrchestratorServiceTests|FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --logger "console;verbosity=minimal"`

## Step 55
Runtime connectivity warning evidence: add temporary propagation diagnostics.
Status: completed
Scope:
- diagnostics only
- no warning semantic changes
- no runtime/TUN changes
Outcome:
- added temporary service log lines when warning evidence is:
  - set by classifier
  - cleared/reset at runtime boundaries
  - present in a pushed status payload
- diagnostics are explicit enough to confirm whether classifier, reset, or payload propagation fired
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.OrchestratorServiceTests" --logger "console;verbosity=minimal"`

## Step 56
Persistent service-side file log.
Status: completed
Scope:
- diagnostics/logging only
- no runtime/TUN changes
Outcome:
- added a persistent `TunnelFlow.Service` log file at:
  - `C:\ProgramData\TunnelFlow\logs\service.log`
- service-side orchestration/status/error logs now persist alongside `singbox.log`
- kept existing logging behavior and added only a small file sink
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`

## Step 57
Runtime connectivity warning evidence: repeated weak transport-close aggregation.
Status: completed
Scope:
- narrow classifier refinement only
- no runtime/TUN changes
- no healthy/connectivity semantics
Outcome:
- added a small per-session weak-evidence counter
- threshold `2` weak close/reset lines now raises `Connection problem`
- one isolated weak close/reset line still does not warn
- preserved strict auth classification and strong connection classification
- reset weak aggregation on the existing safe runtime boundaries
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.OrchestratorServiceTests|FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --logger "console;verbosity=minimal"`

## Step 58
Tunnel lifecycle hardening: focused validation repair after stricter TUN prerequisite gating.
Status: completed
Scope:
- keep the new lifecycle semantics intact
- fix the failing focused overlap tests without weakening them
- no new runtime/TUN behavior changes beyond the already-intended lifecycle hardening
Outcome:
- confirmed the service-side lifecycle model was not the direct cause of the three focused test failures
- found the actual issue in the focused harness:
  - the fake TUN orchestrator claimed activation support but exposed a non-existent `ResolvedWintunPath`
  - the stricter TUN prerequisite check therefore blocked `StartCaptureAsync` before fake TUN/sing-box calls occurred
- updated the harness to create and use a temporary fake `wintun.dll` path so the lifecycle tests exercise the intended `Starting` / `Stopping` overlap behavior
- removed a no-op fake-event warning so the focused build stays clean
- validated that the overlap/ordering tests now pass with the intended lifecycle semantics still in place:
  - start allowed only from `Stopped`
  - stop allowed only from `Running`
  - overlapping start during `Starting` or `Stopping` fails closed immediately
  - stop order remains `sing-box` first, then TUN
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.OrchestratorServiceTests|FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --logger "console;verbosity=minimal"`

## Step 59
UI graceful shutdown flow for active tunnel sessions.
Status: completed
Scope:
- narrow UI shutdown hardening only
- no owner/lease/heartbeat yet
- no runtime/TUN behavior changes
Outcome:
- `MainViewModel` now tracks the service `LifecycleState` from state/status payloads
- closing the main window while lifecycle is not fully `Stopped` now:
  - cancels the first close
  - runs an async shutdown flow
  - requests `StopCapture` only when lifecycle is `Running`
  - waits on real lifecycle transitions for `Starting` / `Stopping`
  - disposes the client cleanly before allowing the final close
- duplicate close/shutdown flows are now blocked in `MainWindow`
- `ServiceClient.Dispose()` is now explicit and idempotent so pending pipe/connect resources are torn down cleanly
- removed the old synchronous `App.OnExit` stop path that could fight the new close flow
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --logger "console;verbosity=minimal"`

## Step 60
Tunnel owner lease / heartbeat for non-graceful UI termination, plus reconnect-hang repair.
Status: completed
Scope:
- narrow owner/lease/heartbeat only
- no broad IPC refactor
- preserve graceful shutdown behavior
Outcome:
- added a stable `SessionId` on `ServiceClient`
- `StartCapture` now carries `ownerSessionId`
- added `OwnerHeartbeat` IPC command/payload
- service now tracks:
  - active owner session id
  - last heartbeat time
- UI now sends heartbeats every `5s` while it owns the active tunnel session
- service now expires the owner lease after `15s` without heartbeat and then performs the normal controlled tunnel stop path
- added owner session propagation in `StatePayload` / `StatusPayload`
- preserved owner session across service-side profile-activation restart while already running
- reconnect hang root cause was validated as a test cleanup issue:
  - the reconnect heartbeat test called graceful shutdown while still leaving lifecycle at `Running`
  - no `Stopped` payload was ever applied, so shutdown waited forever by design
- narrow reconnect-lifecycle repair applied:
  - test now supplies a final `Stopped` status before cleanup
  - heartbeat loop restart now uses loop-version invalidation so canceled loops cannot continue sending after reconnect replacement
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests.ApplyStatePayload_WhenOwnedTunnelReconnects_RestartsHeartbeatLoop" --blame-hang --blame-hang-timeout 15s --logger "console;verbosity=minimal"`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.OrchestratorServiceTests|FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --blame-hang --blame-hang-timeout 20s --logger "console;verbosity=minimal"`

## Step 61
Current-session traffic stats Phase 1: V2Ray API viability check.
Status: blocked
Scope:
- audit only
- verify whether the current bundled sing-box path can support honest current-session traffic counters through V2Ray API stats
- do not implement fake counters or alternate telemetry in this step
Outcome:
- blocked on sing-box build capability
- the bundled binary at `third_party/singbox/sing-box.exe` reports:
  - `with_clash_api`
  - but not `with_v2ray_api`
- official sing-box docs say:
  - V2Ray API is not included by default
  - traffic stats require `experimental.v2ray_api.stats`
  - V2Ray API support is tied to the `with_v2ray_api` build tag
- current repo config generation does not yet emit `experimental.v2ray_api`
- current service/runtime path has no API stats poller or session-baseline counter model
Decision:
- stop implementation here instead of adding fake counters
- do not silently fall back to:
  - log-derived byte estimates
  - Windows interface counters
  - a different sing-box API
Next recommended step:
- either:
  - ship/build the bundled Windows sing-box binary with `with_v2ray_api`, then implement session-baseline polling against V2Ray API stats
- or:
  - explicitly approve a separate design step to evaluate Clash API traffic counters as an alternative source
Validation:
- docs-only blocker audit
- no build/test run in this step

## Step 62
Profile tab subscription UI cleanup: move presence state into the active header and replace crowded selector lines with a compact subscription block.
Status: completed
Scope:
- Profile tab only
- smallest necessary view-model/test surface
- no runtime/service/TUN changes
Outcome:
- active profile header now includes subscription presence in parentheses when relevant
- header suffix colors:
  - green for `Present in subscription`
  - red for `Missing from subscription`
- removed the old subscription/source/helper lines from under the selector
- added a compact `Subscription` block with:
  - `Server`
  - read-only subscription URL textbox
  - existing `Update subscription` button
- preserved stale missing-from-source warning and explicit stale cleanup action inside the subscription block
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.ProfileViewModelTests" --logger "console;verbosity=minimal"`

## Step 63
Profile tab runtime-crash repair after subscription UI cleanup.
Status: completed
Scope:
- narrow Profile XAML runtime-crash isolation and fix only
- no runtime/service/TUN changes
Outcome:
- confirmed the split active-profile header fragment was not the crash source
- confirmed the new `Subscription` block under the selector was the crash source in the real WPF render path
- kept the colored split header
- replaced the crashing bordered/grid-heavy subscription block with a flatter safe block
- kept these subscription affordances:
  - source URL display
  - `Update subscription`
  - stale missing-from-source warning
  - `Remove stale profile`
- safe presentation compromise:
  - URL display now uses a trimmed `TextBlock` instead of the previous read-only `TextBox`
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.ProfileViewModelTests" --logger "console;verbosity=minimal"`
- launched `src/TunnelFlow.UI/bin/Debug/net8.0-windows/TunnelFlow.UI.exe`
- invoked the `Profile` navigation button via UI Automation
- verified the process remained running after opening the Profile tab

## Step 64
Profile subscription block follow-up: safe read-only URL TextBox and shorter Update button label.
Status: completed
Scope:
- very small Profile XAML follow-up only
- keep the current safe subscription block structure
- no runtime/service/view-model behavior changes
Outcome:
- replaced the subscription URL `TextBlock` with a plain read-only `TextBox`
- renamed the subscription action button from `Update subscription` to `Update`
- kept the block otherwise structurally close to the already-safe version
- narrow runtime-only fix applied:
  - `TextBox.Text` now binds with `Mode=OneWay` so the read-only display property does not crash the real WPF Profile tab through default two-way `TextBox` binding semantics
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.ProfileViewModelTests" --logger "console;verbosity=minimal"`
- launched `src\TunnelFlow.UI\bin\Debug\net8.0-windows\TunnelFlow.UI.exe`
- invoked the `Profile` navigation button via UI Automation
- verified the process remained running after opening the Profile tab

## Step 65
Profile subscription row visual polish: align the safe URL field and Update button more cleanly.
Status: completed
Scope:
- very small Profile XAML polish only
- keep the current safe subscription block structure and bindings
- no runtime/service/view-model behavior changes
Outcome:
- preserved the same simple safe `Subscription` row structure
- widened the read-only one-way-bound URL `TextBox` slightly
- moved the gap to the textbox right margin for cleaner row rhythm
- added a small fixed minimum width and centered alignment to the `Update` button so the row lines up more like the form fields below
- did not reintroduce the older richer/crashy subscription block
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.ProfileViewModelTests" --logger "console;verbosity=minimal"`
- launched `src\TunnelFlow.UI\bin\Debug\net8.0-windows\TunnelFlow.UI.exe`
- invoked the `Profile` navigation button via UI Automation
- verified the process remained running after opening the Profile tab

## Step 66
Profile title polish: move the edit hint into the title and remove the standalone helper line.
Status: completed
Scope:
- very small Profile-only UI polish
- reuse the existing edit-hint state and wording
- no runtime/service/view-model behavior changes beyond a computed title string
Outcome:
- added `ProfileTitle` in the view-model so the title now renders as:
  - `VLESS Profile`
  - or `VLESS Profile (<existing hint text>)` when the existing edit hint is active
- removed the old standalone helper line from below the selector area
- preserved the existing hint wording and logic; only the presentation changed
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.ProfileViewModelTests" --logger "console;verbosity=minimal"`
- launched `src\TunnelFlow.UI\bin\Debug\net8.0-windows\TunnelFlow.UI.exe`
- invoked the `Profile` navigation button via UI Automation
- verified the process remained running after opening the Profile tab
- verified the title was present in the UI Automation tree and the old standalone helper line text was absent

## Step 67
Release-hardening cleanup: remove legacy localhost-SOCKS / WinpkFilter active release-path assumptions.
Status: completed
Scope:
- focused release-surface cleanup only
- no broad refactor
- preserve the working TUN-only runtime path
Outcome:
- removed the dormant localhost-SOCKS readiness branch from `SingBoxManager`
  - deleted `WaitForSocksPortAsync`
  - deleted strategy-selection plumbing for SOCKS readiness
  - active startup readiness is now always TUN/process-observation
- tightened `SingBoxConfigBuilder` to the actual release path
  - builder now throws if called with `UseTunMode = false`
  - generated config no longer has a legacy localhost SOCKS inbound branch
- aligned focused service tests with the TUN-only builder/readiness path
- rewrote the root release-facing docs so they no longer present WinpkFilter / `ndisapi.net` / `TunnelFlow.Capture` / localhost SOCKS as the active architecture:
  - `README.md`
  - `ARCHITECTURE.md`
  - `CURSOR_RULES.md`
  - `DATAFLOW.md`
  - `DECISIONS.md`
  - `RISKS.md`
  - `COMPONENTS.md`
  - `PHASE2_PLAN.md` now retained only as a short historical marker
- confirmed repo metadata state:
  - `.gitmodules` already absent
  - no `ndisapi`-specific `.gitignore` cleanup remained to do
- focused validation repair:
  - updated one stale `MainViewModelTests` punctuation expectation so the requested focused suite matched the current UI text
- runtime/config verification:
  - `TunnelFlow.UI.exe` launched successfully
  - short UI Automation snapshot showed:
    - `Service: Off`
    - `Install Service`
    - disabled `Start Tunnel` / `Stop Tunnel`
  - direct launch of the built `TunnelFlow.Service.exe` as a normal process exited immediately in this environment and did not append fresh `service.log` lines
  - existing `singbox_last.json` still showed:
    - `tun-in`
    - no localhost `socks` / `socks-in` / `127.0.0.1` inbound
  - result:
    - build/test validation is green
    - fresh live tunnel start validation is currently blocked until the service backend is available to the UI again
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.OrchestratorServiceTests|FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --logger "console;verbosity=minimal"`

## Step 68
Add a simple About tab to the main navigation shell.
Status: completed
Scope:
- narrow UI/navigation step only
- no runtime/service behavior changes
- keep the page compact and release-friendly
Outcome:
- added a new main navigation destination:
  - `About`
- wired `MainViewModel.CurrentView` switching for:
  - `AboutViewModel`
- added a compact `AboutView` that shows:
  - app name `TunnelFlow`
  - runtime assembly version
  - icon/logo placeholder area
  - short product description
  - placeholder project link `http://www.sample.com`
  - small footer text
- used the narrowest straightforward version source:
  - `AssemblyInformationalVersionAttribute`
  - falling back to assembly version
- extended focused UI navigation coverage so `MainViewModelTests` now includes About navigation
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --logger "console;verbosity=minimal"`
- launched `src\TunnelFlow.UI\bin\Debug\net8.0-windows\TunnelFlow.UI.exe`
- verified:
  - About appears in navigation
  - switching to About works
  - version is shown
  - `http://www.sample.com` is visible

## Step 49
Subscription status display cleanup: remove persistent import/update text from under the Import URL row.
Status: in progress
Scope:
- very small Profile UI cleanup only
- keep persistent subscription state in the selected-profile subscription/source block
- keep import/update logic unchanged
Outcome so far:
- removed the rendered `ImportStatus` line from directly under the `Import from URL` field
- persistent subscription state remains in the selected-profile subscription/source block
- underlying import/update result state remains intact for possible reuse elsewhere
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`

## Step 50
Subscription wording clarification: rename subscription presence state to avoid fake health semantics.
Status: in progress
Scope:
- wording/UI clarification only
- keep subscription logic unchanged
- no runtime/TUN changes
Outcome so far:
- renamed the selected-profile subscription state wording from:
  - `Subscription active`
  - `Subscription not active`
- to:
  - `Present in subscription`
  - `Missing from subscription`
- this now clearly describes presence in the latest known subscription source, not service health or upstream reachability
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`

## Step 51
Runtime connectivity state Phase 1: audit current observability before adding any UI health semantics.
Status: completed
Scope:
- audit/inventory only
- no runtime/TUN changes
- no speculative health checks
Outcome:
- current reliable structured state is limited to local runtime/service facts:
  - service connected/disconnected
  - selected mode
  - sing-box process status
  - tunnel interface up/down
  - active profile and rule counts
- current upstream/connectivity evidence exists only in raw log text:
  - sing-box stdout/stderr forwarded over IPC
  - sing-box file log output
  - service log lines
- there is currently no honest structured signal for:
  - upstream reachable
  - auth/account valid
  - transport healthy
  - traffic proven working
- agreed minimal next step:
  - add a small service-side classifier for existing sing-box log text and surface only detected failure evidence, without inventing a "healthy" state
Validation:
- docs-only audit
- no build was needed

## Step 47
Subscription UX polish: make subscription-backed profiles and manual update state easier to understand.
Status: completed
Scope:
- small Profile UX polish only
- no import/update semantic changes
- no background sync or scheduling
Outcome:
- subscription-backed profiles now show a clear selector suffix:
  - `(Subscription)`
  - `(Active, Subscription)` when applicable
- the Profile subscription source area now shows:
  - explicit source wording
  - the saved source URL
  - a short helper line explaining manual update
- import/update result messages are more explicit about subscription actions
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.ProfileViewModelTests" --logger "console;verbosity=minimal"`

## Step 48
Subscription import Phase 3: safely retain profiles that disappear from the remote source.
Status: completed
Scope:
- manual update safety only
- no silent deletion
- no background sync or scheduler
- no runtime/TUN changes
Outcome:
- added `SubscriptionMissingFromSource` metadata on subscription-backed profiles
- preserved that metadata through config persistence and offline UI loading
- manual update now detects profiles from the same source that no longer appear remotely
- those profiles are kept locally and marked instead of being deleted
- selector/UI wording now makes the missing-from-source state explicit
- update summaries now include missing-from-source counts
Validation:
- `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
- `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.DirectUrlProfileImportServiceTests|FullyQualifiedName~TunnelFlow.Tests.UI.ProfileViewModelTests" --logger "console;verbosity=minimal"`
