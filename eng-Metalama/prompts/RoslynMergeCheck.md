# Nightly Roslyn merge check

This is the instruction set for the `RoslynMergeCheck` TeamCity build configuration, which runs unattended
every night. Follow it exactly and stop at the first step whose answer is "nothing to do".

Read `docs-Metalama/Merging.md` first. It is the authority on which upstream branch to merge and why; this
file only sequences the decision. Read `CLAUDE.md` too, and respect it *STRICTLY*.

## 1. Decide whether a merge is due

A merge is due when **the Roslyn version bundled in the latest GA .NET SDK is newer than the one this
repository bundles**. Nothing else — not new commits on the upstream branch, not a new Visual Studio release.

1. Read `<RoslynVersion>` from `eng/Versions.props`. That is what this repository bundles.
2. Fetch `https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json` and take the
   channel whose `support-phase` is `active`. Its `latest-sdk` is the current GA SDK.
3. Find the Roslyn version of that SDK. `eng-Metalama/DownloadNetSdkAnalyzers/net-sdk-releases.json` maps SDK
   versions to Roslyn versions, but it lists only the primary SDK of each release, so it may not have the one
   in question. When it does not, run the tool, which downloads and reads the SDK itself:

   ```powershell
   dotnet run --project eng-Metalama\DownloadNetSdkAnalyzers <RoslynVersion> -sdk-version
   ```

   That prints the newest SDK whose Roslyn is at most `<RoslynVersion>`. If it prints the current GA SDK, the
   bundled Roslyn is current.
4. Compare feature bands carefully. Different bands of the same .NET version carry very different Roslyn
   versions — SDK 10.0.111 carries Roslyn 5.0, SDK 10.0.400 carries 5.9. Compare against the GA band, not the
   newest patch of an older band.

**If the bundled Roslyn is not older than the GA SDK's, stop.** Report "no merge due", with both versions.
This is the expected outcome on almost every run.

## 2. Check whether the merge is already in flight

Do not open a second pull request for a merge that someone, or a previous run of this job, already started.

```bash
gh pr list --repo metalama/Metalama.Compiler --state open --json number,title,headRefName,createdAt
```

Treat as "already in flight" any open pull request whose head branch matches `topic/*/*merge-roslyn*` or whose
title mentions merging upstream Roslyn. Also check for an unmerged branch without a pull request:

```bash
git ls-remote --heads origin "refs/heads/topic/*merge-roslyn*"
```

**If one exists, stop.** Report its number and title, and do not touch it.

## 3. Perform the merge and open the pull request

Only reach this step when a merge is due and nothing is in flight.

Follow `docs-Metalama/Merging.md` from step 1 through step 6, and the repository's pull request conventions.
In particular:

- Merge from the **branch tip** of the upstream branch that produces the GA SDK's Roslyn version, never from
  an arbitrary commit.
- Resolve conflicts according to the table in `Merging.md` §2. Every Metalama change to a Roslyn file is
  delimited by `<Metalama>` markers and states why it diverges; preserve them, and mark new divergences the
  same way with a justification.
- Update `RoslynVersion`, regenerate generated sources, and match the build agent SDK to `global.json`.
- Target `develop/2026.1`, and follow the repository's pull request conventions for linking and assignment.

### Milestone

The pull request must carry a milestone, and the milestone for the *next* release usually does not exist yet.
Determine it rather than guessing:

1. Read the latest published version from nuget.org — that is what users have, and it is authoritative:

   ```bash
   curl -s https://api.nuget.org/v3-flatcontainer/metalama.compiler/index.json
   ```

   Take the highest **stable** version (ignore any with a pre-release suffix).
2. The milestone is the next version, incrementing the build number: `2026.1.15` published means the milestone
   is `2026.1.16`.
3. List the repository's milestones, including closed ones, and use the matching one if it is open:

   ```bash
   gh api "repos/metalama/Metalama.Compiler/milestones?state=all&per_page=100" --jq '.[] | "\(.number) \(.title) \(.state)"'
   ```

4. If no milestone with that title exists, create it and use it:

   ```bash
   gh api repos/metalama/Metalama.Compiler/milestones -f title=<version>
   ```

   Never attach the pull request to a **closed** milestone. If the computed title exists but is closed, that
   means the release already happened and nuget.org has not caught up — increment again and re-check rather
   than reopening it.

If the merge cannot be completed — genuine conflicts needing a product decision, or tests failing for reasons
that are not by design — **do not open a pull request with broken code**. Push the branch, open a draft pull
request describing precisely where it stopped and what is unresolved, and say so in the report.

## Reporting

End every run with a short statement of which of the three outcomes occurred: no merge due, already in
flight, or a pull request opened (with its number). That text is the build's summary for a human skimming it
the next morning.

Then emit `<promptly-done/>` as the very last line — including, and especially, when the answer was "no merge
due" after step 1. Without it `eng/RunClaude.ps1` treats the run as unfinished, resumes the session, and the
whole check runs a second time for nothing. Use `<promptly-blocked/>` only if genuinely unable to determine
the answer. See the completion contract in `CLAUDE.md`.
