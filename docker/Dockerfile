FROM mcr.microsoft.com/dotnet/sdk:10.0 AS worker-builder
ARG TARGETARCH
WORKDIR /src
COPY src/Vcontrol.Worker/ ./Vcontrol.Worker/
RUN dotnet restore ./Vcontrol.Worker/Vcontrol.Worker.csproj
RUN if [ "$TARGETARCH" = "amd64" ]; then RID=linux-x64; \
    elif [ "$TARGETARCH" = "arm64" ]; then RID=linux-arm64; \
    else RID=linux-x64; fi; \
    dotnet publish ./Vcontrol.Worker/Vcontrol.Worker.csproj -c Release -r $RID --self-contained true -p:PublishSingleFile=true -o /out

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
             netcat-openbsd \
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

EXPOSE ${VCONTROLD_PORT}

ENTRYPOINT ["/entrypoint.sh"]
