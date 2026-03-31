# TunnelFlow — Phase Plan for WFP TCP Redirect PoC

## Status at handoff

Phase 0 has now been implemented as repository scaffolding only:
- documentation references are in place
- TCP redirect abstractions/models are present
- feature flag `UseWfpTcpRedirect` is persisted in config
- DI wires a no-op redirect provider and in-memory destination store
- no real WFP redirect behavior exists yet

The existing packet-level TCP redirection path has been investigated and should be considered diagnostically complete.

Confirmed findings from the previous phase:
- Rule matching works for the target application.
- `CaptureEngine` reaches `Proxy` decisions for matched TCP flows.
- NAT state is created.
- Packet rewrite and reinjection were observed successfully.
- `LocalRelay` starts and passes self-check.
- Even after changing reinjection from adapter to MSTCP, `LocalRelay` still receives `accepted=0`.

Conclusion:
The current model — rewriting raw outbound TCP packets into a local `TcpListener` endpoint — is not viable as the basis for production TCP proxy redirection.

## New phase goal

Replace packet-level TCP redirection with **Windows Filtering Platform (WFP) ALE connect redirection** for TCP, while preserving as much of the current application architecture as possible.

Target outcome:
- Any ordinary Windows application that uses standard outbound TCP connections can be proxied per-app.
- `LocalRelay` receives a **real accepted TCP connection**.
- Original destination metadata is preserved and available to `LocalRelay`.
- Existing relay pipeline remains intact:
  - `LocalRelay`
  - `ProtocolSniffer`
  - `Socks5Connector`
  - `sing-box`

## Preserve vs replace

### Keep
- `PolicyEngine`
- `CaptureEngine` orchestration flow
- `LocalRelay`
- `ProtocolSniffer`
- `Socks5Connector`
- `SingBoxManager` / config builder / profile handling
- UI / IPC / config persistence
- Existing high-level diagnostics and logging style

### Replace or phase out
- TCP-specific packet rewrite path in `WinpkFilterPacketDriver`
- TCP packet-level NAT-as-transport bridge assumptions
- TCP redirect delivery logic based on rewritten packets reaching `TcpListener`

### Keep temporarily for coexistence / observation
- `WinpkFilterPacketDriver` for non-TCP observation and diagnostics
- Existing logging and flow diagnostics where still useful

## Architecture direction

### Old model
`app connect() -> raw packet capture -> packet rewrite -> reinject -> LocalRelay listener`

### New model
`app connect() -> WFP ALE connect redirect -> real TCP connect to LocalRelay -> LocalRelay accept -> original destination lookup -> SOCKS5 connect -> sing-box`

This is a **connection-level redirect**, not a packet-level redirect.

## Core design requirements

1. **TCP-first PoC only**
   - No UDP redirect in the first implementation.
   - No IPv6 redirect in the first implementation.

2. **Feature-flagged migration**
   - Old path remains available until the new path is validated.
   - New WFP TCP redirect path can be enabled in a controlled manner.

3. **Preserve `LocalRelay` contract**
   - `LocalRelay` should continue to accept a normal TCP stream.
   - It should learn the original destination from metadata, not packet NAT.

4. **Per-app policy remains authoritative**
   - Redirection must still depend on existing policy/rule decisions.

5. **Observability remains strong**
   - We must be able to see whether a TCP connect was redirected.
   - We must be able to see whether `LocalRelay` accepted the redirected connection.
   - We must be able to diagnose metadata lookup success or failure.

## File plan

### New files to add

#### `src/TunnelFlow.Capture/TcpRedirect/`
- `ITcpRedirectProvider.cs`
- `IOriginalDestinationStore.cs`
- `ConnectionRedirectRecord.cs`
- `ConnectionLookupKey.cs`
- `ConnectionRedirectStore.cs`
- `TcpRedirectStats.cs`
- `TcpRedirectConfig.cs`
- `NoOpTcpRedirectProvider.cs`
- `WfpTcpRedirectProvider.cs` *(stub first, real implementation later)*

#### `src/TunnelFlow.Capture/TcpRedirect/Interop/`
- `WfpNativeInterop.cs` *(stub or placeholder in early phases)*

