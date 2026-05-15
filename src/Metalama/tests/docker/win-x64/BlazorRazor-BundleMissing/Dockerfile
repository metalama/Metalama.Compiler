# escape=`
FROM mcr.microsoft.com/windows/servercore:ltsc2025

# Hybrid-fallback Windows scenario: install BOTH the primary SDK (whose Razor
# analyzers are too new for our Roslyn) AND a compatible LKG SDK. Then strip
# the sdkAnalyzers/ bundle out of the local-feed Metalama.Compiler nupkg before
# project build. Validates the Windows fallback-backstop: when the bundle is
# absent for any reason, the installed-SDK scan finds the LKG and the build
# succeeds with a LAMA0617 warning instead of a LAMA0625 error.
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

SHELL ["cmd", "/S", "/C"]
ENTRYPOINT ["C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", "-NoProfile", "-File", "C:\\app\\test.ps1"]
