using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace Vcontrol.Worker;

public class Worker(ILogger<Worker> logger) : BackgroundService
{   
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var portEnv = Environment.GetEnvironmentVariable("VCONTROLD_PORT");
        var port = 3002;
        if (int.TryParse(portEnv, out var parsed)) port = parsed;

        // MQTT configuration
        var mqttHost = Environment.GetEnvironmentVariable("MQTT_HOST");
        var mqttPort = 1883;
        var mqttPortEnv = Environment.GetEnvironmentVariable("MQTT_PORT");
        if (int.TryParse(mqttPortEnv, out var mqttParsed)) mqttPort = mqttParsed;
        var mqttUser = Environment.GetEnvironmentVariable("MQTT_USER");
        var mqttPassword = Environment.GetEnvironmentVariable("MQTT_PASSWORD");
        var _mqttTopic = Environment.GetEnvironmentVariable("MQTT_TOPIC");
        IMqttClient? _mqttClient = null;
        MqttClientOptions? options = null;

        if (!string.IsNullOrWhiteSpace(mqttHost) && !string.IsNullOrWhiteSpace(_mqttTopic))
        {
            try
            {
                var mqttFactory = new MqttClientFactory();
                _mqttClient = mqttFactory.CreateMqttClient();

                options = new MqttClientOptionsBuilder()
                    .WithClientId($"vcontrol-worker-{Guid.NewGuid():N}")
                    .WithTcpServer(mqttHost, mqttPort)
                    .WithCredentials(mqttUser ?? string.Empty, mqttPassword ?? string.Empty)
                    .Build();

                await _mqttClient.ConnectAsync(options, stoppingToken);
                logger.LogInformation("Connected to MQTT {Host}:{Port}; publishing to topic {Topic}.", mqttHost, mqttPort, _mqttTopic);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to connect to MQTT broker at {Host}:{Port}. MQTT publish will be skipped until connected.", mqttHost, mqttPort);
            }
        }
        else
        {
            logger.LogInformation("MQTT not configured. Set MQTT_HOST and MQTT_TOPIC to enable publishing.");
        }

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

                        if (_mqttClient?.IsConnected == false)
                        {
                            try
                            {
                                logger.LogInformation("Attempting to reconnect to MQTT {Host}:{Port}...", mqttHost, mqttPort);
                                await _mqttClient.ConnectAsync(options, stoppingToken);
                                logger.LogInformation("Reconnected to MQTT {Host}:{Port}.", mqttHost, mqttPort);
                            }
                            catch
                            {
                                logger.LogWarning("Failed to reconnect to MQTT broker at {Host}:{Port}. Will retry in next loop.", mqttHost, mqttPort);
                                continue;
                            }
                        }

                        if (_mqttClient?.IsConnected == true)
                        {
                            try
                            {
                                var message = new MqttApplicationMessageBuilder()
                                    .WithTopic(_mqttTopic)
                                    .WithPayload(output)
                                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                                    .Build();

                                await _mqttClient.PublishAsync(message, stoppingToken);
                                logger.LogInformation("Published getTempA to MQTT topic {Topic}.", _mqttTopic);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to publish to MQTT topic {Topic}.", _mqttTopic);
                            }
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
