FROM ubuntu:24.04

# Primary SDK: 10.0.300 ships Razor analyzers built for Roslyn 5.5 (newer than Metalama Compiler's
# bundled Roslyn 5.3) → triggers the redirect. LKG SDK: 10.0.108 in the same major lane, still
# Roslyn 5.3 → the redirect target. Installed side-by-side under /opt/dotnet.
ARG DOTNET_PRIMARY=10.0.300
ARG DOTNET_LKG=10.0.203

RUN apt-get update && apt-get install -y --no-install-recommends \
        curl ca-certificates libicu74 \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /opt/dotnet \
    && curl -sSL "https://builds.dotnet.microsoft.com/dotnet/Sdk/${DOTNET_PRIMARY}/dotnet-sdk-${DOTNET_PRIMARY}-linux-x64.tar.gz" -o /tmp/dotnet.tar.gz \
    && tar -xzf /tmp/dotnet.tar.gz -C /opt/dotnet \
    && rm /tmp/dotnet.tar.gz \
    && curl -sSL "https://builds.dotnet.microsoft.com/dotnet/Sdk/${DOTNET_LKG}/dotnet-sdk-${DOTNET_LKG}-linux-x64.tar.gz" -o /tmp/dotnet.tar.gz \
    && tar -xzf /tmp/dotnet.tar.gz -C /opt/dotnet \
    && rm /tmp/dotnet.tar.gz

ENV PATH="/opt/dotnet:${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1

WORKDIR /app
COPY src/ /app/
COPY local-feed/ /local-feed/

# Issue-#180 regression sentinels: build must succeed, must not emit CS8034 / BadImageFormatException,
# and (with the new design) must not emit LAMA0625 ERR_NoCompatibleSdkForAnalyzer.
# LAMA0617 (analyzer redirected) IS expected and benign — its presence proves the new fallback fired.
CMD ["bash", "-lc", "\
    set -eo pipefail; \
    dotnet --info; \
    echo '--- restore ---'; \
    dotnet restore BlazorRazor.csproj; \
    echo '--- build ---'; \
    LOG=$(mktemp); \
    dotnet build BlazorRazor.csproj --no-restore -c Debug 2>&1 | tee $LOG; \
    BUILD_RC=${PIPESTATUS[0]}; \
    if [ $BUILD_RC -ne 0 ]; then echo \"BUILD FAILED (rc=$BUILD_RC)\"; exit $BUILD_RC; fi; \
    if grep -q 'BadImageFormatException' $LOG; then echo 'FAIL: BadImageFormatException found in build output'; exit 1; fi; \
    if grep -q 'warning CS8034' $LOG; then echo 'FAIL: CS8034 analyzer load failure'; exit 1; fi; \
    if grep -q 'error LAMA0625' $LOG; then echo 'FAIL: LAMA0625 ERR_NoCompatibleSdkForAnalyzer'; exit 1; fi; \
    if ! grep -q 'warning LAMA0617' $LOG; then echo 'WARN: expected LAMA0617 redirect notice but none found'; fi; \
    echo 'OK: BlazorRazor scenario passed'"]
