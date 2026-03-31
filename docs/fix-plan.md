# Fix plan

## Current stage
Environment prepared for Codex-guided debugging and patching.

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
Phase 3 proof of concept: add Windows-native connection-level TCP redirect compatible with LocalRelay and prove accepted connections plus original-destination lookup.
Status: in progress
Current narrow milestone:
- add the first real native `ALE_CONNECT_REDIRECT_V4` slice for IPv4 TCP only
- target one test app-id / executable only
- emit one redirect metadata event into `WfpTcpRedirectProvider`
- prove one real redirected accept reaches `LocalRelay` with `source=redirect-store`

## Step 12
Phase 4 integration: route TCP proxy decisions through the new redirect provider while preserving PolicyEngine, CaptureEngine orchestration, LocalRelay, ProtocolSniffer, and SOCKS flow.
Status: pending

## Step 13
Phase 5 cleanup: remove obsolete TCP packet rewrite/NAT bridging logic from WinpkFilterPacketDriver once connection-level redirect is stable.
Status: pending

## Step 14
Investigate/patch IPv6 capture path if still required after earlier fixes.
Status: pending
