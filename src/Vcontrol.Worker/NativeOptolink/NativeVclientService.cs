// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;

namespace Vcontrol.Worker;

internal sealed class NativeVclientService(
    ILogger<NativeVclientService> logger,
    OptolinkConfiguration configuration,
    VcontrolMetrics metrics,
    Func<Stream> openStream,
    TimeProvider timeProvider) : IVclientService, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<VclientQueryResult> QueryAsync(IEnumerable<string> commands, string source, CancellationToken ct)
    {
        var inputs = commands.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (inputs.Count == 0)
        {
            return new VclientQueryResult { Readings = [], Stderr = string.Empty, ExitCode = 0 };
        }

        var stopwatch = Stopwatch.StartNew();
        var acquired = await _semaphore.WaitAsync(TimeSpan.FromSeconds(20), ct);
        if (!acquired)
        {
            logger.LogError("Timed out waiting for the previous native Optolink request.");
            return Complete([], -3, "timeout waiting for previous native Optolink request");
        }
        try
        {
            var codec = new OptolinkValueCodec(configuration.Units);
            List<VclientReading> readings = [];
            List<string> errors = [];
            var exitCode = 0;
            foreach (var input in inputs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var command = configuration.Prepare(input, timeProvider);
                    var (bytes, unit) = await ExecuteAsync(command, ct);
                    if (command.IsWrite && (bytes.Length != 1 || bytes[0] != 0))
                    {
                        throw new IOException($"Optolink write was not acknowledged successfully: {Convert.ToHexString(bytes)}.");
                    }
                    readings.Add(bytes.Length == 0
                        ? new VclientReading { Command = input, Value = 0, Raw = "OK" }
                        : codec.Decode(input, unit, bytes, command.Bit));
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    AddError(input, "native Optolink operation timed out", -3);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
                {
                    AddError(input, ex.Message, ex is TimeoutException ? -3 : -1);
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException or NotSupportedException or InvalidDataException)
                {
                    AddError(input, ex.Message, 1);
                }
            }
            return Complete(readings, exitCode, string.Join(Environment.NewLine, errors));

            void AddError(string input, string message, int code)
            {
                logger.LogError("Native Optolink command {Command} failed: {Error}", input, message);
                var error = "ERR: " + message;
                readings.Add(new VclientReading { Command = input, Value = 0, Raw = error, Error = error });
                errors.Add($"{input}: {message}");
                if (exitCode == 0 || code < 0)
                {
                    exitCode = code;
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }

        VclientQueryResult Complete(IReadOnlyList<VclientReading> readings, int exitCode, string stderr)
        {
            var commandKey = inputs.Count == 1 ? inputs[0].Split(' ', 2)[0] : "composite-command";
            metrics.RecordVclientRequest(commandKey, source, exitCode == 0 ? "success" : "error");
            metrics.RecordVclientDuration(commandKey, source, stopwatch.Elapsed.TotalSeconds);
            if (exitCode == 0)
            {
                metrics.UpdateVclientLastSuccess(source);
            }
            else
            {
                metrics.RecordVclientError("hardware", exitCode == -3 ? "timeout" : exitCode == 1 ? "invalid_command" : "exception");
            }
            return new VclientQueryResult { Readings = readings, ExitCode = exitCode, Stderr = stderr };
        }
    }

    private async Task<(byte[] Bytes, string? Unit)> ExecuteAsync(OptolinkCommand command, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(command.Timeout);
            try
            {
                using var stream = openStream();
                var protocol = new OptolinkProtocol(stream, configuration.ProtocolName);
                await protocol.InitializeAsync(timeout.Token);
                if (command.Precommand is not null)
                {
                    var precommand = configuration.Prepare(command.Precommand, timeProvider);
                    if (precommand.IsWrite)
                    {
                        throw new InvalidDataException("An Optolink precommand must not write hardware.");
                    }
                    _ = await protocol.ExecuteAsync(precommand, timeout.Token);
                }
                return await protocol.ExecuteAsync(command, timeout.Token);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < command.Attempts
                && ex is IOException or TimeoutException or OperationCanceledException)
            {
                logger.LogWarning(ex, "Retrying native Optolink read {Command} after attempt {Attempt}.", command.Input, attempt);
            }
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
