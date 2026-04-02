# TunnelFlow project memory

## Active Architecture Docs
- Current primary design reference:
  - `docs/tunnelflow-wintun-singbox-tun-design.md`
- Historical / diagnostic R&D reference:
  - `docs/wfp-tcp-redirect-poc-plan.md`

## TUN Phase 6 status-model plumbing
- Implemented in this step:
  - extended the shared state/status contract with a TUN-oriented runtime summary
  - kept runtime networking behavior unchanged
  - explicitly kept new status work aligned with the TUN-first / future TUN-only direction rather than legacy capture internals
- Exact files changed:
  - `src/TunnelFlow.Core/IPC/Responses/TunnelStatusMode.cs`
  - `src/TunnelFlow.Core/IPC/Responses/StatusPayload.cs`
  - `src/TunnelFlow.Core/IPC/Responses/StatePayload.cs`
  - `src/TunnelFlow.Service/Ipc/PipeServer.cs`
  - `src/TunnelFlow.Service/OrchestratorService.cs`
  - `src/TunnelFlow.Tests/Service/OrchestratorServiceTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- New status fields added:
  - `selectedMode`
  - `singBoxRunning`
  - `tunnelInterfaceUp`
  - `activeProfileId`
  - `activeProfileName`
  - `proxyRuleCount`
  - `directRuleCount`
  - `blockRuleCount`
- Population notes:
  - `selectedMode`
    - uses the active runtime mode while running
    - falls back to current mode selection logic from config/prerequisites while stopped
  - `singBoxRunning`
    - derived from `SingBoxStatus == Running`
  - `tunnelInterfaceUp`
    - currently a narrow service-side TUN summary:
      - true only when selected mode is `Tun`
      - TUN activation is active
      - sing-box is running
    - this is intentionally TUN-oriented and avoids expanding legacy capture-state semantics
  - `activeProfileId` / `activeProfileName`
    - derived from the current saved config and active profile lookup
  - rule counts
    - count enabled process-path rules by mode (`Proxy` / `Direct` / `Block`)
    - this matches the current TUN policy model more closely than legacy capture internals
- Contract notes:
  - `GetState` now returns the richer TUN-oriented summary via `StatePayload`
  - `StatusChanged` now uses a typed `StatusPayload` instead of an ad hoc hand-built JSON object
  - legacy `captureRunning` and `singboxStatus` compatibility fields remain present so existing UI plumbing continues to compile
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.OrchestratorServiceTests" --logger "console;verbosity=minimal"`
    - passed: 5
    - failed: 0
    - skipped: 0

## TUN Phase 6 UI runtime status card
- Implemented in this step:
  - repaired the partial Phase 6 UI/status patch so it compiles cleanly
  - `MainViewModel` now consumes the richer TUN-oriented `StatePayload` / `StatusPayload`
  - the main window sidebar now shows a compact runtime card instead of extending legacy capture-first status concepts
- Exact files changed:
  - `src/TunnelFlow.UI/ViewModels/MainViewModel.cs`
  - `src/TunnelFlow.UI/MainWindow.xaml`
  - `src/TunnelFlow.UI/Properties/InternalsVisibleTo.cs`
  - `src/TunnelFlow.Tests/TunnelFlow.Tests.csproj`
  - `src/TunnelFlow.Tests/UI/MainViewModelTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- TUN-oriented fields now displayed in the UI:
  - mode
  - engine status
  - tunnel status
  - active profile name
  - proxy/direct/block rule counts
- View-model notes:
  - `MainViewModel` now applies both `GetState` and `StatusChanged` through the richer shared contract
  - compact summaries are exposed as:
    - `ModeSummary`
    - `EngineStatusSummary`
    - `TunnelStatusSummary`
    - `RuleCountsSummary`
  - the UI still keeps `captureRunning` only for existing start/stop command enablement compatibility; new status presentation is TUN-oriented
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --logger "console;verbosity=minimal"`
    - passed: 2
    - failed: 0
    - skipped: 0

## TUN Phase 6 Sessions mode-aware cleanup
- Implemented in this step:
  - the Sessions UI is now explicitly mode-aware
  - in TUN mode, Sessions is no longer presented as a normal active feature
  - legacy mode keeps the existing Sessions grid behavior unchanged
- Exact files changed:
  - `src/TunnelFlow.UI/ViewModels/SessionsViewModel.cs`
  - `src/TunnelFlow.UI/ViewModels/MainViewModel.cs`
  - `src/TunnelFlow.UI/Views/SessionsView.xaml`
  - `src/TunnelFlow.Tests/UI/MainViewModelTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- UI behavior:
  - TUN mode:
    - the Sessions view replaces the grid with a simple message:
      - title: `Sessions unavailable in TUN mode`
      - body: `Sessions are available only in legacy transparent-proxy mode.`
  - Legacy mode:
    - the existing active-sessions grid remains visible and unchanged
- View-model notes:
  - `SessionsViewModel` now has:
    - `IsAvailable`
    - `UnavailableMessage`
    - `SetMode(TunnelStatusMode selectedMode)`
  - `MainViewModel` now propagates `selectedMode` into `SessionsViewModel`
  - this keeps the cleanup narrowly UI-focused and avoids expanding legacy capture semantics elsewhere
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.UI.MainViewModelTests" --logger "console;verbosity=minimal"`
    - passed: 2
    - failed: 0
    - skipped: 0

## TUN pivot Phase 0.5 service skeleton
- Implemented in this step:
  - persisted `UseTunMode` flag in service config storage
  - added a no-op service-side TUN orchestration abstraction and DI registration
  - intentionally did not change runtime selection or sing-box config generation yet
- Exact files changed:
  - `src/TunnelFlow.Service/Configuration/TunnelFlowConfig.cs`
  - `src/TunnelFlow.Service/Configuration/ConfigStore.cs`
  - `src/TunnelFlow.Service/Program.cs`
  - `src/TunnelFlow.Service/Tun/ITunOrchestrator.cs`
  - `src/TunnelFlow.Service/Tun/TunOrchestrationConfig.cs`
  - `src/TunnelFlow.Service/Tun/NoOpTunOrchestrator.cs`
  - `src/TunnelFlow.Tests/Service/ConfigStoreTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Current effect:
  - config persistence can now express the future TUN mode
  - the service container now has a dedicated seam for future Wintun/TUN lifecycle work
  - current runtime behavior remains unchanged because the new orchestrator is a stub and is not yet selecting the TUN path
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.ConfigStoreTests" --logger "console;verbosity=minimal"`

## TUN pivot Phase 1 builder skeleton
- Implemented in this step:
  - added `UseTunMode` to `SingBoxConfig`
  - `SingBoxConfigBuilder` now emits a minimal TUN inbound skeleton when `UseTunMode=true`
  - legacy SOCKS inbound generation remains unchanged when `UseTunMode=false`
  - service runtime selection is still not cut over to TUN mode yet
- Exact files changed:
  - `src/TunnelFlow.Core/Models/SingBoxConfig.cs`
  - `src/TunnelFlow.Service/SingBox/SingBoxConfigBuilder.cs`
  - `src/TunnelFlow.Tests/Service/SingBoxConfigBuilderTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Minimal TUN skeleton shape now generated:
  - inbound `type = "tun"`
  - `tag = "tun-in"`
  - `interface_name = "TunnelFlow"`
  - `mtu = 1500`
  - `auto_route = true`
  - `strict_route = true`
- Current effect:
  - builder can now express the future TUN path in config snapshots/tests
  - current service runtime behavior remains unchanged because `UseTunMode` is not yet passed into the active startup path
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.SingBoxConfigBuilderTests" --logger "console;verbosity=minimal"`

