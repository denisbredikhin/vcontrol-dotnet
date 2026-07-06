using AwesomeAssertions;
using Vcontrol.Vclient;

namespace Vclient.DotNet.Tests;

public sealed class CommandSourceTests
{
    [Fact]
    public void Load_merges_inline_commands_file_commands_and_positional_commands()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(tempFile, ["file-one", "", " file-two "]);
            var options = CliOptions.Parse(["-c", "inline-one", "-f", tempFile, "positional-one"]);

            var commands = CommandSource.Load(options);

            commands.Should().Equal("inline-one", "file-one", "file-two", "positional-one");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
