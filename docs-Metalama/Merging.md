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
| `upstream/release/stable` | `5.10.0-1.*` | .NET SDK 11.0.100-preview.7 |
| `upstream/release/insiders` | `5.11.0-1.*` | no released SDK yet |
| `upstream/release/10.0.4xx` | `5.9.0-1.*` | .NET SDK 10.0.400 (GA) |
| `upstream/release/10.0.3xx` | `5.6.0-2.*` | .NET SDK 10.0.302 |
| `upstream/main` | — | never merge |

**The branch names do not pin a version — the branches rotate.** `release/stable` produced `5.9.0` while it
fed the .NET 10 GA SDK; once the .NET 11 previews started it was snapped to `5.10.0`, the `5.9.0` line moved to
the band branch `release/10.0.4xx`, and `release/insiders` moved on to `5.11.0`. The table above is a snapshot
taken on 2026-08-31. Re-derive it from `eng/Versions.props` on each branch every time rather than trusting the
row.

The mapping is verifiable, not guesswork: `eng-Metalama/DownloadNetSdkAnalyzers/net-sdk-releases.json`
records the Roslyn version of each .NET SDK it lists, and the same value is in the product version of
`C:\Program Files\dotnet\sdk\<version>\Roslyn\bincore\Microsoft.CodeAnalysis.dll`.

That file lists only the **primary SDK of each .NET release**, not every SDK band — as of 2026-08 it has
`10.0.400` but neither `10.0.110` nor `10.0.111`. Treat it as a lookup table for the bands it covers, and read
the product version of the installed `Microsoft.CodeAnalysis.dll` when a specific SDK is not in it.

**Normally the right source is `upstream/release/stable`.** Merge from the branch tip, which carries the
latest servicing fixes for that line.

`release/stable` is the branch of the newest *released* SDK, which during a .NET preview cycle is a preview
SDK rather than the GA one. Choosing between it and the GA band branch is a product decision, not a mechanical
one:

- **`release/stable`** when the goal is to support the current .NET preview SDK — for instance a `LAMA0617`
  raised against a preview SDK, as in issue #206.
- **the band branch of the GA SDK** (`release/10.0.4xx` as of 2026-08) when the goal is to stay on GA.
- **`release/insiders`** only to get ahead of every released SDK. It produces a version that no SDK ships yet,
  so nothing can validate the result end to end.

Confirm which SDK a branch actually feeds rather than inferring it from the branch name:

```powershell
# Latest released SDK of a channel. 'support-phase' is 'preview' for a preview channel.
Invoke-RestMethod https://builds.dotnet.microsoft.com/dotnet/release-metadata/11.0/releases.json |
    Select-Object channel-version, latest-sdk, latest-release, latest-release-date, support-phase
```

Then cross-check that SDK's Roslyn version against
`eng-Metalama/DownloadNetSdkAnalyzers/net-sdk-releases.json` and against the branch's `eng/Versions.props`.

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

`NU1101` has a second cause that looks identical from the build log: the nginx allow-list in front of ProGet
can reject the package id before ProGet ever sees it. Tell them apart by requesting the package through the
proxy — a `text/html` 404 body is nginx refusing it, while a 404 with no content type is ProGet reporting that
it holds no such package. The 2026-08 merge needed `microsoft.webtools` added to that allow-list for the Razor
test projects; see the infrastructure repository under `build/package-feeds.md`.

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

- `eng/Version.Details.props` / `.xml` — **take upstream wholesale.** Both files are generated by Maestro,
  and as of the Roslyn 5.10 merge they carry no Metalama divergence at all: resolve the conflict by replacing
  them with upstream's copies and check that `diff` against upstream is then empty.

  Until that merge the five `Microsoft.CodeAnalysis.*` bootstrap dependencies were held back (at
  `5.3.0-2.25625.1` while upstream was on `5.10.0-1.26365.3`) on the grounds that only those versions
  restored through the `roslyn-consolidated` mirror. That is no longer true — the mirror serves upstream's
  versions, and the repo builds with them — so the hold-back was removed. Do not reintroduce it: the mirror
  caches on first request, so a version it has not seen yet is not a reason to pin, it is a reason to
  request it once. Pin here only with a demonstrated, written reason.

  The runtime/BCL versions likewise must come from upstream, otherwise central package management reports
  `NU1109` downgrades against the newer transitive floors.