## TUN pivot Phase 2 service mode selection
- Implemented in this step:
  - `OrchestratorService` now evaluates an effective runtime mode from:
    - requested `UseTunMode`
    - whether a Wintun DLL appears present
    - whether the active TUN orchestrator supports activation
  - the effective mode is passed into `SingBoxConfig.UseTunMode`
  - legacy mode remains the effective fallback when TUN is requested but activation is not yet supported
- Exact files changed:
  - `src/TunnelFlow.Service/OrchestratorService.cs`
  - `src/TunnelFlow.Service/Tun/ITunOrchestrator.cs`
  - `src/TunnelFlow.Service/Tun/NoOpTunOrchestrator.cs`
  - `src/TunnelFlow.Service/Tun/TunModeSelection.cs`
  - `src/TunnelFlow.Tests/Service/TunModeSelectorTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Structured logs added:
  - requested TUN mode
  - selected effective mode
  - whether TUN prerequisites appear satisfied
  - whether TUN activation is supported yet
  - selection reason
  - selected Wintun path candidate
- Current effect:
  - legacy runtime behavior stays unchanged
  - if `UseTunMode=true`, the service now logs why it still remains on legacy mode until a real activating TUN orchestrator exists
  - no full runtime cutover to Wintun/TUN has been introduced yet
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.TunModeSelectorTests" --logger "console;verbosity=minimal"`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.SingBoxConfigBuilderTests" --logger "console;verbosity=minimal"`

## TUN pivot Phase 3 minimal process-based route and DNS rules
- Implemented in this step:
  - `SingBoxConfig` now carries app rules into builder generation
  - when `UseTunMode=true`, `SingBoxConfigBuilder` now emits:
    - proxy-app `route.rules` entries keyed by `process_path`
    - proxy-app `dns.rules` entries keyed by `process_path`
    - `route.final = "direct"` so non-selected apps remain direct by default
    - `dns.final = "local-dns"` so non-selected apps keep local DNS by default
  - legacy mode generation remains unchanged
- Exact files changed:
  - `src/TunnelFlow.Core/Models/SingBoxConfig.cs`
  - `src/TunnelFlow.Service/OrchestratorService.cs`
  - `src/TunnelFlow.Service/SingBox/SingBoxConfigBuilder.cs`
  - `src/TunnelFlow.Tests/Service/SingBoxConfigBuilderTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Minimal rule shape introduced for effective TUN mode:
  - route rule:
    - `process_path = ["C:\\Path\\App.exe"]`
    - `action = "route"`
    - `outbound = "vless-out"`
  - dns rule:
    - `process_path = ["C:\\Path\\App.exe"]`
    - `action = "route"`
    - `server = "remote-dns"`
  - only enabled `Proxy` app rules are included in this first slice
- Current effect:
  - config snapshots can now express one minimal per-app TUN routing model
  - current runtime fallback behavior is unchanged because effective TUN mode is still not activated by the no-op orchestrator
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.SingBoxConfigBuilderTests" --logger "console;verbosity=minimal"`

## TUN pivot Phase 4 activation-capable orchestrator slice
- Implemented in this step:
  - replaced the TUN no-op-only DI path with a real `WintunTunOrchestrator`
  - added Wintun path resolution for the first likely local asset locations:
    - `third_party/wintun/bin/amd64/wintun.dll`
    - `third_party/wintun/wintun.dll`
    - `wintun.dll` next to the service binary
  - `SupportsActivation` is now true only when a resolved Wintun DLL actually exists
  - the first real activation boundary is now:
    - load `wintun.dll` into the service process on TUN activation start
    - unload it again on stop
  - `OrchestratorService` now:
    - logs the resolved Wintun path through mode selection
    - attempts TUN activation before sing-box startup when TUN mode is effectively selected
    - falls back to legacy mode if TUN activation fails
- Exact files changed:
  - `src/TunnelFlow.Service/Tun/ITunOrchestrator.cs`
  - `src/TunnelFlow.Service/Tun/TunOrchestrationConfig.cs`
  - `src/TunnelFlow.Service/Tun/NoOpTunOrchestrator.cs`
  - `src/TunnelFlow.Service/Tun/WintunPathResolver.cs`
  - `src/TunnelFlow.Service/Tun/WintunTunOrchestrator.cs`
  - `src/TunnelFlow.Service/Program.cs`
  - `src/TunnelFlow.Service/OrchestratorService.cs`
  - `src/TunnelFlow.Tests/Service/WintunTunOrchestratorTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Structured logs added:
  - resolved Wintun path in mode selection logs
  - TUN activation attempt
  - TUN activation success
  - TUN activation failure with fallback to legacy mode
- Current effect:
  - the service can now honestly enter effective TUN mode when the Wintun DLL asset is present and loads successfully
  - legacy behavior remains the fallback when Wintun is missing or activation fails
  - this is still not full production TUN lifecycle management yet; adapter/interface creation and full runtime validation remain next
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.WintunTunOrchestratorTests" --logger "console;verbosity=minimal"`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.TunModeSelectorTests.Select_" --logger "console;verbosity=minimal"`

## TUN inbound validity fix
- Confirmed runtime blocker:
  - after successful TUN mode selection and Wintun DLL activation, sing-box still crashed on startup with:
    - `start inbound/tun[tun-in]: missing interface address`
- Narrow fix applied:
  - added a minimal IPv4 `address` field to the TUN inbound skeleton:
    - `["172.19.0.1/30"]`
- Exact files changed:
  - `src/TunnelFlow.Service/SingBox/SingBoxConfigBuilder.cs`
  - `src/TunnelFlow.Tests/Service/SingBoxConfigBuilderTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Current effect:
  - the builder now emits a minimally valid TUN inbound address for sing-box runtime startup
  - legacy mode remains unchanged
  - broader production TUN address/routing policy is still deferred
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.SingBoxConfigBuilderTests.Build_UseTunModeTrue_" --logger "console;verbosity=minimal"`

## TUN-mode sing-box readiness fix
- Confirmed runtime blocker:
  - after TUN mode selection, successful Wintun activation, and successful sing-box process launch, `SingBoxManager` still always waited for the SOCKS inbound port to become responsive
  - that readiness rule only matches the legacy architecture; in TUN mode the generated sing-box config uses a `tun` inbound and has no SOCKS listener to probe
- Narrow fix applied:
  - `SingBoxManager` now selects startup readiness by effective config mode:
    - legacy mode:
      - keep the existing SOCKS port readiness probe unchanged
    - TUN mode:
      - skip the SOCKS probe
      - require that the sing-box process stays alive during a short startup observation window before marking it `Running`
  - added explicit readiness logs for:
    - startup mode
    - readiness strategy
    - readiness success/failure reason
- Exact files changed:
  - `src/TunnelFlow.Service/TunnelFlow.Service.csproj`
  - `src/TunnelFlow.Service/SingBox/SingBoxManager.cs`
  - `src/TunnelFlow.Tests/Service/SingBoxManagerTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Current effect:
  - legacy mode startup behavior is unchanged
  - TUN mode no longer waits on an invalid SOCKS readiness condition
  - if sing-box exits during the TUN startup observation window, readiness fails explicitly instead of incorrectly marking the process as running
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.SingBoxManagerTests" --logger "console;verbosity=minimal"`
    - passed: 4
    - failed: 0
    - skipped: 0

## TUN-mode service lifecycle split fix
- Confirmed runtime blocker:
  - even after the service selected TUN mode, activated Wintun successfully, and generated a correct TUN-mode sing-box config, the service still continued into the legacy transparent-proxy startup path
  - that meant `CaptureEngine`, WinpkFilter-backed redirect behavior, and `LocalRelay` startup were still mixed into the same runtime, which polluted logs and contradicted the new TUN architecture
