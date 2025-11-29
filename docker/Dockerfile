FROM debian:stable-slim

ENV OPTOLINK_DEVICE="/dev/ttyUSB0" \
    VCONTROLD_PORT=3002

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       build-essential git pkg-config libxml2-dev libtool autoconf automake \
       netcat-openbsd \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt

RUN git clone https://github.com/openv/vcontrold.git \
    && cd vcontrold \
    && ./autogen.sh \
    && ./configure \
    && make \
    && make install

WORKDIR /app

COPY vcontrold.xml /etc/vcontrold/vcontrold.xml
COPY vito.xml /etc/vcontrold/vito.xml
COPY entrypoint.sh /entrypoint.sh

RUN chmod +x /entrypoint.sh

EXPOSE ${VCONTROLD_PORT}

ENTRYPOINT ["/entrypoint.sh"]
