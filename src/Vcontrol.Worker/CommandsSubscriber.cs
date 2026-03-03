using System.Text.Json;

namespace Vcontrol.Worker;


internal sealed class CommandsSubscriber(ILogger<CommandsSubscriber> logger, MqttService mqtt, VclientService vclient, VcontrolMetrics metrics) : IHostedService
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
                var result = await vclient.QueryAsync(commands, "command", CancellationToken.None);

                foreach (var r in result.Readings)
                {
                    var json = JsonSerializer.Serialize(r);
                    logger.LogInformation("vclient result: {Json}", json);
                }

                if (!string.IsNullOrWhiteSpace(result.Stderr))
                {
                    logger.LogWarning("vclient stderr: {Stderr}", result.Stderr);
                }

                if (result.ExitCode != 0)
                {
                    logger.LogWarning("vclient exited with code {Code}.", result.ExitCode);
                }

                var commandResult = result.ExitCode == 0 ? "success" : "error";
                foreach (var cmd in commands)
                    metrics.RecordCommandsMessage(cmd, commandResult);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CommandsSubscriber: exception while executing vclient.");
                foreach (var cmd in commands)
                    metrics.RecordCommandsMessage(cmd, "error");
            }
        };

        var ok = await mqtt.SubscribeAsync("commands", _handler, cancellationToken);
        if (!ok)
        {
            logger.LogWarning("Failed to subscribe to MQTT 'commands' subtopic.");
            _handler = null;
            return;
        }
        metrics.SetCommandsSubscriptionActive(true);
        logger.LogInformation("CommandsSubscriber listening on {Base}/commands", mqtt.Topic);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_handler != null)
        {
            await mqtt.UnsubscribeAsync("commands", _handler, cancellationToken);
            _handler = null;
            metrics.SetCommandsSubscriptionActive(false);
        }
    }
}
