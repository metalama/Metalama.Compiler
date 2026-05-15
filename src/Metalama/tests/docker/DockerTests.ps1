# Copyright (c) SharpCrafters s.r.o. All rights reserved.
# This project is not open source. Please see the LICENSE.md file in the repository root for details.

<#
.SYNOPSIS
    Run the Metalama.Compiler #180 Docker-based regression test matrix.

.DESCRIPTION
    Enumerates every scenario subdirectory under the requested platform root
    (linux-x64, linux-arm64, win-x64). For each: builds the scenario's
    Dockerfile (the locally-built Metalama.Compiler nupkgs are staged into
    the build context as ./local-feed/) and runs the resulting image, which
    must exit with code 0. The scenario's csproj references Metalama.Compiler
    at the local build version via a nuget.config that points at /local-feed.

.PARAMETER Platform
    Subdirectory of this script's directory: 'linux-x64', 'linux-arm64' or 'win-x64'.

.PARAMETER Scenario
    Optional name of a single scenario to run (matches the subdirectory name).
    Default runs every scenario found under <Platform>/.

.PARAMETER LocalFeed
    Path to the directory containing the locally-built Metalama.Compiler nupkgs.
    Defaults to <repo>/artifacts/packages/Debug/Shipping.

.PARAMETER Wsl
    Run docker via 'wsl -d Ubuntu-24.04' rather than the host docker. Required
    on a Windows host with Linux containers in WSL (typical local-dev setup).

.EXAMPLE
    .\DockerTests.ps1 -Platform linux-x64 -Wsl
    Run the full linux-x64 matrix from a Windows host using WSL.

.EXAMPLE
    .\DockerTests.ps1 -Platform linux-x64 -Scenario BlazorRazor
    Run just the BlazorRazor scenario.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('linux-x64', 'linux-arm64', 'win-x64')]
    [string]$Platform,

    [string]$Scenario,

    [string]$LocalFeed,

    [switch]$Wsl
)

$ErrorActionPreference = 'Stop'

$scriptDir   = $PSScriptRoot
$platformDir = Join-Path $scriptDir $Platform
$repoRoot    = (Resolve-Path (Join-Path $scriptDir '../../../..')).Path

if (-not $LocalFeed) {
    $LocalFeed = Join-Path $repoRoot 'artifacts\packages\Debug\Shipping'
}

if (-not (Test-Path $platformDir -PathType Container)) {
    throw "Platform directory not found: $platformDir"
}

if (-not (Test-Path $LocalFeed -PathType Container)) {
    throw "Local NuGet feed directory not found: $LocalFeed. Run .\Build.ps1 build -c Debug from the repo root first."
}

function ConvertTo-WslPath {
    param([string]$WindowsPath)
    $p = $WindowsPath -replace '\\', '/'
    if ($p -match '^([A-Za-z]):') {
        $p = $p -replace '^([A-Za-z]):', "/mnt/$($matches[1].ToLower())"
    }
    return $p
}

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$DockerArgs)
    if ($Wsl) {
        # Pass docker + its args directly to wsl as a native-command argument vector so
        # arguments containing spaces (e.g. a temp-dir build context on a host with a
        # spaced user profile) survive the shell hop without quoting.
        Write-Host "wsl> docker $($DockerArgs -join ' ')"
        & wsl -d Ubuntu-24.04 -- docker @DockerArgs
    } else {
        Write-Host "docker $($DockerArgs -join ' ')"
        & docker @DockerArgs
    }
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($DockerArgs[0]) exited with code $LASTEXITCODE"
    }
}

# Enumerate scenarios.
$scenarios = Get-ChildItem -Path $platformDir -Directory
if ($Scenario) {
    $scenarios = $scenarios | Where-Object Name -EQ $Scenario
    if (-not $scenarios) {
        throw "Scenario '$Scenario' not found under $platformDir"
    }
}
if (-not $scenarios) {
    Write-Host "No scenarios found under $platformDir." -ForegroundColor Yellow
    exit 0
}

