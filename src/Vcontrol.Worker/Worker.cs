using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq;
using System.Text.Json;

namespace Vcontrol.Worker;

public class Worker(ILogger<Worker> logger, MqttService mqtt, VclientService vclient, IOptions<VcontrolOptions> vcontrolOptions) : BackgroundService
{   
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var commands = vcontrolOptions.Value.Commands ?? new List<string>();
        if (commands.Count == 0)
        {
            logger.LogError("COMMANDS must be provided (comma-separated). Set env COMMANDS or configuration Vcontrol:Commands.");
            throw new InvalidOperationException("COMMANDS is required");
        }
        logger.LogInformation("Starting periodic batch of {Count} commands every 60s on {Host}:{Port}...", commands.Count, vclient.Host, vclient.Port);

        while (true)
        {
            try
            {
                stoppingToken.ThrowIfCancellationRequested();
                var (stdout, stderr, exitCode) = await vclient.RunAsync(commands, stoppingToken);

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    foreach (var item in ParseBatch(stdout))
                    {
                        logger.LogInformation("{Command}: {Payload}", item.command, item.json);
                        var published = await mqtt.PublishToAsync(item.command, item.json, stoppingToken);
                        if (!published)
                        {
                            logger.LogDebug("MQTT publish skipped or failed for {Command}.", item.command);
                        }
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

    private static IEnumerable<(string command, string json)> ParseBatch(string stdout)
    {
        var list = new List<(string command, string json)>();
        var objects = SplitConcatenatedJson(stdout);
        int idx = 0;
        foreach (var obj in objects)
        {
            try
            {
                using var doc = JsonDocument.Parse(obj);
                var root = doc.RootElement;
                string? name = TryGetProperty(root, "name")
                               ?? TryGetProperty(root, "command")
                               ?? TryGetProperty(root, "cmd");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"cmd{idx}";
                }
                list.Add((SanitizeTopicPart(name), obj));
            }
            catch
            {
                // skip non-JSON fragments
            }
            idx++;
        }
        return list;
    }

    private static string? TryGetProperty(JsonElement root, string prop)
    {
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase))
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString();
                return p.Value.ToString();
            }
        }
        return null;
    }

    private static IEnumerable<string> SplitConcatenatedJson(string input)
    {
        var results = new List<string>();
        int depth = 0;
        bool inString = false;
        bool escape = false;
        int start = -1;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else
            {
                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '{')
                {
                    if (depth == 0)
                    {
                        start = i;
                    }
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        results.Add(input.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }
        }

        // Fallback: split by lines for any remaining JSON-like lines
        if (results.Count == 0)
        {
            foreach (var line in input.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
                {
                    results.Add(trimmed);
                }
            }
        }

        return results;
    }

    private static string SanitizeTopicPart(string s)
    {
        // Allow alnum, dash, underscore, slash; replace others with '_'
        var arr = s.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '/' ? ch : '_').ToArray();
        return new string(arr).Trim('/');
    }
}
