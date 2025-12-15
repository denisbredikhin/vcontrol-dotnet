#!/bin/sh
set -e

DEVICE_PATH="${OPTOLINK_DEVICE:-/dev/ttyUSB0}"

sed -i "s#/dev/ttyUSB0#${DEVICE_PATH}#" /etc/vcontrold/vcontrold.xml

vcontrold -n -x /etc/vcontrold/vcontrold.xml &

VCONTROLD_PID=$!

echo "vcontrold started with PID ${VCONTROLD_PID} on device ${DEVICE_PATH}"
echo "You can connect from within the container using:"
echo "  vclient -h 127.0.0.1 -p ${VCONTROLD_PORT:-3002}"

if [ -x "/bin/bash" ]; then
	export SHELL=/bin/bash
fi
exec "${SHELL:-/bin/sh}"
