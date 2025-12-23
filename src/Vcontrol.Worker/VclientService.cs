using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Vcontrol.Worker;

public sealed class VclientService
{
    private readonly ILogger<VclientService> _logger;
    public string Host { get; }
    public int Port { get; }

    public VclientService(ILogger<VclientService> logger)
    {
        _logger = logger;
        Host = Environment.GetEnvironmentVariable("VCONTROLD_HOST")?.Trim() ?? "127.0.0.1";
        var portEnv = Environment.GetEnvironmentVariable("VCONTROLD_PORT");
        Port = int.TryParse(portEnv, out var parsed) ? parsed : 3002;
    }

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
                _logger.LogError("Failed to start vclient process.");
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
            _logger.LogError(ex, "Exception while running vclient command {Command}.", command);
            return (string.Empty, ex.Message, -1);
        }
    }
}
