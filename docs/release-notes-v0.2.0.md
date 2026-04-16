# TunnelFlow v0.2.0

TunnelFlow 0.2.0 advances the Windows desktop client for VLESS profiles and
per-application tunneling through a virtual adapter with stronger startup
reliability, cleaner portable runtime behavior, and clearer UI state handling.

## Highlights

- TUN-only runtime path built around Wintun and sing-box
- Simple VLESS profile workflow
- Per-application `Proxy`, `Direct`, and `Block` rules
- Service-managed lifecycle for the Windows desktop client
- More conservative TUN startup/readiness handling
- App-local portable runtime state and portable ZIP packaging layout
- Pending/draft App Rules UX that stays honest before and after service
  availability

## Download Options

- `TunnelFlow-win-x64-v0.2.0.zip`
  - Main package without a bundled `sing-box.exe`
- `TunnelFlow-win-x64-with-core-v0.2.0.zip`
  - Convenience package with a bundled `sing-box.exe`

Some packages may include separate third-party components that remain under
their own licenses.
