# vcontrol-dotnet

Container image for integrating Viessmann boilers with MQTT using `vcontrold`. It runs `vcontrold` as a TCP service and a lightweight .NET worker that periodically reads configured boiler parameters and publishes them to your MQTT broker. In addition, it can subscribe to a dedicated command topic and execute incoming requests.

### Quick Run (Windows PowerShell)
```powershell
# Pull the public image
docker pull ghcr.io/denisbredikhin/vcontrol-dotnet:latest

# Run the container (adjust device mapping and envs)
# Example: host device appears as COM3; container uses /dev/ttyUSB0
docker run --rm -it `
  --device "COM3" `
  -e OPTOLINK_DEVICE="/dev/ttyUSB0" `
  -e VCONTROLD_HOST="127.0.0.1" `
  -e VCONTROLD_PORT="3002" `
  -e COMMANDS="get_temp,get_pressure" `
  -e MQTT_HOST="mqtt.local" `
  -e MQTT_PORT="1883" `
  -e MQTT_USER="user" `
  -e MQTT_PASSWORD="pass" `
  -e MQTT_TOPIC="vcontrol" `
  -e POLL_SECONDS="60" `
  -e PUBLISH_VALUE_ONLY="" `
  -e LOG_LEVEL="Information" `
  -p 3002:3002 `
  ghcr.io/denisbredikhin/vcontrol-dotnet:latest
```

### Docker Compose Example
```yaml
services:
  vcontrol:
    image: ghcr.io/denisbredikhin/vcontrol-dotnet:latest
    container_name: vcontrol-dotnet
    restart: unless-stopped
    environment:
      OPTOLINK_DEVICE: "/dev/ttyUSB0"
      VCONTROLD_HOST: "127.0.0.1"
      VCONTROLD_PORT: "3002"
      COMMANDS: "get_temp,get_pressure"
      MQTT_HOST: "mqtt.local"
      MQTT_PORT: "1883"
      MQTT_USER: "user"
      MQTT_PASSWORD: "pass"
      MQTT_TOPIC: "vcontrol"
      POLL_SECONDS: "60"
      PUBLISH_VALUE_ONLY: ""
      LOG_LEVEL: "Information"
    ports:
      - "3002:3002"
    # Adjust device mapping to your host
    devices:
      - "/dev/ttyUSB0:/dev/ttyUSB0"
```

## Features
- vcontrold served on TCP (default port 3002).
- Mandatory `COMMANDS` env for periodic `vclient` batch execution (CSV).
- MQTT publishing per command topic: `MQTT_TOPIC/<command>`.
- Optional subscription to `MQTT_TOPIC/commands` to execute incoming command payloads.
- Configurable polling interval and payload mode (full JSON vs value-only).

## Configuration
Key environment variables (all strings unless noted):
- `OPTOLINK_DEVICE`: Path to the USB device inside the container (e.g., `/dev/ttyUSB0`).
- `VCONTROLD_HOST`: Host for the worker to reach `vcontrold` (default `127.0.0.1`).
- `VCONTROLD_PORT`: TCP port for `vcontrold` (default `3002`).
- `COMMANDS` (required): Comma-separated `vclient` commands to run in a batch (e.g., `get_temp,get_pressure`).
- `POLL_SECONDS` (int): Worker polling interval in seconds (default `60`).
- `PUBLISH_VALUE_ONLY` (bool-like): When set (e.g., `true`), publishes only the `Value` field; otherwise publishes full JSON.
- `MQTT_HOST`, `MQTT_PORT`, `MQTT_USER`, `MQTT_PASSWORD`, `MQTT_TOPIC`: MQTT connection parameters.
- `LOG_LEVEL`: Minimum log level for the worker (`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`; default `Information`). Synonyms supported: `info`, `warn`, `err`, `fatal`.

Behavioral notes:
- Each command in `COMMANDS` is published to `MQTT_TOPIC/<command>`.
- Subscribing service listens on `MQTT_TOPIC/commands` and executes payloads as CSV commands.
- Logging uses `ILogger` with timestamps.

## Contributing & Local Build
- Tech stack: upstream `vcontrold` + .NET 10 worker (MQTTnet, DI, options pattern).
- To build locally:
  - Docker: `cd docker; docker build -t vcontrol-dotnet-local .`
  - Run: `docker run --rm -it --device "COM3" -e OPTOLINK_DEVICE="/dev/ttyUSB0" -e COMMANDS="get_temp" -p 3002:3002 vcontrol-dotnet-local`

## Licensing
- Repository code: licensed under Apache License 2.0. See [LICENSE](LICENSE).
- vcontrold component: the container image bundles upstream `vcontrold`, which is licensed under GPL-3.0. See [NOTICE.md](NOTICE.md) and the full text at [licenses/GPL-3.0.txt](licenses/GPL-3.0.txt).
- Combined distribution: When distributing container images that include `vcontrold`, you must comply with GPL-3.0 obligations for that component. This repository includes attribution and a written offer in [NOTICE.md](NOTICE.md). If you obtained binaries, you may request corresponding source as described there.
- Upstream sources: `vcontrold` from https://github.com/openv/vcontrold and reference docs at https://github.com/openv/openv/wiki/vcontrold.xml.
- Trademarks: Viessmann and related marks belong to their respective owners.

## Acknowledgments
- Thanks to the OpenV community for `vcontrold`.
- MQTT integration via MQTTnet.
