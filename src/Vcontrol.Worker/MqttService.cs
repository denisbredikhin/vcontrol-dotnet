using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace Vcontrol.Worker;

public class MqttService
{
    private readonly ILogger<MqttService> _logger;
    private readonly IOptions<MqttOptions> _optionsSnapshot;

    private readonly IMqttClient? _client;
    private readonly MqttClientOptions? _clientOptions;

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
}
