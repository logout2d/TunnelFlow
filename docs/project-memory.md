# TunnelFlow project memory

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
