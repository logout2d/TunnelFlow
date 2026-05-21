# TunnelFlow v1.0.1

TunnelFlow is a Windows desktop client for VLESS profiles and per-application
tunneling through a virtual adapter. This release keeps the current TUN-only,
Windows-service-based architecture and focuses on shared-config reliability,
App Rules usability, and desktop-shell polish.

## Highlights

- Shared-config secret encryption now uses a versioned machine-scoped format for
  newly written secrets, with migration support for readable legacy values so
  the UI and Windows service can read the same config on Windows 10 and
  Windows 11.
- App Rules now support executable-name matching for apps launched from changing
  or versioned install folders.
- Added an `Add Exe` App Rules flow for entering names such as `Discord.exe` or
  `firefox.exe`.
- Added a simple manual local build-and-run PowerShell script.
- Added system tray behavior:
  - minimize stays on the taskbar
  - close button hides TunnelFlow to the tray
  - tray menu supports `Open` and `Exit`
- The `Add Exe` dialog now focuses the text box automatically when opened.

## Download Options

- `TunnelFlow-win-x64-v1.0.1.zip`
  - Main Windows x64 package without a bundled `sing-box.exe`
- `TunnelFlow-win-x64-with-core-v1.0.1.zip`
  - Convenience Windows x64 package with a bundled `sing-box.exe`

## Notes

- TunnelFlow remains a TUN-only client built around Wintun and sing-box.
- The Windows service architecture is retained for runtime actions.
- Administrator privileges may be required for service/bootstrapper and
  tunnel-related actions.
- Bundled third-party components remain separate components under their own
  licenses.
