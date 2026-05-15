FROM ubuntu:24.04

# Primary 10.0.300 + LKG 10.0.203 side-by-side: primary's Logging.Generators may reference a Roslyn
# newer than Metalama Compiler's bundled one, triggering the redirect to the LKG SDK.
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

CMD ["bash", "-lc", "\
    set -eo pipefail; \
    dotnet --info; \
    echo '--- restore ---'; \
    dotnet restore LoggerMessage.csproj; \
    echo '--- build ---'; \
    LOG=$(mktemp); \
    dotnet build LoggerMessage.csproj --no-restore -c Debug 2>&1 | tee $LOG; \
    BUILD_RC=${PIPESTATUS[0]}; \
    if [ $BUILD_RC -ne 0 ]; then echo 'BUILD FAILED'; exit $BUILD_RC; fi; \
    if grep -q 'BadImageFormatException' $LOG; then echo 'FAIL: BadImageFormatException'; exit 1; fi; \
    if grep -q 'warning CS8034' $LOG; then echo 'FAIL: CS8034 analyzer load failure'; exit 1; fi; \
    echo '--- run ---'; \
    OUT=$(dotnet run --no-build --project LoggerMessage.csproj 2>&1); \
    echo \"$OUT\"; \
    if ! echo \"$OUT\" | grep -q 'WidgetCount: 42'; then echo 'FAIL: expected log line missing (LoggerMessage source generator did not emit code)'; exit 1; fi; \
    echo 'OK: LoggerMessage scenario passed'"]
