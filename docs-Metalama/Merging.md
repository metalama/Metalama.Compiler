# Merging from new Roslyn branches

## What we track: the .NET SDK, not Visual Studio

**Metalama.Compiler must keep up with the Roslyn version bundled in the latest GA .NET SDK.** That is the
version our users actually get, and it is the only signal that matters when deciding whether a merge is due.

The reason is the analyzers shipped inside the SDK. Analyzers such as `Microsoft.CodeAnalysis.Razor.Compiler.dll`
are compiled against the Roslyn of their SDK; when Metalama Compiler bundles an older Roslyn, it cannot load
them and falls back to an older SDK's copy, reporting:

```
LAMA0617: Analyzer 'Microsoft.CodeAnalysis.Razor.Compiler.dll' from .NET SDK 10.0.400 requires
Roslyn 5.9.0.0, newer than the Roslyn 5.6.0.0 bundled with Metalama Compiler. …
```

A `LAMA0617` in `Metalama.Tests.DotNetSdk` CI is the canonical "a merge is overdue" signal.

**Do not use Visual Studio releases or nuget.org to decide the target version.** Both lag, sometimes by
several minor versions:

- `Microsoft.Net.Compilers.Toolset` on nuget.org is published only for some releases (as of 2026-08 it stops
  at 5.6.0, while the GA SDK ships 5.9).
- `Visual-Studio-2026-Version-*` tags stop being created for newer lines (as of 2026-08 the newest is `18.7`,
  which is also the same commit as its own `-Preview-1`).

Historically this document was written around VS tags and the toolset package. That guidance is obsolete.

## Source selection policy

The merge source must be a **named upstream branch whose Roslyn version matches a released .NET SDK**.
Never merge from an arbitrary commit SHA, and never from `upstream/main`.

`dotnet/roslyn` publishes a small set of long-lived branches, each of which feeds a specific product band.
Identify them by the version triple in `eng/Versions.props` (`MajorVersion`, `MinorVersion`,
`PreReleaseVersionLabel`) — a branch producing `<Major>.<Minor>.0-<Label>.*` is the source of any SDK whose
Roslyn version has that shape:

| upstream branch | version produced | ships in |
|---|---|---|
| `upstream/release/stable` | `5.9.0-1.*` | .NET SDK 10.0.400 (GA) |
| `upstream/release/10.0.3xx` | `5.6.0-2.*` | .NET SDK 10.0.302 |
| `upstream/release/insiders` | `5.10.0-1.*` | .NET SDK 11.0.100-preview.7 |
| `upstream/main` | — | never merge |

The mapping is verifiable, not guesswork: `eng-Metalama/DownloadNetSdkAnalyzers/net-sdk-releases.json`
records the exact Roslyn version of every .NET SDK release, and the same value is in the product version of
`C:\Program Files\dotnet\sdk\<version>\Roslyn\bincore\Microsoft.CodeAnalysis.dll`.

**Normally the right source is `upstream/release/stable`** — the branch behind the current GA SDK. Merge from
the branch tip, which carries the latest servicing fixes for that line. Use `release/insiders` only when the
goal is deliberately to support a .NET preview SDK ahead of GA.

Note that these branches are not ancestors of each other; `release/stable` and `release/10.0.3xx` fork from a
common point, so consecutive merges are not always a straight line. Check
`git merge-base develop/YYYY.N upstream/release/stable` before starting: if it is the commit of the previous
merge, the merge is a clean single hop.

## NuGet package sources

Metalama.Compiler restores all non-nuget.org packages (dependencies of old Roslyn versions, VS SDK packages, etc.) from the **`roslyn-consolidated`** feed on `proget.postsharp.net`. This feed is a **mirroring proxy**: the first time a package is requested, ProGet fetches it from the upstream feed and caches it permanently, so dependencies of old Roslyn versions are never lost even after upstream removes them.