- Narrow fix applied:
  - `OrchestratorService` now builds a runtime plan from the selected mode:
    - legacy mode:
      - `legacyCaptureEnabled=true`
      - `localRelayEnabled=true`
      - `winpkFilterEnabled=true`
    - TUN mode:
      - `legacyCaptureEnabled=false`
      - `localRelayEnabled=false`
      - `winpkFilterEnabled=false`
  - in TUN mode the service now starts only:
    - Wintun orchestration
    - sing-box with the TUN config
  - in TUN mode the service no longer starts:
    - `CaptureEngine`
    - `LocalRelay`
    - legacy transparent redirect behavior
  - `SingBoxManager` now truncates/recreates the configured sing-box log output file before each start so every run has clean evidence
- Exact files changed:
  - `src/TunnelFlow.Service/OrchestratorService.cs`
  - `src/TunnelFlow.Service/SingBox/SingBoxManager.cs`
  - `src/TunnelFlow.Tests/Service/OrchestratorServiceTests.cs`
  - `src/TunnelFlow.Tests/Service/SingBoxManagerTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Current effect:
  - legacy runtime behavior remains unchanged
  - TUN mode now follows the intended primary architecture without starting the old capture/relay path in parallel
  - sing-box log output is reset on each start, which makes runtime evidence easier to interpret
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.OrchestratorServiceTests" --logger "console;verbosity=minimal"`
    - passed: 2
    - failed: 0
    - skipped: 0
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.SingBoxManagerTests" --logger "console;verbosity=minimal"`
    - passed: 5
    - failed: 0
    - skipped: 0

## TUN-mode policy expansion for Direct and Block rules
- Confirmed current runtime milestone:
  - TUN mode starts successfully
  - the TunnelFlow interface is up
  - selected Floorp traffic is routed through `outbound/vless[vless-out]`
  - non-selected browser traffic remains direct
- Narrow policy expansion applied:
  - in TUN mode, `SingBoxConfigBuilder` now maps all enabled process-path app rules by mode:
    - `Proxy`:
      - route matched apps to `vless-out`
      - keep corresponding DNS rules to `remote-dns`
    - `Direct`:
      - explicitly route matched apps to `direct`
      - no extra DNS rule added because TUN-mode DNS already defaults to `local-dns`
    - `Block`:
      - explicitly reject matched apps with a process-path route rule
      - no extra DNS rule added in this narrow step
- Exact files changed:
  - `src/TunnelFlow.Service/SingBox/SingBoxConfigBuilder.cs`
  - `src/TunnelFlow.Tests/Service/SingBoxConfigBuilderTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Route and DNS rule shape now covered in TUN mode:
  - `Proxy` route rule:
    - `process_path = ["C:\\Path\\App.exe"]`
    - `action = "route"`
    - `outbound = "vless-out"`
  - `Proxy` DNS rule:
    - `process_path = ["C:\\Path\\App.exe"]`
    - `action = "route"`
    - `server = "remote-dns"`
  - `Direct` route rule:
    - `process_path = ["C:\\Path\\App.exe"]`
    - `action = "route"`
    - `outbound = "direct"`
  - `Block` route rule:
    - `process_path = ["C:\\Path\\App.exe"]`
    - `action = "reject"`
- Current effect:
  - legacy mode remains unchanged
  - the working TUN-mode `Proxy` behavior is preserved
  - TUN mode can now express explicit per-app `Direct` and `Block` outcomes without widening lifecycle or UI scope
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.SingBoxConfigBuilderTests" --logger "console;verbosity=minimal"`
    - passed: 21
    - failed: 0
    - skipped: 0

## TUN-mode policy observability improvement
- Confirmed runtime gap:
  - TUN-mode `Block` behavior appeared to work at runtime, but the service logs did not clearly show how each app rule had been translated into sing-box policy
- Narrow diagnostics added:
  - `OrchestratorService` now emits a TUN policy summary during TUN-mode startup only
  - for each enabled process-path app rule, the service logs:
    - app path
    - rule mode
    - mapped sing-box action
    - mapped outbound when applicable
  - summary examples now emitted:
    - `Proxy` -> `mappedAction=route`, `mappedOutbound=vless-out`
    - `Direct` -> `mappedAction=route`, `mappedOutbound=direct`
    - `Block` -> `mappedAction=reject`, `mappedOutbound=(none)`
- Exact files changed:
  - `src/TunnelFlow.Service/OrchestratorService.cs`
  - `src/TunnelFlow.Tests/Service/OrchestratorServiceTests.cs`
  - `src/TunnelFlow.Tests/Service/SingBoxConfigBuilderTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Test readability improvement:
  - the `Block` builder test now extracts the specific block rule first and asserts its `action` and missing `outbound` directly, which makes the expected mapping easier to read
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.SingBoxConfigBuilderTests" --logger "console;verbosity=minimal"`
    - passed: 21
    - failed: 0
    - skipped: 0

## TUN-mode DNS hardening for Block rules
- Confirmed policy gap:
  - TUN-mode `Block` apps were rejected at the route layer, but they still had no explicit DNS hardening
  - `Proxy` DNS behavior and default `Direct` DNS behavior were already correct and did not need broader changes
- Narrow hardening applied:
  - in TUN mode, `SingBoxConfigBuilder` now emits explicit DNS rules for enabled `Block` apps:
    - `process_path = ["C:\\Path\\App.exe"]`
    - `action = "reject"`
  - existing behavior remains unchanged for:
    - `Proxy` apps:
      - `process_path -> remote-dns`
    - `Direct` apps:
      - no extra DNS rule
      - default `local-dns`
  - legacy mode remains unchanged
- Exact files changed:
  - `src/TunnelFlow.Service/SingBox/SingBoxConfigBuilder.cs`
  - `src/TunnelFlow.Tests/Service/SingBoxConfigBuilderTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- DNS behavior summary after this step:
  - `Proxy`:
    - explicit DNS rule to `remote-dns`
  - `Direct`:
    - no explicit DNS rule
    - falls through to `local-dns`
  - `Block`:
    - explicit DNS reject rule
- Validation:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.SingBoxConfigBuilderTests" --logger "console;verbosity=minimal"`
    - passed: 22
    - failed: 0
    - skipped: 0

## WFP Redirect Docs
- Active migration design reference:
  - `docs/wfp-tcp-redirect-poc-plan.md`
- Current repository status:
  - Phase 0 skeleton/scaffolding is now in place
  - no real WFP redirect behavior has been implemented yet

## Goal
Bring TunnelFlow to a stable working state for selective per-application transparent proxying on Windows.

## Architectural pivot
- The packet-level TCP redirect path and the WFP-driver redirect exploration are no longer the main delivery architecture.
- The new primary path is:
  - Wintun
  - sing-box TUN inbound
  - process-based routing and DNS rules on Windows
- The previous packet-level / WFP work remains useful as:
  - diagnostics history
  - architecture validation of what did not work as the main product path
  - possible future experimental/reference material
- The active implementation direction is now defined in:
  - `docs/tunnelflow-wintun-singbox-tun-design.md`

## Current user-reported symptom
- Proxy chain starts and reaches the stage of application proxying.
- In the target browser ("froorp"), Google opens, but most other sites do not.

## Current architectural understanding
Intended traffic path:
packet capture -> local relay -> SOCKS5 CONNECT -> sing-box -> remote VLESS server

Updated primary architecture direction:
- TunnelFlow Service controls sing-box in TUN mode on top of Wintun
- sing-box applies process-based route and DNS rules for selected applications
- selected apps are routed into the tunnel path
- non-selected apps remain direct
- the old packet capture / local relay path is no longer the mainline architecture

Important components:
- TunnelFlow.Capture:
  packet interception, process resolution, rule matching, session tracking, transparent relay helpers
- TunnelFlow.Service:
  orchestration, config persistence, IPC server, sing-box lifecycle
- TunnelFlow.UI:
  WPF client for status/config/rules/sessions/logs
- TunnelFlow.Core:
  shared models/interfaces/contracts

