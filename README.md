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

## Metrics & Observability

Metrics are implemented using `System.Diagnostics.Metrics` (meter name `vcontrol.mqtt`) and are compatible with Prometheus / OpenTelemetry naming conventions.

### Enabling metrics

Metrics are **disabled by default**. Two independent env vars activate the pipeline:

| Variable | Effect when set |
|---|---|
| `ENABLE_PROMETHEUS_EXPORTER=true` | Enables Prometheus scrape endpoint at `GET /metrics` (port 8080) |
| `OTEL_EXPORTER_OTLP_ENDPOINT=<url>` | Enables OTLP push export to the given endpoint |

Either variable (or both) activates the shared OpenTelemetry pipeline (meter, runtime instrumentation, ASP.NET Core instrumentation). They can be combined.

When `OTEL_EXPORTER_OTLP_ENDPOINT` is set, the full set of standard OTel SDK env vars is respected automatically — no extra configuration is required:

- `OTEL_EXPORTER_OTLP_ENDPOINT` — collector endpoint (e.g. `http://otel-collector:4318`)
- `OTEL_EXPORTER_OTLP_HEADERS` — authentication headers
- `OTEL_EXPORTER_OTLP_PROTOCOL` — `grpc` or `http/protobuf`
- `OTEL_SERVICE_NAME` *(optional)* — service name reported in telemetry; defaults to `vcontrol-dotnet`

### Available metrics

**vclient**

| Metric | Type | Labels |
|--------|------|--------|
| `vclient_requests_total` | Counter | `command`, `source` (`timer`/`command`), `result` (`success`/`error`) |
| `vclient_request_duration_seconds` | Histogram | `command`, `source` |
| `vclient_last_success_timestamp_seconds` | Gauge | `source` |
| `vclient_errors_total` | Counter | `stage` (`process`/`deserialize`), `reason` (`non_zero_exit_code`/`exception`) |

**MQTT**

| Metric | Type | Labels |
|--------|------|--------|
| `mqtt_client_connected` | Gauge | — |
| `mqtt_connect_attempts_total` | Counter | `result` (`success`/`failure`) |
| `mqtt_publish_total` | Counter | `topic`, `result` (`success`/`failure`) |
| `mqtt_last_publish_timestamp_seconds` | Gauge | `topic` |

**Commands topic**

| Metric | Type | Labels |
|--------|------|--------|
| `mqtt_commands_messages_total` | Counter | `command`, `result` (`success`/`error`) |
| `mqtt_commands_subscription_active` | Gauge | — |

### Integration

- **Prometheus:** set `ENABLE_PROMETHEUS_EXPORTER=true` and scrape `http://<host>:8080/metrics`. Expose port 8080 in your Compose or `docker run` command.
- **OpenTelemetry Collector / OTLP:** set `OTEL_EXPORTER_OTLP_ENDPOINT=http://collector:4318` and the worker will push metrics automatically. No port exposure needed.
- Both exporters can be active simultaneously.

### Local debug with Aspire Dashboard

For a quick local observability setup with no extra infrastructure, use the provided [`docker/docker-compose.aspire.yml`](docker/docker-compose.aspire.yml). It starts the worker alongside the [standalone Aspire Dashboard](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone) — a developer UI that receives metrics, traces, and logs via OTLP/gRPC:

```sh
docker compose -f docker/docker-compose.aspire.yml up
```

Then open **http://localhost:18888** — no login token is required (anonymous access is pre-configured). The worker pushes all telemetry automatically; the Prometheus scraping endpoint (`/metrics`) is kept disabled.

> The Aspire Dashboard is a short-lived developer tool. Telemetry is held in memory and is lost on container restart. For production monitoring use Prometheus/Grafana or an OTLP Collector instead.

## Health Checks
The container exposes HTTP health check endpoints on port **8080** using ASP.NET Core minimal APIs:

- **GET `/health/live`** – Liveness probe
  - Returns `200 OK` if the process and HTTP server are running
  - Use this to verify the container is alive and responsive

- **GET `/health/ready`** – Readiness probe
  - Returns `200 OK` if the last `vclient` reply was successful
  - Returns `503 Service Unavailable` if the last reply failed or no replies have been recorded yet
  - Response includes JSON with diagnostic information, for example:

    ```json
    {
      "status": "Healthy|Degraded|Unhealthy",
      "lastSuccess": true,
      "lastSuccessAt": "2026-02-10T12:34:56.789Z",
      "lastFailureAt": null,
      "lastExitCode": 0,
      "lastError": null
    }
    ```

The Docker image includes a built-in `HEALTHCHECK` that calls `/health/ready` every 30 seconds. Containers will be marked as unhealthy if the readiness check fails.

To access health endpoints from outside the container, expose port 8080, for example:

```powershell
docker run --rm -it `
  --device "COM3" `
  -e OPTOLINK_DEVICE="/dev/ttyUSB0" `
  -p 3002:3002 `
  -p 8080:8080 `
  ghcr.io/denisbredikhin/vcontrol-dotnet:latest
```

Then test the endpoints:

```powershell
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
```

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
