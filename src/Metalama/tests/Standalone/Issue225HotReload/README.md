# Issue225HotReload

Regression scenario for [#225](https://github.com/metalama/Metalama.Compiler/issues/225).

## Symptom

Visual Studio offers Hot Reload, and Edit and Continue, for a project that uses Metalama. The edits
are applied without any error, but the edited methods lose their aspects
([metalama/Metalama#2292](https://github.com/metalama/Metalama/issues/2292)).

## Root cause

Hot Reload does not run the build compiler. The IDE compiles the edits with its own copy of Roslyn,
which does not run the Metalama transformer pipeline. The PDB emitted by Metalama.Compiler maps the
transformed code to the original source files, so the Edit and Continue engine considers that the
documents match the build output and does not report the edits as unsupported.

## Fix

`AnyCpu\build\Metalama.Compiler.props` sets `SupportsHotReload` to `false` when the project does not
define it. `Microsoft.NET.Sdk.CSharp.targets` then does not add the `SupportsHotReload` project
capability, and Visual Studio does not offer Hot Reload. A project can set `SupportsHotReload` to
`true` to enable Hot Reload again.

## What the scenario asserts

The `AssertHotReloadCapability` target checks the `SupportsHotReload` item of `@(ProjectCapability)`
and fails with `MLC0225`. `test.json` forbids this code.

| Entry | Properties | Expected capability |
|---|---|---|
| `default` | none | absent |
| `opt-in` | `Issue225OptIn=true` | present |

The `opt-in` entry sets `SupportsHotReload=true` in the `Directory.Build.props` of the scenario, not
as a global property. A global property would override the package props, so it would not test that
the package props preserve the value set by the user.

The scenario does not depend on desktop MSBuild. It is in `Standalone` because it consumes the
locally built package in the same way as the other scenarios.
