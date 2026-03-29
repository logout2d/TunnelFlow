# Phase 2 Plan — Real Packet Capture via ndisapi.net

## Goal
Replace StubPacketDriver with a real WinpkFilter-based implementation
using ndisapi.net (MIT license).

## Prerequisites
- [ ] ndisapi.net NuGet package added to TunnelFlow.Capture
- [ ] WinpkFilter driver installer available in third_party/winpkfilter/
- [ ] Driver installed on dev machine for testing

## Driver installation (dev machine)
Download WinpkFilter driver installer from:
https://github.com/wiresock/ndisapi/releases
Run as Administrator to install the NDIS driver.

## Implementation steps

### Step 1: Add ndisapi.net NuGet package
dotnet add src/TunnelFlow.Capture package ndisapi.net

### Step 2: Implement WinpkFilterPacketDriver
Create src/TunnelFlow.Capture/Interop/WinpkFilterPacketDriver.cs
implementing IPacketDriver using ndisapi.net API.

Key responsibilities:
- Open(): call NdisApi.IsDriverLoaded(), throw if not installed
- ReadLoopAsync(): enumerate adapters, set filter mode, read packets
- RedirectFlow(): rewrite destination IP:port to SOCKS endpoint
- DropFlow(): drop packet (block)
- PassFlow(): pass packet unchanged

### Step 3: Process-to-flow mapping
The ndisapi.net API operates at Ethernet frame level — it does not
provide PID per packet directly. We use the existing WindowsProcessResolver
(GetExtendedTcpTable / GetExtendedUdpTable) to map source port → PID.

### Step 4: Register in DI
In Program.cs, replace:
  builder.Services.AddSingleton<IPacketDriver, StubPacketDriver>();
With:
  builder.Services.AddSingleton<IPacketDriver, WinpkFilterPacketDriver>();

### Step 5: Integration test
Manual test: add browser.exe as Proxy rule, start tunnel,
verify browser traffic goes through SOCKS5 via sing-box.

## Known complexity areas
- Ethernet frame parsing (need to handle IPv4 TCP and UDP frames)
- NAT table for redirected flows (rewrite src/dst, track for response)
- ICMP pass-through
- IPv6 pass-through (not tunneled in MVP)

## Estimated effort
~3 Cursor prompts (Opus 4.6 recommended for all)
