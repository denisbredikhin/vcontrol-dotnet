using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using System.Text;
using System.Buffers;

namespace Vcontrol.Worker;

public class MqttService
{
    private readonly ILogger<MqttService> _logger;
    private readonly IOptions<MqttOptions> _optionsSnapshot;

    private readonly IMqttClient? _client;
    private readonly MqttClientOptions? _clientOptions;
    private readonly List<SubscriptionEntry> _subscriptions = new();
    private readonly object _subsLock = new();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_optionsSnapshot.Value.Host) && !string.IsNullOrWhiteSpace(_optionsSnapshot.Value.Topic);

    public string? Topic => _optionsSnapshot.Value.Topic;

    public MqttService(ILogger<MqttService> logger, IOptions<MqttOptions> options)
    {
        _logger = logger;
        _optionsSnapshot = options;

        if (!IsConfigured)
        {
            _logger.LogInformation("MQTT not configured. Set MQTT_HOST and MQTT_TOPIC to enable publishing.");
            return;
        }

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();
        _clientOptions = new MqttClientOptionsBuilder()
            .WithClientId($"vcontrol-worker-{Guid.NewGuid():N}")
            .WithTcpServer(_optionsSnapshot.Value.Host!, _optionsSnapshot.Value.Port)
            .WithCredentials(_optionsSnapshot.Value.User ?? string.Empty, _optionsSnapshot.Value.Password ?? string.Empty)
            .Build();
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        if (!IsConfigured || _client == null || _clientOptions == null)
        {
            return false;
        }

        if (_client.IsConnected)
        {
            return true;
        }

        try
        {
            _logger.LogInformation("Connecting to MQTT {Host}:{Port}...", _optionsSnapshot.Value.Host, _optionsSnapshot.Value.Port);
            await _client.ConnectAsync(_clientOptions, ct);
            _logger.LogInformation("Connected to MQTT {Host}:{Port}.", _optionsSnapshot.Value.Host, _optionsSnapshot.Value.Port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to MQTT {Host}:{Port}.", _optionsSnapshot.Value.Host, _optionsSnapshot.Value.Port);
            return false;
        }
    }

    public Task<bool> PublishAsync(string payload, CancellationToken ct)
        => PublishToAsync(null, payload, ct);

    public async Task<bool> PublishToAsync(string? subtopic, string payload, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return false;
        }

        if (!await EnsureConnectedAsync(ct))
        {
            return false;
        }

        var topic = BuildTopic(subtopic);

        try
        {
            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client!.PublishAsync(msg, ct);
            _logger.LogInformation("Published payload to MQTT topic {Topic}.", topic);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish to MQTT topic {Topic}.", topic);
            return false;
        }
    }

    private string BuildTopic(string? subtopic)
    {
        var baseTopic = _optionsSnapshot.Value.Topic!;
        if (string.IsNullOrWhiteSpace(subtopic))
        {
            return baseTopic;
        }
        if (!baseTopic.EndsWith('/'))
        {
            baseTopic += "/";
        }
        return baseTopic + subtopic;
    }

    private sealed class SubscriptionEntry
    {
        public required string Topic { get; init; }
        public required string Subtopic { get; init; }
        public required Func<string, string, Task> UserHandler { get; init; }
        public required Func<MQTTnet.MqttApplicationMessageReceivedEventArgs, Task> WrappedHandler { get; init; }
    }

    public async Task<bool> SubscribeAsync(string subtopic, Func<string, string, Task> handler, CancellationToken ct)
    {
        if (!IsConfigured || _client == null)
        {
            return false;
        }
        if (!await EnsureConnectedAsync(ct))
        {
            return false;
        }

        var topic = BuildTopic(subtopic);

        Func<MQTTnet.MqttApplicationMessageReceivedEventArgs, Task> wrapped = async args =>
        {
            try
            {
                var msgTopic = args.ApplicationMessage?.Topic;
                if (!string.Equals(msgTopic, topic, StringComparison.Ordinal))
                {
                    return;
                }
                var seq = args.ApplicationMessage?.Payload;
                string text = seq.HasValue ? Encoding.UTF8.GetString(seq.Value.ToArray()) : string.Empty;
                await handler(msgTopic ?? string.Empty, text);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in MQTT received handler for topic {Topic}", args.ApplicationMessage?.Topic);
            }
        };

        _client.ApplicationMessageReceivedAsync += wrapped;

        try
        {
            var filter = new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            await _client.SubscribeAsync(filter, ct);
            lock (_subsLock)
            {
                _subscriptions.Add(new SubscriptionEntry
                {
                    Topic = topic,
                    Subtopic = subtopic,
                    UserHandler = handler,
                    WrappedHandler = wrapped
                });
            }
            _logger.LogInformation("Subscribed to MQTT topic {Topic}.", topic);
            return true;
        }
        catch (Exception ex)
        {
            _client.ApplicationMessageReceivedAsync -= wrapped;
            _logger.LogWarning(ex, "Failed to subscribe to MQTT topic {Topic}.", topic);
            return false;
        }
    }

    public async Task UnsubscribeAsync(string subtopic, Func<string, string, Task> handler, CancellationToken ct)
    {
        if (!IsConfigured || _client == null)
        {
            return;
        }

        SubscriptionEntry? entry;
        lock (_subsLock)
        {
            entry = _subscriptions.FirstOrDefault(s => s.Subtopic == subtopic && s.UserHandler == handler);
            if (entry != null)
            {
                _subscriptions.Remove(entry);
            }
        }
        if (entry == null)
        {
            return;
        }

        try
        {
            await _client.UnsubscribeAsync(entry.Topic, ct);
        }
        catch
        {
            // ignore errors during unsubscribe
        }

        _client.ApplicationMessageReceivedAsync -= entry.WrappedHandler;
        _logger.LogInformation("Unsubscribed from MQTT topic {Topic}.", entry.Topic);
    }
}
