TunnelFlow — Solution structure
================================

This file documents the intended .sln and .csproj structure.
Run the bootstrap script below to create the actual solution.

Solution: TunnelFlow.sln
Projects:
  src/TunnelFlow.Core/TunnelFlow.Core.csproj           (classlib, net8.0-windows)
  src/TunnelFlow.Capture/TunnelFlow.Capture.csproj     (classlib, net8.0-windows)
  src/TunnelFlow.Service/TunnelFlow.Service.csproj     (exe, net8.0-windows, Worker Service)
  src/TunnelFlow.UI/TunnelFlow.UI.csproj               (exe, net8.0-windows, WPF)
  src/TunnelFlow.Tests/TunnelFlow.Tests.csproj         (xunit, net8.0-windows)

Project dependencies:
  Core      → (no deps)
  Capture   → Core
  Service   → Core, Capture
  UI        → Core (NOT Capture — enforced by ProjectReference absence)
  Tests     → Core, Capture, Service, UI (for unit testing)

Bootstrap (run in repo root, requires .NET 8 SDK):
---------------------------------------------------
dotnet new sln -n TunnelFlow
dotnet new classlib -n TunnelFlow.Core -o src/TunnelFlow.Core -f net8.0-windows
dotnet new classlib -n TunnelFlow.Capture -o src/TunnelFlow.Capture -f net8.0-windows
dotnet new worker -n TunnelFlow.Service -o src/TunnelFlow.Service -f net8.0-windows
dotnet new wpf -n TunnelFlow.UI -o src/TunnelFlow.UI -f net8.0-windows
dotnet new xunit -n TunnelFlow.Tests -o src/TunnelFlow.Tests -f net8.0-windows

dotnet sln add src/TunnelFlow.Core/TunnelFlow.Core.csproj
dotnet sln add src/TunnelFlow.Capture/TunnelFlow.Capture.csproj
dotnet sln add src/TunnelFlow.Service/TunnelFlow.Service.csproj
dotnet sln add src/TunnelFlow.UI/TunnelFlow.UI.csproj
dotnet sln add src/TunnelFlow.Tests/TunnelFlow.Tests.csproj

dotnet add src/TunnelFlow.Capture/TunnelFlow.Capture.csproj reference src/TunnelFlow.Core/TunnelFlow.Core.csproj
dotnet add src/TunnelFlow.Service/TunnelFlow.Service.csproj reference src/TunnelFlow.Core/TunnelFlow.Core.csproj
dotnet add src/TunnelFlow.Service/TunnelFlow.Service.csproj reference src/TunnelFlow.Capture/TunnelFlow.Capture.csproj
dotnet add src/TunnelFlow.UI/TunnelFlow.UI.csproj reference src/TunnelFlow.Core/TunnelFlow.Core.csproj
dotnet add src/TunnelFlow.Tests/TunnelFlow.Tests.csproj reference src/TunnelFlow.Core/TunnelFlow.Core.csproj
dotnet add src/TunnelFlow.Tests/TunnelFlow.Tests.csproj reference src/TunnelFlow.Capture/TunnelFlow.Capture.csproj
dotnet add src/TunnelFlow.Tests/TunnelFlow.Tests.csproj reference src/TunnelFlow.Service/TunnelFlow.Service.csproj
