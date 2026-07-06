using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace Vcontrol.Vclient;

internal sealed class VclientSession(string host, int port) : IDisposable
{
    private const string Prompt = "vctrld>";
    private const int TimeoutMilliseconds = 25_000;

    private readonly TcpClient _client = new();

    public async Task<IReadOnlyList<VclientReading>> ExecuteAsync(IReadOnlyList<string> commands, CancellationToken cancellationToken)
    {
        _client.ReceiveTimeout = TimeoutMilliseconds;
        _client.SendTimeout = TimeoutMilliseconds;
        await _client.ConnectAsync(host, port, cancellationToken);

        using var stream = _client.GetStream();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true
        };

        var readings = new List<VclientReading>(commands.Count);
        _ = await ReadResponseAsync(stream, cancellationToken);

        foreach (var command in commands)
        {
            await writer.WriteLineAsync(command.AsMemory(), cancellationToken);
            var raw = await ReadResponseAsync(stream, cancellationToken);
            readings.Add(ParseReading(command, raw));
        }

        await writer.WriteLineAsync("quit".AsMemory(), cancellationToken);
        _ = await ReadResponseAsync(stream, cancellationToken);
        return readings;
    }

    public void Dispose() => _client.Dispose();

    internal static VclientReading ParseReading(string command, string raw)
    {
        var trimmed = raw.TrimEnd('\r', '\n', '\t', ' ');
        var reading = new VclientReading
        {
            Command = command,
            Raw = trimmed
        };

        if (trimmed.StartsWith("ERR:", StringComparison.Ordinal))
        {
            reading.Error = trimmed;
            reading.Value = 0;
            return reading;
        }

        var firstToken = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (double.TryParse(firstToken, CultureInfo.InvariantCulture, out var value))
        {
            reading.Value = value;
        }

        return reading;
    }

    private static async Task<string> ReadResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        var output = new MemoryStream();
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            var text = Encoding.UTF8.GetString(output.ToArray());
            var promptIndex = text.IndexOf(Prompt, StringComparison.Ordinal);
            if (promptIndex >= 0)
            {
                return text[..promptIndex];
            }
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }
}
