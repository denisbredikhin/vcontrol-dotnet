using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vcontrol.Worker;

public sealed class VclientService(ILogger<VclientService> logger, IOptions<VcontrolOptions> options)
{
    public string Host => options.Value.Host;
    public int Port => options.Value.Port;

    public async Task<(string stdout, string stderr, int exitCode)> RunAsync(string command, CancellationToken ct)
    {
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
        psi.ArgumentList.Add(command);

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                logger.LogError("Failed to start vclient process.");
                return (string.Empty, "failed to start vclient", -1);
            }

            var stdout = (await proc.StandardOutput.ReadToEndAsync(ct)).Trim();
            var stderr = (await proc.StandardError.ReadToEndAsync(ct)).Trim();
            await proc.WaitForExitAsync(ct);

            var code = proc.ExitCode;
            return (stdout, stderr, code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Exception while running vclient command {Command}.", command);
            return (string.Empty, ex.Message, -1);
        }
    }
}
