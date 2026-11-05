using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Vcontrol.Worker.Tests;

public sealed class OptolinkProtocolTests
{
    [Theory]
    [InlineData("2098", "01F7080002")]
    [InlineData("2094", "01F7080002")]
    [InlineData("2053", "01CB6F0104")]
    public async Task Legacy_protocol_reads_fragmented_serial_data(string id, string request)
    {
        var configuration = OptolinkTestData.Configuration(id);
        var command = configuration.Prepare("getTempA", TimeProvider.System);
        using var stream = new OptolinkTranscriptStream(("04", "0005"), (request, id == "2053" ? "F6" : "CBFF"));
        var protocol = CreateProtocol(stream, configuration.ProtocolName, command.Input);

        var result = await protocol.ExecuteAsync(command, TestContext.Current.CancellationToken);

        Convert.ToHexString(result.Bytes).Should().Be(id == "2053" ? "F6" : "CBFF");
        result.Unit.Should().Be(id == "2053" ? "UT1" : "UT");
        stream.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Kw2_write_sends_header_and_data_before_reading_status()
    {
        var configuration = OptolinkTestData.Configuration("2098");
        var command = configuration.Prepare("setTempWWsoll 45", TimeProvider.System);
        using var stream = new OptolinkTranscriptStream(("04", "05"), ("01F46300012D", "00"));
        var protocol = CreateProtocol(stream, "KW2", command.Input);

        var result = await protocol.ExecuteAsync(command, TestContext.Current.CancellationToken);

        result.Bytes.Should().Equal([0]);
        result.Unit.Should().Be("SR");
        stream.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task P300_initialization_resets_and_enables_the_link()
    {
        using var stream = new OptolinkTranscriptStream(("04", "0005"), ("160000", "06"));
        var protocol = CreateProtocol(stream, "P300", "initialization");

        await protocol.InitializeAsync(TestContext.Current.CancellationToken);

        stream.Writes.Should().Equal("04", "160000");
        stream.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task P300_read_validates_frame_and_acknowledges_response()
    {
        var command = OptolinkTestData.Configuration().Prepare("getTempA", TimeProvider.System);
        using var stream = new OptolinkTranscriptStream(("4105000108000210", "0641070101080002D700EA"), ("06", ""));
        var protocol = CreateProtocol(stream, "P300", command.Input);

        var result = await protocol.ExecuteAsync(command, TestContext.Current.CancellationToken);

        result.Bytes.Should().Equal([0xD7, 0]);
        result.Unit.Should().Be("UT");
        stream.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task P300_write_requires_a_matching_success_response()
    {
        var command = OptolinkTestData.Configuration().Prepare("setTempWWsoll 45", TimeProvider.System);
        using var stream = new OptolinkTranscriptStream(("410600026300012D99", "06410501026300016C"), ("06", ""));
        var protocol = CreateProtocol(stream, "P300", command.Input);

        var result = await protocol.ExecuteAsync(command, TestContext.Current.CancellationToken);

        result.Bytes.Should().Equal([0]);
        result.Unit.Should().Be("SR");
        stream.IsComplete.Should().BeTrue();
    }

    [Theory]
    [InlineData("15", "*P300 request acknowledgement*")]
    [InlineData("0642070101080002D700EA", "*header*")]
    [InlineData("0641070101080002D700EB", "*checksum*")]
    [InlineData("0641070102080002D700EB", "*function/address*")]
    [InlineData("0641070101080102D700EB", "*function/address*")]
    [InlineData("0641060101080001D7E8", "*length*")]
    [InlineData("06410603010800010114", "*device reported an error*")]
    public async Task P300_rejects_invalid_or_unrelated_responses(string response, string message)
    {
        var command = OptolinkTestData.Configuration().Prepare("getTempA", TimeProvider.System);
        using var stream = new OptolinkTranscriptStream(("4105000108000210", response));
        var protocol = CreateProtocol(stream, "P300", command.Input);

        var act = () => protocol.ExecuteAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IOException>().WithMessage(message);
        stream.Writes.Should().ContainSingle();
    }

    [Fact]
    public async Task P300_rejects_invalid_write_status()
    {
        var command = OptolinkTestData.Configuration().Prepare("setTempWWsoll 45", TimeProvider.System);
        using var stream = new OptolinkTranscriptStream(("410600026300012D99", "06410501026300006B"));
        var protocol = CreateProtocol(stream, "P300", command.Input);

        var act = () => protocol.ExecuteAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IOException>().WithMessage("*write acknowledgement*");
        stream.Writes.Should().ContainSingle();
    }

    [Fact]
    public async Task P300_initialization_identifies_the_rejected_acknowledgement_stage()
    {
        using var stream = new OptolinkTranscriptStream(("04", "05"), ("160000", "15"));
        var protocol = CreateProtocol(stream, "P300", "getTempKist");

        var act = () => protocol.InitializeAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IOException>()
            .WithMessage("*0x15*P300 session enable acknowledgement*");
    }

    private static OptolinkProtocol CreateProtocol(Stream stream, string protocolName, string command) =>
        new(stream, protocolName, "test-device", command, NullLogger<OptolinkProtocol>.Instance, _ => { });
}