Service / UI split remains unchanged:
- Service owns elevated/system operations:
  - sing-box lifecycle
  - Wintun / TUN lifecycle
  - route and DNS generation
  - system networking changes
  - config persistence
  - diagnostics/logging
  - IPC server
- UI remains user-mode:
  - profile/rule editing
  - status/log viewing
  - start/stop requests
  - communication with the service via IPC

## Confirmed code observations from analysis
1. Socks5Connector currently does not handle SOCKS5 reply ATYP=0x03 (domain) in connect response parsing.
2. ProtocolSniffer appears fragile for fragmented or partial TLS ClientHello reads and may fall back too early.
3. SingBoxConfigBuilder appears to ignore profile.Network even though UI/profile models support tcp/ws/grpc selection.
4. Capture path currently appears IPv4-focused; IPv6 handling may be incomplete or absent.
5. SingBoxManager may mark sing-box as running even when SOCKS readiness is not fully confirmed.

## Confirmed patch outcome
- Root cause addressed in this patch:
  hostname-preserving transparent relay could fail when SOCKS5 CONNECT responses used ATYP=0x03 (domain) and when early TLS bytes arrived fragmented, causing sniffing to fall back too early and lose hostname/SNI context.
- Exact files changed:
  - src/TunnelFlow.Capture/TransparentProxy/Socks5Connector.cs
  - src/TunnelFlow.Capture/TransparentProxy/ProtocolSniffer.cs
  - src/TunnelFlow.Tests/Capture/Socks5ConnectorTests.cs
  - src/TunnelFlow.Tests/Capture/ProtocolSnifferTests.cs
- Exact validation results:
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.Socks5ConnectorTests" --logger "console;verbosity=minimal"`
    - passed: 4
    - failed: 0
    - skipped: 0
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.ProtocolSnifferTests" --logger "console;verbosity=minimal"`
    - passed: 1
    - failed: 0
    - skipped: 0
- Local environment note:
  `third_party/ndisapi.net/ndisapi.dll` must exist locally for build/test because TunnelFlow.Capture copies it into output and the capture stack expects it at runtime.

## Working hypothesis
Most likely immediate browsing failure is caused by losing the destination hostname and falling back to IP-based connection for sites that require proper SNI/hostname-based routing.
Google may still work in some fallback scenarios, while many other sites fail.

## New main-path risks
- Routing loops:
  - service/sing-box/self traffic must not route back into the tunnel
  - local management endpoints and control traffic need explicit exclusion
- DNS leaks / `strict_route` behavior:
  - route correctness alone is not enough; DNS handling must be aligned with app-selection behavior
  - strict routing on Windows may create connectivity surprises if local/private exceptions are not handled carefully
- Service / elevation lifecycle:
  - the service remains the privileged owner of system networking actions
  - start/stop/restart behavior for sing-box, Wintun, and related routes must be robust and reversible
- Version pinning:
  - sing-box and Wintun versions should be pinned during early rollout
  - unexpected upstream behavior changes are now a primary product risk

## Diagnostic logging patch
- Runtime evidence now points to the failure occurring before traffic reaches LocalRelay, because sing-box, LocalRelay, and WinpkFilter start successfully but LocalRelay stats remain:
  - `active=0 total=0 sni=0 http=0 ipFallback=0`
- Hypothesis tree the new logs are intended to prove or eliminate:
  - no capture:
    - driver stats remain near zero
    - no `Driver redirect-decision` logs appear when opening a site
  - process resolution mismatch:
    - `Capture flow unresolved` appears for browser flows
    - driver sees new flows but CaptureEngine cannot map them to a process path
  - policy mismatch:
    - `Policy rule-no-match` or unexpected `rule-match` / exclusion logs show flows becoming `Direct`
    - `Capture flow decision` shows action/reason inconsistent with the intended app rule
  - QUIC path:
    - `Policy quic-override` appears for UDP 443
    - driver UDP counters rise but relay accepts remain zero
  - IPv6 bypass:
    - browser symptom persists while IPv4 driver counters stay unexpectedly low
    - this would suggest traffic is not entering the current IPv4-only capture path
  - relay or SOCKS failure:
    - capture and redirect logs appear
    - `Relay accept`, `Relay nat-lookup`, `Relay sniff`, `Relay path-select`, or `SOCKS ...` logs identify the exact handoff failure
- Structured logging added in:
  - `src/TunnelFlow.Capture/CaptureEngine.cs`
  - `src/TunnelFlow.Capture/Policy/PolicyEngine.cs`
  - `src/TunnelFlow.Capture/Interop/WinpkFilterPacketDriver.cs`
  - `src/TunnelFlow.Capture/TransparentProxy/LocalRelay.cs`
  - `src/TunnelFlow.Capture/TransparentProxy/Socks5Connector.cs`
- What the new logs should prove during one `github.com` open:
  - `Driver redirect-decision` confirms capture saw a new flow and whether NAT redirect was armed
  - `Capture flow decision` confirms pid, resolved process path, and final action
  - `Policy ...` confirms which rule matched or why the flow became direct/block
  - `Relay accept` and `Relay nat-lookup` confirm whether redirected traffic actually reached the relay
  - `Relay sniff` and `Relay path-select` confirm domain-preserving vs IP fallback behavior
  - `SOCKS connect-start` through `SOCKS connect-reply` confirm whether sing-box accepted the connect request
- Local environment note:
  - `third_party/ndisapi.net/ndisapi.dll` must exist locally for build/test because TunnelFlow.Capture copies it into output and the capture stack expects it at runtime.

## Priority order for fixes
1. SOCKS5 domain reply parsing
2. Protocol sniffing robustness
3. sing-box outbound builder honoring transport/network mode
4. sing-box readiness validation / health check
5. IPv6 support

## Rules for future investigation
- Prefer minimal patches first.
- Do not perform broad refactors before validating the top hypotheses.
- After each patch, record:
  - files changed
  - test results
  - manual browser/app validation outcome
  - remaining symptoms

## Manual validation checklist
- Start service cleanly
- Start sing-box cleanly
- Enable proxy rule for test app/browser
- Open:
  - google.com
  - github.com
  - cloudflare.com
  - a random CDN-hosted site
- Record:
  - which sites open
  - whether CONNECT/domain logs show hostname preservation
  - whether fallback to IP occurred
  
## Confirmed fix: SingBoxConfigBuilder network mode
- Confirmed that VlessProfile.Network was defined in the model, exposed in the UI, and persisted, but ignored by SingBoxConfigBuilder.
- Before the fix, tcp/ws/grpc profile selections produced the same outbound JSON.
- The builder now:
  - omits transport for tcp
  - emits transport.type = "ws" for ws
  - emits transport.type = "grpc" for grpc

## Validation
- Ran:
  - dotnet test .\src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Service.SingBoxConfigBuilderTests" --logger "console;verbosity=minimal"
- Result:
  - 12 passed, 0 failed

## Diagnostic runtime checklist
- Open `github.com` once and watch for this sequence:
  - `Driver redirect-decision`
  - `Capture flow decision`
  - `Policy rule-match` or `Policy rule-no-match`
  - `Relay accept`
  - `Relay nat-lookup`
  - `Relay sniff`
  - `Relay path-select`
  - `SOCKS connect-start`
  - `SOCKS auth-ok`
  - `SOCKS connect-request`
  - `SOCKS connect-reply`
- Distinguishing failures:
  - no capture:
    - no `Driver redirect-decision` for the browser request
    - driver stats counters do not move
  - process mismatch:
    - `Capture flow unresolved` appears
    - no matching relay activity follows
  - policy mismatch:
    - `Policy rule-no-match`, exclusion, or unexpected `Capture flow decision ... action=Direct`
  - ipv6 bypass:
    - browser request fails but IPv4 counters/logs stay quiet
  - quic path:
    - UDP counters rise and `Policy quic-override` appears, but no TCP redirect reaches relay yet
  - relay failure:
    - redirect and NAT logs appear, but relay or SOCKS milestone logs stop at a specific handoff

