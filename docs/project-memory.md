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