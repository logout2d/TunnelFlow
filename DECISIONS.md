# Architecture Decision Records — TunnelFlow

---

## ADR-001: WinpkFilter over TDI / WFP

**Status**: Accepted

**Context**: Need packet-level interception with per-process filtering on Windows 10/11. Options: WFP (Windows Filtering Platform, built-in), TDI (deprecated), LSP (deprecated and blocked in Win10), WinpkFilter (commercial NDIS driver SDK).

**Decision**: WinpkFilter.

**Rationale**:
- WFP supports traffic inspection but not transparent TCP redirection without a TUN adapter.
- WFP `FWPM_LAYER_ALE_CONNECT_REDIRECT_V4` can redirect, but requires complex callout driver development and signing — effectively building our own driver from scratch.
- WinpkFilter provides a ready-made NDIS intermediate driver with a managed .NET wrapper, packet-level redirect, and proven per-process filtering used by commercial products.
- Time-to-working-prototype dramatically lower.

**Tradeoffs**:
- WinpkFilter is a commercial SDK. Must verify license for open-source distribution before first release. License review is RISKS.md item R-005.
- Driver is signed by Windows Hardware Quality Labs (WHQL) — no test signing required for end users.

---

## ADR-002: sing-box as transport core

**Status**: Accepted

**Context**: Need a VLESS client with SOCKS5 inbound. Options: build own VLESS implementation, use xray-core, use sing-box.

**Decision**: sing-box.

**Rationale**:
- sing-box is actively maintained, supports VLESS + all TLS variants + reality.
- SOCKS5 inbound is a first-class feature.
- Cross-platform Go binary — easy to bundle, no installer.
- Config-driven via JSON — easy to generate from C# code.
- Larger community and more recent codebase vs xray-core.

**Tradeoffs**:
- External dependency — we don't control sing-box release cycle.
- Pin a specific version in `third_party/singbox/` and update deliberately.
- Apache 2.0 license — compatible with our open-source distribution.

---

## ADR-003: C# / .NET 8 + WPF for UI and Service

**Status**: Accepted

**Context**: Need a Windows-native stack that Cursor/AI assistants know well, with good Windows Service and WinAPI interop support.

**Decision**: .NET 8 throughout (UI, Service, Capture wrapper). WPF for the UI.

**Rationale**:
- .NET 8 has excellent P/Invoke for `GetExtendedTcpTable`, `QueryFullProcessImageName`, Named Pipes, DPAPI.
- WPF is mature — Cursor has strong training data for it.
- WinUI 3 is more modern but has less training data and more packaging complexity (MSIX required).
- Single runtime target reduces complexity; .NET 8 LTS supported until 2026.

**Tradeoffs**:
- WPF uses older XAML patterns vs WinUI 3 Fluent. Acceptable for v1.
- Can migrate UI to WinUI 3 in a future major version without touching Service or Capture.

---

## ADR-004: Named Pipe for UI ↔ Service IPC

**Status**: Accepted

**Context**: UI runs as normal user; Service runs elevated. Need bidirectional communication.

**Decision**: Named Pipe with line-delimited JSON.

**Rationale**:
- Named Pipes are the standard Windows IPC for user↔service communication.
- Work across elevation boundaries with correct pipe security descriptor (allow Users to connect).
- Line-delimited JSON is simple to implement, debuggable, and extensible.
- No external dependencies (no gRPC, no HTTP server in the service).

**Tradeoffs**:
- Lower throughput than gRPC. Not a concern — message volume is low (config changes, status updates, log lines).
- Need to handle reconnect on UI restart (Service persists, UI may open/close).

---

## ADR-005: UDP port 443 block for tunneled processes

**Status**: Accepted

**Context**: Browsers use QUIC (HTTP/3) over UDP 443 aggressively. sing-box SOCKS5 inbound does not reliably support QUIC proxying (UDP ASSOCIATE path has known limitations). If UDP 443 is redirected to SOCKS5, QUIC connections fail silently or cause hangs.

**Decision**: Hard-block UDP port 443 for all tunneled processes at the PolicyEngine level.

**Rationale**:
- Browsers (Chrome, Firefox, Edge) detect UDP 443 unreachable and fall back to TCP+TLS within 0–300ms.
- The fallback is transparent to the user — the browser still works, just via HTTP/2 instead of HTTP/3.
- This is industry-standard practice in transparent proxy implementations.

**Tradeoffs**:
- HTTP/3 performance benefits lost for tunneled browser sessions.
- Must be documented in UI to avoid user confusion ("Why is HTTP/3 disabled?").

---

## ADR-006: Fail-closed on sing-box crash

**Status**: Accepted

**Context**: If sing-box crashes during an active tunneling session, packets from tunneled apps are redirected to a dead SOCKS5 port. Two options: (a) temporarily switch tunneled apps to direct (fail-open), or (b) let them fail with connection errors while sing-box restarts (fail-closed).

**Decision**: Fail-closed (option b).

**Rationale**:
- The primary use case is privacy/security — accidentally sending tunneled traffic direct is worse than a brief outage.
- sing-box restart is fast (3s delay). App connections will retry and succeed.
- Fail-open would require complex state management (temporarily update WinpkFilter rules, then revert).

**Tradeoffs**:
- Apps experience 3–10 seconds of connection errors during sing-box restart.
- Acceptable given the security guarantee.

---

## ADR-007: DPAPI for credential storage

**Status**: Accepted

**Context**: VLESS profiles contain sensitive credentials (UUID, server address, TLS config). Need secure storage.

**Decision**: Windows DPAPI (`System.Security.Cryptography.ProtectedData`) for encrypting credential fields within `config.json`.

**Rationale**:
- DPAPI is the Windows-native, zero-dependency solution for per-user or per-machine secret storage.
- Credentials are unreadable outside the user/machine context — no separate keystore needed.
- Simple API: `ProtectedData.Protect` / `Unprotect`.

**Tradeoffs**:
- Config files are not portable across machines (by design — credentials are machine-bound).
- Users need to re-enter credentials if they move to a new machine or reinstall Windows.
- Export feature must strip DPAPI fields or re-encrypt them separately.
