$ErrorActionPreference = 'Stop'
Set-Location C:\app

dotnet --info
Write-Host '--- restore ---'
dotnet restore Net48LoggerMessage.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '--- build (net48 via Metalama.Compiler net472 csc) ---'
dotnet build Net48LoggerMessage.csproj --no-restore -c Debug 2>&1 | Tee-Object -FilePath build.log
$buildRc = $LASTEXITCODE

if ($buildRc -ne 0) {
    Write-Host "BUILD FAILED (rc=$buildRc)"
    exit $buildRc
}
if (Select-String -Path build.log -Pattern 'BadImageFormatException' -Quiet) {
    Write-Host 'FAIL: BadImageFormatException'
    exit 1
}
if (Select-String -Path build.log -Pattern 'error LAMA0625' -Quiet) {
    Write-Host 'FAIL: LAMA0625 ERR_NoCompatibleSdkForAnalyzer'
    exit 1
}
if (Select-String -Path build.log -Pattern 'Strong name validation failed' -Quiet) {
    Write-Host 'FAIL: strong-name validation failed'
    exit 1
}

Write-Host '--- run ---'
$out = & 'C:\app\bin\Debug\net48\Net48LoggerMessage.exe' 2>&1 | Out-String
Write-Host $out
if ($out -notmatch 'WidgetCount: 42') {
    Write-Host 'FAIL: expected log line missing'
    exit 1
}
Write-Host 'OK: Net48LoggerMessage scenario passed'
