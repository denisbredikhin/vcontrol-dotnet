using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using Vcontrol.Vclient;

namespace Vclient.DotNet.Tests;

public sealed class VclientSessionEndToEndTests
{
    [Fact]
    public async Task ExecuteAsync_reads_two_commands_from_fake_server()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunServerAsync(listener, TestContext.Current.CancellationToken);

        using var session = new VclientSession("127.0.0.1", port);
        var readings = await session.ExecuteAsync(["get_temp", "get_pressure"], TestContext.Current.CancellationToken);

        readings.Should().HaveCount(2);
        readings[0].Command.Should().Be("get_temp");
        readings[0].Value.Should().Be(21.5);
        readings[0].Raw.Should().Be("21.5 Celsius");
        readings[0].Error.Should().BeNull();

        readings[1].Command.Should().Be("get_pressure");
        readings[1].Value.Should().Be(1.2);
        readings[1].Raw.Should().Be("1.2 bar");
        readings[1].Error.Should().BeNull();

        await serverTask;
    }

    private static async Task RunServerAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true
        };
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        await writer.WriteAsync("vctrld>");

        var firstCommand = (await reader.ReadLineAsync(cancellationToken))!;
        firstCommand.Should().Be("get_temp");
        await writer.WriteAsync("21.5 Celsius\nvctrld>");

        var secondCommand = (await reader.ReadLineAsync(cancellationToken))!;
        secondCommand.Should().Be("get_pressure");
        await writer.WriteAsync("1.2 bar\nvctrld>");

        var quitCommand = (await reader.ReadLineAsync(cancellationToken))!;
        quitCommand.Should().Be("quit");
        await writer.WriteAsync("good bye!\nvctrld>");
    }
}