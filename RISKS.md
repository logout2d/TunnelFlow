# Risks — TunnelFlow

Risk register with severity, probability, and mitigation plan.

Severity: **High** / Medium / Low  
Probability: **High** / Medium / Low

---

## R-001: Circular traffic interception (self-loop)

**Severity**: High — causes infinite redirect loop, system hang  
**Probability**: High — will happen without explicit exclusions

**Description**: WinpkFilter intercepts ALL matching traffic. If sing-box.exe or TunnelFlow.Service.exe are not excluded, their outbound packets get redirected back to sing-box, creating an infinite loop.

**Mitigation**:
- Hard-coded exclusion list in `CaptureConfig.ExcludedProcessPaths`: sing-box binary path, service binary path.
- Hard-coded exclusion for VLESS server IP in `CaptureConfig.ExcludedDestinations`.
- Exclusions are applied before any user-defined rules — cannot be overridden by UI.
- Integration test: start capture, verify sing-box traffic reaches remote server and does not loop.

---

## R-002: DNS leaks for tunneled applications

**Severity**: High (for privacy use case) / Low (for performance use case)  
**Probability**: High in MVP — DNS is intentionally not intercepted

**Description**: DNS queries from tunneled apps use the Windows system resolver, which bypasses the tunnel. An observer can see DNS queries even if TCP/UDP flows are hidden.

**Mitigation (MVP)**:
- Document prominently in UI: "⚠️ DNS queries are not tunneled. Your DNS provider can see which domains tunneled apps resolve."
- Add a "DNS leak test" link in the UI pointing to an external test tool.

**Mitigation (Production — Phase 3)**:
- Intercept UDP/TCP port 53 from tunneled processes in PolicyEngine.
- Route to local sing-box DNS inbound on `127.0.0.1:5353`.
- See DATAFLOW.md DNS section for implementation plan.

---

## R-003: Antivirus / EDR interference with WinpkFilter

**Severity**: High — may prevent the app from working at all  
**Probability**: Medium — consumer AV usually allows signed NDIS drivers; corporate EDR is stricter

**Description**: WinpkFilter installs an NDIS intermediate driver. Security software may block driver load, flag the process as suspicious, or quarantine binaries.

**Mitigation**:
- WinpkFilter driver is WHQL-signed — reduces AV false positives significantly.
- Document known compatibility issues (Windows Defender = OK, common EDR list TBD).
- Provide a diagnostic command: `TunnelFlow.exe diag driver` to check driver load status.
- Test against: Windows Defender, Malwarebytes, Kaspersky in CI smoke tests.

---

## R-004: Race condition — process exits between packet arrival and PID lookup

**Severity**: Medium — wrong policy may be applied to a brief window of packets  
**Probability**: Low — only occurs at process startup/shutdown boundaries

**Description**: A packet arrives, we call `GetExtendedTcpTable`, the process has exited, and a new unrelated process reused the same port. We may apply the wrong policy.

**Mitigation**:
- Cross-check process start time from `SYSTEM_PROCESS_INFORMATION` vs connection creation time.
- On ambiguity: apply `Direct` (never proxy unknown traffic).
- Log the ambiguous case with all available info for post-hoc diagnosis.
- Acceptable residual risk — the window is microseconds and the fallback is safe (Direct, not Block).

---

## R-005: WinpkFilter license for open-source distribution

**Severity**: High — could block entire project if unresolvable  
**Probability**: Medium — WinpkFilter has commercial SDK license; open-source redistribution terms unclear

**Description**: WinpkFilter is a commercial product. Bundling its driver and managed wrapper in an open-source repository may violate the license terms.

**Mitigation**:
- **Must be resolved before first public commit containing WinpkFilter binaries.**
- Contact NT Kernel Resources (WinpkFilter vendor) about open-source redistribution.
- Alternative if blocked: evaluate **npcap** (open-source, but different interception model requiring more custom code) or WFP callout (complex, requires driver signing, but 100% Microsoft stack).
- Document license decision in DECISIONS.md as ADR-008 once resolved.

---

## R-006: UDP association table memory growth

**Severity**: Medium — service memory leak under high UDP load  
**Probability**: Low — only with very high-volume UDP applications (video streaming, gaming)

**Description**: Every unique UDP 4-tuple creates a Session Registry entry. Under high load (e.g., gaming with many UDP flows), the table could grow large before the 30s timeout cleans entries.

**Mitigation**:
- Hard cap: `ISessionRegistry` max 10,000 entries. On cap hit: evict LRU entries.
- Purge job runs every 10 seconds (not just on new entries).
- Expose current session count in UI diagnostics.
- Monitor in testing with a video streaming + gaming simulation.

---

## R-007: sing-box version incompatibility after update

**Severity**: Medium — generated config may break  
**Probability**: Medium — sing-box config schema has changed between major versions

**Description**: sing-box is bundled at a pinned version. If a user manually replaces the binary, or if we update the bundled version, the config generation template may produce invalid JSON.

**Mitigation**:
- Pin sing-box version in `third_party/singbox/VERSION` file.
- On startup, validate sing-box version output against expected version.
- If mismatch: log warning, attempt to start anyway, surface error in UI if sing-box fails.
- Keep a `SingBoxConfigBuilder` class that targets a specific sing-box API version.

---

## R-008: WPF app not receiving Named Pipe push events

**Severity**: Low — UI becomes stale, but tunneling still works  
**Probability**: Low — pipe reconnect handling is standard

**Description**: If the Named Pipe connection is dropped (service restart, pipe error), the UI stops receiving push events (status, log lines, session updates).

**Mitigation**:
- UI implements automatic reconnect with exponential backoff (1s, 2s, 4s, max 30s).
- UI shows "Reconnecting..." banner while pipe is disconnected.
- On reconnect, immediately send `GetState` to refresh full state.

---

## Open questions (must resolve before Phase 2)

| # | Question | Impact |
|---|----------|--------|
| OQ-1 | WinpkFilter license for open-source — see R-005 | Blocks entire project |
| OQ-2 | Does WinpkFilter managed wrapper support .NET 8 out of the box, or needs recompile? | Affects Capture project setup |
| OQ-3 | sing-box UDP ASSOCIATE over SOCKS5 — does it actually work for DNS/non-QUIC UDP? | Affects Phase 2 UDP scope |
| OQ-4 | Windows Service installer strategy — NSIS, WiX, or self-installing exe? | Affects Phase 3 distribution |
| OQ-5 | Code signing certificate for installer — required for SmartScreen pass | Affects Phase 4 |
