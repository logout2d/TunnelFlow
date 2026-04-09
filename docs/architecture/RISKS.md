# Risks

Current TUN-only risk register for TunnelFlow.

## R-001: TUN prerequisite mismatch

**Severity**: High  
**Probability**: Medium

If Wintun runtime files are missing or unusable, the service cannot start the
active product path.

**Mitigation**:
- explicit TUN prerequisite selection and logging
- fail-closed start gating
- clear service/UI status messaging

## R-002: sing-box config/version drift

**Severity**: High  
**Probability**: Medium

The active runtime depends on generated sing-box config matching the bundled
binary's expected schema.

**Mitigation**:
- keep the bundled sing-box version pinned
- validate config generation with focused builder tests
- keep generated config snapshots and logs for diagnosis

## R-003: Rule or DNS mismatch causes apparent connectivity failure

**Severity**: Medium  
**Probability**: Medium

A bad route or DNS rule can make selected apps appear broken even while the
local tunnel started correctly.

**Mitigation**:
- keep TUN policy mapping explicit and testable
- preserve service.log and singbox.log for diagnosis
- keep UI warning evidence conservative and honest

## R-004: Orphaned tunnel after UI loss

**Severity**: Medium  
**Probability**: Low

If the UI exits unexpectedly while the tunnel is active, the tunnel could stay
running without an owner unless the owner lease model works correctly.

**Mitigation**:
- heartbeat-based owner lease
- conservative lease timeout
- controlled auto-stop on lease expiry

## R-005: Startup/shutdown lifecycle races

**Severity**: Medium  
**Probability**: Medium

Quick start/stop/start sequences can corrupt the runtime if lifecycle state is
implicit.

**Mitigation**:
- explicit lifecycle state machine
- fail-closed start/stop gating
- stop sing-box before stopping TUN

## Historical note

Risks specific to WinpkFilter, `ndisapi.net`, and localhost SOCKS startup are
part of the retired architecture and are no longer active release risks.
