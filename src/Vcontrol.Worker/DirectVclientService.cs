using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace Vcontrol.Worker;

internal sealed class DirectVclientService(
    ILogger<DirectVclientService> logger,
    IOptions<VcontrolOptions> options,
    VcontrolMetrics metrics) : IVclientService, IDisposable
{
    private const string Prompt = "vctrld>";
    private const string Goodbye = "good bye!\n";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan SemaphoreTimeout = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<VclientQueryResult> QueryAsync(IEnumerable<string> commands, string source, CancellationToken ct)
    {
        var commandList = commands.Where(command => !string.IsNullOrWhiteSpace(command)).ToList();
        if (commandList.Count == 0)
        {
            return new VclientQueryResult { Readings = [], Stderr = string.Empty, ExitCode = 0 };
        }

        var commandKey = commandList.Count == 1
            ? commandList[0].Split(' ', 2)[0]
            : "composite-command";
        var stopwatch = Stopwatch.StartNew();
        var acquired = false;
        VclientQueryResult result;

        try
        {
            acquired = await _semaphore.WaitAsync(SemaphoreTimeout, ct);
            if (!acquired)
            {
                result = new VclientQueryResult
                {
                    Readings = [],
                    Stderr = "timeout waiting for previous vclient request",
                    ExitCode = -3
                };
            }
            else
            {
                var readings = await ExecuteAsync(commandList, ct);
                result = new VclientQueryResult { Readings = readings, Stderr = string.Empty, ExitCode = 0 };
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result = new VclientQueryResult { Readings = [], Stderr = "timed out waiting for vcontrold", ExitCode = -3 };
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            logger.LogError(ex, "Direct vcontrold request failed for commands {Commands}.", string.Join(',', commandList));
            result = new VclientQueryResult { Readings = [], Stderr = ex.Message, ExitCode = -1 };
        }
        finally
        {
            if (acquired)
            {
                _semaphore.Release();
            }
        }

        stopwatch.Stop();
        RecordCompletion(commandKey, source, result.ExitCode, stopwatch.Elapsed.TotalSeconds);
        return result;
    }

    public void Dispose() => _semaphore.Dispose();

    private async Task<IReadOnlyList<VclientReading>> ExecuteAsync(List<string> commands, CancellationToken ct)
    {
        using var client = new TcpClient();
        using (var timeout = CreateOperationTimeout(ct))
        {
            await client.ConnectAsync(options.Value.Host, options.Value.Port, timeout.Token);
        }

        using var stream = client.GetStream();
        var pending = new List<byte>();
        _ = await ReadUntilAsync(stream, pending, Prompt, ct);

        var readings = new List<VclientReading>(commands.Count);
        foreach (var command in commands)
        {
            await WriteLineAsync(stream, command, ct);
            var response = await ReadUntilAsync(stream, pending, Prompt, ct);
            readings.Add(ParseReading(command, response));
        }

        await WriteLineAsync(stream, "quit", ct);
        _ = await ReadUntilAsync(stream, pending, Goodbye, ct);
        return readings;
    }

    private static VclientReading ParseReading(string command, string raw)
    {
        var valueText = raw.TrimEnd('\r', '\n', '\t', ' ');
        var reading = new VclientReading { Command = command, Raw = valueText, Value = 0 };
        if (valueText.StartsWith("ERR:", StringComparison.Ordinal))
        {
            reading.Error = valueText;
            return reading;
        }

        var separator = valueText.IndexOf(' ', StringComparison.Ordinal);
        var numberText = separator >= 0 ? valueText[..separator] : valueText;
        if (double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            reading.Value = value;
        }

        return reading;
    }

    private static async Task WriteLineAsync(NetworkStream stream, string value, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\n");
        using var timeout = CreateOperationTimeout(ct);
        await stream.WriteAsync(bytes, timeout.Token);
    }

    private static async Task<string> ReadUntilAsync(NetworkStream stream, List<byte> pending, string terminator, CancellationToken ct)
    {
        var terminatorBytes = Encoding.UTF8.GetBytes(terminator);
        var buffer = new byte[1024];

        while (true)
        {
            var terminatorIndex = IndexOf(pending, terminatorBytes);
            if (terminatorIndex >= 0)
            {
                var response = Encoding.UTF8.GetString(pending.Take(terminatorIndex).ToArray());
                pending.RemoveRange(0, terminatorIndex + terminatorBytes.Length);
                return response;
            }

            using var timeout = CreateOperationTimeout(ct);
            var bytesRead = await stream.ReadAsync(buffer, timeout.Token);
            if (bytesRead == 0)
            {
                throw new IOException("vcontrold closed the connection before completing its response.");
            }

            for (var index = 0; index < bytesRead; index++)
            {
                pending.Add(buffer[index]);
            }
        }
    }

    private static int IndexOf(List<byte> source, byte[] value)
    {
        for (var sourceIndex = 0; sourceIndex <= source.Count - value.Length; sourceIndex++)
        {
            var matches = true;
            for (var valueIndex = 0; valueIndex < value.Length; valueIndex++)
            {
                if (source[sourceIndex + valueIndex] != value[valueIndex])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return sourceIndex;
            }
        }

        return -1;
    }

    private static CancellationTokenSource CreateOperationTimeout(CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OperationTimeout);
        return timeout;
    }

    private void RecordCompletion(string command, string source, int exitCode, double elapsedSeconds)
    {
        metrics.RecordVclientRequest(command, source, exitCode == 0 ? "success" : "error");
        metrics.RecordVclientDuration(command, source, elapsedSeconds);

        if (exitCode == 0)
        {
            metrics.UpdateVclientLastSuccess(source);
            return;
        }

        var reason = exitCode switch
        {
            -1 => "exception",
            -3 => "timeout",
            _ => "internal_error"
        };
        metrics.RecordVclientError("process", reason);
    }
}