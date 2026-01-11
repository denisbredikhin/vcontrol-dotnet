using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vcontrol.Worker;

public sealed class VclientService(ILogger<VclientService> logger, IOptions<VcontrolOptions> options)
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<VclientProcessResult> RunAsync(string command, CancellationToken ct)
    {
        return await RunAsync([command], ct);
    }

    public async Task<VclientProcessResult> RunAsync(IEnumerable<string> commands, CancellationToken ct)
    {
        var acquired = false;
        var timeoutMs = 20_000;
        try
        {
            if (!_semaphore.Wait(0, ct))
            {
                logger.LogDebug("Another vclient invocation is running; waiting up to {TimeoutSeconds}s.", timeoutMs / 1000);
                var got = await _semaphore.WaitAsync(timeoutMs, ct);
                if (!got)
                {
                    logger.LogError("Timeout ({TimeoutSeconds}s) waiting for previous vclient invocation to finish.", timeoutMs / 1000);
                    return new VclientProcessResult { Stdout = string.Empty, Stderr = "timeout waiting for previous vclient invocation", ExitCode = -3 };
                }
            }
            acquired = true;

        var cmdList = commands.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (cmdList.Count == 0)
        {
            return new VclientProcessResult { Stdout = string.Empty, Stderr = string.Empty, ExitCode = 0 };
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
                return new VclientProcessResult { Stdout = string.Empty, Stderr = "failed to start vclient", ExitCode = -1 };
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();
            var code = proc.ExitCode;
            return new VclientProcessResult { Stdout = stdout, Stderr = stderr, ExitCode = code };
            }
            catch (Exception ex) when (ex is not OperationCanceledException) 
            {
            logger.LogError(ex, "Exception while running vclient commands: {Commands}.", string.Join(',', cmdList));
            return new VclientProcessResult { Stdout = string.Empty, Stderr = ex.Message, ExitCode = -1 };
            }
        }
        finally
        {
            if (acquired) _semaphore.Release();
        }
    }

    public async Task<VclientQueryResult> QueryAsync(IEnumerable<string> commands, CancellationToken ct)
    {
        var proc = await RunAsync(commands, ct);
        if (string.IsNullOrWhiteSpace(proc.Stdout))
        {
            return new VclientQueryResult { Readings = Array.Empty<VclientReading>(), Stderr = proc.Stderr, ExitCode = proc.ExitCode };
        }

        try
        {
            var readings = JsonSerializer.Deserialize<List<VclientReading>>(proc.Stdout) ?? [];
            return new VclientQueryResult { Readings = readings, Stderr = proc.Stderr, ExitCode = proc.ExitCode };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to deserialize vclient output.");
            return new VclientQueryResult { Readings = [], Stderr = proc.Stderr, ExitCode = proc.ExitCode != 0 ? proc.ExitCode : -2 };
        }
    }
}
