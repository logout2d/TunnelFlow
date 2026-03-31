# Fix plan

## Current stage
Environment prepared for Codex-guided debugging and patching.

## Step 1
Add repository instructions and project memory for Codex.
Status: in progress

## Step 2
Patch SOCKS5 domain reply parsing and improve ProtocolSniffer robustness.
Status: completed

## Step 3
Add focused tests for SOCKS5/domain and fragmented TLS sniffing behavior.
Status: completed

## Step 4
Patch SingBoxConfigBuilder to honor profile.Network (tcp/ws/grpc as supported by the project).
Status: pending

## Step 5
Improve sing-box readiness validation and logging.
Status: pending

## Step 6
Investigate/patch IPv6 capture path if still required after earlier fixes.
Status: pending
