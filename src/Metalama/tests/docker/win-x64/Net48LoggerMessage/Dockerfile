# escape=`
FROM mcr.microsoft.com/windows/servercore:ltsc2025

# Hybrid-bundle Windows scenario: install ONLY the primary SDK, no LKG side-by-side.
# This is the core Windows promise of the hybrid approach — even when the only SDK
# on the host carries analyzers that reference a Roslyn newer than ours (because
# e.g. VS Update replaced the previous SDK), the bundled sdkAnalyzers/ folder in
# Metalama.Compiler's nupkg supplies a compatible older copy that the net472 csc
# loads directly. .NET Framework targeting comes via the
# Microsoft.NETFramework.ReferenceAssemblies.net48 NuGet, no .NET Framework SDK
# image needed.
ARG DOTNET_PRIMARY=10.0.300

SHELL ["powershell", "-NoProfile", "-Command"]

RUN $ErrorActionPreference = 'Stop'; `
    Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1; `
    & .\dotnet-install.ps1 -Version $env:DOTNET_PRIMARY -InstallDir 'C:\Program Files\dotnet'; `
    Remove-Item dotnet-install.ps1

ENV PATH="C:\Program Files\dotnet;${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1

WORKDIR C:/app
COPY src/ C:/app/
COPY local-feed/ C:/local-feed/
COPY test.ps1 C:/app/test.ps1

# Reset SHELL so the ENTRYPOINT exec-form below isn't wrapped by the docker CMD shell.
# Use full path to powershell.exe — exec-form doesn't go through PATH resolution.
SHELL ["cmd", "/S", "/C"]
ENTRYPOINT ["C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", "-NoProfile", "-File", "C:\\app\\test.ps1"]
