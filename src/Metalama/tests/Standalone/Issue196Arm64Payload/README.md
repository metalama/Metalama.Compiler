# Issue196Arm64Payload

Regression scenario for [#196](https://github.com/metalama/Metalama.Compiler/issues/196).

## Symptom

On Windows ARM64, a build driven by desktop `MSBuild.exe` selects `tasks\net472-arm64`. `csc.exe`
starts `VBCSCompiler.exe` from that directory, the process is created, and no named pipe ever
appears. `csc.exe` waits for Roslyn's normal 20 s connect timeout and then exits 1:

```
Successfully created process with process id 8788
Attempt to connect named pipe '...'
Connecting to server timed out after 20000 ms: 'TimeoutException'
CompilerServer: server failed - cannot connect to the server
...\tasks\net472-arm64\Microsoft.CSharp.Core.targets(104,5): error MSB6006: "csc.exe" exited with code 1
```

`dotnet build` on the same machine succeeds, because the Core host runs the CoreCLR compiler.
`msbuild-x64` and `msbuild-x86` succeed, because they use `tasks\net472`.

## Root cause

`tasks\net472` and `tasks\net472-arm64` hold the same .NET Framework compiler deployment built for
two architectures, but they are filled from two separate item lists:
`DesktopCompilerArtifacts.targets` and `Arm64DesktopCompilerArtifacts.targets`.

The ARM64 list took the `System.*.dll` dependency closure from the `csi` build output. This
repository does not build `csi` — `csi.csproj` is not in `Metalama.Compiler.slnf`, and the AnyCpu
list carries a comment recording that it was changed away from `csi` for that reason. An MSBuild
wildcard over a directory that does not exist expands to nothing and reports no error, so
`tasks\net472-arm64` shipped without `System.Collections.Immutable.dll`,
`System.Reflection.Metadata.dll` and the rest of the closure that `Microsoft.CodeAnalysis.dll`
requires on .NET Framework.

`VBCSCompiler.exe` therefore fails to load its dependencies during startup and terminates before it
creates the named pipe, which is exactly what the client observes: a process that is created
successfully and then never answers.

## What the scenario asserts

Every file that `tasks\net472` ships must also be shipped by `tasks\net472-arm64`. The two are the
same deployment, so any file present in one and absent from the other is a packaging defect,
whatever its cause.

The scenario reads the package; it does not run the ARM64 compiler. It therefore reproduces the
defect on any architecture, which matters because the failure was reported from a machine that is
not available for development.

`AssertArm64PayloadMatchesNet472` reports the missing files by name under `MLC0196`, which
`test.json` forbids. A second check fails if `tasks\net472` itself is empty, so the assertion cannot
pass because the layout was renamed.

## Placement

The scenario needs the built `Metalama.Compiler` package, which is what the `Standalone` directory
provides through `MetalamaCompilerVersion` and the local package feed. It does not depend on the
desktop MSBuild host, unlike the other scenarios here.