## Redirect delivery diagnosis
- New confirmed runtime finding:
  - `CaptureEngine` sees many Floorp TCP flows and marks them `Proxy`
  - `PolicyEngine` matches the proxy rule correctly
  - `WinpkFilterPacketDriver` adds NAT entries and logs `redirect=True` for many TCP flows
  - `LocalRelay` still reports:
    - `total=0 accepted=0 natMiss=0 sni=0 http=0 ipFallback=0 socksOk=0 socksFail=0`
  - a later run also confirms:
    - `LocalRelay selfCheckOk=1`
    - `LocalRelay accepted=0 total=0 socksOk=0 socksFail=0`
- Current diagnosis:
  - redirect decision is happening
  - the selected relay endpoint is reachable locally
  - but captured redirected traffic still never reaches the `LocalRelay` listener
  - the highest-probability seam is now the post-decision redirect application path inside `WinpkFilterPacketDriver`
- Additional diagnostics added for this stage:
  - `WinpkFilterPacketDriver` now traces the first redirected TCP flows with:
    - original src/dst/protocol
    - redirect target
    - whether destination headers were actually rewritten
    - rewritten destination after mutation
    - whether checksum recomputation ran
    - whether `SendPacket` reported success
  - compact driver counters now include:
    - `redirectApplied`
    - `redirectRewriteFailed`
    - `redirectSendFailed`
  - `LocalRelay` now performs a one-time startup self-check by connecting to its own configured listen endpoint before the accept loop begins
  - relay counters now include:
    - `selfCheckOk`
    - `selfCheckFail`
- What the next runtime pass should prove:
  - if `LocalRelay self-check result=ok`, the listener can bind and accept locally
  - if `Driver redirect-apply ... headersRewritten=true ... sendOk=true`, packet mutation/reinject appears successful
  - if those both happen and `Relay accept` still stays at zero for browser traffic, the remaining suspicion is driver-to-stack delivery rather than policy, NAT, SOCKS, or relay sniffing
  - the driver log now also needs to show:
    - `rewriteRan=true`
    - `reinjectPath=adapter` or `reinjectPath=mstcp`
  - if `redirect=True` is present but `Driver redirect-apply` is absent, the remaining gap is inside the post-decision redirect application logging itself

## Relay addressing correction
- Confirmed flaw:
  - the relay endpoint had been hard-coded to `127.0.0.1:2070`
  - but the current WinpkFilter rewrite/reinject model keeps redirected outbound packets on the adapter-oriented path and rewrites responses on the normal receive path
  - that addressing model is incompatible with a loopback relay target
- Implemented correction:
  - `CaptureEngine` now selects a non-loopback local IPv4 host address for the relay listener and driver rewrite target
  - the same selected address and port are passed to both:
    - `WinpkFilterPacketDriver.Configure(...)`
    - `LocalRelay(...)`
  - selection strategy:
    - enumerate active non-loopback network interfaces
    - choose the first usable IPv4 unicast address
    - skip loopback, `0.0.0.0`, IPv6, and link-local `169.254.x.x`
  - if no usable non-loopback IPv4 address exists, startup now fails fast instead of silently falling back to the broken loopback model
- Exact files changed for this step:
  - `src/TunnelFlow.Capture/CaptureEngine.cs`
  - `src/TunnelFlow.Tests/Capture/CaptureEngineTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`

## Redirect reinjection fix
- New confirmed runtime result before this fix:
  - `redirect=True` was logged
  - `Driver redirect-apply` showed:
    - `rewrittenDst=<local relay endpoint>`
    - `reinjectPath=adapter`
    - `sendOk=True`
  - `LocalRelay` still reported:
    - `accepted=0 total=0`
- Exact flaw fixed:
  - redirected outbound packets that had already been rewritten to the local relay endpoint were still being reinjected via the generic outbound path
  - for outbound packets, `SendPacket(...)` follows the packet's send flag and routes them to the adapter path
  - local-relay-directed packets instead need to be reinjected to MSTCP/local stack
- Implemented fix:
  - keep the existing reinjection behavior for all non-redirected packets
  - for redirected outbound relay packets only, force reinjection through `SendPacketToMstcp(...)`
  - existing NAT, destination rewrite, response rewrite, policy, relay, and SOCKS logic remain unchanged
- Exact files changed for this step:
  - `src/TunnelFlow.Capture/Interop/WinpkFilterPacketDriver.cs`
  - `src/TunnelFlow.Tests/Capture/WinpkFilterPacketDriverTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`

## TCP redirect migration design
- Architectural conclusion:
  - the current packet-level TCP-to-listener redirect design is not viable as implemented
  - even after destination rewrite to the relay endpoint and reinjection through MSTCP, `LocalRelay` still receives no accepted TCP connections
  - this confirms that packet mutation/reinject does not create the listener-visible transport connection that `TcpListener.AcceptTcpClientAsync` expects
- Recommended replacement mechanism:
  - Windows Filtering Platform (WFP) ALE connect redirection for TCP
  - use a connection-level redirect at the outbound connect stage so the OS creates a real TCP connection to `LocalRelay`
  - preserve UDP/other packet-observation uses of WinpkFilter separately for now if still needed
- Components to keep:
  - `PolicyEngine`
  - `CaptureEngine` orchestration and session/event flow
  - `LocalRelay`
  - `ProtocolSniffer`
  - `Socks5Connector`
  - sing-box integration
  - UI / IPC / config store / profile model
- Responsibilities inside `WinpkFilterPacketDriver` that must be replaced for TCP:
  - outbound TCP redirect by packet destination rewrite
  - outbound TCP reinjection-path decision for redirected flows
  - packet-level expectation that redirecting a SYN creates a real `TcpListener` accept
  - packet-level NAT-as-transport-bridge assumption for relay-bound TCP
- Responsibilities that can remain or be adapted:
  - flow observation / new-flow events
  - process association for policy evaluation
  - flow end/data activity notifications
  - optional UDP handling if the project still needs WinpkFilter there
- New intended TCP data flow:
  - app creates outbound TCP connect
  - connection-level redirect layer intercepts the connect before it becomes a raw packet rewrite problem
  - OS redirects the connect to `LocalRelay` as a real local TCP connection
  - original destination metadata is stored in a per-flow redirect table keyed by the accepted local client endpoint or a redirect flow identifier
  - `LocalRelay` accepts the real connection and looks up the original destination metadata
  - `ProtocolSniffer` extracts hostname when available
  - `Socks5Connector` performs CONNECT to sing-box using domain or IP as today
- Original-destination metadata model for the migration:
  - preserve the `LocalRelay -> lookup original destination -> SOCKS5` pattern
  - replace the current packet NAT table with a redirect metadata table populated by the connection-level redirect layer
  - key choices for phase 1:
    - accepted client endpoint (`srcIP:srcPort`) if stable and observable
    - or a redirect flow/context id carried in the redirect layer and mapped before relay handling
- Phased migration plan:
  - phase 1: proof of concept
    - build a minimal TCP-only redirect provider using WFP ALE connect redirection
    - redirect a single matched process/path rule to `LocalRelay`
    - populate original-destination metadata and prove `LocalRelay accepted>0`
  - phase 2: integration
    - wire the new TCP redirect provider behind the existing capture/orchestration flow
    - keep `PolicyEngine`, `CaptureEngine`, `LocalRelay`, and SOCKS flow intact
    - route TCP proxy decisions through the new redirect provider instead of packet rewrite
    - keep diagnostic logging around redirect, accepted connections, metadata lookup, and SOCKS outcome
  - phase 3: cleanup
    - remove obsolete TCP packet rewrite/reinject logic and TCP-specific NAT assumptions from `WinpkFilterPacketDriver`
    - retain only the parts still needed for observation and any non-TCP handling
    - simplify relay metadata lookup to the new connection-level redirect table
