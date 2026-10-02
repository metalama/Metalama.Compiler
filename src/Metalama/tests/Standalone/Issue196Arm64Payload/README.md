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

## What the scenario found

`tasks\net472` and `tasks\net472-arm64` hold the same .NET Framework compiler deployment built for
two architectures, but they are filled from two separate item lists:
`DesktopCompilerArtifacts.targets` and `Arm64DesktopCompilerArtifacts.targets`.

The ARM64 list took the `System.*.dll` dependency closure from the `csi` build output rather than
from `csc-arm64`. `csi` is the scripting host. It does not carry the project references to
`Microsoft.CodeAnalysis.Workspaces` and `Microsoft.CodeAnalysis.Features` that `csc-arm64` and
`VBCSCompiler-arm64` add, so its output holds eight `System.*.dll` where `csc-arm64` holds
seventeen. An MSBuild wildcard reports nothing when it matches nothing, so `tasks\net472-arm64`
shipped without three assemblies with no build error:

| Assembly | Required by |
| --- | --- |
| `System.IO.Pipelines.dll` | `Microsoft.CodeAnalysis.Workspaces.dll` |
| `System.Text.Json.dll` | `Microsoft.CodeAnalysis.Features.dll`, `Microsoft.CodeAnalysis.CSharp.Features.dll` |
| `System.Text.Encodings.Web.dll` | `System.Text.Json.dll` |

Those four Roslyn assemblies are referenced by `csc-arm64.csproj` and `VBCSCompiler-arm64.csproj`
for the reason their comment states: they are used by Metalama.Framework inside the compiler
process. On ARM64 they could not load.

The standalone `Metalama.Compiler.Arm64` package was never affected. It reuses
`DesktopCompilerArtifacts.targets` with `RoslynPackageArch=arm64`, which takes the closure from
`csc-arm64`, and ships all seventeen.

## What the scenario asserts

Every file that `tasks\net472` ships must also be shipped by `tasks\net472-arm64`. The two are the
same deployment, so a file present in one and absent from the other is a packaging defect, whatever
its cause. Stating the invariant rather than naming the three assemblies keeps the scenario
meaningful if the closure changes again.

`AssertArm64PayloadMatchesNet472` reports the missing files by name under `MLC0196`, which
`test.json` forbids. A second check fails if `tasks\net472` itself is empty, so the assertion cannot
pass because the layout was renamed.

The scenario reads the package; it does not run the ARM64 compiler. It therefore reproduces the
defect on any architecture, which matters because the failure was reported from a machine that is
not available for development.

## Scope

The missing assemblies do not, on their own, reproduce the 20 s connect timeout. Removing exactly
those three from the working `tasks\net472` payload on x64 leaves `VBCSCompiler.exe` starting,
listening and serving a compilation. They break a compilation that loads Workspaces or Features in
the compiler process, which is the ARM64-only part. Whether anything further is wrong on ARM64
hardware is not settled by this scenario.

## Placement

The scenario needs the built `Metalama.Compiler` package, which is what the `Standalone` directory
provides through `MetalamaCompilerVersion` and the local package feed. Unlike the other scenarios
here, it does not depend on the desktop MSBuild host.
