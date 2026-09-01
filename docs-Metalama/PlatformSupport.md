# Platform support

This document describes the doctrine that governs which external platforms a Metalama release supports, how that
set is derived from the vendors' own support calendars, and how it is named. It also records the resulting set —
the **platform baseline** — for Metalama 2027.0.

It is the authoritative source for the question *"can we drop `net8.0`?"* and for every question of that shape.

> **Home.** This doctrine is product-wide, not specific to the compiler fork. Its intended home is
> `metalama/Metalama`, at `Metalama.Framework/docs/platform-support.md`, next to the other doctrine documents and
> cross-linked from `Directory.Packages.md` — which governs the *versions of NuGet packages* we may reference,
> while this document governs the *platforms those packages must load into*. It lives here for now because
> Metalama.Compiler's own target frameworks derive from it. When the companion change lands in `metalama/Metalama`,
> replace this file with a link rather than keeping two copies.

## Why a doctrine is needed

Metalama is loaded into processes we do not control: `devenv.exe`, Visual Studio's out-of-process Roslyn analyzer
host, Rider's backend, the VS Code C# Dev Kit language server, MSBuild, and `Metalama.Compiler.exe`. Each of those
has a *host runtime* and a *host Roslyn*, and neither is chosen by the user's project — the user's target framework
says nothing about either. A design-time host running .NET 8 cannot load a `net10.0` assembly, whatever the edited
project targets.

The failure is also asymmetric. Getting the *upper* bound wrong (referencing a newer package than the host ships)
produces a load error at the first invocation. Getting the *lower* bound wrong — shipping only a TFM the host
cannot load — produces, in Visual Studio, **no visible error at all**: `ServiceHub.RoslynCodeAnalysisService` logs
the failure and the IDE simply shows no Metalama diagnostics, no code lens and no generated code. Issue
metalama/Metalama#1710 was diagnosed only after finding 8396 silently logged exceptions. We therefore derive the
lower bound deliberately rather than discovering it from bug reports.

## The doctrine

A platform version is **in the supported set** of a Metalama release if all of the following hold. The rules
generalise the VS-floor rules previously stated in `metalama/Metalama`'s `Directory.Packages.md` to every axis
below.

1. **In vendor support today.** Not already end-of-life when the decision is taken.
2. **In vendor support at our GA date.**
3. **Thirty-day runway.** At least 30 days of vendor support remain *after* our GA date. Supporting a platform
   that dies two weeks after we ship costs a full TFM or variant for two weeks of coverage, so we do not.
4. **Latest patch tracks the channel.** Where the vendor supports only the latest patch of a channel, our floor is
   that latest patch, not the version we happened to test against.