- Main risks:
  - WFP ALE connect redirection requires Windows-native integration work and likely a new interop layer/driver component
  - original-destination metadata must be mapped reliably from redirected connect to accepted relay socket
  - rollback path should preserve current diagnostics so TCP redirect behavior remains observable during migration

## WFP exploration status
- The WFP redirect effort should now be treated as:
  - diagnostic and R&D work
  - useful history for why the packet-level/local-relay main path was abandoned
  - not the primary delivery path going forward
- The current main implementation direction is the TUN design documented in:
  - `docs/tunnelflow-wintun-singbox-tun-design.md`

## WFP Phase 0 skeleton
- Implemented in this phase:
  - new TCP redirect abstraction layer under `src/TunnelFlow.Capture/TcpRedirect/`
  - feature flag/config switch: `UseWfpTcpRedirect`
  - no-op `ITcpRedirectProvider` implementation
  - in-memory original-destination store skeleton
  - DI wiring in service startup
- Intentionally not implemented yet:
  - native WFP interop
  - real connection-level redirect behavior
  - runtime capture-path switching away from WinpkFilter
- Phase 0 added files:
  - `src/TunnelFlow.Capture/TcpRedirect/ITcpRedirectProvider.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/IOriginalDestinationStore.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/ConnectionRedirectRecord.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/ConnectionLookupKey.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/TcpRedirectStats.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/WfpRedirectConfig.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/NoOpTcpRedirectProvider.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/InMemoryOriginalDestinationStore.cs`

## WFP Phase 1 metadata path preparation
- Implemented in this phase:
  - `LocalRelay` now resolves original destination through the new connection-level metadata path first
  - if that lookup misses, it falls back to the existing WinpkFilter NAT lookup path
  - runtime behavior stays unchanged while the no-op provider/store has no record for a connection
- Exact files changed:
  - `src/TunnelFlow.Capture/CaptureEngine.cs`
  - `src/TunnelFlow.Capture/TransparentProxy/LocalRelay.cs`
  - `src/TunnelFlow.Service/Program.cs`
  - `src/TunnelFlow.Tests/Capture/LocalRelayTests.cs`
  - `src/TunnelFlow.Tests/Capture/InMemoryOriginalDestinationStoreTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Lookup order after this patch:
  1. `ITcpRedirectProvider` / `IOriginalDestinationStore` metadata by `ConnectionLookupKey`
  2. existing WinpkFilter NAT lookup by `srcIP:srcPort`
- Exact validation results:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.InMemoryOriginalDestinationStoreTests" --logger "console;verbosity=minimal"`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.LocalRelayTests" --logger "console;verbosity=minimal"`
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.CaptureEngineTests" --logger "console;verbosity=minimal"`
- Purpose of the phase:
  - prepare `LocalRelay` for future connection-level redirect metadata without enabling real WFP redirect behavior yet

## WFP Phase 2 provider lifecycle skeleton
- Implemented in this phase:
  - added a real `WfpTcpRedirectProvider` skeleton with start/stop/logging only
  - added `FeatureFlagTcpRedirectProvider` to choose the active provider implementation from `UseWfpTcpRedirect`
  - integrated TCP redirect provider lifecycle into `OrchestratorService.StartCaptureAsync()` / `StopCaptureAsync()`
  - kept current WinpkFilter capture path unchanged
- Exact files changed:
  - `src/TunnelFlow.Capture/TcpRedirect/WfpTcpRedirectProvider.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/FeatureFlagTcpRedirectProvider.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/NoOpTcpRedirectProvider.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/TcpRedirectStats.cs`
  - `src/TunnelFlow.Service/Program.cs`
  - `src/TunnelFlow.Service/OrchestratorService.cs`
  - `src/TunnelFlow.Tests/Capture/FeatureFlagTcpRedirectProviderTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Runtime behavior:
  - when `UseWfpTcpRedirect=false`:
    - `FeatureFlagTcpRedirectProvider` selects `NoOpTcpRedirectProvider`
    - provider lifecycle starts/stops, but traffic behavior remains unchanged
  - when `UseWfpTcpRedirect=true`:
    - `FeatureFlagTcpRedirectProvider` selects `WfpTcpRedirectProvider`
    - the provider logs that it is a placeholder stub
    - traffic behavior still remains unchanged because no native redirect is implemented yet
- Structured logs added for this phase:
  - `TCP redirect feature state useWfpTcpRedirect={UseWfpTcpRedirect}`
  - `TCP redirect provider select useWfpTcpRedirect={UseWfpTcpRedirect} implementation={Implementation}`
  - `TCP redirect provider start implementation=wfp-stub useWfpTcpRedirect={UseWfpTcpRedirect} status=placeholder`
  - `TCP redirect provider initialized mode=no-op useWfpTcpRedirect={UseWfpTcpRedirect}`
  - `TCP redirect provider lifecycle-stop implementation={Implementation} useWfpTcpRedirect={UseWfpTcpRedirect}`
  - `TCP redirect provider stop implementation=wfp-stub`
  - `TCP redirect provider stopped mode=no-op`
- Exact validation results:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.FeatureFlagTcpRedirectProviderTests" --logger "console;verbosity=minimal"`
    - passed: 3
    - failed: 0
    - skipped: 0

## WFP Phase 3 minimal vertical slice
- Implemented in this phase:
  - `WfpTcpRedirectProvider` now owns real redirect-metadata lifecycle operations:
    - add metadata record
    - remove metadata record
    - lookup with hit/miss accounting
  - `FeatureFlagTcpRedirectProvider` forwards metadata record operations to the active provider
  - added a live-socket integration test that proves:
    - a metadata record created through `WfpTcpRedirectProvider`
    - is consumed by `LocalRelay`
    - during a real accepted TCP connection path
    - and drives the SOCKS target selection using the metadata path instead of NAT fallback
- Exact files changed:
  - `src/TunnelFlow.Capture/TcpRedirect/IOriginalDestinationStore.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/InMemoryOriginalDestinationStore.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/ITcpRedirectProvider.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/NoOpTcpRedirectProvider.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/WfpTcpRedirectProvider.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/FeatureFlagTcpRedirectProvider.cs`
  - `src/TunnelFlow.Tests/Capture/WfpTcpRedirectProviderIntegrationTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- What is real in this slice:
  - provider-created metadata records are now real state, not placeholders
  - `LocalRelay` metadata lookup path is exercised on a real TCP accept path in tests
  - SOCKS target selection can be driven by metadata from the WFP-side provider path
- What is still stubbed:
  - no native WFP ALE connect redirection
  - no Windows-native interception of outbound connects
  - no real OS-level redirect that causes an arbitrary app connect to arrive at `LocalRelay`
- Exact validation results:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.WfpTcpRedirectProviderIntegrationTests" --logger "console;verbosity=minimal"`
    - passed: 1
    - failed: 0
    - skipped: 0
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.FeatureFlagTcpRedirectProviderTests" --logger "console;verbosity=minimal"`
    - passed: 3
    - failed: 0
    - skipped: 0
- Remaining blocker to the first true redirected accept from a real app:
  - Windows-native WFP ALE connect redirection still has to create the metadata record from a real outbound connect and redirect that connect to `LocalRelay` at the OS connection layer

## WFP Native Slice Plan
- Next goal:
  - implement the smallest real Windows-native slice that can cause one outbound app TCP `connect()` to arrive at `LocalRelay` as a true accepted socket
- Exact native/interop boundary:
  - native component owns:
    - WFP callout registration
    - ALE connect-redirection classify logic
    - redirect-handle creation/destruction
    - redirect-context packaging for one redirected connect
    - loop-prevention checks inside classify
  - managed component owns:
    - provider lifecycle and feature flag
    - opening/closing the native control channel
    - pushing relay endpoint/config to native code
    - receiving redirect-metadata events from native code
    - inserting/removing `ConnectionRedirectRecord` entries in `IOriginalDestinationStore`
