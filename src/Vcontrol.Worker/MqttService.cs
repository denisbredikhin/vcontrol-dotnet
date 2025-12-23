using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace Vcontrol.Worker;

public class MqttService
{
    private readonly ILogger<MqttService> _logger;
    private readonly string? _host;
    private readonly int _port;
    private readonly string? _user;
    private readonly string? _password;
    private readonly string? _topic;

    private IMqttClient? _client;
    private MqttClientOptions? _options;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_host) && !string.IsNullOrWhiteSpace(_topic);

    public string? Topic => _topic;

    public MqttService(ILogger<MqttService> logger)
    {
        _logger = logger;

        _host = Environment.GetEnvironmentVariable("MQTT_HOST");
        var portEnv = Environment.GetEnvironmentVariable("MQTT_PORT");
        _port = int.TryParse(portEnv, out var p) ? p : 1883;
        _user = Environment.GetEnvironmentVariable("MQTT_USER");
        _password = Environment.GetEnvironmentVariable("MQTT_PASSWORD");
        _topic = Environment.GetEnvironmentVariable("MQTT_TOPIC");

        if (!IsConfigured)
        {
            _logger.LogInformation("MQTT not configured. Set MQTT_HOST and MQTT_TOPIC to enable publishing.");
            return;
        }

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();
        _options = new MqttClientOptionsBuilder()
            .WithClientId($"vcontrol-worker-{Guid.NewGuid():N}")
            .WithTcpServer(_host!, _port)
            .WithCredentials(_user ?? string.Empty, _password ?? string.Empty)
            .Build();
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        if (!IsConfigured || _client == null || _options == null)
        {
            return false;
        }

        if (_client.IsConnected)
        {
            return true;
        }

        try
        {
            _logger.LogInformation("Connecting to MQTT {Host}:{Port}...", _host, _port);
            await _client.ConnectAsync(_options, ct);
            _logger.LogInformation("Connected to MQTT {Host}:{Port}.", _host, _port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to MQTT {Host}:{Port}.", _host, _port);
            return false;
        }
    }

    public async Task<bool> PublishAsync(string payload, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return false;
        }

        if (!await EnsureConnectedAsync(ct))
        {
            return false;
        }

        try
        {
            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(_topic!)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client!.PublishAsync(msg, ct);
            _logger.LogInformation("Published payload to MQTT topic {Topic}.", _topic);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish to MQTT topic {Topic}.", _topic);
            return false;
        }
    }
}
