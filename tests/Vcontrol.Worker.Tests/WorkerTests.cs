using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Vcontrol.Worker.Tests;

public sealed class WorkerTests
{
    [Fact]
    public async Task Reading_with_error_reports_failure()
    {
        var result = new VclientQueryResult
        {
            ExitCode = 0,
            Readings =
            [
                new VclientReading { Command = "getTemp", Value = 21.5, Raw = "21.5 Celsius" },
                new VclientReading
                {
                    Command = "getBetriebArt",
                    Value = 0,
                    Raw = "ERR: >FRAMER: could not close (3 attempts)\nError opening /dev/ttyUSB0",
                    Error = "ERR: >FRAMER: could not close (3 attempts)\nError opening /dev/ttyUSB0"
                }
            ]
        };

        var snapshot = await RunWorkerOnceAsync(result);

        snapshot.HasReported.Should().BeTrue();
        snapshot.LastSuccess.Should().BeFalse();
        snapshot.LastError.Should().Contain("getBetriebArt").And.Contain("Error opening /dev/ttyUSB0");
    }

    [Fact]
    public async Task Readings_without_error_report_success()
    {
        var result = new VclientQueryResult
        {
            ExitCode = 0,
            Readings = [new VclientReading { Command = "getTemp", Value = 21.5, Raw = "21.5 Celsius" }]
        };

        var snapshot = await RunWorkerOnceAsync(result);

        snapshot.HasReported.Should().BeTrue();
        snapshot.LastSuccess.Should().BeTrue();
        snapshot.LastError.Should().BeNull();
    }

    private static async Task<LastReplySnapshot> RunWorkerOnceAsync(VclientQueryResult result)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var metrics = new VcontrolMetrics(enabled: false);
        var mqtt = new MqttService(NullLogger<MqttService>.Instance, Options.Create(new MqttOptions()), metrics);
        var state = new LastReplyState();
        var vclient = new FakeVclientService(result, stopping);
        var options = Options.Create(new VcontrolOptions { Commands = ["getTemp", "getBetriebArt"], PollSeconds = 3600 });
        using var sut = new TestableWorker(mqtt, vclient, options, state);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.RunAsync(stopping.Token));

        return state.GetSnapshot();
    }

    private sealed class TestableWorker(MqttService mqtt, IVclientService vclient, IOptions<VcontrolOptions> options, LastReplyState state)
        : Worker(NullLogger<Worker>.Instance, mqtt, vclient, options, state)
    {
        public Task RunAsync(CancellationToken stoppingToken) => ExecuteAsync(stoppingToken);
    }

    // Cancels the stopping token once queried so the worker completes exactly one polling cycle.
    private sealed class FakeVclientService(VclientQueryResult result, CancellationTokenSource stopping) : IVclientService
    {
        public async Task<VclientQueryResult> QueryAsync(IEnumerable<string> commands, string source, CancellationToken ct)
        {
            await stopping.CancelAsync();
            return result;
        }
    }
}
