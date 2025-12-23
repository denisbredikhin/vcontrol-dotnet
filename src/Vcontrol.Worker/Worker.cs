using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vcontrol.Worker;

public class Worker(ILogger<Worker> logger, MqttService mqtt, VclientService vclient) : BackgroundService
{   
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting periodic vclient getTempA every 60s on {Host}:{Port}...", vclient.Host, vclient.Port);

        while (true)
        {
            try
            {
                stoppingToken.ThrowIfCancellationRequested();
                var (stdout, stderr, exitCode) = await vclient.RunAsync("getTempA", stoppingToken);

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    logger.LogInformation("getTempA: {Output}", stdout);
                    var published = await mqtt.PublishAsync(stdout, stoppingToken);
                    if (!published)
                    {
                        logger.LogDebug("MQTT publish skipped or failed.");
                    }
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    logger.LogWarning("vclient stderr: {Stderr}", stderr);
                }

                if (exitCode != 0)
                {
                    logger.LogWarning("vclient exited with code {Code}.", exitCode);
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Worker exception.");
            }
        }
    }
}
