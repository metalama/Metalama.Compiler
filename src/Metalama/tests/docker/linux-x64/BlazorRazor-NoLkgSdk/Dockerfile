FROM ubuntu:24.04

# Negative test for issue #180: install ONLY the primary SDK (whose Razor analyzers Metalama
# cannot use), no side-by-side LKG. The build MUST fail with LAMA0625 ERR_NoCompatibleSdkForAnalyzer
# (NOT a silent analyzer-disable that cascades into opaque CS0246 / CS0115 errors).
ARG DOTNET_PRIMARY=10.0.300

RUN apt-get update && apt-get install -y --no-install-recommends \
        curl ca-certificates libicu74 \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /opt/dotnet \
    && curl -sSL "https://builds.dotnet.microsoft.com/dotnet/Sdk/${DOTNET_PRIMARY}/dotnet-sdk-${DOTNET_PRIMARY}-linux-x64.tar.gz" -o /tmp/dotnet.tar.gz \
    && tar -xzf /tmp/dotnet.tar.gz -C /opt/dotnet \
    && rm /tmp/dotnet.tar.gz

ENV PATH="/opt/dotnet:${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1

WORKDIR /app
COPY src/ /app/
COPY local-feed/ /local-feed/

CMD ["bash", "-lc", "\
    set -o pipefail; \
    dotnet --info; \
    echo '--- restore ---'; \
    dotnet restore BlazorRazor.csproj; \
    echo '--- build (expected to fail with LAMA0625) ---'; \
    LOG=$(mktemp); \
    set +e; \
    dotnet build BlazorRazor.csproj --no-restore -c Debug 2>&1 | tee $LOG; \
    BUILD_RC=${PIPESTATUS[0]}; \
    set -e; \
    if grep -q 'error LAMA0625' $LOG; then echo 'OK: BlazorRazor-NoLkgSdk produced expected LAMA0625 error'; exit 0; fi; \
    echo 'FAIL: expected error LAMA0625 was not emitted (BUILD_RC=' $BUILD_RC ')'; exit 1"]
