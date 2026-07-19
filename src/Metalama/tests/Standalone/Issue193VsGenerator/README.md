# Issue #193 — third-party generator fails to load under desktop MSBuild

Regression test for [metalama/Metalama.Compiler#193](https://github.com/metalama/Metalama.Compiler/issues/193)
(transferred from metalama/Metalama#1531).

## Symptom

Building a project that references both `Metalama.Compiler` and `XenoAtom.Logging`
succeeds with `dotnet build` but fails inside Visual Studio 2026 with:

```
CSC : warning CS8785: Generator 'LogFormatterGenerator' failed to generate source.
  Exception was of type 'TypeLoadException' with message 'Could not load type
  'XenoAtom.Logging.Generators.FormatterTemplateNode' from assembly
  'XenoAtom.Logging.Generators, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'.'
error CS9248: Partial property 'Program.MyLogFormatter' must have an implementation part.
```

## Why the two hosts differ

The compiler resolves an analyzer's dependencies by different mechanisms per platform,
and only the .NET Framework one is Metalama-modified.

On **.NET Core** (`dotnet build`), each analyzer directory gets its own
`AssemblyLoadContext` and `CompilerResolver` is first and unconditional in the
resolver chain (`AnalyzerAssemblyLoader.Core.cs`). It returns whatever the compiler's
ALC binds, so `System.Collections.Immutable` unifies on the compiler's copy. This file
is unmodified from upstream Roslyn.

On **.NET Framework** (Visual Studio, `MSBuild.exe`) there is no ALC and no
`CompilerResolver` — that file is `#if NET`. Unification is supposed to come from
Fusion binding redirects, with the `AssemblyResolve` handler as a fallback. Metalama
inserts an AppDomain pre-scan as the first statement of that handler
(`AnalyzerAssemblyLoader.Desktop.cs`, added for issue #142, `System.Memory`):

```csharp
var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
    .Select( a => (Assembly: a, AssemblyName: a.GetName()) )
    .Where( a => a.AssemblyName.Name == requestedAssemblyName.Name
                 && AssemblyName.ReferenceMatchesDefinition( requestedAssemblyName, a.AssemblyName ) )
    .OrderByDescending( a => a.AssemblyName.Version )
    .FirstOrDefault()
    .Assembly;
```

The same file defines a strict `IsMatch` that mimics Fusion — `candidate.Version >=
requested.Version` plus a public-key-token comparison — but the pre-scan does not use
it. It uses `AssemblyName.ReferenceMatchesDefinition`, which on .NET Framework
effectively compares only the simple name; since the `Where` already compares `.Name`,
the filter reduces to *"any loaded assembly with this simple name, highest version
wins"*, with **no floor at the requested version**. And because it is the first
statement of the handler, it short-circuits before upstream's `GetBestResolvedPath`.

So a generator requesting `System.Collections.Immutable, Version=9.0.0.0` in a host
that already has an older copy loaded gets the older one. `FormatterTemplateNode` is a
struct whose only external dependency is an `ImmutableArray<T>` field, and a struct
whose field type will not resolve fails to load with `TypeLoadException`.

## What this scenario does

The project references `Metalama.Compiler` (which replaces the compiler toolset) and
`XenoAtom.Logging` (which ships `LogFormatterGenerator`). No aspects and no
`Metalama.Framework` reference: the issue is analyzer dependency resolution, not the
transformation pipeline.

`test.json` builds it twice with `MSBuild.exe`, requiring both runs to be free of `CS8785`,
`TypeLoadException` and `CS9248`:

| Matrix entry | Loader path | Compiler host |
| --- | --- | --- |
| `shared` (`UseSharedCompilation=true`) | `AnalyzerAssemblyLoader.Desktop.cs` | `VBCSCompiler.exe` |
| `no-shared` (`UseSharedCompilation=false`) | `AnalyzerAssemblyLoader.Desktop.cs` | a fresh `csc.exe` |

Both modes are covered because the two host processes start with different sets of
already-loaded assemblies, and the pre-scan resolves against exactly that set. As of the
diagnosis, both fail — which tells us the too-old `System.Collections.Immutable` comes from the
compiler's own binding rather than from host-specific pollution by Visual Studio.

`Target` is `Rebuild` so that up-to-date outputs from a previous run cannot mask the failure.

For reference, the same project built with `dotnet build` succeeds with 0 warnings and 0 errors —
that path goes through `AnalyzerAssemblyLoader.Core.cs`. That control is not part of the scenario
because `ManyMSBuildSolutions` builds only with `MSBuild.exe`; it is stated here so the asymmetry
is on record.

## Why this is a standalone and not a Docker test

The bug only reproduces under the desktop (.NET Framework) MSBuild, which needs a real
Visual Studio or VS Build Tools installation on the host. This is also why the earlier
attempt at a repro in the Metalama repo could never fail: it was a `dotnet build`
standalone test, and the suspect code is `#if !NETCOREAPP` — not even compiled into
that path.

## Running it

```powershell
.\Build.ps1 build -c Debug
.\Build.ps1 test -c Debug --solution 2 --include-tests
```