Because the proxy caches automatically, there is **no manual backup or push step** when merging a new Roslyn version — simply restoring/building the merged code populates the mirror. The original upstream feeds (`dotnet-eng`, `dotnet-tools`, `dotnet6`, `vs-impl`, etc.) stay commented out in `nuget.config` and must not be re-enabled.

If a merge brings in a dependency the proxy cannot serve (`NU1101: Unable to find package …`), the fix is to
add the missing upstream feed as a **connector on the `roslyn-consolidated` feed**, not to re-enable the feed
in `nuget.config`.

## 1. Identify the target branch

Follow the [source selection policy](#source-selection-policy) above: find the Roslyn version of the latest GA
.NET SDK, then the upstream branch that produces it.

```powershell
git fetch upstream
git show upstream/release/stable:eng/Versions.props | Select-String "MajorVersion|MinorVersion|PreReleaseVersionLabel"
```

## 2. Merge the selected Roslyn branch to Metalama.Compiler repo

See Modifications.md to better understand the changes done for Metalama.

Recurring conflicts and how they are resolved:

- `eng/Version.Details.props` / `.xml` — mostly ours (the Metalama dependency manifest), **except**: Arcade /
  Helix / XliffTasks follow upstream (they pair with the merged `eng/common` scripts), and the runtime/BCL
  package versions must be taken from upstream, otherwise central package management reports `NU1109`
  downgrades against the newer transitive floors. The `Microsoft.CodeAnalysis.*` bootstrap pins stay ours.
- `eng/Versions.props`, `eng/targets/Settings.props`, `eng/build.sh` — ours (Metalama versioning, branding,
  `Metalama.Compiler.slnf` as the default solution).
- `eng/targets/TargetFrameworks.props` — ours (`NetRoslyn`, `NetVS`, `MetalamaNetRoslyn`), plus any new
  property upstream added.
- `global.json` — upstream's SDK and `msbuild-sdks` versions, plus our `PostSharp.Engineering.Sdk` entry.
- `Roslyn.slnx` — both sides; watch the XML nesting, a naive union breaks the `</Folder>` pairing.
- `Metalama.Compiler.slnf` — ours, but re-check every path: upstream moves projects (e.g. `src/Tools/Source/*`
  → `src/Tools/*`). Validate that every entry in `projects` exists after the merge.
- Generated files under `Generated/CSharpSyntaxGenerator/` — keep the Metalama `TreeTracker` hooks and take
  upstream's `return` statement; step 4 regenerates them anyway.

## 3. Update eng\Versions.props

Set RoslynVersion to the source Roslyn version (`<Major>.<Minor>.0`).

## 4. Regenerate generated source files

See Modifications.md for details. Run `eng\generate-compiler-code.cmd` and check that it produces no diff.

## 5. Make sure all test are green

To run Metalama.Compiler tests, execute `b test`.
To run all Roslyn tests, execute `b test -p TestAll`.

`dotnet build Metalama.Compiler.slnf` is the fast local check, but note that it needs
`eng-Metalama\Versions.g.props` (produced by `Build.ps1 prepare`); without it `VersionPrefix` and
`AssemblyVersion` evaluate to empty and unrelated errors appear (`MSB4184` on `[System.Version]::Parse('')`,
`CS1705` in `Microsoft.CodeAnalysis.Test.Utilities`).

The new packages are mirrored automatically by the `roslyn-consolidated` ProGet proxy on first restore (see [NuGet package sources](#nuget-package-sources) above), so no manual backup step is required.

## 6. Update Metalama Framework

See docs\updating-roslyn.md in the Metalama repo.

## 7. Update LowestSupportedRoslynVersion

When removing the support for the old Roslyn version, (which mainly involves removing projects for that version in the Metalama repo), also update the LowestSupportedRoslynVersion in Metalama.Compiler.Sdk.csproj.

## 8. Review

- Use gitk command.
- Show the changes done in the merge commit.
- Tick the "ignore space change".
- Pay attention to changes marked with "++" - these are the changes that have been done manually, not coming from either of the merged branches.
