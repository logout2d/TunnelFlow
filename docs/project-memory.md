# TunnelFlow project memory

## WFP Redirect Docs
- Active migration design reference:
  - `docs/wfp-tcp-redirect-poc-plan.md`
- Current repository status:
  - Phase 0 skeleton/scaffolding is now in place
  - no real WFP redirect behavior has been implemented yet

## Goal
Bring TunnelFlow to a stable working state for selective per-application transparent proxying on Windows.

## Current user-reported symptom
- Proxy chain starts and reaches the stage of application proxying.
- In the target browser ("froorp"), Google opens, but most other sites do not.

## Current architectural understanding
Intended traffic path:
packet capture -> local relay -> SOCKS5 CONNECT -> sing-box -> remote VLESS server

Important components:
- TunnelFlow.Capture:
  packet interception, process resolution, rule matching, session tracking, transparent relay helpers
- TunnelFlow.Service:
  orchestration, config persistence, IPC server, sing-box lifecycle
- TunnelFlow.UI:
  WPF client for status/config/rules/sessions/logs
- TunnelFlow.Core:
  shared models/interfaces/contracts

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
