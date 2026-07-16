$ErrorActionPreference = 'Stop'
Set-Location C:\app

dotnet --info

# Prove the reproduction precondition: the tools path this SDK reports must
# contain parentheses, otherwise the scenario would pass vacuously.
Write-Host '--- MSBuildToolsPath ---'
'<Project><Target Name="P"><Message Importance="high" Text="MSBuildToolsPath=[$(MSBuildToolsPath)]" /></Target></Project>' |
    Set-Content -Path probe.proj -Encoding utf8
$probe = dotnet msbuild probe.proj -nologo -v:m 2>&1 | Out-String
Write-Host $probe
if ($probe -notmatch '\(x86\)') {
    Write-Host "FAIL: MSBuildToolsPath does not contain '(x86)' - scenario would not exercise the bug."
    exit 1
}

Write-Host '--- restore ---'
dotnet restore Issue1699.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '--- build ---'
dotnet build Issue1699.csproj --no-restore -c Debug 2>&1 | Tee-Object -FilePath build.log
$buildRc = $LASTEXITCODE

# The bug surfaces as a task-factory load error with an escaped path.
if (Select-String -Path build.log -Pattern 'MSB4175' -Quiet) {
    Write-Host 'FAIL: MSB4175 - RoslynCodeTaskFactory could not be loaded (issue #1699).'
    exit 1
}
if (Select-String -Path build.log -Pattern '%28|%29' -Quiet) {
    Write-Host 'FAIL: build log contains an URL-escaped path (%28/%29) - issue #1699 not fixed.'
    exit 1
}
if ($buildRc -ne 0) {
    Write-Host "BUILD FAILED (rc=$buildRc)"
    exit $buildRc
}

Write-Host 'OK: win-x64/Issue1699X86Sdk scenario passed'
