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
    Write-Host 'WARN: expected LAMA0617 redirect notice but none found'
}

Write-Host 'OK: win-x64/BlazorRazor scenario passed'
