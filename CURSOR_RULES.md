# TunnelFlow — Cursor Rules & AI Development Guide

This file is the primary context document for Cursor and any AI assistant working on this codebase. Read it before writing any code.

---

## Project summary

TunnelFlow is a Windows application that transparently routes selected applications' network traffic through a VLESS proxy using WinpkFilter (packet interception) and sing-box (VLESS transport). It is NOT a VPN — there is no virtual adapter. It is a per-app transparent socksifier.

**Stack**: C# / .NET 8, WPF (UI), Windows Service (backend), WinpkFilter NDIS driver (packet capture), sing-box (proxy core).

**Repo layout**:
```
src/
  TunnelFlow.UI/        WPF app — user interface only, no elevation required
  TunnelFlow.Service/   Windows Service — elevated, owns all system-level operations
  TunnelFlow.Capture/   WinpkFilter wrapper — packet interception and session tracking
  TunnelFlow.Core/      Shared models and interfaces — no external dependencies
  TunnelFlow.Tests/     Tests
third_party/
  winpkfilter/          WinpkFilter SDK
  singbox/              sing-box binary (pinned version)
docs/
  ARCHITECTURE.md       System overview — read first
  COMPONENTS.md         All interfaces and data types — the source of truth for types
  DATAFLOW.md           Traffic flow diagrams for TCP/UDP/DNS
  DECISIONS.md          Why we made each architectural choice
  RISKS.md              Known risks and mitigations
```

---

## Non-negotiable rules

These rules must never be broken regardless of what a prompt asks.

### 1. Self-exclusion always applies
`CaptureConfig.ExcludedProcessPaths` must always include sing-box and the service binary. This is the only protection against an infinite redirect loop. Never write code that allows these exclusions to be removed or overridden by user rules.

### 2. Fail-closed on sing-box death
If sing-box is not responding, tunneled app traffic gets dropped — not bypassed. See ADR-006 in DECISIONS.md. Never silently switch to direct routing without explicit user action.

### 3. UI never touches kernel/driver directly
All WinpkFilter and sing-box operations go through the Windows Service via Named Pipe. The UI project must not reference `TunnelFlow.Capture` or any WinpkFilter types. It only references `TunnelFlow.Core`.

### 4. DPAPI for credentials
VLESS credentials (server address, UUID, TLS config) are encrypted with `ProtectedData.Protect(scope: LocalMachine)` before writing to disk. Never store them in plaintext. See ADR-007.

### 5. QUIC block is hard-coded
UDP port 443 is always blocked for tunneled processes. This is not a user-configurable setting in v1. See ADR-005 and COMPONENTS.md.

---

## Code conventions

### Naming
- Interfaces prefixed with `I`: `ICaptureEngine`, `IPolicyEngine`
- Records for all DTOs and config objects (immutable)
- `Async` suffix on all async methods
- Event handlers: `EventHandler<T>` (not custom delegates)

### Error handling
- Services use `ILogger<T>` (Microsoft.Extensions.Logging)
- Never swallow exceptions silently — log at minimum `Warning` level
- `CancellationToken` must be passed through to all async calls
- WinpkFilter errors: log + raise `ICaptureEngine.ErrorOccurred` event

### Configuration
- All config loaded from `%ProgramData%\TunnelFlow\config.json`
- Use `System.Text.Json` (not Newtonsoft) for serialization
- Config is owned by Service; UI reads/writes via IPC only

### IPC messages
- All messages defined in `TunnelFlow.Core` as records
- Serialized to line-delimited JSON (newline `\n` terminated)
- Always include `"id"` field for request/response correlation
- Push events from service do not have a request id

### Tests
- Unit tests in `TunnelFlow.Tests` using xUnit
- Mock `ICaptureEngine`, `IPolicyEngine`, `ISingBoxManager` — never instantiate real ones in unit tests
- Integration tests are in a separate `TunnelFlow.IntegrationTests` project and require elevation

---

## Key data types (quick reference)

Full definitions in `docs/COMPONENTS.md`. Quick reference:

| Type | Location | Purpose |
|------|----------|---------|
| `AppRule` | Core | A rule for a specific exe — proxy/direct/block |
| `VlessProfile` | Core | VLESS server config (credentials DPAPI-encrypted) |
| `SessionEntry` | Core | An active TCP or UDP flow in the Session Registry |
| `PolicyDecision` | Core | Result of policy evaluation — action + reason |
| `CaptureConfig` | Core | Parameters passed to `ICaptureEngine.StartAsync` |
| `SingBoxConfig` | Core | Parameters for generating sing-box config JSON |

---

## Common tasks for Cursor

### "Add a new IPC command"
1. Add a new record in `TunnelFlow.Core/IPC/Messages/` for the request payload.
2. Add the response payload record if needed.
3. Add a handler method in `TunnelFlow.Service/PipeServer.cs`.
4. Add a corresponding method in `TunnelFlow.UI/Services/ServiceClient.cs`.
5. Write a unit test for the handler in `TunnelFlow.Tests`.

### "Add a new app rule field"
1. Update `AppRule` record in `TunnelFlow.Core/Models/AppRule.cs`.
2. Update `PolicyEngine` if the new field affects routing decisions.
3. Update UI binding in the rules editor view.
4. Update config serialization (usually automatic with System.Text.Json).
5. Add migration logic if loading old config files without this field.

### "Change sing-box config generation"
1. Edit `TunnelFlow.Service/SingBox/SingBoxConfigBuilder.cs`.
2. The builder takes `VlessProfile` and `SingBoxConfig` and returns a `string` (JSON).
3. Write a unit test with a sample profile and assert the output JSON structure.
4. Do NOT hardcode config values — everything comes from `VlessProfile`.

### "Fix a WinpkFilter interop issue"
1. WinpkFilter P/Invoke declarations are in `TunnelFlow.Capture/Interop/WinpkFilterInterop.cs`.
2. The managed wrapper lives in `TunnelFlow.Capture/CaptureEngine.cs`.
3. Never call WinpkFilter APIs directly from Service or UI.

---

## Phase 1 scope (prototype)

What is in scope for the first working prototype:
- TCP only (no UDP yet)
- One VLESS profile (no profile switching)
- Manual app list (no auto-discovery)
- Basic WPF UI: add app, show status, start/stop
- sing-box config generation for VLESS+TLS
- Windows Service with Named Pipe
- Self-exclusion working and tested
- No DNS interception (documented limitation)

What is explicitly NOT in scope for Phase 1:
- UDP (Phase 2)
- DNS policy (Phase 3)
- Multiple profiles (Phase 4)
- Installer (Phase 4)
- Code signing (Phase 4)