$failures = @()

foreach ($s in $scenarios) {
    $scenarioName = $s.Name
    Write-Host ''
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "  Scenario: $Platform/$scenarioName" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan

    $dockerfile = Join-Path $s.FullName 'Dockerfile'
    if (-not (Test-Path $dockerfile)) {
        Write-Warning "Skipping ${scenarioName}: no Dockerfile."
        continue
    }

    # Stage the build context in a temp dir so we can drop in ./local-feed/.
    $context = Join-Path $env:TEMP "metalama-compiler-test-${scenarioName}-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
    New-Item -ItemType Directory -Path $context -Force | Out-Null

    try {
        # Copy scenario sources (Dockerfile + everything in the scenario dir).
        Copy-Item -Path (Join-Path $s.FullName '*') -Destination $context -Recurse -Force
        # Copy local-feed nupkgs into the build context.
        $stagedFeed = Join-Path $context 'local-feed'
        New-Item -ItemType Directory -Path $stagedFeed -Force | Out-Null
        Get-ChildItem $LocalFeed -Filter '*.nupkg' | Copy-Item -Destination $stagedFeed -Force

        # Discover the Metalama.Compiler version that the local-feed contains, then substitute
        # any $LOCAL_COMPILER_VERSION$ placeholder in the staged scenario sources. Scenarios
        # should reference Metalama.Compiler at this exact version via PackageReference
        # VersionOverride, otherwise NuGet picks up the (older) transitive version from
        # Metalama.Framework.
        # Sort by LastWriteTime desc so that when the local feed contains multiple builds
        # of Metalama.Compiler (a common case for repeated local-dev runs) we pick the
        # one most recently produced instead of a filesystem-enumeration-order winner.
        $compilerNupkg = Get-ChildItem $stagedFeed -Filter 'Metalama.Compiler.*.nupkg' |
            Where-Object Name -notlike 'Metalama.Compiler.Sdk.*' |
            Where-Object Name -notlike 'Metalama.Compiler.Arm64.*' |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if (-not $compilerNupkg) {
            throw "No Metalama.Compiler.*.nupkg found in $LocalFeed."
        }
        if ($compilerNupkg.BaseName -notmatch '^Metalama\.Compiler\.(.+)$') {
            throw "Unexpected nupkg name: $($compilerNupkg.Name)"
        }
        $localCompilerVersion = $matches[1]
        Write-Host "Using Metalama.Compiler version: $localCompilerVersion" -ForegroundColor DarkGray

        Get-ChildItem -Path $context -Recurse -Include '*.csproj', '*.props', '*.targets', 'NuGet.config' | ForEach-Object {
            $content = [System.IO.File]::ReadAllText($_.FullName)
            if ($content.Contains('$LOCAL_COMPILER_VERSION$')) {
                $content = $content.Replace('$LOCAL_COMPILER_VERSION$', $localCompilerVersion)
                [System.IO.File]::WriteAllText($_.FullName, $content)
            }
        }

        $imageTag = "metalama-compiler-${Platform}-${scenarioName}".ToLowerInvariant()
        $contextForDocker = if ($Wsl) { ConvertTo-WslPath $context } else { $context }

        Invoke-Docker 'build' '-t' $imageTag $contextForDocker
        Invoke-Docker 'run' '--rm' $imageTag

        Write-Host "PASS: $scenarioName" -ForegroundColor Green
    }
    catch {
        Write-Host "FAIL: $scenarioName -- $($_.Exception.Message)" -ForegroundColor Red
        $failures += $scenarioName
    }
    finally {
        Remove-Item -Path $context -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host "============================================================" -ForegroundColor Cyan
if ($failures.Count -eq 0) {
    Write-Host "All scenarios passed." -ForegroundColor Green
    exit 0
} else {
    Write-Host "Failed scenarios: $($failures -join ', ')" -ForegroundColor Red
    exit 1
}
