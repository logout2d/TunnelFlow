param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "TunnelFlow.sln"

dotnet build $solutionPath -c $Configuration

$uiExe = Join-Path $repoRoot "src\TunnelFlow.UI\bin\$Configuration\net8.0-windows\TunnelFlow.UI.exe"
if (-not (Test-Path -LiteralPath $uiExe)) {
    throw "UI executable not found at $uiExe"
}

Write-Host "Launching $uiExe"
Start-Process -FilePath $uiExe
