<#
.SYNOPSIS
    Builds the distributable Windows installer, end to end.

.DESCRIPTION
    Publishes the app (build\publish.ps1) and then compiles installer\JobApplicationManager.iss
    with Inno Setup, producing artifacts\JobApplicationManager-Setup-<version>.exe.

    The version comes from <Version> in Directory.Build.props and is passed to the script as
    /DAppVersion, so the installer and the exe always agree.

    Requires Inno Setup 6:  winget install JRSoftware.InnoSetup
#>
[CmdletBinding()]
param(
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

$repoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishDir = Join-Path $repoRoot 'artifacts\publish'
$outputDir  = Join-Path $repoRoot 'artifacts'
$issScript  = Join-Path $repoRoot 'installer\JobApplicationManager.iss'

# --- version -------------------------------------------------------------------------------
$props   = [xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props'))
$version = $props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read <Version> from Directory.Build.props."
}
$version = $version.Trim()

# --- Inno Setup ----------------------------------------------------------------------------
# winget installs Inno per-user under %LOCALAPPDATA%\Programs unless it was elevated, so check
# there first - the two Program Files paths only exist after a machine-wide install.
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { $iscc = $onPath.Source }
}
if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it with:  winget install JRSoftware.InnoSetup"
}

# --- publish -------------------------------------------------------------------------------
if ($SkipPublish) {
    if (-not (Test-Path (Join-Path $publishDir 'JobApplicationManager.exe'))) {
        throw "-SkipPublish was passed but $publishDir holds no published app. Run without it."
    }
    Write-Host "Skipping publish; packing the existing $publishDir" -ForegroundColor Yellow
} else {
    & (Join-Path $PSScriptRoot 'publish.ps1') | Out-Null
}

# --- compile the installer -----------------------------------------------------------------
Write-Host "Compiling installer (version $version)" -ForegroundColor Cyan

& $iscc `
    "/DAppVersion=$version" `
    "/DPublishDir=$publishDir" `
    "/O$outputDir" `
    $issScript

if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

$setup = Join-Path $outputDir "JobApplicationManager-Setup-$version.exe"
$mb    = (Get-Item $setup).Length / 1MB
Write-Host ("Installer ready: {0} ({1:N0} MB)" -f $setup, $mb) -ForegroundColor Green
