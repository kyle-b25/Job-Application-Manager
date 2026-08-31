<#
.SYNOPSIS
    Publishes the WPF app as a self-contained win-x64 folder, ready for the installer to pack.

.DESCRIPTION
    Deliberately a FOLDER publish, not a single file. The single-file variant has to unpack the
    Skia, HarfBuzz and e_sqlite3 native DLLs into %TEMP% on every cold start, which costs startup
    time and is the shape corporate antivirus tends to flag. Inno Setup compresses the folder
    anyway, so nothing is gained by pre-packing it.

    No PublishTrimmed: WPF is not trim-safe, and EF Core's migrations reflect over the model at
    runtime.

    -SingleFile switches to one self-extracting exe instead. That is NOT what the installer packs;
    it exists for the standalone copy on the desktop, where a folder of 200-odd files would be
    unusable and the slower cold start is the price of that convenience.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $OutputDir,
    [switch] $SingleFile
)

$ErrorActionPreference = 'Stop'

# Resolved in the body rather than as a parameter default: $PSScriptRoot is not reliably
# populated while default expressions are evaluated under Windows PowerShell 5.1.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if (-not $OutputDir) {
    $OutputDir = Join-Path $scriptDir '..\artifacts\publish'
}

# The SDK is not always on PATH on this machine; CLAUDE.md and the README both note where it lives.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:Path = "C:\Program Files\dotnet;$env:Path"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found on PATH and is not at 'C:\Program Files\dotnet'. Install the .NET 8 SDK."
}

$repoRoot = Resolve-Path (Join-Path $scriptDir '..')
$project  = Join-Path $repoRoot 'src\JobAppManager.App\JobAppManager.App.csproj'

# Publish appends rather than replaces, so a stale run can leave orphaned files in the payload.
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}

$extra = @()
if ($SingleFile) {
    # IncludeNativeLibrariesForSelfExtract is required, not optional: libSkiaSharp,
    # libHarfBuzzSharp and e_sqlite3 are unmanaged, and without it they stay as loose DLLs
    # beside the exe - which defeats the whole point of a single portable file.
    $extra = @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')
}

Write-Host "Publishing $project -> $OutputDir" -ForegroundColor Cyan

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    @extra `
    --output $OutputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$resolved = Resolve-Path $OutputDir
$size     = (Get-ChildItem $resolved -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("Published {0:N0} MB to {1}" -f ($size / 1MB), $resolved) -ForegroundColor Green

$resolved.Path
