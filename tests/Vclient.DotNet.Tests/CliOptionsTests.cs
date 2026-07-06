using AwesomeAssertions;
using Vcontrol.Vclient;

namespace Vclient.DotNet.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void Parse_collects_flags_and_commands()
    {
        var options = CliOptions.Parse(["-h", "example", "-p", "1234", "-c", "foo", "bar"]);

        options.Host.Should().Be("example");
        options.Port.Should().Be(1234);
        options.InlineCommands.Should().ContainSingle().Which.Should().Be("foo");
        options.PositionalCommands.Should().ContainSingle().Which.Should().Be("bar");
    }
}