5. **Mainstream, not extended.** "In vendor support" means the phase in which the vendor still fixes functionality
   defects. A version in a security-only phase (Visual Studio *extended* support, a VS LTSC's second year) is
   **not** in the supported set: we cannot get a Roslyn bug fixed there, so we cannot support it.
6. **Grace on the way out.** Within a release we do not withdraw support from a platform sooner than three months
   after it leaves vendor mainstream support. Rule 3 governs what a *new* release takes on; rule 6 governs what a
   *shipped* release keeps. An LTS branch does not freeze its declared floor — as the vendor drops a version, our
   supported set drops with it; we keep testing against the floor that was current at LTS GA.
7. **Roslyn follows fast.** A new stable Roslyn version is supported within three weeks of its stable release.
8. **An axis enters the matrix only if some shipped asset depends on it.** Before adding a TFM, a Roslyn variant
   or a version cap for a platform, name the asset whose selection actually changes. Most of our surface is
   `netstandard2.0` and is host-agnostic; see *What actually varies* below.

The supported set is the union over each axis of the versions satisfying rules 1–5, and the **floor** of an axis
is the lowest such version. Shipped TFMs are then derived from the floors, never chosen independently.

## The axes

| Axis | What it constrains | Selected at run time by |
| --- | --- | --- |
| Visual Studio | The private runtime and the Roslyn our design-time payload loads into, and the version cap of every VS-shipped dependency | The user's VS install |
| Other design-time hosts | Same, for Rider and the VS Code C# Dev Kit | The user's IDE |
| .NET SDK | The runtime MSBuild executes on, hence the toolset and build-task TFM | `global.json` / installed SDKs |
| .NET runtime | What a user application may target | The user's project |
| .NET Framework | The `net472` floor, and the binding-redirect ceilings of `devenv.exe` | Windows / the user's project |
| Roslyn API | Which per-Roslyn variant of our payload NuGet resolves | The host's Roslyn version |

## What actually varies

Only three selections in the product depend on the host, and each is worth knowing before arguing about a TFM.
The first two are in `metalama/Metalama`; the third is in this repository.

1. **Desktop or Core, with no version fallback.** `Metalama.Framework.CompilerExtensions` is a single
   `netstandard2.0` analyzer shim that embeds two flavours of the real implementation and picks between them on one
   boolean — `RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework")`, in
   `ResourceExtractor._isNetFramework`. There is **one** Core flavour, not one per .NET major, and no fallback if
   it fails to load. Its TFM is therefore the single most consequential number in this document: it must be
   loadable by *every* non-.NET-Framework host in the supported set, and it is set in
   `Metalama.Framework.CompilerExtensions.Resources.csproj` and the `CoreAssemblyToEmbed` items of
   `Metalama.Framework.CompilerExtensions.csproj`.
2. **Which Roslyn variant.** `ResourceExtractor.GetRoslynVersion` reads the version of the assembly containing
   `SyntaxNode` and maps it to a variant directory. It has an explicit branch for the JetBrains build, whose Roslyn
   reports `42.42.42.42` and carries the real version in `AssemblyInformationalVersionAttribute`. The variants live
   in `eng/RoslynVersions/` and are bounded by `RoslynApiMinVersion` / `RoslynApiMaxVersion`.
3. **Which toolset directory.** `build/Metalama.Compiler.props` picks `tasks/<tfm>` from `$(MSBuildRuntimeType)`
   and the host runtime version. This is a *build-time* selection driven by the .NET SDK, and is unrelated to the
   two above. `Metalama.Compiler.Interface.dll`, the only asset of this repository that an IDE ever loads, is
   `netstandard2.0` and is host-agnostic.

Everything else — the user-surfacing packages, the analyzer shim itself, the compile-time compilation (always
`netstandard2.0`) — is TFM-agnostic and does not participate in this analysis.

## Denomination

Each release names its supported set a **platform baseline**, written `PB-<release>`, whose canonical short form
lists the six floors in a fixed order:

```
PB-<release> = <VS floor> · <other-IDE floor> · <SDK floor> · <.NET Framework floor> · Roslyn <min>–<max> · Core=<tfm> / Desktop=<tfm>
```

Cite the baseline by name in issues, release notes and PR descriptions ("this drops below PB-2027.0"), and change
its contents only through this document.

## PB-2027.0 — Metalama 2027.0, GA 2027-01-01

```
PB-2027.0 = VS 2026 LTSC · VS Code C# Dev Kit / Rider current · .NET 10 SDK · .NET Framework 4.7.2 ·
            Roslyn 5.0–5.x · Core=net10.0 / Desktop=net472
```

### Visual Studio

Evaluated at GA 2027-01-01.

| Channel / version | Vendor status | Runway from GA | In set |
| --- | --- | --- | --- |
| VS 2022 17.10 LTSC and earlier | EOL 2026-01-13 or before | — | No (rule 1) |
| VS 2022 17.12 LTSC | EOL 2026-07-14 | — | No (rule 1) |
| VS 2022 17.14 Current Channel | Mainstream ends **2027-01-13**; extended (security-only) to 2032-01-13 | **12 days** | **No (rules 3, 5)** |
| VS 2026 LTSC (baseline of 2026-11) | Security servicing through ~2027-11 | ~10 months | Yes |
| VS 2027 Stable (released 2026-11) | Feature updates and servicing through ~2027-11 | rolling | Yes |

17.14 is the only VS 2022 version that reaches 2027 at all: Microsoft made it the final 2022 baseline on the
Current Channel and created no 17.14 LTSC, and every earlier LTSC channel has expired. It leaves mainstream
support 12 days after our GA, so rule 3 excludes it — and its remaining lifetime to 2032 is security-only, which
rule 5 excludes independently. **Metalama 2027.0 does not support Visual Studio 2022.**

VS 2026 and later update in place each November (VS 2026 shipped 2025-11-11 with .NET 10; VS 2027 is expected
2026-11 with .NET 11 and C# 15), with one year of feature updates and servicing followed by one year of LTSC
security servicing. The VS 2026 LTSC channel opens in November 2026, so it is the first pinnable VS 2026 version
and therefore our floor. Under rule 5 its second (security-only) year does not extend our set: the VS 2026 LTSC
leaves our supported set when VS 2027 has itself moved to LTSC.

### Consequence: the Core flavour is `net10.0`

Roslyn's own `docs/contributing/target-framework-strategy.md` names the Visual Studio private runtime in each
branch. It reads *"`$(NetVisualStudio)` (presently `net8.0`)"* on `release/dev18.0` (Roslyn 5.0 / VS 18.0) and
`release/dev18.3` (Roslyn 5.3 / VS 18.3), and *"presently `net10.0`"* on `release/stable` (Roslyn 5.10) and `main`.
Roslyn minor versions map to VS minor versions (4.14 ↔ 17.14, 5.0 ↔ 18.0, 5.3 ↔ 18.3), so the private runtime moved
to .NET 10 at approximately VS 18.10, before the November 2026 LTSC baseline.

With VS 2022 out of the set, **no host in PB-2027.0 runs a .NET runtime below 10**, and the single embedded Core
flavour becomes `net10.0`. This is the change that makes `net8.0` droppable; it is *not* implied by dropping
`net8.0` as a user target framework, which is a separate and unrelated decision.

This inference has one dependency worth watching: the upstream move off .NET 8
([dotnet/roslyn#84192](https://github.com/dotnet/roslyn/pull/84192)) was merged in June 2026, reverted the next
day, and relanded. **Confirm the actual private runtime of the VS 2026 LTSC baseline once it ships (2026-11-10)**
before 2027.0 GA — see the checklist below. If it turns out to be .NET 8, the Core flavour must stay `net8.0` for
2027.0.

### Other design-time hosts

| Host | Runtime | Roslyn | In set |
| --- | --- | --- | --- |
| VS Code + C# Dev Kit | Ships its own runtime; Roslyn's `$(NetVSCode)` is `net10.0` on `release/dev18.3` and `main` | `roslyn-language-server` 5.8+ | Yes, current version |
| JetBrains Rider | Backend runtime **to be measured** | Reports `42.42.42.42`; real version in `AssemblyInformationalVersionAttribute` | Yes, current version |
| OmniSharp | — | — | No — deprecated, untested |
| Visual Studio for Mac | — | — | No — sunset by Microsoft |

We support the current release of Rider and of the C# Dev Kit, not a named floor: JetBrains and the C# extension
both update continuously and neither publishes a support calendar we can apply rules 1–3 to. Rider's backend
runtime is the one unmeasured input to the `net10.0` decision and is on the checklist.

### .NET SDK (build time)

.NET 8 and .NET 9 both reach end of support on **2026-11-10**, seven weeks before GA, so neither is in the set
under rule 1. The floor is the **.NET 10 SDK**; the .NET 11 SDK (2026-11) is also in the set.

### .NET runtime (user target frameworks)

`net10.0` (LTS, supported to 2028-11) and `net11.0` (STS, released 2026-11). `net8.0` and `net9.0` are out of
support at GA and are no longer supported target frameworks — this is a **breaking change** for users, most
visibly for `Metalama.Patterns.Wpf`, whose `net8.0-windows` asset becomes `net10.0-windows` and leaves a WPF
application on .NET 8 or .NET 9 with no compatible asset.

### .NET Framework

The floor stays **4.7.2**. .NET Framework 4.6.2 reaches end of support on 2027-01-12 and is already below our
floor; 4.7.2, 4.8 and 4.8.1 are supported for the lifetime of the operating systems that carry them. The `net472`
assets serve `devenv.exe`, `MSBuild.exe` and user projects targeting .NET Framework, and they also fix the
binding-redirect ceilings on the Out-of-band package family documented in `Directory.Packages.md`.

### Roslyn API

`RoslynApiMinVersion` is the lowest Roslyn that any host in the set presents. With VS 2022 17.14 (Roslyn 4.14) out
of the set, the remaining hosts are VS 2026 LTSC and VS 2027 (Roslyn 5.11+), the .NET 10 and .NET 11 SDKs, and the
C# Dev Kit (Roslyn 5.8+) — all Roslyn 5. **The `Roslyn.4.12.0` variant therefore has no remaining host except
possibly Rider**, and can be dropped, raising `RoslynApiMinVersion` to `5.0.0` and collapsing the payload to a
single variant, *if and only if* Rider's Roslyn is measured at 5.0 or above. That measurement is on the checklist;
until it is made, keep the 4.12 variant.

`RoslynApiMaxVersion` follows VS 2027 and the .NET 11 SDK, within three weeks of their stable release (rule 7).

### Shipped assets under PB-2027.0

| Asset | Repository | TFM |
| --- | --- | --- |
| User-surfacing packages (`Metalama.Framework`, `Metalama.Patterns.*`, `Metalama.Backstage`, `Flashtrace*`) | Metalama | `netstandard2.0`, plus `net472` / `net10.0` where a package needs them |
| `Metalama.Framework.CompilerExtensions` (analyzer shim) | Metalama | `netstandard2.0` |
| Embedded **Desktop** flavour | Metalama | `net472` |
| Embedded **Core** flavour | Metalama | `net10.0` |
| Compile-time compilation | Metalama | `netstandard2.0` (always — unrelated to this baseline) |
| `Metalama.Compiler.Interface` (from `Metalama.Compiler.Sdk`) | Metalama.Compiler | `netstandard2.0` |
| `Metalama.Compiler` toolset, `Metalama.Compiler.Sdk` tasks | Metalama.Compiler | `net472`, `net10.0` |

### What PB-2027.0 drops relative to 2026.1

- Visual Studio 2022 in its entirety (rules 3 and 5 on 17.14).
- The `net8.0` and `net9.0` .NET SDKs at build time, and the corresponding toolset and build-task directories.
- The `net8.0` and `net9.0` user target frameworks.
- The `net8.0` embedded Core flavour.
- Conditionally, the `Roslyn.4.12.0` variant — pending the Rider measurement.

## What this means in this repository

Metalama.Compiler ships two packages, `Metalama.Compiler` and `Metalama.Compiler.Sdk`, and neither contains a
design-time assembly: the only asset an IDE loads is `analyzers/dotnet/cs/Metalama.Compiler.Interface.dll`, which
is `netstandard2.0`. **The VS axis of the baseline therefore does not constrain this repository at all**; only the
.NET SDK axis does.

Concretely, under PB-2027.0:

- `MetalamaNetRoslyn` and `NetRoslynAll` in `eng/targets/TargetFrameworks.props` follow the .NET SDK floor:
  `net10.0`. `NetVS` and `NetVSShared` may hold their upstream values, because every project that reads them is in
  `Ide.slnf` and only `Metalama.Compiler.slnf` is built — the one in-solution reference, in
  `Metalama.Compiler.Arm64.Package.csproj`, is a condition on a `net472` project and is false either way.
- The toolset and the `Metalama.Compiler.Sdk` tasks ship `net472` and `net10.0`. `net11.0` is not needed: the
  `net10.0` compiler has `rollForward=Major` and runs on .NET 11.
- A host runtime below the SDK floor must be reported, not left to fail while loading an assembly. The toolset does
  this with `LAMA0622` from `MetalamaCompilerCheckHostRuntime` in `build/Metalama.Compiler.targets`.

Two consequences to keep in sync with this document when the floor next moves:

- `buildTransitive/Metalama.Compiler.Sdk.props` selects `tasks/net10.0` on `$(MSBuildRuntimeType)` alone, with no
  version guard and no equivalent of `LAMA0622`. Below the SDK floor it fails with a raw assembly-load error.
- The .NET Framework MSBuild bridge in `build/Metalama.Compiler.props` probes the shared framework directory for
  `10.*` only. When the SDK floor moves past .NET 10, or on a machine that has only a later runtime, the bridge
  silently falls back to `net472` instead of driving the CoreCLR compiler.

## Verification checklist before 2027.0 GA

Rules 1–8 are applied against calendars; these three items are applied against machines, and no `net8.0` removal
should ship without them.

1. **VS 2026 LTSC baseline private runtime.** After 2026-11-10, install the LTSC baseline and confirm
   `ServiceHub.RoslynCodeAnalysisService` runs on .NET 10 — either from the VS install layout
   (`…\Microsoft Visual Studio\<year>\<sku>\dotnet\net10.0\runtime\`) or from Roslyn's
   `docs/contributing/target-framework-strategy.md` on the branch that shipped it. If it is .NET 8, the Core
   flavour stays `net8.0` for 2027.0.
2. **Rider backend runtime and Roslyn version.** Measure both on the current Rider. The runtime decides whether
   `Core=net10.0` is safe; the Roslyn version decides whether `Roslyn.4.12.0` can be dropped.
3. **Design-time smoke test on the floor.** Run basic design-time testing on the floor VS *and* the previous one.
   A `net8.0`/`net10.0` mismatch does not surface in the IDE; check `ServiceHub.RoslynCodeAnalysisService`'s log
   for load failures, not the editor.

## Sources

- [.NET 8 and .NET 9 end of support (2026-11-10)](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)
- [Visual Studio 2022 product lifecycle and servicing](https://learn.microsoft.com/en-us/visualstudio/releases/2022/servicing-vs2022)
- [Visual Studio product lifecycle and servicing (2026 and later)](https://learn.microsoft.com/en-us/visualstudio/releases/2026/servicing-vs)
- [Visual Studio channels and release rhythm (2026 and later)](https://learn.microsoft.com/en-us/visualstudio/releases/2026/release-rhythm)
- [.NET Framework lifecycle FAQ](https://learn.microsoft.com/en-us/lifecycle/faq/dotnet-framework)
- [Roslyn target framework strategy](https://github.com/dotnet/roslyn/blob/main/docs/contributing/target-framework-strategy.md)
- [dotnet/roslyn#84192 — move the VS private runtime off .NET 8](https://github.com/dotnet/roslyn/pull/84192)
- [Metalama requirements (public)](https://doc.metalama.net/conceptual/requirements)
