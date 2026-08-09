# Issue199CoreTargetsPath

Regression scenario for [#199](https://github.com/metalama/Metalama.Compiler/issues/199).

## Symptom

Under Visual Studio — that is, desktop `MSBuild.exe` — a project fails as soon as it references a
package whose targets probe the Roslyn version:

```
CommunityToolkit.Mvvm.SourceGenerators.targets(34,5): error MSB3441:
Cannot get assembly name for
"...\metalama.compiler\<version>\build\..\tasks\net10.0\binfx\Microsoft.Build.Tasks.CodeAnalysis.dll".
Could not load file or assembly 'Microsoft.Build.Tasks.CodeAnalysis.dll' or one of its dependencies.
```

`dotnet build` is unaffected.

## Root cause

`AnyCpu\build\Metalama.Compiler.props` derived both the tasks assembly and the core targets path from
one directory. When the Framework→Core bridge task of
[#194](https://github.com/metalama/Metalama.Compiler/issues/194) is selected, that directory became
`tasks\net10.0\binfx`. `RoslynTasksAssembly` was corrected there to the bridge assembly
(`Microsoft.Build.Tasks.CodeAnalysis.Sdk.dll`), but `CSharpCoreTargetsPath` and
`VisualBasicCoreTargetsPath` followed the same directory.

Third-party targets locate the compiler task by the long-standing convention that
`Microsoft.Build.Tasks.CodeAnalysis.dll` sits **beside** `Microsoft.CSharp.Core.targets`.
`tasks\net10.0\binfx` holds only the bridge task, so the probe fails. It also holds no
`Microsoft.VisualBasic.Core.targets` at all, so `VisualBasicCoreTargetsPath` named a file that does
not exist.

The .NET SDK, which the bridge arrangement mirrors, keeps the two apart: `Roslyn\` holds the
`.targets` next to `Microsoft.Build.Tasks.CodeAnalysis.dll`, and `Roslyn\binfx\` holds the bridge
task alone. The package layout already matched that — `tasks\net10.0\` has both — only the property
pointed elsewhere.

## What the scenario asserts

It must run under desktop MSBuild, because `_UseRoslynBridgeTask` requires
`'$(MSBuildRuntimeType)' != 'Core'`; a scenario built with `dotnet` could never fail. Hence its place
in `Standalone` rather than under `docker`.

Two independent checks, either of which fails the build:

- `CommunityToolkit.Mvvm` is referenced, so the real reported failure (MSB3441) is exercised through
  the actual third-party targets that reported it.
- `AssertCompilerLayoutFollowsConvention` in the project states the convention directly
  (`MLC0199`), so the scenario keeps its meaning if that package ever changes how it probes. It also
  checks `VisualBasicCoreTargetsPath`, but only under the bridge: `tasks\net472` has never shipped
  `Microsoft.VisualBasic.Core.targets`, so that path does not resolve on the .NET Framework payload
  either — a pre-existing gap, not #199.

`test.json` forbids both diagnostic codes.

## Matrix

| Entry | Properties | Covers |
|---|---|---|
| `bridge` | none | the bridged path — the configuration that regressed |
| `framework` | `RoslynCompilerType=Framework` | upstream's documented opt-out, i.e. the `tasks\net472` fallback, where the convention already held and must keep holding |

The `bridge` entry only exercises the bridge on a machine that has a .NET 10 runtime installed, since
`_UseRoslynBridgeTask` is gated on that (#194). Without one it degrades to the net472 fallback and
passes vacuously; the `Issue199:` message logged by the assertion target reports the resolved
`_UseRoslynBridgeTask`, `RoslynTasksAssembly` and `CSharpCoreTargetsPath` in the build log so this is
visible.
