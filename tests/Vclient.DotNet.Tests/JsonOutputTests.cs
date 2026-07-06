using System.Text.Json;
using AwesomeAssertions;
using Vcontrol.Vclient;

namespace Vclient.DotNet.Tests;

public sealed class JsonOutputTests
{
    [Fact]
    public void JsonLong_shape_matches_reading_contract()
    {
        var reading = new VclientReading
        {
            Command = "temp",
            Value = 42.5,
            Raw = "42.5 Celsius",
            Error = string.Empty
        };

        var json = JsonSerializer.Serialize(new[] { reading });

        json.Should().Contain("\"command\":\"temp\"");
        json.Should().Contain("\"value\":42.5");
        json.Should().Contain("\"raw\":\"42.5 Celsius\"");
        json.Should().Contain("\"error\":\"\"");
    }
}
