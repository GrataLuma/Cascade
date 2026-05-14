#requires -Version 5.1
<#
.SYNOPSIS
  F7: Build self-contained Windows distribution ZIP for the empirical evaluation suite.

.DESCRIPTION
  Publishes EvalSuite, HashFirstAttacker, and NistTests as self-contained single-file
  win-x64 executables, assembles a release directory with bin/, configs/, reports/ (empty),
  and documentation, then zips the whole thing.

  No .NET runtime is required on the target machine.

.PARAMETER Version
  Version stamp for the ZIP filename (default: today's date YYYY-MM-DD).

.PARAMETER OutputDir
  Where to assemble and zip the release (default: release/).

.PARAMETER Configuration
  Build configuration (default: Release).

.PARAMETER SkipZip
  Build the staging directory but skip ZIP creation (faster iteration).

.EXAMPLE
  pwsh scripts/build-release.ps1
  pwsh scripts/build-release.ps1 -Version 1.0
#>
[CmdletBinding()]
param(
    [string]$Version    = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$OutputDir  = 'release',
    [string]$Configuration = 'Release',
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'

# Resolve repo root (script is in scripts/, so repo root is its parent)
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

$Rid = 'win-x64'
$Tfm = 'net9.0'
$ReleaseName = "EmpiricalEvaluation-v$Version-$Rid"
$Staging = Join-Path $OutputDir $ReleaseName
$BinDir  = Join-Path $Staging 'bin'

Write-Host "==> Build settings" -ForegroundColor Cyan
Write-Host "    Repo root      : $RepoRoot"
Write-Host "    Configuration  : $Configuration"
Write-Host "    Runtime        : $Rid ($Tfm)"
Write-Host "    Release name   : $ReleaseName"
Write-Host "    Staging        : $Staging"
Write-Host ""

# Clean staging
if (Test-Path $Staging) {
    Write-Host "==> Removing previous staging $Staging"
    Remove-Item -Recurse -Force $Staging
}
New-Item -ItemType Directory -Force -Path $BinDir | Out-Null

$Projects = @(
    @{ Name = 'EvalSuite';          Path = 'EmpiricalEvaluation/EvalSuite/EvalSuite.csproj' }
    @{ Name = 'HashFirstAttacker';  Path = 'EmpiricalEvaluation/HashFirstAttacker/HashFirstAttacker.csproj' }
    @{ Name = 'NistTests';          Path = 'EmpiricalEvaluation/NistTests/NistTests.csproj' }
)

foreach ($p in $Projects) {
    Write-Host "==> Publishing $($p.Name)" -ForegroundColor Cyan
    $publishDir = Join-Path (Split-Path $p.Path -Parent) "bin/$Configuration/$Tfm/$Rid/publish"

    & dotnet publish $p.Path `
        --configuration $Configuration `
        --runtime $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false `
        -p:DebugType=embedded `
        --nologo `
        --verbosity minimal

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($p.Name)" }

    $exe = Join-Path $publishDir "$($p.Name).exe"
    if (-not (Test-Path $exe)) { throw "Expected output not found: $exe" }

    Copy-Item $exe $BinDir
    Write-Host "    -> $BinDir/$($p.Name).exe" -ForegroundColor Green
}

# Copy configs
Write-Host "==> Copying configs/" -ForegroundColor Cyan
Copy-Item -Recurse 'configs' (Join-Path $Staging 'configs')

# Empty reports/ placeholder
New-Item -ItemType Directory -Force -Path (Join-Path $Staging 'reports') | Out-Null
Set-Content -Path (Join-Path $Staging 'reports/.gitkeep') -Value '' -Encoding ASCII

# Copy documentation (paths relative to repo root)
Write-Host "==> Copying documentation" -ForegroundColor Cyan
$Docs = @('docs/README.md', 'docs/USAGE.md', 'docs/REPRODUCE_PAPER.md', 'docs/config_format.md')
foreach ($d in $Docs) {
    if (Test-Path $d) {
        $dest = Join-Path $Staging (Split-Path $d -Leaf)
        Copy-Item $d $dest
    } else {
        Write-Warning "Missing doc: $d (skipped)"
    }
}

# Summary of staged files
Write-Host ""
Write-Host "==> Staging contents" -ForegroundColor Cyan
Get-ChildItem -Recurse $Staging | Select-Object @{N='Size(KB)';E={[math]::Round($_.Length/1KB,1)}}, FullName | Format-Table -AutoSize | Out-String | Write-Host

# Compute total size
$totalBytes = (Get-ChildItem -Recurse -File $Staging | Measure-Object -Property Length -Sum).Sum
Write-Host ("    Total: {0:N1} MB" -f ($totalBytes / 1MB)) -ForegroundColor Yellow

if ($SkipZip) {
    Write-Host ""
    Write-Host "==> SkipZip set; staged at $Staging" -ForegroundColor Yellow
    return
}

$ZipPath = Join-Path $OutputDir "$ReleaseName.zip"
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }

Write-Host ""
Write-Host "==> Compressing to $ZipPath" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $Staging '*') -DestinationPath $ZipPath -CompressionLevel Optimal

$zipSize = (Get-Item $ZipPath).Length
Write-Host ("    ZIP size: {0:N1} MB" -f ($zipSize / 1MB)) -ForegroundColor Green

# SHA-256 integrity hash next to ZIP
$hash = Get-FileHash -Algorithm SHA256 -Path $ZipPath
"$($hash.Hash)  $($ReleaseName).zip" | Set-Content -Path "$ZipPath.sha256" -Encoding ASCII
Write-Host "    SHA-256: $($hash.Hash)" -ForegroundColor Green

Write-Host ""
Write-Host "==> Done. Distribution at $ZipPath" -ForegroundColor Green
