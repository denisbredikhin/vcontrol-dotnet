#!/bin/sh
set -e

# Configure device path in vcontrold.xml
DEVICE_PATH="${OPTOLINK_DEVICE:-/dev/ttyUSB0}"
sed -i "s#/dev/ttyUSB0#${DEVICE_PATH}#" /etc/vcontrold/vcontrold.xml

echo "Starting vcontrold on device ${DEVICE_PATH} (port ${VCONTROLD_PORT:-3002})"
echo "Use 'docker logs' to view output and 'docker exec -it <container> bash' to run commands."
echo "Example: vclient -h 127.0.0.1 -p ${VCONTROLD_PORT:-3002}"

# Run vcontrold in foreground (-n). Allocate a pseudo-TTY via 'script' so output
# appears in docker logs even without an attached TTY.
exec script -q -c "vcontrold -n -x /etc/vcontrold/vcontrold.xml" /dev/null