#### Tests
`src/TunnelFlow.Tests/Capture/TcpRedirect/`
- `ConnectionRedirectStoreTests.cs`
- `CaptureEngineTcpRedirectRoutingTests.cs`

#### Documentation
- `docs/wfp-tcp-redirect-poc.md`
- optional later: `docs/wfp-native-integration-notes.md`

### Existing files expected to change
- `src/TunnelFlow.Capture/CaptureEngine.cs`
- `src/TunnelFlow.Capture/TransparentProxy/LocalRelay.cs`
- `src/TunnelFlow.Service/Program.cs`
- `docs/project-memory.md`
- `docs/fix-plan.md`

### Existing files intentionally not touched in early phases
- `PolicyEngine.cs`
- `ProtocolSniffer.cs`
- `Socks5Connector.cs`
- `SingBoxManager.cs`
- UI/ViewModels/Profile model files

## New interfaces and responsibilities

### `ITcpRedirectProvider`
Responsibility:
- lifecycle of TCP redirect mechanism
- redirect statistics
- original destination lookup for accepted relay connections

Suggested surface:
- `Task StartAsync(TcpRedirectConfig config, CancellationToken ct = default)`
- `Task StopAsync(CancellationToken ct = default)`
- `bool TryGetOriginalDestination(TcpClient client, out IPEndPoint destination)`
- `TcpRedirectStats GetStats()`

### `IOriginalDestinationStore`
Responsibility:
- map redirected connection metadata to later relay lookup

Suggested surface:
- `void Add(ConnectionRedirectRecord record)`
- `bool TryGet(ConnectionLookupKey key, out ConnectionRedirectRecord record)`
- `void Remove(ConnectionLookupKey key)`
- `void PurgeExpired()`

## Metadata model

### `ConnectionRedirectRecord`
Suggested fields:
- `OriginalDestination`
- `RelayEndpoint`
- `ProcessId`
- `ProcessPath`
- `Protocol`
- `CreatedAtUtc`
- `CorrelationId`

### `ConnectionLookupKey`
Initial PoC lookup should be simple and explicit.
Candidate key material:
- accepted socket remote endpoint
- local endpoint
- process id if available
- correlation id where feasible

Exact key choice should be driven by what the first WFP PoC can reliably correlate.

## Phase-by-phase implementation plan

## Phase 0 — Foundation and documentation

### Goal
Lay down the migration skeleton without changing runtime behavior.

### Deliverables
- Add design document to repository.
- Add new interfaces and model skeletons.
- Add feature flag: `UseWfpTcpRedirect`.
- Add no-op provider wired into DI.
- Update project memory and fix plan.

### Acceptance criteria
- Solution builds cleanly.
- Runtime behavior is unchanged.
- No old code is removed.
- New abstractions are present and compile.

### Exit artifact
A buildable repo with the migration scaffolding in place.

## Phase 1 — Original-destination metadata path

### Goal
Prepare the relay side to consume original-destination metadata from a connection-level store instead of packet NAT.

### Deliverables
- Implement in-memory `ConnectionRedirectStore`.
- Add unit tests for add/get/remove/expiry behavior.
- Add a new lookup path in `LocalRelay` that can use `ITcpRedirectProvider` / `IOriginalDestinationStore`.
- Keep the old NAT lookup path in place as fallback while WFP path is not active.

### Acceptance criteria
- Tests for store pass.
- `LocalRelay` compiles and can query the new metadata source.
- Existing runtime still behaves as before if `UseWfpTcpRedirect=false`.

### Exit artifact
Relay is ready to consume connection-level redirect metadata.

## Phase 2 — WFP provider skeleton

### Goal
Introduce the WFP provider lifecycle and wiring, still with minimal or stubbed native behavior.

### Deliverables
- `WfpTcpRedirectProvider` class with start/stop/logging skeleton.
- `WfpNativeInterop` placeholder or initial binding layer.
- DI/config wiring to instantiate provider when feature flag is enabled.
- Clear diagnostics around provider startup.

### Acceptance criteria
- Build stays green.
- Service starts with `UseWfpTcpRedirect=true` without breaking unrelated code.
- Logs clearly indicate which redirect path is active.

