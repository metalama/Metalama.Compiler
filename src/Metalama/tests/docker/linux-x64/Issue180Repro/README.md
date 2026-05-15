# Repro for Metalama.Compiler issue #180

Reproduces https://github.com/metalama/Metalama.Compiler/issues/180 in a Linux
container, driven by Metalama.Compiler's analyzer-assembly redirector loading
`sdkAnalyzers/Microsoft.CodeAnalysis.Razor.Compiler.dll`, which the
deployed-2026.0.12 package ships as a Windows-only ReadyToRun image.

Unlike the other docker scenarios in this tree, this one pulls the deployed
`Metalama.Compiler 2026.0.12` from nuget.org rather than the local build —
it demonstrates the *bug*, not the fix.

## Run

From a Windows host with WSL + Linux Docker available:

```powershell
wsl -d Ubuntu-24.04 -- bash -c "cd /mnt/c/src/Metalama-2026.0/Metalama.Compiler/src/Metalama/tests/docker/linux-x64/Issue180Repro && \
    docker build -t metalama-issue180-repro . && \
    docker run --rm metalama-issue180-repro"
```

## What you should see

A `BadImageFormatException` for
`metalama.compiler/2026.0.12/sdkAnalyzers/Microsoft.CodeAnalysis.Razor.Compiler.dll`,
preceded by `LAMA0617` (analyzer downgrade) and followed by `CS8034`
(analyzer load failure).
