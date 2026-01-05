using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vcontrol.Worker;

public sealed class VclientService(ILogger<VclientService> logger, IOptions<VcontrolOptions> options)
{
    public async Task<(string stdout, string stderr, int exitCode)> RunAsync(string command, CancellationToken ct)
    {
        return await RunAsync([command], ct);
    }

    public async Task<(string stdout, string stderr, int exitCode)> RunAsync(IEnumerable<string> commands, CancellationToken ct)
    {
        var cmdList = commands.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (cmdList.Count == 0)
        {
            return (string.Empty, string.Empty, 0);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "vclient",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--json-long");
        psi.ArgumentList.Add("-h");
        psi.ArgumentList.Add(options.Value.Host);
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(options.Value.Port.ToString());
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(string.Join(',', cmdList));

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                logger.LogError("Failed to start vclient process.");
                return (string.Empty, "failed to start vclient", -1);
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();
            var code = proc.ExitCode;
            return (stdout, stderr, code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) 
        {
            logger.LogError(ex, "Exception while running vclient commands: {Commands}.", string.Join(',', cmdList));
            return (string.Empty, ex.Message, -1);
        }
    }

    public async Task<(IReadOnlyList<VclientReading> readings, string stderr, int exitCode)> QueryAsync(IEnumerable<string> commands, CancellationToken ct)
    {
        var (stdout, stderr, exitCode) = await RunAsync(commands, ct);
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return (Array.Empty<VclientReading>(), stderr, exitCode);
        }

        try
        {
            var readings = JsonSerializer.Deserialize<List<VclientReading>>(stdout);
            if (readings == null)
            {
                return (Array.Empty<VclientReading>(), stderr, exitCode);
            }
            return (readings, stderr, exitCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to deserialize vclient output.");
            return (Array.Empty<VclientReading>(), stderr, exitCode != 0 ? exitCode : -2);
        }
    }
}
