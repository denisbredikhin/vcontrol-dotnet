using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Reflection;

namespace Vcontrol.Worker;

public class Worker(ILogger<Worker> logger, MqttService mqtt, VclientService vclient, IOptions<VcontrolOptions> vcontrolOptions, LastReplyState lastReplyState) : BackgroundService
{   
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var asm = typeof(Worker).Assembly;
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var asmVer = asm.GetName().Version?.ToString() ?? "unknown";
        var fileVer = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown";
        logger.LogInformation("Vcontrol.Worker version {InfoVersion} (Assembly {AssemblyVersion}, File {FileVersion})", infoVer, asmVer, fileVer);

        var commands = vcontrolOptions.Value.Commands ?? [];
        if (commands.Count == 0)
        {
            logger.LogError("COMMANDS must be provided (comma-separated). Set env COMMANDS or configuration Vcontrol:Commands.");
            throw new InvalidOperationException("COMMANDS is required");
        }
        var pollSeconds = vcontrolOptions.Value.PollSeconds <= 0 ? 60 : vcontrolOptions.Value.PollSeconds;
        logger.LogInformation("Starting periodic batch of {Count} commands every {Poll}s on {Host}:{Port}...", commands.Count, pollSeconds, vcontrolOptions.Value.Host, vcontrolOptions.Value.Port);

        while (true)
        {
            try
            {
                stoppingToken.ThrowIfCancellationRequested();
                var result = await vclient.QueryAsync(commands, stoppingToken);

                foreach (var r in result.Readings)
                {
                    var topicPart = SanitizeTopicPart(r.Command ?? "");
                    string payload;
                    if (vcontrolOptions.Value.PublishValueOnly)
                    {
                        payload = r.Value?.ToString("G", CultureInfo.InvariantCulture) ?? string.Empty;
                    }
                    else
                    {
                        payload = JsonSerializer.Serialize(r);
                    }
                    logger.LogInformation("{Command}: {Payload}", r.Command, payload);
                    var published = await mqtt.PublishToAsync(topicPart, payload, stoppingToken);
                    if (!published)
                    {
                        logger.LogDebug("MQTT publish skipped or failed for {Command}.", r.Command);
                    }
                }

                if (!string.IsNullOrWhiteSpace(result.Stderr))
                {
                    logger.LogWarning("vclient stderr: {Stderr}", result.Stderr);
                }

                if (result.ExitCode != 0)
                {
                    logger.LogWarning("vclient exited with code {Code}.", result.ExitCode);
                    lastReplyState.ReportFailure(result.ExitCode, string.IsNullOrWhiteSpace(result.Stderr) ? "vclient exited with non-zero exit code." : result.Stderr);
                }
                else
                {
                    lastReplyState.ReportSuccess(result.ExitCode, result.Stderr);
                }

                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Worker exception.");
                lastReplyState.ReportFailure(-1, ex.Message);
            }
        }
    }

    private static string SanitizeTopicPart(string s)
    {
        // Allow alnum, dash, underscore, slash; replace others with '_'
        var arr = s.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '/' ? ch : '_').ToArray();
        return new string(arr).Trim('/');
    }
}
