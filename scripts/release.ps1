<#
.SYNOPSIS
    Builds and publishes a manual DictateFlow release: installer, portable ZIP and GitHub release.

.DESCRIPTION
    Runs the full local release pipeline for DictateFlow:
      1. dotnet clean
      2. dotnet publish (self-contained win-x64) with the assembly version set to -Version
      3. builds the Inno Setup installer  -> artifacts\DictateFlowSetup-v<Version>.exe
      4. creates the portable ZIP          -> artifacts\DictateFlowPortable-v<Version>.zip
      5. creates and pushes the git tag     v<Version>
      6. creates the GitHub release and uploads both artifacts

    This is a manual, local process. It does not auto-update anything and adds no CI/CD.

.PARAMETER Version
    The release version, e.g. 0.1.0 (or 0.1.0-beta.1). Required. Drives the assembly version,
    both artifact filenames and the git tag / GitHub release (all use the same value).

.PARAMETER ReleaseNotes
    Optional release notes text. When omitted, GitHub auto-generates notes from commits.

.PARAMETER InnoSetupPath
    Optional full path to ISCC.exe if it is not on PATH or in the default install location.

.PARAMETER Prerelease
    Marks the GitHub release as a pre-release.

.PARAMETER SkipGitHubRelease
    Builds the installer and ZIP but does not tag, push, or create the GitHub release.

.EXAMPLE
    .\scripts\release.ps1 -Version 0.1.0

.NOTES
    Requires: .NET 10 SDK, Inno Setup 6, GitHub CLI (gh, authenticated). See docs\release.md.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')]
    [string]$Version,

    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$ReleaseNotes,
    [string]$InnoSetupPath,
    [switch]$Prerelease,
    [switch]$SkipGitHubRelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-LastExitCode {
    param([string]$What)
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed (exit code $LASTEXITCODE)."
    }
}

function Find-InnoSetup {
    param([string]$Override)

    if ($Override) {
        if (Test-Path -LiteralPath $Override) { return (Resolve-Path -LiteralPath $Override).Path }
        throw "Inno Setup compiler not found at -InnoSetupPath '$Override'."
    }

    $onPath = Get-Command 'iscc.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }

    throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install it from https://jrsoftware.org/isdl.php, add it to PATH, or pass -InnoSetupPath. See docs\release.md."
}

# --- Resolve paths from the script location (no hardcoded developer paths) ---------------
$repoRoot     = Split-Path -Parent $PSScriptRoot
$tag          = "v$Version"
$appProject   = Join-Path $repoRoot 'src\DictateFlow.App\DictateFlow.App.csproj'
$issScript    = Join-Path $repoRoot 'installer\DictateFlow.iss'
$artifactsDir = Join-Path $repoRoot 'artifacts'
$publishDir   = Join-Path $artifactsDir "publish\$Runtime"
$installerOut = Join-Path $artifactsDir "DictateFlowSetup-$tag.exe"
$zipOut       = Join-Path $artifactsDir "DictateFlowPortable-$tag.zip"

Write-Host "DictateFlow release $Version" -ForegroundColor Green
Write-Host "  Repository root : $repoRoot"
Write-Host "  Publish output  : $publishDir"
Write-Host "  Installer       : $installerOut"
Write-Host "  Portable ZIP    : $zipOut"
Write-Host "  Git tag         : $tag"

# --- Pre-flight checks (fail fast before the long build) ---------------------------------
Write-Step "Checking prerequisites"

if (-not (Get-Command 'dotnet' -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK (dotnet) was not found on PATH. See docs\release.md."
}

$iscc = Find-InnoSetup -Override $InnoSetupPath
Write-Host "  Inno Setup: $iscc"

if (-not $SkipGitHubRelease) {
    if (-not (Get-Command 'gh' -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) was not found on PATH. Install it and run 'gh auth login', or pass -SkipGitHubRelease. See docs\release.md."
    }

    # Refuse to reuse an existing tag so a release is never silently overwritten.
    git rev-parse -q --verify "refs/tags/$tag" *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "Git tag $tag already exists. Bump -Version or delete the tag first."
    }
}

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

# --- 1. Clean ----------------------------------------------------------------------------
Write-Step "dotnet clean"
dotnet clean $appProject -c $Configuration
Assert-LastExitCode "dotnet clean"

# --- 2. Publish (self-contained) with the release version --------------------------------
Write-Step "dotnet publish ($Runtime, self-contained, v$Version)"
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -Recurse -Force -LiteralPath $publishDir
}
dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    -o $publishDir
Assert-LastExitCode "dotnet publish"

# --- 3. Installer ------------------------------------------------------------------------
Write-Step "Building Inno Setup installer"
& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$artifactsDir" $issScript
Assert-LastExitCode "Inno Setup build"
if (-not (Test-Path -LiteralPath $installerOut)) {
    throw "Installer was not produced at expected path: $installerOut"
}

# --- 4. Portable ZIP ---------------------------------------------------------------------
Write-Step "Creating portable ZIP"
if (Test-Path -LiteralPath $zipOut) {
    Remove-Item -Force -LiteralPath $zipOut
}
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipOut
if (-not (Test-Path -LiteralPath $zipOut)) {
    throw "Portable ZIP was not produced at expected path: $zipOut"
}

if ($SkipGitHubRelease) {
    Write-Step "Done (GitHub release skipped)"
    Write-Host "  $installerOut"
    Write-Host "  $zipOut"
    return
}

# --- 5. Tag and push ---------------------------------------------------------------------
Write-Step "Tagging $tag and pushing"
git tag -a $tag -m "DictateFlow $Version"
Assert-LastExitCode "git tag"
git push origin $tag
Assert-LastExitCode "git push"

# --- 6. GitHub release -------------------------------------------------------------------
Write-Step "Creating GitHub release $tag"
$ghArgs = @('release', 'create', $tag, $installerOut, $zipOut, '--title', "DictateFlow $Version")
if ($ReleaseNotes) {
    $ghArgs += @('--notes', $ReleaseNotes)
} else {
    $ghArgs += '--generate-notes'
}
if ($Prerelease) {
    $ghArgs += '--prerelease'
}
gh @ghArgs
Assert-LastExitCode "gh release create"

Write-Step "Release $Version complete"
Write-Host "  Installer : $installerOut"
Write-Host "  Portable  : $zipOut"
Write-Host "  Release   : https://github.com/michaelou/DictateFlow/releases/tag/$tag"
