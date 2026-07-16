# escape=`
FROM mcr.microsoft.com/windows/servercore:ltsc2025

# Regression test for Metalama.Compiler issue #1699.
#
# Metalama.Compiler.targets declares a RoslynCodeTaskFactory UsingTask whose
# AssemblyFile is computed with a property function ($([System.IO.Path]::Combine(...)))
# over $(MSBuildToolsPath). Property functions escape the MSBuild-special chars
# '(' and ')' to '%28'/'%29', and a TaskFactory's AssemblyFile is consumed WITHOUT
# unescaping. So when the tools path contains parentheses the literal
# 'Program Files %28x86%29' reaches assembly loading and the build fails with
# MSB4175 ("could not be loaded from the assembly ...").
#
# The customer hit this via VS Build Tools MSBuild under 'C:\Program Files (x86)'.
# The simplest deterministic reproduction is the x86 .NET SDK, which installs
# under 'C:\Program Files (x86)\dotnet', so a plain 'dotnet build' sees a
# paren-containing $(MSBuildToolsPath).
ARG DOTNET_PRIMARY=10.0.301

SHELL ["powershell", "-NoProfile", "-Command"]

# Install the x86 SDK into the parenthesised Program Files (x86) path on purpose.
RUN $ErrorActionPreference = 'Stop'; `
    Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1; `
    & .\dotnet-install.ps1 -Version $env:DOTNET_PRIMARY -Architecture x86 -InstallDir 'C:\Program Files (x86)\dotnet'; `
    Remove-Item dotnet-install.ps1

ENV PATH="C:\Program Files (x86)\dotnet;${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1
ENV DOTNET_ROLL_FORWARD=latestMajor

WORKDIR C:/app
COPY src/ C:/app/
COPY local-feed/ C:/local-feed/
COPY test.ps1 C:/app/test.ps1

SHELL ["cmd", "/S", "/C"]
ENTRYPOINT ["C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", "-NoProfile", "-File", "C:\\app\\test.ps1"]
