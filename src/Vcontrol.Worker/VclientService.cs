using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vcontrol.Worker;

public sealed class VclientService(ILogger<VclientService> logger, IOptions<VcontrolOptions> options)
{
    public string Host => options.Value.Host;
    public int Port => options.Value.Port;

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
        psi.ArgumentList.Add(Host);
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(Port.ToString());
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
}
