using AwesomeAssertions;
using Vcontrol.Vclient;

namespace Vclient.DotNet.Tests;

public sealed class VclientSessionTests
{
    [Fact]
    public void ParseReading_marks_error_and_preserves_raw()
    {
        var reading = VclientSession.ParseReading("temp", "ERR: command unknown\r\n");

        reading.Command.Should().Be("temp");
        reading.Error.Should().Be("ERR: command unknown");
        reading.Raw.Should().Be("ERR: command unknown");
        reading.Value.Should().Be(0);
    }

    [Fact]
    public void ParseReading_reads_numeric_value_from_first_token()
    {
        var reading = VclientSession.ParseReading("temp", "12.5 Celsius");

        reading.Command.Should().Be("temp");
        reading.Error.Should().BeNull();
        reading.Raw.Should().Be("12.5 Celsius");
        reading.Value.Should().Be(12.5);
    }
}
