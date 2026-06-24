# Claude instructions for Metalama.Compiler

## Build troubleshooting

### `vsdconfigtool.exe` failures (MSB3073, exit code -2146232576 / 0x80131700)

During the Release build, the VS SDK post-build step on the ExpressionCompiler projects
(`Microsoft.CodeAnalysis.CSharp.ExpressionCompiler`, `Microsoft.CodeAnalysis.VisualBasic.ExpressionCompiler`)
can fail with:

```
Microsoft.VSSDK.Debugger.VSDConfigTool.targets(31,5): error MSB3073:
  The command "...vsdconfigtool.exe ..." exited with code -2146232576.
```

**Cause:** the NuGet package cache (and therefore `vsdconfigtool.exe`) resolves under
`C:\WINDOWS\system32\config\systemprofile\.nuget\packages\...`. That is the NuGet cache of the
`LocalSystem`/service account, used when `NUGET_PACKAGES` is **not** set and `USERPROFILE` points
to the service profile (as it does for the TeamCity agent running as a service). Running the native
`vsdconfigtool.exe` out of a cache under `system32\config\systemprofile` intermittently fails to launch
(`0x80131700` is a CLR host activation failure). The failure is flaky — the same commit can succeed on
another run.

**Fix:** define `NUGET_PACKAGES` explicitly to a stable, normal path so the cache never resolves under
`system32\config\systemprofile`. The project already does this for Azure Pipelines
(`azure-pipelines.yml`: `NUGET_PACKAGES = $(Build.SourcesDirectory)\.packages`, a workaround for
https://github.com/dotnet/arcade/issues/15970). Set `NUGET_PACKAGES` the same way for the TeamCity
agent / Docker build environment.
