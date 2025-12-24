using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Linq;

namespace Vcontrol.Worker;


public sealed class CommandsSubscriber(ILogger<CommandsSubscriber> logger, MqttService mqtt, VclientService vclient) : IHostedService
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
            var commands = text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (commands.Count == 0)
            {
                logger.LogWarning("CommandsSubscriber: empty payload, skipping.");
                return;
            }

            try
            {
                var (readings, stderr, exitCode) = await vclient.QueryAsync(commands, CancellationToken.None);

                foreach (var r in readings)
                {
                    var json = JsonSerializer.Serialize(r);
                    logger.LogInformation("vclient result: {Json}", json);
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    logger.LogWarning("vclient stderr: {Stderr}", stderr);
                }

                if (exitCode != 0)
                {
                    logger.LogWarning("vclient exited with code {Code}.", exitCode);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CommandsSubscriber: exception while executing vclient.");
            }
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
