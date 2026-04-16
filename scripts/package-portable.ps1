param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "artifacts\portable",
    [string]$Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

function Get-VersionFromProps {
    param([string]$PropsPath)

    [xml]$props = Get-Content $PropsPath
    $versionNode = $props.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($versionNode)) {
        throw "Could not read <Version> from $PropsPath."
    }

    return $versionNode.Trim()
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-VersionFromProps (Join-Path $repoRoot "Directory.Build.props")
}

$outputRootPath = Join-Path $repoRoot $OutputRoot
$publishRoot = Join-Path $outputRootPath "publish"
$stageRoot = Join-Path $outputRootPath "stage"
$zipRoot = Join-Path $outputRootPath "zip"

if (Test-Path $outputRootPath) {
    Remove-Item -LiteralPath $outputRootPath -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot | Out-Null
New-Item -ItemType Directory -Path $stageRoot | Out-Null
New-Item -ItemType Directory -Path $zipRoot | Out-Null

$uiPublishDir = Join-Path $publishRoot "ui"
$servicePublishDir = Join-Path $publishRoot "service"
$bootstrapperPublishDir = Join-Path $publishRoot "bootstrapper"

function Invoke-PortablePublish {
    param(
        [string]$ProjectPath,
        [string]$PublishDir
    )

    & dotnet publish $ProjectPath `
        -c $Configuration `
        -o $PublishDir `
        -p:PortableReleasePublish=true `
        -p:RuntimeIdentifier=$RuntimeIdentifier `
        -p:SelfContained=false `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:PublishReadyToRun=false `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath"
    }
}

Invoke-PortablePublish "src\TunnelFlow.UI\TunnelFlow.UI.csproj" $uiPublishDir
Invoke-PortablePublish "src\TunnelFlow.Service\TunnelFlow.Service.csproj" $servicePublishDir
Invoke-PortablePublish "src\TunnelFlow.Bootstrapper\TunnelFlow.Bootstrapper.csproj" $bootstrapperPublishDir

$quickStartPath = Join-Path $repoRoot "QUICK_START.txt"
$licensePath = Join-Path $repoRoot "LICENSE"
$thirdPartyNoticesPath = Join-Path $repoRoot "THIRD_PARTY_NOTICES.md"
$appSettingsPath = Join-Path $repoRoot "src\TunnelFlow.Service\appsettings.json"
$singBoxSourcePath = Join-Path $repoRoot "docs\SING_BOX_SOURCE.txt"
$wintunPath = Join-Path $repoRoot "third_party\wintun\bin\amd64\wintun.dll"
$singBoxPath = Join-Path $repoRoot "third_party\singbox\sing-box.exe"
$libCronetPath = Join-Path $repoRoot "third_party\singbox\libcronet.dll"

$requiredCommonFiles = @(
    $quickStartPath,
    $licensePath,
    $thirdPartyNoticesPath,
    $appSettingsPath,
    $wintunPath
)

foreach ($path in $requiredCommonFiles) {
    if (-not (Test-Path $path)) {
        throw "Required packaging input was not found: $path"
    }
}

function Copy-WhitelistedFile {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (-not (Test-Path $Source)) {
        throw "Whitelisted source file was not found: $Source"
    }

    $destinationDir = Split-Path -Parent $Destination
    if (-not [string]::IsNullOrWhiteSpace($destinationDir)) {
        New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
    }

    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function New-PackageLayout {
    param(
        [string]$PackageName,
        [bool]$IncludeCore
    )

    $packageRoot = Join-Path $stageRoot $PackageName
    New-Item -ItemType Directory -Path $packageRoot | Out-Null

    $configDir = Join-Path $packageRoot "config"
    $systemDir = Join-Path $packageRoot "system"
    $coreDir = Join-Path $packageRoot "core"
    $licensesDir = Join-Path $packageRoot "licenses"

    New-Item -ItemType Directory -Path $configDir | Out-Null
    New-Item -ItemType Directory -Path $systemDir | Out-Null
    New-Item -ItemType Directory -Path $coreDir | Out-Null
    New-Item -ItemType Directory -Path $licensesDir | Out-Null

    Copy-WhitelistedFile (Join-Path $uiPublishDir "TunnelFlow.UI.exe") (Join-Path $packageRoot "TunnelFlow.exe")
    Copy-WhitelistedFile $quickStartPath (Join-Path $packageRoot "QUICK_START.txt")

    Copy-WhitelistedFile $appSettingsPath (Join-Path $configDir "appsettings.json")

    Copy-WhitelistedFile (Join-Path $servicePublishDir "TunnelFlow.Service.exe") (Join-Path $systemDir "TunnelFlow.Service.exe")
    Copy-WhitelistedFile (Join-Path $bootstrapperPublishDir "TunnelFlow.Bootstrapper.exe") (Join-Path $systemDir "TunnelFlow.Bootstrapper.exe")

    Copy-WhitelistedFile $wintunPath (Join-Path $coreDir "wintun.dll")

    Copy-WhitelistedFile $licensePath (Join-Path $licensesDir "LICENSE")
    Copy-WhitelistedFile $thirdPartyNoticesPath (Join-Path $licensesDir "THIRD_PARTY_NOTICES.md")

    if ($IncludeCore) {
        Copy-WhitelistedFile $singBoxPath (Join-Path $coreDir "sing-box.exe")
        Copy-WhitelistedFile $libCronetPath (Join-Path $coreDir "libcronet.dll")
        Copy-WhitelistedFile $singBoxSourcePath (Join-Path $licensesDir "SING_BOX_SOURCE.txt")
    }

    $zipPath = Join-Path $zipRoot ($PackageName + ".zip")
    Compress-Archive -Path $packageRoot -DestinationPath $zipPath -Force
}

$standardPackageName = "TunnelFlow-$RuntimeIdentifier-v$Version"
$withCorePackageName = "TunnelFlow-$RuntimeIdentifier-with-core-v$Version"

New-PackageLayout -PackageName $standardPackageName -IncludeCore:$false
New-PackageLayout -PackageName $withCorePackageName -IncludeCore:$true

Write-Host "Portable package folders:"
Write-Host "  $stageRoot"
Write-Host "Portable zip files:"
Write-Host "  $zipRoot"
