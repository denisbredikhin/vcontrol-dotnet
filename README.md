# vcontrol-dotnet

Container image for integrating Viessmann boilers with MQTT using `vcontrold`. It runs `vcontrold` as a TCP service and a lightweight .NET worker that periodically reads configured boiler parameters and publishes them to your MQTT broker. In addition, it can subscribe to a dedicated command topic and execute incoming requests.
An opt-in experimental native mode bypasses the daemon and accesses Optolink directly.

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
      VCLIENT_DIRECT_CONNECTION: "false"
      VCLIENT_NATIVE_CONNECTION: "false"
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
- `VCLIENT_DIRECT_CONNECTION` (bool, default `false`): Experimental mode that connects to `vcontrold` directly from the worker instead of launching the `vclient` process. Set to `true` to enable it.
- `VCLIENT_NATIVE_CONNECTION` (bool, default `false`): Experimental C# Optolink implementation that communicates directly with `OPTOLINK_DEVICE`. Neither `vclient` nor `vcontrold` is started or contacted in this mode. Cannot be enabled together with `VCLIENT_DIRECT_CONNECTION`.
- `COMMANDS` (required): Comma-separated `vclient` commands to run in a batch (e.g., `get_temp,get_pressure`).
- `POLL_SECONDS` (int): Worker polling interval in seconds (default `60`).
- `PUBLISH_VALUE_ONLY` (bool-like): When set (e.g., `true`), publishes only the `Value` field; otherwise publishes full JSON.
- `MQTT_HOST`, `MQTT_PORT`, `MQTT_USER`, `MQTT_PASSWORD`, `MQTT_TOPIC`: MQTT connection parameters.
- `LOG_LEVEL`: Minimum log level for the worker (`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`; default `Information`). Synonyms supported: `info`, `warn`, `err`, `fatal`.

Behavioral notes:
- Each command in `COMMANDS` is published to `MQTT_TOPIC/<command>`.
- Subscribing service listens on `MQTT_TOPIC/commands` and executes payloads as CSV commands.
- Logging uses `ILogger` with timestamps.

### Experimental native Optolink mode

Set `VCLIENT_NATIVE_CONNECTION=true` and leave `VCLIENT_DIRECT_CONNECTION=false`.
The worker opens the serial device at **4800 baud, 8 data bits, even parity,
2 stop bits**, serializes polling and MQTT command requests, and uses the same
`command`, `value`, `raw`, and `error` reading shape as the existing modes.
Do not run another process against the same device. Native mode has no TCP
service on port 3002; health and optional metrics endpoints remain on port 8080.

The device ID in `/etc/vcontrold/vcontrold.xml` selects the protocol and
device-specific command overrides from its local `vito.xml` XInclude, as it does
for the daemon. This is **configured selection, not hardware autodetection**:
the shipped ID `20CB` selects P300. IDs `2098` and `2094` select KW2; ID `2053`
selects GWG. Mount your configuration files to select the correct controller.
`Vcontrol__OptolinkConfigPath` can select another local configuration path;
`Vcontrol__OptolinkDevice` is also supported, with `OPTOLINK_DEVICE` taking
precedence. Section-style flags `Vcontrol__UseNativeConnection` and
`Vcontrol__UseDirectConnection` work when their flat flags are unset/empty.
Invalid native flags and conflicting modes fail startup explicitly.
The Docker image sets `VCLIENT_NATIVE_CONNECTION=false` by default; clear it
explicitly when selecting native mode through a section-style environment flag.

Native mode interprets the shipped protocol macros and register mappings,
including signed/scaled numeric values, enums, bit status, schedules, BCD
system time, error history, and GWG address spaces. Empty device overrides
disable commands. Unknown/invalid commands are reported as errors, not zero
readings. Reads retry only as specified by the XML. Writes are never
automatically replayed after a timeout or lost acknowledgement: their outcome
may be unknown, so verify the hardware state before retrying.

Some shipped definitions cannot execute safely even in the upstream daemon:
GWG `setaddr` references an undefined `SETADDR` macro, P300 `gettestaddr` is
empty, and GWG bit/BH units have no usable write conversion. Native mode
reports these limitations without inventing write opcodes or conversions.
KW2/P300 mapped writes with valid definitions are supported. Numeric writes
must fit the configured register width without fractional truncation. Schedule
writes take one to four time pairs such as `06:00 22:00 -- --`, in ten-minute
steps; system-time writes take ISO 8601 with an offset (or no argument to use
the host clock). Read the controller's documentation before changing settings.

This feature is experimental: deterministic serial transcript tests are not
physical controller certification. Validate against your controller before
relying on it for heating operation. The container still bundles upstream
`vcontrold` to preserve the other two modes, but does not launch it in native mode.
The [OpenV-derived P300 protocol notes](https://github.com/bertmelis/VitoWiFi/blob/main/docs/protocol_vs2.md)
describe the framing and byte-count acknowledgement used by this implementation.

## Metrics & Observability

Metrics are implemented using `System.Diagnostics.Metrics` (meter name `vcontrol.mqtt`) and are compatible with Prometheus / OpenTelemetry naming conventions. When OTLP export is enabled, structured logs produced via `ILogger` are also exported over OTLP alongside metrics.

### Enabling metrics and logs

Metrics are **disabled by default**. Two independent env vars activate the pipeline:

| Variable | Effect when set |
|---|---|
| `ENABLE_PROMETHEUS_EXPORTER=true` | Enables Prometheus scrape endpoint at `GET /metrics` (port 8080) |
| `OTEL_EXPORTER_OTLP_ENDPOINT=<url>` | Enables OTLP push export of **metrics and logs** to the given endpoint |

Either variable (or both) activates the shared OpenTelemetry pipeline (meter, runtime instrumentation, ASP.NET Core instrumentation). They can be combined. Structured log export via OTLP is active only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

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
| `vclient_errors_total` | Counter | `stage` (`process`/`deserialize`/`hardware`), `reason` (`non_zero_exit_code`/`exception`/`timeout`/`invalid_command`/`internal_error`) |

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
- **OpenTelemetry Collector / OTLP:** set `OTEL_EXPORTER_OTLP_ENDPOINT=http://collector:4318` and the worker will push metrics and structured logs automatically. No port exposure needed.
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
- Native-mode entrypoint checks (a POSIX shell, no device required):
  `sh tests/entrypoint.tests.sh`.

## Licensing
- Original repository code: licensed under Apache License 2.0. See [LICENSE](LICENSE).
- Native Optolink implementation: GPL-3.0-or-later, as marked in its source files.
  Its protocol behavior was implemented with reference to upstream GPL source.
  The combined worker executable is distributed under GPL-3.0-or-later,
  including when the native feature flag is disabled. Existing Apache-2.0
  permissions/notices for original files are retained. See [NOTICE.md](NOTICE.md).
- vcontrold component: the container image bundles upstream `vcontrold`, which is licensed under GPL-3.0. See [NOTICE.md](NOTICE.md) and the full text at [licenses/GPL-3.0.txt](licenses/GPL-3.0.txt).
- Combined distribution: Provide corresponding source for both the combined worker
  and upstream `vcontrold`, including build scripts and configuration, as required
  by GPL-3.0-or-later. License texts and notices accompany the worker output.
  See [NOTICE.md](NOTICE.md) for attribution and source availability.
- Upstream sources: `vcontrold` from https://github.com/openv/vcontrold and reference docs at https://github.com/openv/openv/wiki/vcontrold.xml.
- Trademarks: Viessmann and related marks belong to their respective owners.

## Acknowledgments
- Thanks to the OpenV community for `vcontrold`.
- MQTT integration via MQTTnet.
