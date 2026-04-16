# TunnelFlow Release Optimization Decisions

Date recorded: 2026-04-16
Status: accepted for the current portable-release direction

## Context

TunnelFlow keeps its current Windows service-based architecture and active
TUN-only runtime path:

UI -> Service -> Wintun -> sing-box `tun` inbound -> VLESS outbound

The release direction remains a portable ZIP, but the decision surface now needs
to define not only archive shape, but also where runtime state lives and how the
service architecture behaves in that portable direction.

## Main Goal

Move the release to a cleaner, controlled portable ZIP layout that:

- keeps the current service model in place
- keeps the active TUN-only runtime path intact
- keeps runtime state app-local for the portable direction
- avoids installer work in the current stage

## Decisions

### 1. Target release format remains portable ZIP

TunnelFlow keeps the portable ZIP direction for the current release stage.

This means:

- the user receives a ZIP archive
- the product runs from the extracted folder
- installer-based setup remains postponed
- "portable" currently describes package shape and runtime-state placement, not
  removal of the service architecture

### 2. Managed executables stay on single-file publish

The following managed components remain targeted for single-file publish:

- `TunnelFlow.UI`
- `TunnelFlow.Service`
- `TunnelFlow.Bootstrapper`

Base publish assumptions remain:

- `PublishSingleFile=true`
- `SelfContained=false`
- `RuntimeIdentifier=win-x64`

### 3. Final release assembly remains a separate whitelist-based packaging step

The release must not be a raw publish-output archive.

The final package is assembled through a dedicated packaging step with an
explicit whitelist, so only intentionally included files enter the portable ZIP.

### 4. The portable layout is standardized

The target package layout remains:

```text
TunnelFlow-win-x64-with-core-vX.Y.Z/
  TunnelFlow.exe
  QUICK_START.txt

  config/
    appsettings.json

  logs/
    ui.log
    service.log
    singbox.log

  system/
    TunnelFlow.Service.exe
    TunnelFlow.Bootstrapper.exe

  core/
    sing-box.exe
    wintun.dll

  licenses/
    LICENSE
    THIRD_PARTY_NOTICES.md
    SING_BOX_SOURCE.txt
```

Notes:

- `TunnelFlow.UI.exe` may still be presented as `TunnelFlow.exe` in the final
  package
- service-side executables stay under `system/`
- external runtime dependencies stay under `core/`
- user-editable configuration stays under `config/`
- runtime logs stay under `logs/`

### 5. Portable runtime state is app-local

For the portable release direction, runtime state should live alongside the app
instead of being scattered into system-wide folders.

This means the portable direction standardizes on:

- configuration under `config/`
- logs under `logs/`
- `ui.log` under `logs/ui.log`
- `service.log` under `logs/service.log`
- `singbox.log` under `logs/singbox.log`

This is a product direction decision, not just a UI-only convenience. Future
runtime state usage should follow the same app-local rule unless a clearly
justified exception is documented later.

### 6. Portable does not remove the Windows service model

The current Windows service architecture remains in place.

For the current stage, "portable" means:

- portable package layout
- app-local runtime state
- predictable path resolution from the extracted folder

It does not mean:

- removing the Windows service
- collapsing the product into a service-free design
- introducing installer work now

Installer-based setup is explicitly still postponed.

### 7. Runtime path resolution must be centralized product-wide

Runtime path usage should be centralized around `AppContext.BaseDirectory` and a
single shared path-resolution approach.

That centralized resolution should cover at least:

- `config/appsettings.json`
- `logs/ui.log`
- `logs/service.log`
- `logs/singbox.log`
- `system/TunnelFlow.Service.exe`
- `system/TunnelFlow.Bootstrapper.exe`
- `core/sing-box.exe`
- `core/wintun.dll`

This decision applies across UI, Service, and Bootstrapper.

### 8. The next code migration target is portable runtime path cleanup

After packaging cleanup, the next implementation phase should focus on runtime
path migration rather than broader packaging experiments.

The next code migration target is:

- central path resolver usage across UI, Service, and Bootstrapper
- migration away from scattered ProgramData-style runtime state toward
  app-local portable paths
- preserving compatibility with the current TUN-only architecture
- preserving the existing service-based runtime model

## Practical Implementation Order

The narrow implementation order remains:

1. Keep single-file publish targets for UI, Service, and Bootstrapper.
2. Keep the whitelist-based packaging step and standardized portable layout.
3. Introduce centralized path resolver usage across UI, Service, and
   Bootstrapper.
4. Migrate runtime state from scattered system-wide locations toward app-local
   portable paths:
   - `config/`
   - `logs/`
5. Smoke-test the portable package with the existing service-based TUN-only
   runtime flow.
6. Revisit optional size/startup optimizations only after the portable layout
   and runtime-state migration are stable.

## Readiness Criteria

This direction is considered implemented when:

1. The release is assembled through a whitelist-based packaging step.
2. The final package follows the standardized portable layout.
3. Configuration is app-local under `config/`.
4. UI and service-side logs are app-local under `logs/`.
5. Runtime path resolution is centralized around the extracted app base.
6. The current service-based TUN-only architecture still works from the
   portable package.
7. Installer work is still deferred rather than partially reintroduced.

## Deferred Decisions

Still deferred for a later step:

- installer-based packaging
- trimming
- ReadyToRun
- further packaging optimization experiments beyond what is needed for the
  portable layout and runtime-state migration

## Summary

The portable-release direction is now defined as two linked decisions:

- a clean portable ZIP layout
- app-local runtime state rooted beside the app

This is intentionally compatible with the current Windows service-based,
TUN-only architecture and does not start installer work or service-model
removal in this step.
