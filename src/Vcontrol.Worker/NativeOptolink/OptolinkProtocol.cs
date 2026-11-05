// SPDX-License-Identifier: GPL-3.0-or-later
namespace Vcontrol.Worker;

internal sealed class OptolinkProtocol(
    Stream stream,
    string protocolName,
    string deviceId,
    string command,
    ILogger logger,
    Action<string> stageChanged)
{
    public string CurrentStage { get; private set; } = "protocol startup";

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (protocolName != "P300")
        {
            return;
        }
        SetStage("P300 session reset request");
        await WriteAsync(new byte[] { 4 }, ct);
        await WaitForAsync([5], "P300 session reset response (0x05)", ct);
        SetStage("P300 session enable request");
        await WriteAsync(new byte[] { 0x16, 0, 0 }, ct);
        await RequireByteAsync(6, "P300 session enable acknowledgement", ct);
    }

    public async Task<(byte[] Bytes, string? Unit)> ExecuteAsync(OptolinkCommand command, CancellationToken ct)
    {
        List<byte> pending = [];
        byte[] result = [];
        string? unit = null;
        for (var index = 0; index < command.Instructions.Count; index++)
        {
            var instruction = command.Instructions[index];
            SetStage($"instruction {index + 1}/{command.Instructions.Count} {instruction.Operation}");
            switch (instruction.Operation)
            {
                case "SEND":
                    pending.AddRange(instruction.Bytes);
                    break;
                case "WAIT":
                    await FlushAsync(pending, ct);
                    await WaitForAsync(instruction.Bytes,
                        $"instruction {index + 1}/{command.Instructions.Count} WAIT for {Convert.ToHexString(instruction.Bytes)}", ct);
                    break;
                case "PAUSE":
                    await FlushAsync(pending, ct);
                    await Task.Delay(instruction.Count, ct);
                    break;
                case "RECV":
                    result = protocolName == "P300"
                        ? await ExchangeP300Async(pending.ToArray(), instruction.Count, command.IsWrite, ct)
                        : await ReceiveLegacyAsync(pending, instruction.Count, ct);
                    pending.Clear();
                    unit = instruction.Unit;
                    break;
                default:
                    throw new InvalidDataException($"Unknown Optolink operation '{instruction.Operation}'.");
            }
        }
        await FlushAsync(pending, ct);
        return (result, unit);
    }

    private async Task<byte[]> ReceiveLegacyAsync(List<byte> pending, int count, CancellationToken ct)
    {
        SetStage($"legacy response data ({count} byte(s))");
        await FlushAsync(pending, ct);
        var bytes = new byte[count];
        await ReadExactlyAsync(bytes, ct);
        return bytes;
    }

    private async Task FlushAsync(List<byte> pending, CancellationToken ct)
    {
        if (pending.Count > 0)
        {
            await WriteAsync(pending.ToArray(), ct);
            pending.Clear();
        }
    }

    private async Task<byte[]> ExchangeP300Async(byte[] payload, int count, bool isWrite, CancellationToken ct)
    {
        SetStage("P300 request frame preparation");
        var request = CreateP300Request(payload, isWrite);
        SetStage("P300 request frame transmission");
        await WriteAsync(request, ct);
        await RequireByteAsync(6, "P300 request acknowledgement", ct);

        SetStage("P300 response header");
        var header = new byte[2];
        await ReadExactlyAsync(header, ct);
        if (header[0] != 0x41 || header[1] is < 5 or > 250)
        {
            throw new IOException("Invalid P300 response header.");
        }
        var response = new byte[header[1] + 1];
        SetStage($"P300 response frame ({response.Length} byte(s))");
        await ReadExactlyAsync(response, ct);
        ValidateP300Response(payload, header[1], response);
        var result = GetP300ResponseData(payload, header[1], response, count, isWrite);
        SetStage("P300 response acknowledgement transmission");
        await WriteAsync(new byte[] { 6 }, ct);
        return result;
    }

    private static byte[] CreateP300Request(byte[] payload, bool isWrite)
    {
        if (payload.Length is < 5 or > 250 || payload[0] != 0 || payload[1] != (isWrite ? 2 : 1)
            || payload.Length != (isWrite ? 5 + payload[4] : 5))
        {
            throw new InvalidDataException("Invalid P300 request payload.");
        }
        var request = new byte[payload.Length + 3];
        request[0] = 0x41;
        request[1] = (byte)payload.Length;
        payload.CopyTo(request, 2);
        request[^1] = Checksum(request.AsSpan(1, request.Length - 2));
        return request;
    }

    private static void ValidateP300Response(byte[] payload, byte length, byte[] response)
    {
        var checksum = unchecked((byte)(length + Checksum(response.AsSpan(0, response.Length - 1))));
        if (response[^1] != checksum)
        {
            throw new IOException("P300 response checksum mismatch.");
        }
        if (response[0] == 3)
        {
            throw new IOException($"P300 device reported an error: {Convert.ToHexString(response.AsSpan(0, response.Length - 1))}.");
        }
        if (response[0] != 1 || response[1] != payload[1] || response[2] != payload[2] || response[3] != payload[3])
        {
            throw new IOException("P300 response does not match the requested function/address.");
        }
    }

    private static byte[] GetP300ResponseData(byte[] payload, byte length, byte[] response, int count, bool isWrite)
    {
        if (isWrite)
        {
            if (length != 5 || response[4] != payload[4] || count != 1)
            {
                throw new IOException("Invalid P300 write acknowledgement.");
            }
            return [0];
        }
        if (response[4] != count || length != 5 + count)
        {
            throw new IOException("P300 response data length mismatch.");
        }
        return response.AsSpan(5, count).ToArray();
    }

    private async Task RequireByteAsync(byte expected, string stage, CancellationToken ct)
    {
        SetStage(stage);
        var bytes = new byte[1];
        await ReadExactlyAsync(bytes, ct);
        if (bytes[0] != expected)
        {
            throw new IOException($"Unexpected Optolink acknowledgement 0x{bytes[0]:X2} during {stage}; expected 0x{expected:X2}.");
        }
    }

    private async Task WaitForAsync(byte[] expected, string stage, CancellationToken ct)
    {
        if (expected.Length == 0)
        {
            throw new InvalidDataException("Empty Optolink synchronization sequence.");
        }
        SetStage(stage);
        logger.LogDebug("Waiting for Optolink bytes {Expected} for command {Command} on device {DeviceId}.",
            Convert.ToHexString(expected), command, deviceId);
        var matched = 0;
        var bytes = new byte[1];
        while (matched < expected.Length)
        {
            await ReadExactlyAsync(bytes, ct);
            if (bytes[0] == expected[matched])
            {
                matched++;
            }
            else
            {
                matched = bytes[0] == expected[0] ? 1 : 0;
            }
        }
    }

    private void SetStage(string stage)
    {
        CurrentStage = stage;
        stageChanged(stage);
        logger.LogDebug("Optolink {Protocol} command {Command} on device {DeviceId}: {Stage}.",
            protocolName, command, deviceId, stage);
    }

    private async Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        await stream.WriteAsync(bytes, ct);
        logger.LogTrace("Optolink TX {Protocol} command {Command} on device {DeviceId} during {Stage}: {Bytes}.",
            protocolName, command, deviceId, CurrentStage, Convert.ToHexString(bytes.Span));
    }

    private async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[offset..], ct);
            if (count == 0)
            {
                throw new EndOfStreamException($"Optolink stream ended during {CurrentStage}.");
            }
            logger.LogTrace("Optolink RX {Protocol} command {Command} on device {DeviceId} during {Stage}: {Bytes}.",
                protocolName, command, deviceId, CurrentStage,
                Convert.ToHexString(buffer.Span.Slice(offset, count)));
            offset += count;
        }
    }

    private static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        var sum = 0;
        foreach (var value in bytes)
        {
            sum += value;
        }
        return unchecked((byte)sum);
    }
}
