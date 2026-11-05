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
            var failure = new VclientQueryResult { Readings = [], ExitCode = -3, Stderr = "timeout waiting for previous native Optolink request" };
            return RecordCompletion(inputs, source, stopwatch, failure);
        }
        try
        {
            var result = await QueryBatchAsync(inputs, ct);
            return RecordCompletion(inputs, source, stopwatch, result);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<VclientQueryResult> QueryBatchAsync(List<string> inputs, CancellationToken ct)
    {
        var codec = new OptolinkValueCodec(configuration.Units);
        List<VclientReading> readings = [];
        List<string> errors = [];
        var exitCode = 0;
        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();
            var (reading, code, message) = await QueryCommandAsync(input, codec, ct);
            readings.Add(reading);
            if (code != 0)
            {
                logger.LogError("Native Optolink command {Command} failed: {Error}", input, message);
                errors.Add($"{input}: {message}");
                if (exitCode == 0 || code < 0)
                {
                    exitCode = code;
                }
            }
        }
        return new VclientQueryResult { Readings = readings, ExitCode = exitCode, Stderr = string.Join(Environment.NewLine, errors) };
    }

    private async Task<(VclientReading Reading, int ExitCode, string? Message)> QueryCommandAsync(
        string input, OptolinkValueCodec codec, CancellationToken ct)
    {
        try
        {
            var command = configuration.Prepare(input, timeProvider);
            var (bytes, unit) = await ExecuteAsync(command, ct);
            if (command.IsWrite && (bytes.Length != 1 || bytes[0] != 0))
            {
                throw new IOException($"Optolink write was not acknowledged successfully: {Convert.ToHexString(bytes)}.");
            }
            var reading = bytes.Length == 0
                ? new VclientReading { Command = input, Value = 0, Raw = "OK" }
                : codec.Decode(input, unit, bytes, command.Bit);
            return (reading, 0, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure("native Optolink operation timed out", -3);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            return Failure(ex.Message, ex is TimeoutException ? -3 : -1);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or NotSupportedException or InvalidDataException)
        {
            return Failure(ex.Message, 1);
        }

        (VclientReading, int, string) Failure(string message, int code)
        {
            var error = "ERR: " + message;
            return (new VclientReading { Command = input, Value = 0, Raw = error, Error = error }, code, message);
        }
    }

    private VclientQueryResult RecordCompletion(List<string> inputs, string source, Stopwatch stopwatch, VclientQueryResult result)
    {
        var commandKey = inputs.Count == 1 ? inputs[0].Split(' ', 2)[0] : "composite-command";
        metrics.RecordVclientRequest(commandKey, source, result.ExitCode == 0 ? "success" : "error");
        metrics.RecordVclientDuration(commandKey, source, stopwatch.Elapsed.TotalSeconds);
        if (result.ExitCode == 0)
        {
            metrics.UpdateVclientLastSuccess(source);
        }
        else
        {
            var reason = result.ExitCode switch { -3 => "timeout", 1 => "invalid_command", _ => "exception" };
            metrics.RecordVclientError("hardware", reason);
        }
        return result;
    }

    private async Task<(byte[] Bytes, string? Unit)> ExecuteAsync(OptolinkCommand command, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= command.Attempts; attempt++)
        {
            var attemptStopwatch = Stopwatch.StartNew();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(command.Timeout);
            var stage = $"opening serial device for {configuration.DeviceId}";
            logger.LogDebug(
                "Starting native Optolink command {Command} on device {DeviceId} using {Protocol}, attempt {Attempt}/{Attempts}, timeout {TimeoutMs} ms.",
                command.Input, configuration.DeviceId, configuration.ProtocolName, attempt, command.Attempts,
                command.Timeout.TotalMilliseconds);
            try
            {
                using var stream = openStream();
                var protocol = new OptolinkProtocol(stream, configuration.ProtocolName, configuration.DeviceId,
                    command.Input, logger, currentStage => stage = currentStage);
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
                var result = await protocol.ExecuteAsync(command, timeout.Token);
                logger.LogDebug(
                    "Native Optolink command {Command} completed on attempt {Attempt}/{Attempts} in {ElapsedMs} ms.",
                    command.Input, attempt, command.Attempts, attemptStopwatch.Elapsed.TotalMilliseconds);
                return result;
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                var failure = new TimeoutException(
                    $"Native Optolink command '{command.Input}' timed out after {command.Timeout.TotalMilliseconds:0} ms during {stage}.",
                    ex);
                LogAttemptFailure(failure, stage, attempt, command.Attempts, attemptStopwatch.Elapsed);
                if (attempt == command.Attempts)
                {
                    throw failure;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is IOException or TimeoutException)
            {
                Exception failure = ex is TimeoutException
                    ? new TimeoutException($"Native Optolink operation timed out during {stage}: {ex.Message}", ex)
                    : new IOException($"Optolink operation failed during {stage}: {ex.Message}", ex);
                LogAttemptFailure(failure, stage, attempt, command.Attempts, attemptStopwatch.Elapsed);
                if (attempt == command.Attempts)
                {
                    throw failure;
                }
            }
        }
        throw new InvalidOperationException("An Optolink command requires at least one attempt.");

        void LogAttemptFailure(Exception exception, string stage, int currentAttempt, int attempts, TimeSpan elapsed)
        {
            var level = currentAttempt < attempts ? LogLevel.Warning : LogLevel.Error;
            logger.Log(level, exception,
                "Native Optolink command {Command} on device {DeviceId} using {Protocol} failed during {Stage} on attempt {Attempt}/{Attempts} after {ElapsedMs} ms.",
                command.Input, configuration.DeviceId, configuration.ProtocolName, stage, currentAttempt, attempts,
                elapsed.TotalMilliseconds);
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
