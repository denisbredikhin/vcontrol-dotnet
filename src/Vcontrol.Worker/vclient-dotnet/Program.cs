using Vcontrol.Vclient;
using System.Net.Sockets;
using System.Text.Json;

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    await Console.Out.WriteLineAsync(CliOptions.GetHelpText());
    return 0;
}

var commands = CommandSource.Load(options);
if (commands.Count == 0)
{
    await Console.Error.WriteLineAsync("No commands were provided.");
    return 1;
}

try
{
    using var session = new VclientSession(options.Host, options.Port);
    var readings = await session.ExecuteAsync(commands, CancellationToken.None);

    if (options.JsonLong)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(readings));
        return 0;
    }

    if (options.JsonShort)
    {
        var payload = readings
            .GroupBy(reading => reading.Command ?? string.Empty)
            .ToDictionary(group => group.Key, group => group.Last().Value);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(payload));
        return 0;
    }

    foreach (var reading in readings)
    {
        await Console.Out.WriteLineAsync($"{reading.Command}:");
        if (!string.IsNullOrWhiteSpace(reading.Error))
        {
            await Console.Error.WriteLineAsync("server error");
            continue;
        }

        if (!string.IsNullOrWhiteSpace(reading.Raw))
        {
            await Console.Out.WriteLineAsync(reading.Raw);
        }
    }

    return 0;
}
catch (OperationCanceledException)
{
    await Console.Error.WriteLineAsync("Timed out waiting for vcontrold.");
    return 2;
}
catch (SocketException ex)
{
    await Console.Error.WriteLineAsync(ex.Message);
    return 3;
}
catch (IOException ex)
{
    await Console.Error.WriteLineAsync(ex.Message);
    return 3;
}
