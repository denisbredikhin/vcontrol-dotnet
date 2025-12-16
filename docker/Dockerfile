FROM debian:stable-slim

ENV OPTOLINK_DEVICE="/dev/ttyUSB0" \
    VCONTROLD_PORT=3002

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

COPY vcontrold.xml /etc/vcontrold/vcontrold.xml
COPY vito.xml /etc/vcontrold/vito.xml
COPY entrypoint.sh /entrypoint.sh

RUN sed -i 's/\r$//' /entrypoint.sh \
    && chmod +x /entrypoint.sh

EXPOSE ${VCONTROLD_PORT}

ENTRYPOINT ["/entrypoint.sh"]
