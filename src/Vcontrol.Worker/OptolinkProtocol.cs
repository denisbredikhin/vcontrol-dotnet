// SPDX-License-Identifier: GPL-3.0-or-later
namespace Vcontrol.Worker;

internal sealed class OptolinkProtocol(Stream stream, string protocolName)
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        if (protocolName != "P300")
        {
            return;
        }
        await stream.WriteAsync(new byte[] { 4 }, ct);
        await WaitForAsync([5], ct);
        await stream.WriteAsync(new byte[] { 0x16, 0, 0 }, ct);
        await RequireByteAsync(6, ct);
    }

    public async Task<(byte[] Bytes, string? Unit)> ExecuteAsync(OptolinkCommand command, CancellationToken ct)
    {
        List<byte> pending = [];
        byte[] result = [];
        string? unit = null;
        foreach (var instruction in command.Instructions)
        {
            switch (instruction.Operation)
            {
                case "SEND":
                    pending.AddRange(instruction.Bytes);
                    break;
                case "WAIT":
                    await FlushAsync(pending, ct);
                    await WaitForAsync(instruction.Bytes, ct);
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
        await FlushAsync(pending, ct);
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, ct);
        return bytes;
    }

    private async Task FlushAsync(List<byte> pending, CancellationToken ct)
    {
        if (pending.Count > 0)
        {
            await stream.WriteAsync(pending.ToArray(), ct);
            pending.Clear();
        }
    }

    private async Task<byte[]> ExchangeP300Async(byte[] payload, int count, bool isWrite, CancellationToken ct)
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
        await stream.WriteAsync(request, ct);
        await RequireByteAsync(6, ct);

        var header = new byte[2];
        await stream.ReadExactlyAsync(header, ct);
        if (header[0] != 0x41 || header[1] is < 5 or > 250)
        {
            throw new IOException("Invalid P300 response header.");
        }
        var response = new byte[header[1] + 1];
        await stream.ReadExactlyAsync(response, ct);
        var checksum = unchecked((byte)(header[1] + Checksum(response.AsSpan(0, response.Length - 1))));
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
        if (isWrite)
        {
            if (header[1] != 5 || response[4] != payload[4] || count != 1)
            {
                throw new IOException("Invalid P300 write acknowledgement.");
            }
            await stream.WriteAsync(new byte[] { 6 }, ct);
            return [0];
        }
        if (response[4] != count || header[1] != 5 + count)
        {
            throw new IOException("P300 response data length mismatch.");
        }
        await stream.WriteAsync(new byte[] { 6 }, ct);
        return response.AsSpan(5, count).ToArray();
    }

    private async Task RequireByteAsync(byte expected, CancellationToken ct)
    {
        var bytes = new byte[1];
        await stream.ReadExactlyAsync(bytes, ct);
        if (bytes[0] != expected)
        {
            throw new IOException($"Unexpected Optolink acknowledgement 0x{bytes[0]:X2}; expected 0x{expected:X2}.");
        }
    }

    private async Task WaitForAsync(byte[] expected, CancellationToken ct)
    {
        if (expected.Length == 0)
        {
            throw new InvalidDataException("Empty Optolink synchronization sequence.");
        }
        var matched = 0;
        var bytes = new byte[1];
        while (matched < expected.Length)
        {
            await stream.ReadExactlyAsync(bytes, ct);
            matched = bytes[0] == expected[matched] ? matched + 1 : bytes[0] == expected[0] ? 1 : 0;
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
