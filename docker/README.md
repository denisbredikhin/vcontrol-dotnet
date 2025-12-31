# vcontrold Docker image

This folder contains a minimal Docker setup to run `vcontrold` inside a container
with a configurable Optolink device and the ability to execute `vclient`/`vcontrol`
commands manually inside the running container.

## Build image

From the repository root:

```powershell
cd docker
docker build -t vcontrol-dev .
```

## Run container (Windows PowerShell example)

Replace `COM3` with your real USB device on Windows and `/dev/ttyUSB0` with
the corresponding path inside WSL/Linux if needed:

```powershell
docker run --rm -it `
  --device "COM3" `
  -e OPTOLINK_DEVICE="/dev/ttyUSB0" `
  -p 3002:3002 `
  vcontrol-dev
```

Inside the container you can run:

```sh
vclient -h 127.0.0.1 -p 3002
```

and execute commands defined in `vito.xml`.

## .NET worker + MQTT

The container also runs a .NET worker that can periodically call `vclient` and, if configured, publish results to MQTT.

- Configure MQTT via envs:
  - `MQTT_HOST`, `MQTT_PORT`, `MQTT_USER`, `MQTT_PASSWORD`, `MQTT_TOPIC`
- Set a batch of commands via required `COMMANDS` (comma-separated).
- Control polling interval with `POLL_SECONDS` (default: 60).
- Control payload content with `PUBLISH_VALUE_ONLY` (set to `true` or `1` to publish only the numeric value; default publishes full JSON).

Example:

```powershell
docker run --rm -it `
  --device "COM3" `
  -e OPTOLINK_DEVICE="/dev/ttyUSB0" `
  -e MQTT_HOST="192.168.1.10" `
  -e MQTT_PORT=1883 `
  -e MQTT_USER="user" `
  -e MQTT_PASSWORD="pass" `
  -e MQTT_TOPIC="home/viessmann" `
  -e COMMANDS="getTempA,getTempB" `
  -e PUBLISH_VALUE_ONLY=true `
  -e POLL_SECONDS=30 `
  -p 3002:3002 `
  vcontrol-dev
```

- `COMMANDS` is required. The worker runs all commands in a single `vclient` call, splits JSON output, and publishes each to `MQTT_TOPIC/<command>`. If `COMMANDS` is missing, the service exits with an error.
