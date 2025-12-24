using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Text;

namespace Vcontrol.Worker;

public sealed class CommandsSubscriber(ILogger<CommandsSubscriber> logger, MqttService mqtt) : IHostedService
{
    private Func<string, string, Task>? _handler;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!mqtt.IsConfigured || string.IsNullOrWhiteSpace(mqtt.Topic))
        {
            logger.LogInformation("MQTT not configured; CommandsSubscriber is idle.");
            return;
        }

        _handler = async (topic, text) =>
        {
            logger.LogInformation("Received on {Topic}: {Payload}", topic, text);
            await Task.CompletedTask;
        };

        var ok = await mqtt.SubscribeAsync("commands", _handler, cancellationToken);
        if (!ok)
        {
            logger.LogWarning("Failed to subscribe to MQTT 'commands' subtopic.");
            _handler = null;
            return;
        }
        logger.LogInformation("CommandsSubscriber listening on {Base}/commands", mqtt.Topic);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_handler != null)
        {
            await mqtt.UnsubscribeAsync("commands", _handler, cancellationToken);
            _handler = null;
        }
    }
}
