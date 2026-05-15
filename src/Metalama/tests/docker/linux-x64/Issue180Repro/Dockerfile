FROM ubuntu:24.04

ARG DOTNET_VERSION=10.0.300

RUN apt-get update && apt-get install -y --no-install-recommends \
        curl ca-certificates libicu74 \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /opt/dotnet \
    && curl -sSL "https://builds.dotnet.microsoft.com/dotnet/Sdk/${DOTNET_VERSION}/dotnet-sdk-${DOTNET_VERSION}-linux-x64.tar.gz" -o /tmp/dotnet.tar.gz \
    && tar -xzf /tmp/dotnet.tar.gz -C /opt/dotnet \
    && rm /tmp/dotnet.tar.gz

ENV PATH="/opt/dotnet:${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1
ENV DOTNET_GENERATE_ASPNET_CERTIFICATE=0

RUN dotnet --info

WORKDIR /app
COPY src/ /app/
COPY test.sh /app/test.sh

# Strip CR characters in case the script was checked out with CRLF on Windows.
RUN sed -i 's/\r$//' /app/test.sh

CMD ["bash", "/app/test.sh"]
