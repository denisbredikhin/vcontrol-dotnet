using System.Diagnostics;
using Microsoft.Extensions.Hosting;

public class Worker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var portEnv = Environment.GetEnvironmentVariable("VCONTROLD_PORT");
        var port = 3002;
        if (int.TryParse(portEnv, out var parsed)) port = parsed;

        Console.WriteLine($"[Worker] Starting periodic vclient getTempA every 60s on port {port}...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "vclient",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    ArgumentList = { "-h", "127.0.0.1", "-p", port.ToString(), "-c", "getTempA" }
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    Console.WriteLine("[Worker] Failed to start vclient.");
                }
                else
                {
                    var stdout = await proc.StandardOutput.ReadToEndAsync();
                    var stderr = await proc.StandardError.ReadToEndAsync();
                    await proc.WaitForExitAsync(stoppingToken);

                    var output = stdout.Trim();
                    if (!string.IsNullOrEmpty(output))
                        Console.WriteLine($"[Worker] getTempA: {output}");
                    if (!string.IsNullOrWhiteSpace(stderr))
                        Console.WriteLine($"[Worker][stderr]: {stderr.Trim()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Worker] Exception: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // stopping
            }
        }

        Console.WriteLine("[Worker] Stopping.");
    }
}