- Exact native components needed for the first real slice:
  - a kernel-mode WFP callout driver at `ALE_CONNECT_REDIRECT_V4`
  - a control device exposed by that driver for user-mode configuration and event delivery
  - one redirect handle for connect redirection
  - one sublayer/callout/filter registration path for IPv4 TCP only
- Exact managed components needed for the first real slice:
  - `src/TunnelFlow.Capture/TcpRedirect/WfpTcpRedirectProvider.cs`
    - evolve from metadata-only stub into the user-mode owner of the native session
  - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpNativeInterop.cs`
    - P/Invoke surface for driver/device control and any user-mode WFP management calls
  - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpNativeSession.cs`
    - safe lifetime wrapper around native startup/shutdown/config/event pump
  - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpRedirectEvent.cs`
    - managed representation of one redirected-connect metadata event
- Minimal file/class layout for the native side:
  - `native/TunnelFlow.WfpRedirectDriver/DriverEntry.c`
  - `native/TunnelFlow.WfpRedirectDriver/RedirectCallout.c`
  - `native/TunnelFlow.WfpRedirectDriver/DeviceControl.c`
  - `native/TunnelFlow.WfpRedirectDriver/SharedTypes.h`
  - `native/TunnelFlow.WfpRedirectDriver/TunnelFlowWfpRedirect.inf`
- Smallest first real data flow:
  1. one allowed test app calls outbound IPv4 TCP `connect(remoteIp, remotePort)`
  2. WFP `ALE_CONNECT_REDIRECT_V4` callout classifies the connect
  3. callout checks loop guards and only redirects if:
     - TCP
     - IPv4
     - configured test app match
     - not already redirected
     - destination is not the relay endpoint
     - process is not service/sing-box
  4. callout redirects the connect to the configured relay endpoint
  5. native side emits one metadata event containing:
     - lookup key for the future accepted relay client endpoint
     - original destination
     - relay endpoint
     - process id / app id if available
  6. managed `WfpTcpRedirectProvider` receives that event and stores `ConnectionRedirectRecord`
  7. `LocalRelay` accepts the redirected connection and resolves original destination from the metadata store
  8. existing `ProtocolSniffer -> SOCKS5 CONNECT -> sing-box` path continues unchanged
- Loop prevention plan for the first real slice:
  - exclude service binary and `sing-box.exe` by app-id/path at filter/config level
  - skip connects whose destination is already the relay endpoint
  - skip connects already marked as redirected by WFP redirect-state query
  - keep `UseWfpTcpRedirect` feature-flagged so old behavior is still available
- First runnable milestone:
  - IPv4 TCP only
  - one test executable/app-id only
  - one relay endpoint only
  - redirect one outbound connect into `LocalRelay`
  - emit/store one metadata record
  - observe:
    - `TCP redirect provider select ... implementation=WfpTcpRedirectProvider`
    - native redirect event logged
    - `Relay accept`
    - `Relay destination-lookup ... source=redirect-store`
  - this milestone does not yet require:
    - UDP
    - IPv6
    - full policy-engine routing replacement
    - broad production hardening
- Main risks/blockers:
  - reliable correlation from the native redirect event to the accepted relay client endpoint
  - loop prevention must be correct before any broad enablement
  - packaging/signing/loading the kernel driver is the largest environmental blocker
  - coexistence with the old WinpkFilter TCP path must remain feature-flagged and explicit
- Recommended implementation order:
  1. define the shared native/managed event structure and control IOCTL contract
  2. add `WfpNativeInterop.cs` and `WfpNativeSession.cs` with no redirect logic beyond driver/session startup
  3. add the kernel callout driver skeleton and device channel
  4. implement one `ALE_CONNECT_REDIRECT_V4` classify path for IPv4 TCP and one test app-id match
  5. emit one redirect metadata event to managed code
  6. store the event as `ConnectionRedirectRecord`
  7. validate one real redirected accept into `LocalRelay`

## WFP Managed Native-Contract Skeleton
- Implemented in this phase:
  - added managed interop/session scaffolding for the future native redirect layer
  - `WfpTcpRedirectProvider` now owns a `WfpNativeSession`
  - redirect events from that session are converted into `ConnectionRedirectRecord` and stored via the existing metadata path
  - all native behavior remains stubbed/no-op unless a synthetic event is explicitly published in tests
- Exact files changed:
  - `src/TunnelFlow.Capture/TcpRedirect/WfpTcpRedirectProvider.cs`
  - `src/TunnelFlow.Service/Program.cs`
  - `src/TunnelFlow.Tests/Capture/FeatureFlagTcpRedirectProviderTests.cs`
  - `src/TunnelFlow.Tests/Capture/WfpTcpRedirectProviderIntegrationTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Exact files added:
  - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpRedirectEvent.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpNativeInterop.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpNativeSession.cs`
  - `src/TunnelFlow.Tests/Capture/WfpRedirectEventTests.cs`
  - `src/TunnelFlow.Tests/Capture/WfpTcpRedirectProviderEventIngestionTests.cs`
- Contract/event shape introduced:
  - `WfpRedirectEvent`
    - `LookupKey`
    - `OriginalDestination`
    - `RelayEndpoint`
    - `ProcessId`
    - `ProcessPath`
    - `AppId`
    - `Protocol`
    - `CorrelationId`
    - `ObservedAtUtc`
  - `WfpRedirectEvent.ToConnectionRedirectRecord(ttl)` maps the event into the existing managed metadata model
  - `WfpNativeSession`
    - start/stop lifecycle
    - background no-op event pump
    - `RedirectEventReceived` event
    - test-only synthetic event publishing path
  - `WfpNativeInterop`
    - no-op open/close/read contract skeleton for the future native device/session boundary
- Exact validation results:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.WfpRedirectEventTests" --logger "console;verbosity=minimal"`
    - passed: 1
    - failed: 0
    - skipped: 0
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.WfpTcpRedirectProviderEventIngestionTests" --logger "console;verbosity=minimal"`
    - passed: 1
    - failed: 0
    - skipped: 0
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.FeatureFlagTcpRedirectProviderTests" --logger "console;verbosity=minimal"`
    - passed: 3
    - failed: 0
    - skipped: 0
- Next concrete step toward the first real native redirect milestone:
  - replace the no-op `WfpNativeInterop.TryReadRedirectEventAsync()` path with a real driver/device control channel and carry a single real native redirect event into `WfpNativeSession`

## WFP Native Session Channel
- Implemented in this phase:
  - replaced the fully no-op managed/native boundary with the smallest real native control path possible for now:
    - a tiny native helper process
    - one stdin command shape
    - one stdout event shape
    - one managed session wrapper that reads real events from that native process
  - `WfpNativeInterop` now:
    - opens a real native helper process when the helper binary exists
    - returns a native session handle with real process/stdin/stdout streams
    - can send one synthetic redirect event command over the native channel
    - can read one real redirect event line back from the native channel
    - falls back to stub mode when the helper binary is absent, preserving existing behavior
  - `WfpNativeSession` now:
    - logs the actual session mode (`Native` or `Stub`)
    - pumps events from the real native process when present
    - publishes synthetic test events through the real native channel instead of directly invoking the event handler
- Exact files added:
  - `native/TunnelFlow.WfpRedirectChannel/TunnelFlow.WfpRedirectChannel.vcxproj`
  - `native/TunnelFlow.WfpRedirectChannel/main.cpp`
- Exact files changed:
  - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpNativeInterop.cs`
  - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpNativeSession.cs`
  - `src/TunnelFlow.Tests/Capture/WfpTcpRedirectProviderEventIngestionTests.cs`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- Exact real behavior now achieved:
  - there is now a real native process boundary behind `WfpNativeInterop` / `WfpNativeSession`
  - a managed test can send a redirect event through that native process
  - `WfpNativeSession` receives the event back from the real native channel
  - `WfpTcpRedirectProvider` ingests the event into the metadata store through the normal provider event path
