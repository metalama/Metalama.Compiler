# Repro for Metalama.Compiler issue #180

Reproduces https://github.com/metalama/Metalama.Compiler/issues/180 in a Linux
container. Not an `Assembly.LoadFrom` synthetic repro — it is an actual
compile-time repro driven by Metalama.Compiler's analyzer-assembly redirector
loading `sdkAnalyzers/Microsoft.CodeAnalysis.Razor.Compiler.dll`, which is
shipped as a Windows-only ReadyToRun image.

## Run

From a Windows host with WSL + Linux Docker available:

```powershell
wsl -d Ubuntu-24.04 -- bash -c "cd /mnt/c/src/Metalama-2026.0/Metalama.Compiler/eng-Metalama/repro/Issue180 && \
    docker build -t metalama-issue180 . && \
    docker run --rm metalama-issue180"
```

## What you should see

A `BadImageFormatException` for
`metalama.compiler/2026.0.12/sdkAnalyzers/Microsoft.CodeAnalysis.Razor.Compiler.dll`,
preceded by `LAMA0617` (analyzer downgrade) and followed by `CS8034`
(analyzer load failure).
