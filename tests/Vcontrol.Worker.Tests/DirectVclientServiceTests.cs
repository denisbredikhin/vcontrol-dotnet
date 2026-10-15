using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Vcontrol.Worker.Tests;

public sealed class DirectVclientServiceTests
{
    [Fact]
    public async Task Invalid_port_fails_host_startup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOptions<VcontrolOptions>()
            .Configure(options => options.Port = 65536)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QueryAsync_sends_commands_and_returns_readings_from_vcontrold()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = RunServerAsync(listener, TestContext.Current.CancellationToken);
        using var metrics = new VcontrolMetrics(enabled: false);
        using var service = new DirectVclientService(
            NullLogger<DirectVclientService>.Instance,
            Options.Create(new VcontrolOptions { Host = "127.0.0.1", Port = port }),
            metrics);

        var result = await service.QueryAsync(["get_temp", "unknown"], "timer", TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        result.Stderr.Should().BeEmpty();
        result.Readings.Should().HaveCount(2);
        result.Readings[0].Command.Should().Be("get_temp");
        result.Readings[0].Value.Should().Be(21.5);
        result.Readings[0].Raw.Should().Be("21.5 Celsius");
        result.Readings[0].Error.Should().BeNull();
        result.Readings[1].Command.Should().Be("unknown");
        result.Readings[1].Error.Should().Be("ERR: command unknown");

        await serverTask;
    }

    private static async Task RunServerAsync(TcpListener listener, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        await WriteAsync(stream, "vctrld>", ct);
        (await reader.ReadLineAsync(ct)).Should().Be("get_temp");
        await WriteAsync(stream, "21.5 Celsius\nvctrld>", ct);
        (await reader.ReadLineAsync(ct)).Should().Be("unknown");
        await WriteAsync(stream, "ERR: command unknown\nvctrld>", ct);
        (await reader.ReadLineAsync(ct)).Should().Be("quit");
        await WriteAsync(stream, "good bye!\n", ct);
    }

    private static async Task WriteAsync(NetworkStream stream, string value, CancellationToken ct)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(value), ct);
    }
}