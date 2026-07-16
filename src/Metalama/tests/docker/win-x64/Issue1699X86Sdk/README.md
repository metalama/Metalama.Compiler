# Regression test for Metalama.Compiler issue #1699

Reproduces
[metalama/Metalama#1699](https://github.com/metalama/Metalama/issues/1699):

> The task factory "RoslynCodeTaskFactory" could not be loaded from the assembly
> `C:\Program Files %28x86%29\...\Microsoft.Build.Tasks.Core.dll` (MSB4175)

## Root cause

`Metalama.Compiler.targets` (added in #180) declares a `RoslynCodeTaskFactory`
`UsingTask` whose `AssemblyFile` is computed with an MSBuild **property function**:

```xml
AssemblyFile="$([System.IO.Path]::Combine('$(MSBuildToolsPath)', 'Microsoft.Build.Tasks.Core.dll'))"
```

Property functions escape the MSBuild-special characters `(` and `)` in their
result to `%28`/`%29`, and a `TaskFactory`'s `AssemblyFile` is consumed **without**
unescaping. So when `$(MSBuildToolsPath)` contains parentheses the literal
`Program Files %28x86%29` reaches assembly loading and the build fails.

It only bites when the tools path has parentheses — i.e. full-framework MSBuild
or an **x86 .NET SDK** under `C:\Program Files (x86)\...`. `dotnet build` on an
x64 SDK (what the CI matrix uses) never has parentheses, which is why this was
not caught before. The customer hit it via `VSBuild@1` (VS Build Tools 2026).

## What this scenario does

Installs the **x86** .NET SDK into `C:\Program Files (x86)\dotnet` so a plain
`dotnet build` sees a paren-containing `$(MSBuildToolsPath)`, then builds a
trivial class library that references the locally-built `Metalama.Compiler`
package. `test.ps1` first asserts the precondition (`MSBuildToolsPath` really
contains `(x86)`), then fails on `MSB4175` or any `%28`/`%29` in the build log.

Red against the buggy targets; green once `AssemblyFile` is wrapped in
`$([MSBuild]::Unescape(...))`.

## Run

Build the local packages first, then run just this scenario:

```powershell
# from the repo root
.\Build.ps1 build -c Debug
.\src\Metalama\tests\docker\DockerTests.ps1 -Platform win-x64 -Scenario Issue1699X86Sdk
```