- `eng/Versions.props`, `eng/targets/Settings.props`, `eng/build.sh` — ours (Metalama versioning, branding,
  `Metalama.Compiler.slnf` as the default solution).
- `eng/targets/TargetFrameworks.props` — ours for `NetRoslynAll`, `NetVS`/`NetVSShared` and
  `MetalamaNetRoslyn`, plus any new property upstream added. **`NetRoslyn` follows upstream**: it is the single
  TFM of the test projects and the internal tools, and the compiler tests compile against the reference
  assemblies of the current .NET and execute the result in-process, so a lower TFM fails them all with
  `Could not load file or assembly System.Runtime`. Nothing shipped uses `NetRoslyn` — the compiler, the
  compiler server and the MSBuild tasks target `NetRoslynSourceBuild` (i.e. `NetRoslynAll`) and the toolset
  package targets `MetalamaNetRoslyn` — so raising it does not drop support for older .NET versions.
- `global.json` — upstream's SDK and `msbuild-sdks` versions, plus our `PostSharp.Engineering.Sdk` entry.
  When the SDK version changes, see [step 5](#5-match-the-build-agent-to-globaljson) — the build agent has to
  be updated to match, or CI fails before it compiles anything.
- `Roslyn.slnx` — both sides; watch the XML nesting, a naive union breaks the `</Folder>` pairing.
- `Metalama.Compiler.slnf` — ours, but re-check every path: upstream moves projects (e.g. `src/Tools/Source/*`
  → `src/Tools/*`). Validate that every entry in `projects` exists after the merge.
- Generated files under `Generated/CSharpSyntaxGenerator/` — keep the Metalama `TreeTracker` hooks and take
  upstream's `return` statement; step 4 regenerates them anyway.

## 3. Update eng\Versions.props

Set RoslynVersion to the source Roslyn version (`<Major>.<Minor>.0`).

## 4. Regenerate generated source files

See Modifications.md for details. Run `eng\generate-compiler-code.cmd` and check that it produces no diff.

## 5. Match the build agent to global.json

If the merge changed the SDK version in `global.json`, update `eng-Metalama/src/Program.cs`, where the
`DotNetComponent` declares the SDK installed on the build agent, then run `Build.ps1 generate-scripts` to
regenerate `eng-Metalama/docker/build.Dockerfile` from it (never edit that file by hand). The comment above the
declaration says "Must match global.json" — when it does not, CI fails during the build step with:

```
A compatible .NET SDK was not found.
Requested SDK version: 10.0.110
Installed SDKs: 9.0.305, 10.0.106 [C:\Program Files\dotnet\sdk]
```

## 6. Make sure all tests are green

There are two distinct suites — Metalama's own tests and Roslyn's — and `b test` runs only the first:

| command | what it runs |
|---|---|
| `b test` | `Metalama.Compiler.UnitTests`, filtered by the product's `Category!=OuterLoop` |
| `b test --property TestAll=True` | the same assembly unfiltered, which adds the `OuterLoop` classes: Roslyn's own semantic and diagnostic fixtures re-executed **through the Metalama transformer** |
| `eng\build.ps1 -c Debug -testCoreClr -msbuildEngine dotnet` | the **Roslyn test suite** itself — every test project in `Metalama.Compiler.slnf`, ~126,000 tests |
| `eng\build.ps1 -c Debug -testDesktop -msbuildEngine dotnet` | the same suite on .NET Framework; CI treats it as a separate leg |

Use `--property TestAll=True`, not `-p TestAll`: `Build.ps1` is declared with `[CmdletBinding]`, so `-p` is
ambiguous against PowerShell's own `-ProgressAction` and `-PipelineVariable` and the call fails before
reaching the build.

About 25 Roslyn tests are disabled on purpose, each marked with a `<Metalama>` comment giving the reason —
mainly the VB compiler-server tests (VB is not served by the Metalama server) and the analyzer-load tests
(`AnalyzerAssemblyRedirector` intercepts before the compiler's own check). Failures beyond those are real.

`dotnet build Metalama.Compiler.slnf` is the fast local check, but note that it needs
`eng-Metalama\Versions.g.props` (produced by `Build.ps1 prepare`); without it `VersionPrefix` and
`AssemblyVersion` evaluate to empty and unrelated errors appear (`MSB4184` on `[System.Version]::Parse('')`,
`CS1705` in `Microsoft.CodeAnalysis.Test.Utilities`).

`Versions.g.props` alone is not enough: it imports
`artifacts\packages\<configuration>\Shipping\Metalama.Compiler.version.props`, which lives under `artifacts`
and is therefore deleted by any clean. After a failed or cleaned build the same `MSB4184` reappears even
though `Versions.g.props` is still present — re-run `Build.ps1 prepare`.

The new packages are mirrored automatically by the `roslyn-consolidated` ProGet proxy on first restore (see [NuGet package sources](#nuget-package-sources) above), so no manual backup step is required.

### `Build.ps1 test` fails in restore with "You must install or update .NET"

The repo-local `.dotnet` directory and the machine-wide SDK can disagree after `global.json` changes, which is
exactly what a merge does. The failure surfaces as a restore error and says nothing about the merge:

```
NuGet.RestoreEx.targets(19,5): error : You must install or update .NET to run this application.
  App: C:\Program Files\dotnet\sdk\10.0.303\NuGet.Build.Tasks.Console.dll
  Framework: 'Microsoft.NETCore.App', version '10.0.11' (x64)
  .NET location: <repo>\.dotnet\
  The following frameworks were found:
    10.0.9 at [<repo>\.dotnet\shared\Microsoft.NETCore.App]
```

Read the two paths together: MSBuild loaded the SDK from **`C:\Program Files\dotnet`** but resolved shared
frameworks from **`<repo>\.dotnet`**, and the two do not carry the same runtime.

Arcade causes the split. `eng/common/tools.ps1` reuses an existing installation only when it contains
`sdk\<the exact version in global.json>`. The machine normally has a higher patch of the same band (`10.0.303`
against a pinned `10.0.301`), so that check fails, Arcade installs the exact SDK into `<repo>\.dotnet` and points
`DOTNET_ROOT` there. The `dotnet` muxer on `PATH` is still the machine one, and it resolves its own newer SDK
because `global.json` sets `rollForward: patch`. That SDK's MSBuild tasks need the newer shared runtime, which
the repo-local install does not have.

**This is not a symptom of the merge.** It reproduces on the branch before the merge; only the version numbers
differ, following whatever `global.json` pins. Do not bisect the merge over it.

The fix is to give `.dotnet` the runtime the machine SDK wants, taking the version from the `Framework:` line of
the error:

```powershell
.\eng\common\dotnet-install.ps1 -runtime dotnet -version 10.0.11
```

Setting `DOTNET_INSTALL_DIR` or `DOTNET_ROOT` to the machine directory does **not** help: Arcade overwrites both
once its exact-version check fails.

### `Build.ps1 test` fails copying `BuildMetalamaCompiler.dll`

```
error MSB3027: Could not copy "obj\Debug\net10.0\BuildMetalamaCompiler.dll" to "bin\...".
  Exceeded retry count of 10. The file is locked by: ".NET Host (25048)"
```

A build that was interrupted, or that failed part way, leaves compiler-server and MSBuild node processes running
and holding the engineering assembly open. Shut them down and re-run:

```powershell
dotnet build-server shutdown
Get-Process dotnet, VBCSCompiler, MSBuild -ErrorAction SilentlyContinue | Stop-Process -Force
```

## 7. Update Metalama Framework

See docs\updating-roslyn.md in the Metalama repo.

## 8. Update LowestSupportedRoslynVersion

When removing the support for the old Roslyn version, (which mainly involves removing projects for that version in the Metalama repo), also update the LowestSupportedRoslynVersion in Metalama.Compiler.Sdk.csproj.

## 9. Review

- Use gitk command.
- Show the changes done in the merge commit.
- Tick the "ignore space change".
- Pay attention to changes marked with "++" - these are the changes that have been done manually, not coming from either of the merged branches.
