namespace Vcontrol.Vclient;

internal sealed record CliOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 3002;
    public bool JsonShort { get; init; }
    public bool JsonLong { get; init; }
    public bool ShowHelp { get; init; }
    public string? CommandFile { get; init; }
    public List<string> InlineCommands { get; init; } = [];
    public List<string> PositionalCommands { get; init; } = [];

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        var inlineCommands = new List<string>();
        var positionalCommands = new List<string>();
        string? host = null;
        int port = 0;
        string? commandFile = null;
        var jsonShort = false;
        var jsonLong = false;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "-h":
                case "--host":
                    host = RequireValue(args, ref index, arg);
                    break;
                case "-p":
                case "--port":
                    port = int.Parse(RequireValue(args, ref index, arg), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "-c":
                case "--command":
                    inlineCommands.Add(RequireValue(args, ref index, arg));
                    break;
                case "-f":
                case "--commandfile":
                    commandFile = RequireValue(args, ref index, arg);
                    break;
                case "-j":
                case "--json-short":
                    jsonShort = true;
                    break;
                case "-J":
                case "--json-long":
                    jsonLong = true;
                    break;
                case "--help":
                    showHelp = true;
                    break;
                default:
                    if (arg.Length > 0 && arg[0] == '-')
                    {
                        throw new ArgumentException($"Unknown argument: {arg}");
                    }

                    positionalCommands.Add(arg);
                    break;
            }
        }

        return options with
        {
            Host = host ?? options.Host,
            Port = port == 0 ? options.Port : port,
            JsonShort = jsonShort,
            JsonLong = jsonLong,
            ShowHelp = showHelp,
            CommandFile = commandFile,
            InlineCommands = inlineCommands,
            PositionalCommands = positionalCommands
        };
    }

    public static string GetHelpText() => "Usage: vclient-dotnet [-h host] [-p port] [-c command] [-f file] [-j|-J] [commands...]";

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {option}");
        }

        index++;
        return args[index];
    }
}
