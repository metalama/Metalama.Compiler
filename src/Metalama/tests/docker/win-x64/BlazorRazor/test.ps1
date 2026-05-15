$ErrorActionPreference = 'Stop'
Set-Location C:\app

dotnet --info
Write-Host '--- restore ---'
dotnet restore BlazorRazor.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '--- build ---'
dotnet build BlazorRazor.csproj --no-restore -c Debug 2>&1 | Tee-Object -FilePath build.log
$buildRc = $LASTEXITCODE

if ($buildRc -ne 0) {
    Write-Host "BUILD FAILED (rc=$buildRc)"
    exit $buildRc
}
if (Select-String -Path build.log -Pattern 'BadImageFormatException' -Quiet) {
    Write-Host 'FAIL: BadImageFormatException'
    exit 1
}
if (Select-String -Path build.log -Pattern 'warning CS8034' -Quiet) {
    Write-Host 'FAIL: CS8034 analyzer load failure'
    exit 1
}
if (Select-String -Path build.log -Pattern 'error LAMA0625' -Quiet) {
    Write-Host 'FAIL: LAMA0625 ERR_NoCompatibleSdkForAnalyzer (the bundle should have covered this)'
    exit 1
}
if (-not (Select-String -Path build.log -Pattern 'warning LAMA0617' -Quiet)) {
    # Both bundle hit and installed-SDK hit emit LAMA0617. Its absence means the
    # redirect never fired and the build only passed by accident (e.g. the analyzer
    # was already binary-compatible). Hard fail so the scenario actually proves the
    # bundle path is in use.
    Write-Host 'FAIL: expected LAMA0617 redirect notice but none found - redirect did not fire'
    exit 1
}

Write-Host 'OK: win-x64/BlazorRazor scenario passed'