- What is still not implemented:
  - no kernel driver/device yet
  - no WFP ALE classify/redirect behavior yet
  - no OS-level app `connect()` interception yet
- Exact validation results:
  - `C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe native\TunnelFlow.WfpRedirectChannel\TunnelFlow.WfpRedirectChannel.vcxproj /p:Configuration=Debug /p:Platform=x64`
    - passed
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.WfpTcpRedirectProviderEventIngestionTests" --logger "console;verbosity=minimal"`
    - passed: 1
    - failed: 0
    - skipped: 0
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.WfpRedirectEventTests" --logger "console;verbosity=minimal"`
    - passed: 1
    - failed: 0
    - skipped: 0
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.FeatureFlagTcpRedirectProviderTests" --logger "console;verbosity=minimal"`
    - passed: 3
    - failed: 0
    - skipped: 0
  - Remaining blocker before the first true runtime redirected accept:
  - replace the native helper process with the first real native driver/device control channel from the future WFP `ALE_CONNECT_REDIRECT_V4` implementation so a real outbound app `connect()` generates the event rather than a synthetic test command

## Corrected next WFP step
- Important correction after the helper-channel milestone:
  - the helper-based native channel is useful only as temporary scaffolding for managed contract testing
  - it should not be expanded further as a product path
  - the next step must move directly toward a real Windows-native WFP/ALE event produced by an actual outbound app `connect()`
- What can stay temporarily:
  - `WfpRedirectEvent`
  - `WfpNativeSession`
  - `WfpNativeInterop` as the managed boundary type, but its implementation should pivot from helper-process transport to the first real driver/device channel
  - `WfpTcpRedirectProvider` event ingestion into `IOriginalDestinationStore`
  - existing relay-side metadata lookup path in `LocalRelay`
- What must be replaced soon:
  - `native/TunnelFlow.WfpRedirectChannel/`
  - helper-process stdin/stdout transport in `WfpNativeInterop`
  - helper-backed synthetic event flow as the main path for native-session development
- Exact minimum additional native components required from here:
  - one kernel-mode WFP callout driver project dedicated to IPv4/TCP only
  - one control device on that driver for:
    - driver start/stop/config from user mode
    - relay endpoint configuration
    - one redirect-event delivery path from kernel to user mode
  - one shared native event contract carrying:
    - original destination
    - redirected relay endpoint
    - process identity
    - correlation material for relay lookup
  - one minimal WFP registration path:
    - provider/sublayer/callout/filter registration
    - `ALE_CONNECT_REDIRECT_V4` classify callback only
- Exact first real native milestone:
  - feature-flagged
  - IPv4 only
  - TCP only
  - one test executable/app-id only
  - one relay endpoint only
  - one real outbound app `connect()` reaches the WFP `ALE_CONNECT_REDIRECT_V4` classify path
  - that classify path emits one real redirect event through the driver/device channel
  - managed `WfpNativeSession` receives the event and `WfpTcpRedirectProvider` stores it
  - full runtime redirect of that connect to `LocalRelay` may remain a follow-up sub-step, but the first real milestone must at least prove a real event from a real outbound connect rather than a helper-generated event
- Exact files/projects required for that milestone:
  - new native project:
    - `native/TunnelFlow.WfpRedirectDriver/TunnelFlow.WfpRedirectDriver.vcxproj`
  - planned native files:
    - `native/TunnelFlow.WfpRedirectDriver/DriverEntry.c`
    - `native/TunnelFlow.WfpRedirectDriver/RedirectCallout.c`
    - `native/TunnelFlow.WfpRedirectDriver/DeviceControl.c`
    - `native/TunnelFlow.WfpRedirectDriver/SharedTypes.h`
    - `native/TunnelFlow.WfpRedirectDriver/TunnelFlowWfpRedirect.inf`
  - managed files to evolve, not replace:
    - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpNativeInterop.cs`
    - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpNativeSession.cs`
    - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpRedirectEvent.cs`
    - `src/TunnelFlow.Capture/TcpRedirect/WfpTcpRedirectProvider.cs`
- Recommended implementation order:
  1. freeze the helper channel as test-only bootstrap scaffolding
  2. define the single shared driver/user-mode event struct and IOCTL contract
  3. add the minimal kernel driver project with control device and loadable skeleton
  4. wire `WfpNativeInterop` to open the real device handle and read one event
  5. implement `ALE_CONNECT_REDIRECT_V4` registration and one classify callback for IPv4/TCP only
  6. in classify, match one test app and emit one real redirect event for a real outbound connect
  7. validate that `WfpNativeSession` receives that event and `WfpTcpRedirectProvider` stores it
  8. only after that, add the actual redirect action needed for the first true runtime redirected accept
- Exact reason this is the shortest path to the first true runtime redirected accept:
  - it stops spending time on simulation infrastructure that cannot prove WFP behavior
  - it preserves the managed provider/session/store work already completed
  - it introduces only the minimum native pieces needed to observe the first real app-driven WFP classify event
  - once a real classify event is flowing into the existing managed metadata path, the remaining gap to the first true redirected accept is narrowed to the redirect action itself, not the surrounding infrastructure

## WFP device-event milestone advancement
- Implemented in this step:
  - `WfpNativeInterop` now applies environment defaults for the first real device-driven slice:
    - `TUNNELFLOW_WFP_TEST_PROCESS_PATH`
    - `TUNNELFLOW_WFP_RELAY_ADDRESS`
    - `TUNNELFLOW_WFP_RELAY_PORT`
    - `TUNNELFLOW_WFP_DETAILED_LOGGING`
  - this keeps scope narrow and avoids broad config/UI changes while still allowing managed code to send a real single-process/single-relay config through the driver IOCTL path
  - the native driver scaffold now enforces that the first event-only path is for one configured test process only:
    - `TF_WFP_IOCTL_CONFIGURE` rejects empty test-process path / empty relay endpoint
    - classify no longer matches "any process" when no test-process path is configured
    - the event-only filter now uses `FWP_ACTION_CALLOUT_INSPECTION`
- Exact files changed in this step:
  - `src/TunnelFlow.Capture/TcpRedirect/Interop/WfpNativeInterop.cs`
  - `src/TunnelFlow.Tests/Capture/WfpNativeInteropTests.cs`
  - `native/TunnelFlow.WfpRedirectDriver/DeviceControl.c`
  - `native/TunnelFlow.WfpRedirectDriver/RedirectCallout.c`
  - `docs/project-memory.md`
  - `docs/fix-plan.md`
- What is real after this step:
  - managed code can now prepare a single-process/single-relay device config without any broader application refactor
  - the driver scaffold has a stricter event-only classify/config contract aligned with the "one matched outbound app connect" milestone
- What is still not validated:
  - no real loaded driver/device was exercised yet
  - no actual outbound app `connect()` has been observed producing an event from the driver
- Local validation note:
  - the local machine did not expose WDK driver build props (`WindowsKernelModeDriver10.0.props`), so native-driver build validation was skipped in this step
- Exact validation results:
  - `dotnet build src\TunnelFlow.Tests\TunnelFlow.Tests.csproj`
    - passed
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.WfpNativeInteropTests" --logger "console;verbosity=minimal"`
    - passed: 1
    - failed: 0
    - skipped: 0
  - `dotnet test src\TunnelFlow.Tests\TunnelFlow.Tests.csproj --no-build --filter "FullyQualifiedName~TunnelFlow.Tests.Capture.WfpTcpRedirectProviderEventIngestionTests" --logger "console;verbosity=minimal"`
    - passed: 1
    - failed: 0
    - skipped: 0
