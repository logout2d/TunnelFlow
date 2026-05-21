# TunnelFlow v1.0.1

TunnelFlow is a Windows desktop client for VLESS profiles and per-app tunneling through a virtual adapter.

Version 1.0.1 is a maintenance and polish release focused on compatibility, App Rules flexibility, and desktop UX improvements.

## Highlights

- Improved shared-config compatibility between the desktop UI and Windows service on both Windows 10 and Windows 11
- Added App Rules matching by executable name (`Exe`) alongside existing full-path rules
- Added `Add Exe` flow for stable process-name-based rules
- Added a simple local `build-and-run` PowerShell script for manual development/testing
- Added tray icon behavior with restore/open and real exit actions
- Improved `Add Exe` dialog usability by focusing the input field automatically when the dialog opens

## Packages

### TunnelFlow-win-x64-v1.0.1.zip

Standard portable package without bundled `sing-box.exe`.

Choose this package if you:
- already manage your own compatible `sing-box.exe`
- prefer a smaller base package
- want to keep the proxy core separate from the app package

### TunnelFlow-win-x64-with-core-v1.0.1.zip

Convenience portable package with bundled `sing-box.exe`.

Choose this package if you:
- want the easiest first-time setup
- prefer a more complete out-of-the-box package

## Changes in v1.0.1

### Compatibility and reliability

- Completed the shared protected-config migration path so the UI and service can safely work with the same config file
- Improved compatibility of protected config handling across Windows 10 and Windows 11
- Reduced service startup failures caused by legacy protected-value formats

### App Rules

- Added explicit App Rule match types:
  - `Path`
  - `Exe`
- Added executable-name matching for applications whose install path changes while the process name stays stable
- Added `Add Exe` for creating process-name-based rules directly from the UI
- Preserved backward compatibility for older rules that do not have an explicit match type
- Kept full-path rules more specific than exe-name rules when both could apply

### Desktop UX

- Added tray icon support
- Minimize now behaves normally and stays on the taskbar
- Closing the main window now hides the app to tray instead of exiting immediately
- Added tray actions for restoring the window and exiting the app
- Improved `Add Exe` dialog usability with automatic input focus

### Developer convenience

- Added `scripts/build-and-run.ps1` for local build-and-run flow during manual testing and development

## Notes

- TunnelFlow remains **TUN-only**
- The Windows service architecture is still retained
- Administrative privileges may be required for service/bootstrapper and tunnel-related actions
- Some packages include separate third-party components distributed under their own licenses

See:
- `LICENSE`
- `THIRD_PARTY_NOTICES.md`

If a package includes `sing-box`, it is provided as a separate third-party component and remains under its own license