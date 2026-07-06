namespace Vcontrol.Vclient;

internal static class CommandSource
{
    public static IReadOnlyList<string> Load(CliOptions options)
    {
        var commands = new List<string>();
        commands.AddRange(options.InlineCommands.Where(command => !string.IsNullOrWhiteSpace(command)).Select(command => command.Trim()));

        if (!string.IsNullOrWhiteSpace(options.CommandFile))
        {
            foreach (var line in File.ReadLines(options.CommandFile))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    commands.Add(trimmed);
                }
            }
        }

        commands.AddRange(options.PositionalCommands.Where(command => !string.IsNullOrWhiteSpace(command)).Select(command => command.Trim()));
        return commands;
    }
}
