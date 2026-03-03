using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Vcontrol.Worker;

internal sealed class VcontrolMetrics : IDisposable
{
    private readonly Meter? _meter;
    private readonly Counter<long>? _vclientRequestsTotal;
    private readonly Histogram<double>? _vclientRequestDurationSeconds;
    private readonly Counter<long>? _vclientErrorsTotal;
    private readonly Counter<long>? _mqttConnectAttemptsTotal;
    private readonly Counter<long>? _mqttPublishTotal;
    private readonly Counter<long>? _mqttCommandsMessagesTotal;

    // State read by observable gauge callbacks
    private readonly ConcurrentDictionary<string, double> _vclientLastSuccessTimestamps = new();
    private readonly ConcurrentDictionary<string, double> _mqttLastPublishTimestamps = new();
    private volatile int _mqttConnected;
    private volatile int _commandsSubscriptionActive;

    public bool IsEnabled { get; }

    public VcontrolMetrics(bool enabled)
    {
        IsEnabled = enabled;
        if (!enabled)
            return;

        _meter = new Meter("vcontrol.mqtt", "1.0.0");

        _vclientRequestsTotal = _meter.CreateCounter<long>(
            "vclient_requests_total",
            description: "Total number of vclient batch executions.");

        _vclientRequestDurationSeconds = _meter.CreateHistogram<double>(
            "vclient_request_duration_seconds",
            unit: "s",
            description: "Duration of vclient batch executions in seconds.");

        _vclientErrorsTotal = _meter.CreateCounter<long>(
            "vclient_errors_total",
            description: "Total number of vclient errors.");

        _meter.CreateObservableGauge<double>(
            "vclient_last_success_timestamp_seconds",
            unit: "s",
            description: "Unix timestamp of the last successful vclient request per source.",
            observeValues: () => _vclientLastSuccessTimestamps.Select(
                kv => new Measurement<double>(kv.Value, new KeyValuePair<string, object?>("source", kv.Key))));

        _mqttConnectAttemptsTotal = _meter.CreateCounter<long>(
            "mqtt_connect_attempts_total",
            description: "Total number of MQTT connection attempts.");

        _meter.CreateObservableGauge<int>(
            "mqtt_client_connected",
            description: "1 if the MQTT client is currently connected, 0 otherwise.",
            observeValue: () => _mqttConnected);

        _mqttPublishTotal = _meter.CreateCounter<long>(
            "mqtt_publish_total",
            description: "Total number of MQTT publish attempts.");

        _meter.CreateObservableGauge<double>(
            "mqtt_last_publish_timestamp_seconds",
            unit: "s",
            description: "Unix timestamp of the last successful MQTT publish per topic.",
            observeValues: () => _mqttLastPublishTimestamps.Select(
                kv => new Measurement<double>(kv.Value, new KeyValuePair<string, object?>("topic", kv.Key))));

        _mqttCommandsMessagesTotal = _meter.CreateCounter<long>(
            "mqtt_commands_messages_total",
            description: "Total number of messages received on the MQTT commands topic.");

        _meter.CreateObservableGauge<int>(
            "mqtt_commands_subscription_active",
            description: "1 if the MQTT commands topic subscription is active, 0 otherwise.",
            observeValue: () => _commandsSubscriptionActive);
    }

    // vclient

    public void RecordVclientRequest(string command, string source, string result)
        => _vclientRequestsTotal?.Add(1,
            new KeyValuePair<string, object?>("command", command),
            new KeyValuePair<string, object?>("source", source),
            new KeyValuePair<string, object?>("result", result));

    public void RecordVclientDuration(string command, string source, double seconds)
        => _vclientRequestDurationSeconds?.Record(seconds,
            new KeyValuePair<string, object?>("command", command),
            new KeyValuePair<string, object?>("source", source));

    public void RecordVclientError(string stage, string reason)
        => _vclientErrorsTotal?.Add(1,
            new KeyValuePair<string, object?>("stage", stage),
            new KeyValuePair<string, object?>("reason", reason));

    public void UpdateVclientLastSuccess(string source)
        => _vclientLastSuccessTimestamps[source] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    // MQTT

    public void RecordMqttConnectAttempt(bool success)
        => _mqttConnectAttemptsTotal?.Add(1, new KeyValuePair<string, object?>("result", success ? "success" : "failure"));

    public void SetMqttConnected(bool connected)
        => _mqttConnected = connected ? 1 : 0;

    public void RecordMqttPublish(string topic, bool success)
    {
        _mqttPublishTotal?.Add(1,
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("result", success ? "success" : "failure"));
        if (success)
            _mqttLastPublishTimestamps[topic] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }

    // Commands

    public void RecordCommandsMessage(string command, string result)
        => _mqttCommandsMessagesTotal?.Add(1,
            new KeyValuePair<string, object?>("command", command),
            new KeyValuePair<string, object?>("result", result));

    public void SetCommandsSubscriptionActive(bool active)
        => _commandsSubscriptionActive = active ? 1 : 0;

    public void Dispose() => _meter?.Dispose();
}
