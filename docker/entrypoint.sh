#!/bin/sh
set -e

# Flat flags override the equivalent ASP.NET Core configuration variables.
NATIVE_MODE="$(printf '%s' "${VCLIENT_NATIVE_CONNECTION:-${Vcontrol__UseNativeConnection:-false}}" | tr '[:upper:]' '[:lower:]' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
DIRECT_MODE="$(printf '%s' "${VCLIENT_DIRECT_CONNECTION:-${Vcontrol__UseDirectConnection:-false}}" | tr '[:upper:]' '[:lower:]' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
case "$NATIVE_MODE" in
  true)
    if [ "$DIRECT_MODE" = "true" ]; then
      echo "VCLIENT_NATIVE_CONNECTION and VCLIENT_DIRECT_CONNECTION cannot both be enabled." >&2
      exit 2
    fi
    echo "Starting experimental native Optolink worker; vcontrold will not be started."
    exec /app/worker/vcontrol-worker
    ;;
  false) ;;
  *)
    echo "VCLIENT_NATIVE_CONNECTION must be 'true' or 'false'." >&2
    exit 2
    ;;
esac

# Configure device path in vcontrold.xml
DEVICE_PATH="${OPTOLINK_DEVICE:-/dev/ttyUSB0}"
sed -i "s#/dev/ttyUSB0#${DEVICE_PATH}#" /etc/vcontrold/vcontrold.xml

echo "Starting vcontrold on device ${DEVICE_PATH} (port ${VCONTROLD_PORT:-3002})"
echo "Use 'docker logs' to view output and 'docker exec -it <container> bash' to run commands."
echo "Example: vclient -h 127.0.0.1 -p ${VCONTROLD_PORT:-3002}"

# Run vcontrold in foreground (-n). Allocate a pseudo-TTY via 'script' so output
# appears in docker logs even without an attached TTY.
# Start vcontrold in background with pseudo-TTY so logs appear in docker logs
script -q -c "vcontrold -n -x /etc/vcontrold/vcontrold.xml" /dev/null &
VCONTROLD_PID=$!
echo "vcontrold started (PID ${VCONTROLD_PID})"

# Run .NET worker in foreground to keep container alive and emit periodic readings
exec /app/worker/vcontrol-worker
