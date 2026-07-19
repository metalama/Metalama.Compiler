# Standalone tests

Host-machine regression scenarios, the counterpart of the Docker tests under
[../docker](../docker).

A scenario belongs here when it must build with the **desktop (.NET Framework) `MSBuild.exe`**
rather than the .NET SDK's MSBuild — that is, when it covers behaviour that only manifests under
the .NET Framework-hosted compiler. The canonical case is anything touching
`AnalyzerAssemblyLoader.Desktop.cs`, which is `#if !NETCOREAPP` and therefore invisible to any
test built with `dotnet`.

## Wiring

The directory is registered in [eng-Metalama/src/Program.cs](../../../../eng-Metalama/src/Program.cs)
as a single entry in `Product.Solutions`:

```csharp
new ManyMSBuildSolutions( @"src\Metalama\tests\Standalone" ) { IsTestOnly = true }
```

`ManyMSBuildSolutions` (PostSharp.Engineering) discovers every project under this directory,
restores it with `dotnet restore`, then builds it with the `MSBuild.exe` found via `vswhere`. It
is the desktop-MSBuild counterpart of `ManyDotNetSolutions`, which the Metalama repo uses for its
own standalone tests. On non-Windows the scenarios are skipped with an explicit log line.

## Layout

Each subdirectory is one scenario:

```
<Scenario>/
  README.md              the analysis: symptom, root cause, what the scenario asserts
  <Scenario>.csproj      the project to build
  test.json              build target, property matrix and diagnostic assertions
```

Isolation from the repository root — which is a Roslyn/Arcade tree whose build logic must not
apply here — is provided by the `Directory.Build.props`, `Directory.Build.targets` and
`Directory.Packages.props` in this directory. Note that Central Package Management walks up
independently of `Directory.Build.props`, which is why the third file is needed; without it,
restore fails with a confusing `NU1008`.

`Directory.Build.props` imports `eng-Metalama\Versions.g.props` directly rather than the
`Directory.Build.props` above it. That is what supplies `MetalamaCompilerVersion` and appends the
local package feed to `RestoreAdditionalProjectSources`, so a scenario can reference the
just-built Metalama.Compiler with no `NuGet.config` and no version substitution.

## test.json

```json
{
    "BuildOnly": true,
    "Target": "Rebuild",
    "ForbiddenDiagnosticsRegexes": [ "CS8785", "TypeLoadException", "CS9248" ],
    "Matrix": [
        { "Name": "shared", "Properties": { "UseSharedCompilation": "true" } },
        { "Name": "no-shared", "Properties": { "UseSharedCompilation": "false" } }
    ]
}
```

- `Target` — use `Rebuild` when incremental up-to-date outputs could otherwise mask a failure.
- `Matrix` — builds the scenario once per entry, asserting each run independently. Each entry gets
  its own build log, named after the entry.
- `ForbiddenDiagnosticsRegexes` — diagnostics that must **not** appear. Matched against every
  output line containing `: error ` or `: warning `, so it catches warnings that do not fail the
  build. Prefer this over `FailOnUnexpectedDiagnostics` for a regression test, which also fires on
  incidental unrelated diagnostics.
- Also available: `ExpectedDiagnosticsRegexes`, `FailOnUnexpectedDiagnostics`, `IgnoreExitCode`,
  `ErrorRegexes`, and `Properties` shared by all matrix entries.

## Running

The scenarios reference the locally-built `Metalama.Compiler` nupkg, so build the product first:

```powershell
.\Build.ps1 build -c Debug
.\Build.ps1 test -c Debug --solution 2 --include-tests
```

`.\Build.ps1 list-solutions` gives the solution id. Omit `--solution` to run the unit tests too.

Build logs, one text log and one binlog per matrix entry, land in `artifacts\logs\`:

```
Issue193VsGenerator.csproj.shared.Rebuild.log
Issue193VsGenerator.csproj.no-shared.Rebuild.binlog
```
