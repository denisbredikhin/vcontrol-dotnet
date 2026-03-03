# Copilot Instructions

## Build, Test, and Lint

```sh
# Restore
dotnet restore vcontrol-dotnet.slnx

# Build (warnings are errors; code style is enforced)
dotnet build vcontrol-dotnet.slnx

# Run all tests
dotnet test --solution vcontrol-dotnet.slnx

# Run a single test class or method (MTP filter syntax)
dotnet test tests/Vcontrol.Worker.Tests/Vcontrol.Worker.Tests.csproj --filter "FullyQualifiedName~LastReplyHealthCheckTests"

# Full CI-style test run with coverage
dotnet test --configuration Release --solution vcontrol-dotnet.slnx \
  --results-directory ./TestResults --report-trx --coverage \
  --coverage-output-format cobertura --coverage-output coverage.cobertura.xml

# Docker local build
cd docker && docker build -t vcontrol-dotnet-local .
```

> **Note:** `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, and `AnalysisMode=All` are enabled in `Vcontrol.Worker.csproj`. All analyzer warnings fail the build.

## Architecture

This is a single .NET 10 `Microsoft.NET.Sdk.Web` worker service (`src/Vcontrol.Worker`) that bridges a `vcontrold` TCP daemon (running inside the same container) with an MQTT broker. The container bundles the upstream `vcontrold` binary (GPL-3.0) alongside the .NET worker (Apache-2.0).

**Service graph (all singletons registered in `Program.cs`):**

| Class | Role |
|---|---|
| `Worker` | `BackgroundService` — polls `vclient` on a configurable interval, publishes readings to MQTT |
| `CommandsSubscriber` | `IHostedService` — subscribes to `{MQTT_TOPIC}/commands`, executes on-demand CSV payloads |
| `MqttService` | Manages MQTTnet connection; lazy-connects on first publish/subscribe |
| `VclientService` | Wraps `vclient` CLI process execution; serialized with a `SemaphoreSlim(1,1)` |
| `LastReplyState` | Thread-safe state bag tracking last vclient success/failure (used by health check) |
| `LastReplyHealthCheck` | ASP.NET Core `IHealthCheck` — exposes readiness status |
| `VcontrolMetrics` | Singleton holding all `System.Diagnostics.Metrics` instruments; no-op when metrics disabled |

**HTTP endpoints (port 8080):**
- `GET /health/live` — always `200 OK` (process alive)
- `GET /health/ready` — `200` if last vclient call succeeded, `503` otherwise; JSON body from `LastReplyState`
- `GET /metrics` — Prometheus scrape endpoint; **only active when `VCONTROL_ENABLE_METRICS=true`**

**Metrics (`VcontrolMetrics`):** A single singleton registered via factory delegate (`services.AddSingleton(_ => new VcontrolMetrics(enableMetrics))`). When `enabled=false` all instruments are `null` and record methods are no-ops — no OTel pipeline is initialized. `VCONTROL_ENABLE_METRICS=true` activates OpenTelemetry with `AddMeter("vcontrol.mqtt")`, runtime and ASP.NET Core instrumentation, and the Prometheus exporter via `UseOpenTelemetryPrometheusScrapingEndpoint()`. Observable gauge state (last-success timestamps, last-publish timestamps) lives in `ConcurrentDictionary<string, double>` fields updated by service calls.

**vclient `source` label:** `VclientService.QueryAsync` takes a `source` string passed by the caller — `"timer"` from `Worker` and `"command"` from `CommandsSubscriber`. All vclient metrics are recorded inside `QueryAsync`.

**Configuration layering:** Options are bound from ASP.NET Core configuration sections (`Mqtt:*`, `Vcontrol:*`) and then overridden by flat environment variables (`MQTT_HOST`, `VCONTROLD_PORT`, `COMMANDS`, etc.) via `PostConfigure` in `Program.cs`. Both styles work simultaneously.

**vclient output format:** `vclient --json-long` returns a JSON array of `VclientReading` objects (`command`, `value`, `raw`, `error`). Each reading is published to `{MQTT_TOPIC}/{command}` as either full JSON or the raw `Value` string (controlled by `PUBLISH_VALUE_ONLY`).

**Versioning:** GitVersion in `ContinuousDeployment` mode. `main` increments Minor. The Docker build passes `VERSION`, `ASSEMBLY_VERSION`, `FILE_VERSION`, and `INFO_VERSION` build args derived from GitVersion outputs.

## Key Conventions

- **All production types are `internal`** — no public API surface in `Vcontrol.Worker`.
- **Primary constructors** are used for DI throughout (e.g., `Worker(ILogger<Worker> logger, MqttService mqtt, ...)`).
- **Collection expressions** `[]` instead of `new List<T>()` or `Array.Empty<T>()`.
- **`Lock` type** (C# 13) instead of `object` for `lock` statements.
- **Local functions** use PascalCase (e.g., `SanitizeTopicPart`, `Wrapped`).
- **Test assertions** use `AwesomeAssertions` (not FluentAssertions) — `result.Status.Should().Be(...)`.
- **Test framework:** xunit v3 via Microsoft Testing Platform runner (`xunit.v3.mtp-v2`). Use `TestContext.Current.CancellationToken` for cancellation in tests, not manually created tokens.
- **`Xunit` namespace** is globally imported via `<Using Include="Xunit" />` in the test `.csproj` — no explicit `using Xunit;` needed in test files.
- **MQTT QoS** is always `AtLeastOnce`; the client reconnects lazily before each publish/subscribe.
- **Topic sanitization:** command names are sanitized to allow only `[a-zA-Z0-9\-_/]` before use as MQTT subtopics.
