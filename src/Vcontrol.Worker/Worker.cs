using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vcontrol.Worker;

public class Worker(ILogger<Worker> logger, MqttService mqtt) : BackgroundService
{   
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var portEnv = Environment.GetEnvironmentVariable("VCONTROLD_PORT");
        var port = 3002;
        if (int.TryParse(portEnv, out var parsed)) port = parsed;

        logger.LogInformation("Starting periodic vclient getTempA every 60s on port {Port}...", port);

        while (true)
        {
            try
            {
                stoppingToken.ThrowIfCancellationRequested();
                var psi = new ProcessStartInfo
                {
                    FileName = "vclient",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    ArgumentList = { "--json-long", "-h", "127.0.0.1", "-p", port.ToString(), "-c", "getTempA" }
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    logger.LogError("Failed to start vclient.");
                }
                else
                {
                    var stdout = await proc.StandardOutput.ReadToEndAsync(stoppingToken);
                    var stderr = await proc.StandardError.ReadToEndAsync(stoppingToken);
                    await proc.WaitForExitAsync(stoppingToken);

                    var output = stdout.Trim();
                    if (!string.IsNullOrEmpty(output))
                    {
                        logger.LogInformation("getTempA: {Output}", output);

                        var published = await mqtt.PublishAsync(output, stoppingToken);
                        if (!published)
                        {
                            logger.LogDebug("MQTT publish skipped or failed.");
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(stderr))
                        logger.LogWarning("vclient stderr: {Stderr}", stderr.Trim());
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
