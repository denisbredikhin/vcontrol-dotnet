#!/bin/sh
set -eu

ROOT="$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)"
TEMP="$(mktemp -d)"
trap 'rm -f "$TEMP/entrypoint.sh" "$TEMP/worker" "$TEMP/vcontrold.xml" "$TEMP/output"; rmdir "$TEMP"' EXIT

# Change only external deployment paths, leaving the startup decisions intact.
sed -e "s#/app/worker/vcontrol-worker#$TEMP/worker#g" \
    -e "s#/etc/vcontrold/vcontrold.xml#$TEMP/vcontrold.xml#g" \
    "$ROOT/docker/entrypoint.sh" > "$TEMP/entrypoint.sh"
printf '#!/bin/sh\necho WORKER_STARTED\n' > "$TEMP/worker"
chmod +x "$TEMP/worker"

assert_native() {
    native_mode="$1"
    env VCLIENT_NATIVE_CONNECTION="$native_mode" VCLIENT_DIRECT_CONNECTION=false \
        sh "$TEMP/entrypoint.sh" > "$TEMP/output" 2>&1
    grep -q WORKER_STARTED "$TEMP/output"
    grep -q 'vcontrold will not be started' "$TEMP/output"
    if grep -q 'vcontrold started (PID' "$TEMP/output"; then
        echo "Native startup launched a daemon." >&2
        exit 1
    fi
}

assert_native true
assert_native ' TRUE '
env VCLIENT_NATIVE_CONNECTION='' Vcontrol__UseNativeConnection=true \
    VCLIENT_DIRECT_CONNECTION=false sh "$TEMP/entrypoint.sh" > "$TEMP/output" 2>&1
grep -q 'vcontrold will not be started' "$TEMP/output"
grep -q WORKER_STARTED "$TEMP/output"

if env VCLIENT_NATIVE_CONNECTION=true VCLIENT_DIRECT_CONNECTION=true \
    sh "$TEMP/entrypoint.sh" > "$TEMP/output" 2>&1; then
    echo "Conflicting flags unexpectedly succeeded." >&2
    exit 1
else
    test "$?" -eq 2
fi
grep -q 'cannot both be enabled' "$TEMP/output"
if grep -q WORKER_STARTED "$TEMP/output"; then exit 1; fi

if env VCLIENT_NATIVE_CONNECTION=invalid VCLIENT_DIRECT_CONNECTION=false \
    sh "$TEMP/entrypoint.sh" > "$TEMP/output" 2>&1; then
    echo "Invalid native flag unexpectedly succeeded." >&2
    exit 1
else
    test "$?" -eq 2
fi
grep -q "must be 'true' or 'false'" "$TEMP/output"
if grep -q WORKER_STARTED "$TEMP/output"; then exit 1; fi

echo "Passed: 5 entrypoint checks; Failed: 0"
