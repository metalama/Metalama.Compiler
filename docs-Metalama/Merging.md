# Merging from new Roslyn branches

## Source selection policy

The merge source must always be a **specific named ref that corresponds to a released Roslyn version** — either a published release branch or a GA tag. **Never merge from an arbitrary commit SHA** (e.g. picked from `main`), even if the package description for a Roslyn release points to such a commit: that commit may be on `main` and may include unreleased changes that follow it on the same branch.

Acceptable sources:

- A published release branch on `dotnet/roslyn` (e.g. `upstream/release/dev18.0`, `upstream/release/dev18.3`). Merge from the **branch tip**, which includes the latest servicing fixes for that release line.
- A **GA tag** of the form `Visual-Studio-2026-Version-<X.Y[.Z]>` (e.g. `Visual-Studio-2026-Version-18.5.2`). Always pick the **latest non-preview tag** in the desired version line — e.g. for Roslyn 5.5 take `Visual-Studio-2026-Version-18.5.2`, not `Visual-Studio-2026-Version-18.5`.

Not acceptable:

- `upstream/main` or any other development branch — may contain unreleased commits.
- `*-Preview-*` tags (e.g. `Visual-Studio-2026-Version-18.6-Preview-3`) — not GA.
- A bare commit SHA, even one referenced from a published Roslyn package's description.

If the desired Roslyn version exists only on `main` (no public release branch, no GA tag), it is **not eligible** — wait for the GA tag to be published.

### Distinguishing GA tags from preview tags

`dotnet/roslyn` uses a consistent naming convention for VS 2026 tags:

- **GA**: `Visual-Studio-2026-Version-<X.Y>` or `Visual-Studio-2026-Version-<X.Y.Z>` — e.g. `Visual-Studio-2026-Version-18.5`, `Visual-Studio-2026-Version-18.5.2`.
- **Preview**: `Visual-Studio-2026-Version-<X.Y>-Preview-<N>` — e.g. `Visual-Studio-2026-Version-18.6-Preview-3`.

If a version line has only `-Preview-N` tags and no bare `<X.Y>` tag, that version has not yet GA'd and is not eligible for merging.

**Do not rely on `eng/Versions.props` to identify GA vs preview** — the `<PreReleaseVersionLabel>` field is a NuGet prerelease label used during development and is set on both GA and preview tags. The tag name is the reliable signal.

To double-check before merging, open the tag on GitHub (e.g. https://github.com/dotnet/roslyn/releases/tag/Visual-Studio-2026-Version-18.5.2). A GA release shows "Latest release" / release notes; a preview is marked "Pre-release".

## NuGet package sources

Metalama.Compiler restores all non-nuget.org packages (dependencies of old Roslyn versions, VS SDK packages, etc.) from the **`roslyn-consolidated`** feed on `proget.postsharp.net`. This feed is a **mirroring proxy**: the first time a package is requested, ProGet fetches it from the upstream feed and caches it permanently, so dependencies of old Roslyn versions are never lost even after upstream removes them.

Because the proxy caches automatically, there is **no manual backup or push step** when merging a new Roslyn version — simply restoring/building the merged code populates the mirror. The original upstream feeds (`dotnet-eng`, `dotnet-tools`, `dotnet6`, `vs-impl`, etc.) stay commented out in `nuget.config` and must not be re-enabled.

## 1. Find the source Roslyn version and branch

Check the versions of Microsoft.Net.Compilers.Toolset NuGet package. In the descrption of each version, you can find the commit from which the version was built. The commit then corresponds to a certain branch.

Alternatively, you can find the branch at https://github.com/dotnet/roslyn/releases. Several releases may share the same branch and version there.

When merging before the package has been published, you can find the commit in the product version of e.g. C:\Program Files\dotnet\sdk\\\<version>\Roslyn\bincore\Microsoft.CodeAnalysis.dll.

Examples:
version 3.8.0, commit https://github.com/dotnet/roslyn/commit/8de9e4b2beba5b7c0edd6f1e6a4f192a51fdc872, branch release/dev16.8-vs-deps
version 3.11.0, commit https://github.com/dotnet/roslyn/commit/ae1fff344d46976624e68ae17164e0607ab68b10, branch release/dev16.11-vs-deps
version 5.5.0, tag https://github.com/dotnet/roslyn/releases/tag/Visual-Studio-2026-Version-18.5.2

## 2. Merge the selected Roslyn branch to Metalama.Compiler repo

See Modifications.md to better understand the changes done for Metalama.

## 3. Update eng\Versions.props

Set RoslynVersion to the source Roslyn version.

## 4. Regenerate generated source files

See Modifications.md for details.

## 5. Make sure all test are green

To run Metalama.Compiler tests, execute `b test`.
To run all Roslyn tests, execute `b test -p TestAll`.

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