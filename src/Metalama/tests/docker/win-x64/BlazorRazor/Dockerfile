# escape=`
FROM mcr.microsoft.com/windows/servercore:ltsc2025

# Hybrid-bundle Windows scenario: install ONLY the primary SDK. SDK 10.0.300 ships
# Razor analyzers built for Roslyn 5.5 (newer than Metalama Compiler's bundled
# Roslyn 5.3) → triggers the redirect. With no compatible LKG SDK installed, the
# only way the build can succeed is via the bundled sdkAnalyzers/ folder shipped
# inside the Metalama.Compiler nupkg. This validates the Windows promise: the
# bundle keeps customers green even when their only installed SDK is too new.
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

SHELL ["cmd", "/S", "/C"]
ENTRYPOINT ["C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", "-NoProfile", "-File", "C:\\app\\test.ps1"]
