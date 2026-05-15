$ErrorActionPreference = 'Stop'
Set-Location C:\app

# Strip sdkAnalyzers/ from the local-feed Metalama.Compiler nupkg before NuGet
# touches it. The nupkg is a ZIP; we delete entries under sdkAnalyzers/ via
# System.IO.Compression then save back in-place.
Write-Host '--- stripping sdkAnalyzers/ from local-feed Metalama.Compiler nupkg ---'
# Match only `Metalama.Compiler.<version>.nupkg` where <version> starts with a digit.
# This excludes sibling packages whose ID starts with "Metalama.Compiler." (e.g.
# Metalama.Compiler.Sdk, Metalama.Compiler.Arm64, or hypothetical Common/etc.).
$compilerNupkg = Get-ChildItem 'C:\local-feed' -Filter 'Metalama.Compiler.*.nupkg' |
    Where-Object { $_.Name -match '^Metalama\.Compiler\.\d' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $compilerNupkg) { throw 'no Metalama.Compiler nupkg found in /local-feed' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($compilerNupkg.FullName, 'Update')
try {
    $entries = @($zip.Entries | Where-Object { $_.FullName -like 'sdkAnalyzers/*' })
    foreach ($e in $entries) {
        Write-Host "  remove $($e.FullName)"
        $e.Delete()
    }
    Write-Host "Stripped $($entries.Count) entries from $($compilerNupkg.Name)"
} finally {
    $zip.Dispose()
}

dotnet --info
Write-Host '--- restore ---'
dotnet restore BlazorRazor.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '--- build ---'
dotnet build BlazorRazor.csproj --no-restore -c Debug 2>&1 | Tee-Object -FilePath build.log
$buildRc = $LASTEXITCODE

if ($buildRc -ne 0) {
    Write-Host "BUILD FAILED (rc=$buildRc) - the installed-SDK fallback should have rescued the build"
    exit $buildRc
}
if (Select-String -Path build.log -Pattern 'BadImageFormatException' -Quiet) {
    Write-Host 'FAIL: BadImageFormatException'
    exit 1
}
if (Select-String -Path build.log -Pattern 'error LAMA0625' -Quiet) {
    Write-Host 'FAIL: LAMA0625 - fallback should have found the LKG SDK'
    exit 1
}
if (-not (Select-String -Path build.log -Pattern 'warning LAMA0617' -Quiet)) {
    Write-Host 'FAIL: expected LAMA0617 (fallback redirect) but none found'
    exit 1
}

Write-Host 'OK: win-x64/BlazorRazor-BundleMissing scenario passed'