### Exit artifact
A controlled runtime path for the new provider exists.

## Phase 3 — Minimal vertical-slice PoC

### Goal
Prove one real redirected TCP connection reaches `LocalRelay` as a genuine accepted socket.

### Deliverables
- Implement enough WFP ALE connect redirection to redirect matched outbound TCP connects to the relay.
- Create redirect metadata records with original destination information.
- Make `LocalRelay` recover original destination using the new provider/store.
- Preserve existing relay pipeline from accepted socket onward.

### Acceptance criteria
For one application rule and one target site:
- `LocalRelay accepted > 0`
- original destination lookup succeeds
- SOCKS connect attempt is made
- logs prove the new redirect path was used

### Exit artifact
A real end-to-end TCP redirect proof of concept.

## Phase 4 — Integration into policy flow

### Goal
Move actual TCP proxy decisions to the new redirect provider while keeping architecture stable.

### Deliverables
- `CaptureEngine` routes proxied TCP decisions through `ITcpRedirectProvider`.
- Old TCP packet rewrite path in `WinpkFilterPacketDriver` is disabled behind feature flag or bypassed for TCP.
- Logging and stats updated to reflect active mechanism.

### Acceptance criteria
- Multiple manual tests with the target browser/app behave consistently.
- `LocalRelay` receives accepted connections through the WFP path.
- No regression in non-TCP observation paths.

### Exit artifact
The new TCP redirect path is functionally integrated.

## Phase 5 — Cleanup and deprecation

### Goal
Remove or sharply reduce obsolete TCP packet rewrite logic once the new path is validated.

### Deliverables
- Remove dead TCP redirect code from `WinpkFilterPacketDriver`.
- Remove packet-level NAT logic used only to feed relay transport semantics.
- Simplify docs and runtime config.
- Update tests and memory docs to reflect final architecture.

### Acceptance criteria
- Build passes.
- Manual validation passes on the chosen target app(s).
- Old TCP packet redirect path is no longer required for normal operation.

### Exit artifact
Cleaner architecture with TCP redirect implemented on the correct layer.

## Testing plan

### Unit tests
- `ConnectionRedirectStoreTests`
  - add/get/remove
  - expiry
  - overwrite/collision behavior

- `CaptureEngineTcpRedirectRoutingTests`
  - proxied TCP routes to redirect provider
  - direct TCP does not route to redirect provider
  - UDP does not route to redirect provider

### Runtime validation checkpoints

#### First PoC success criteria
For one site open attempt:
- WFP redirect provider starts
- redirect metadata is recorded
- `LocalRelay accept` appears in log
- original destination lookup hit appears
- SOCKS connect attempt appears

#### Extended validation after Phase 4
- test Floorp
- test one non-browser TCP app
- test repeated connects
- ensure rules still control which app is proxied

## Logging requirements for the new phase

Must-have log lines:
- provider start/stop
- feature flag active path
- redirect metadata record add/remove
- relay accept
- original destination lookup hit/miss
- SOCKS connect success/failure

Avoid:
- per-packet spam
- duplicating old packet-rewrite logs once TCP redirect no longer uses that path

## Risks

1. **Native Windows integration complexity**
   WFP ALE redirect is the correct layer, but implementation is more complex than the current managed-only packet rewrite approach.

2. **Connection correlation reliability**
   The most important PoC risk is reliably mapping redirected connection metadata back to the accepted relay socket.

3. **Coexistence risk**
   During migration, old TCP packet redirect and new WFP redirect must not both act on the same flow.

4. **Scope creep**
   UDP, IPv6, app-container specifics, and advanced edge cases should stay out of the first PoC.

## Rollback strategy

- Keep the old implementation behind the current code path until the new provider proves `accepted > 0` and relay lookup success.
- Guard new behavior behind `UseWfpTcpRedirect`.
- If PoC fails, disable the feature flag and continue from a known-good diagnostic baseline.

## Recommended next step

Implement **Phase 0 only**:
- documentation
- skeleton interfaces/models
- feature flag
- DI wiring with no-op provider

Do **not** attempt real WFP redirect code in the same step.
