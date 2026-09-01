# Platform support

The doctrine that decides which Visual Studio, other integrated development environment, .NET SDK, .NET runtime,
.NET Framework and Roslyn versions a Metalama release supports is product-wide, and lives in the `metalama/Metalama`
repository:

**[`Metalama.Framework/docs/platform-support.md`](https://github.com/metalama/Metalama/blob/develop/2027.0/Metalama.Framework/docs/platform-support.md)**

That document is added by [metalama/Metalama#1891](https://github.com/metalama/Metalama/pull/1891), so the link
above resolves once that pull request is merged. Remove this paragraph then.

Read it before changing `eng/targets/TargetFrameworks.props`, before adding or removing a target framework from a
package, and before answering a question of the form "can we drop `netX.0`?". It defines the eight rules that put a
platform version in the supported set, names the resulting set a platform baseline (`PB-<release>`), records the
evaluated baseline for the release in preparation, and carries a section titled "What this means in
Metalama.Compiler" that covers this repository specifically.

Three points from that section are worth stating here, because they are the ones a change in this repository runs
into first.

- This repository ships no design-time assembly. The only asset an integrated development environment loads is
  `analyzers/dotnet/cs/Metalama.Compiler.Interface.dll`, which is `netstandard2.0`. The Visual Studio axis of the
  baseline therefore does not constrain this repository at all. Only the .NET SDK axis does.
- `MetalamaNetRoslyn` and `NetRoslynAll` follow the .NET SDK floor of the baseline. `NetVS` and `NetVSShared` are
  read only by projects in `Ide.slnf`, which is not built here, so they may hold their upstream values.
- A host runtime below the SDK floor must be reported rather than left to fail while loading an assembly. The
  toolset does this with `LAMA0622`, from `MetalamaCompilerCheckHostRuntime` in `build/Metalama.Compiler.targets`.
