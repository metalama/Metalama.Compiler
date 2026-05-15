# escape=`
FROM mcr.microsoft.com/windows/servercore:ltsc2025

# Primary 10.0.300 + LKG 10.0.203 side-by-side. The .NET Framework MSBuild + Metalama.Compiler's
# net472 csc.exe task host will load analyzers; if SN handling is wrong on .NET Framework, this
# scenario surfaces it (.NET Core's analyzer loader doesn't enforce SN). net48 targeting comes via
# the Microsoft.NETFramework.ReferenceAssemblies.net48 NuGet, no .NET Framework SDK image needed.
ARG DOTNET_PRIMARY=10.0.300
ARG DOTNET_LKG=10.0.203

SHELL ["powershell", "-NoProfile", "-Command"]

RUN $ErrorActionPreference = 'Stop'; `
    Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1; `
    & .\dotnet-install.ps1 -Version $env:DOTNET_PRIMARY -InstallDir 'C:\Program Files\dotnet'; `
    & .\dotnet-install.ps1 -Version $env:DOTNET_LKG -InstallDir 'C:\Program Files\dotnet'; `
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
