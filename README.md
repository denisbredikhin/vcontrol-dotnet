# vcontrol-dotnet

Containerized vcontrold + .NET 10 worker for Viessmann boilers via Optolink (USB). The image runs `vcontrold` as a TCP service and a .NET background worker that periodically executes `vclient` commands and publishes results to MQTT.

## Overview
- Viessmann boiler integration using upstream `vcontrold` (Optolink protocol).
- .NET 10 worker: polls `vclient` on a schedule; publishes JSON (or value-only) to MQTT.
- Interactive access supported: attach to the container and use `vclient`/`vcontrol` manually.
- Minimal, modular layout: Docker assets in `docker/`, .NET code in `src/`.

See docker usage details in [docker/README.md](docker/README.md).

## Features
- vcontrold built from upstream sources, served on TCP (default port 3002).
- Periodic batch execution of `vclient` via mandatory `COMMANDS` env (CSV).
- MQTT publishing per command topic: `MQTT_TOPIC/<command>`.
- Optional subscription to `MQTT_TOPIC/commands` to execute incoming command payloads.
- Configurable polling interval and payload mode (full JSON vs value-only).

## Quickstart (Windows PowerShell)
Build locally and run interactively:

```powershell
cd docker
# Build the image
docker build -t vcontrol-dev .

# Run the container (adjust device mapping as needed)
# Example: USB device exposed as COM3 on Windows host; container sees it as /dev/ttyUSB0
# -p 3002:3002 exposes vcontrold TCP port
# Mandatory: COMMANDS (CSV of vclient commands)
docker run --rm -it \
  --device "COM3" \
  -e OPTOLINK_DEVICE="/dev/ttyUSB0" \
  -e VCONTROLD_HOST="127.0.0.1" \
  -e VCONTROLD_PORT="3002" \
  -e COMMANDS="get_temp,get_pressure" \
  -e MQTT_HOST="mqtt.local" \
  -e MQTT_PORT="1883" \
  -e MQTT_USER="user" \
  -e MQTT_PASSWORD="pass" \
  -e MQTT_TOPIC="vcontrol" \
  -e POLL_SECONDS="60" \
  -e PUBLISH_VALUE_ONLY="" \
  -p 3002:3002 \
  vcontrol-dev
```

Compose-based run: see [docker/docker-compose.yml](docker/docker-compose.yml).

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

## Development & CI/CD
- Docker assets live in `docker/`.
- The image is suitable for local builds and for publishing to registries.
- For multi-arch builds and GHCR publishing, set up a workflow under `.github/workflows` (not shown here).

## Licensing
- Repository code: licensed under Apache License 2.0. See [LICENSE](LICENSE).
- vcontrold component: the container image bundles upstream `vcontrold`, which is licensed under GPL-3.0. See [NOTICE.md](NOTICE.md) and the full text at [licenses/GPL-3.0.txt](licenses/GPL-3.0.txt).
- Combined distribution: When distributing container images that include `vcontrold`, you must comply with GPL-3.0 obligations for that component. This repository includes attribution and a written offer in [NOTICE.md](NOTICE.md). If you obtained binaries, you may request corresponding source as described there.
- Upstream sources: `vcontrold` from https://github.com/openv/vcontrold and reference docs at https://github.com/openv/openv/wiki/vcontrold.xml.
- Trademarks: Viessmann and related marks belong to their respective owners.

## Acknowledgments
- Thanks to the OpenV community for `vcontrold`.
- MQTT integration via MQTTnet.
