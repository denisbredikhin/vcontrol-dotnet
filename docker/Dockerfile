FROM mcr.microsoft.com/dotnet/sdk:10.0 AS worker-builder
ARG TARGETARCH
ARG VERSION
ARG ASSEMBLY_VERSION
ARG FILE_VERSION
ARG INFO_VERSION
WORKDIR /src
COPY src/Vcontrol.Worker/ ./Vcontrol.Worker/
RUN dotnet restore ./Vcontrol.Worker/Vcontrol.Worker.csproj
RUN if [ "$TARGETARCH" = "amd64" ]; then RID=linux-x64; \
    elif [ "$TARGETARCH" = "arm64" ]; then RID=linux-arm64; \
    else RID=linux-x64; fi; \
        dotnet publish ./Vcontrol.Worker/Vcontrol.Worker.csproj -c Release -r $RID --self-contained true -p:PublishSingleFile=true \
            -p:Version="${VERSION}" -p:AssemblyVersion="${ASSEMBLY_VERSION:-$VERSION}" -p:FileVersion="${FILE_VERSION:-$VERSION}" -p:InformationalVersion="${INFO_VERSION:-$VERSION}" \
            -o /out

FROM debian:stable-slim

ENV OPTOLINK_DEVICE="/dev/ttyUSB0" \
    VCONTROLD_PORT=3002
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
ENV MQTT_HOST="" \
    MQTT_PORT=1883 \
    MQTT_USER="" \
    MQTT_PASSWORD="" \
    MQTT_TOPIC="" \
    COMMANDS="" \
    POLL_SECONDS=60 \
    PUBLISH_VALUE_ONLY=""

RUN apt-get update \
        && apt-get install -y --no-install-recommends \
                 build-essential git pkg-config libxml2-dev libtool autoconf automake cmake bash bsdutils \
             netcat-openbsd curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt

RUN git config --global http.sslVerify false \
    && git clone https://github.com/openv/vcontrold.git \
    && cd vcontrold \
    && cmake -S . -B build -DCMAKE_BUILD_TYPE=Release -DMANPAGES=OFF \
    && cmake --build build --config Release -j$(nproc) \
    && cmake --install build \
    && git config --global --unset http.sslVerify

WORKDIR /app

COPY --from=worker-builder /out /app/worker

COPY docker/vcontrold.xml /etc/vcontrold/vcontrold.xml
COPY docker/vito.xml /etc/vcontrold/vito.xml
COPY docker/entrypoint.sh /entrypoint.sh

RUN sed -i 's/\r$//' /entrypoint.sh \
    && chmod +x /entrypoint.sh

ENV ASPNETCORE_URLS=http://0.0.0.0:8080

EXPOSE ${VCONTROLD_PORT}
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["/entrypoint.sh"]
