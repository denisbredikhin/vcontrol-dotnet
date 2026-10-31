// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Vcontrol.Worker;

internal sealed class OptolinkConfiguration
{
    private readonly Dictionary<string, XElement> _commands;
    private readonly Dictionary<string, string> _macros;
    private readonly Dictionary<string, XElement> _protocolCommands;

    internal OptolinkConfiguration(XDocument control, XDocument vito)
    {
        var root = control.Root ?? throw new InvalidDataException("Missing V-Control configuration.");
        var deviceId = root.Element("unix")?.Element("config")?.Element("device")?.Attribute("ID")?.Value
            ?? throw new InvalidDataException("Missing configured Optolink device ID.");
        var devices = vito.Root?.Element("devices")?.Elements("device") ?? [];
        var device = devices.SingleOrDefault(d => d.Attribute("ID")?.Value == deviceId)
            ?? throw new InvalidDataException($"Optolink device ID '{deviceId}' is not defined in vito.xml.");
        DeviceId = deviceId;
        ProtocolName = device.Attribute("protocol")?.Value
            ?? throw new InvalidDataException($"Missing protocol for device {DeviceId}.");
        if (ProtocolName is not ("P300" or "KW2" or "GWG"))
        {
            throw new InvalidDataException($"Native Optolink does not support protocol '{ProtocolName}'.");
        }

        var protocol = root.Element("protocols")?.Elements("protocol")
            .SingleOrDefault(p => p.Attribute("name")?.Value == ProtocolName)
            ?? throw new InvalidDataException($"Missing protocol definition for {ProtocolName}.");
        _macros = (protocol.Element("macros")?.Elements("macro") ?? [])
            .ToDictionary(m => RequiredAttribute(m, "name"), m => RequiredElement(m, "command"), StringComparer.Ordinal);
        _protocolCommands = (protocol.Element("commands")?.Elements("command") ?? [])
            .ToDictionary(c => RequiredAttribute(c, "name"), StringComparer.Ordinal);
        _commands = (vito.Root?.Element("commands")?.Elements("command") ?? [])
            .ToDictionary(c => RequiredAttribute(c, "name"), StringComparer.Ordinal);
        Units = (root.Element("units")?.Elements("unit") ?? [])
            .ToDictionary(u => RequiredElement(u, "abbrev"), StringComparer.Ordinal);
    }

    public string DeviceId { get; }
    public string ProtocolName { get; }
    public IReadOnlyDictionary<string, XElement> Units { get; }

    public static OptolinkConfiguration Load(string path)
    {
        var control = LoadDocument(path);
        XNamespace xi = "http://www.w3.org/2003/XInclude";
        var include = control.Root?.Element("extern")?.Element(xi + "include")?.Attribute("href")?.Value;
        if (string.IsNullOrWhiteSpace(include) || Uri.TryCreate(include, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            throw new InvalidDataException("Native Optolink requires a local vito.xml XInclude.");
        }

        var localInclude = uri is { IsFile: true } ? uri.LocalPath : include;
        var vitoPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, localInclude));
        return new OptolinkConfiguration(control, LoadDocument(vitoPath));
    }

    public OptolinkCommand Prepare(string input, TimeProvider timeProvider)
    {
        var parts = input.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
        if (!_commands.TryGetValue(parts[0], out var command))
        {
            throw new ArgumentException($"Unknown Optolink command '{parts[0]}'.", nameof(input));
        }

        var deviceOverride = command.Elements("device").SingleOrDefault(d => d.Attribute("ID")?.Value == DeviceId);
        var mapping = deviceOverride ?? command;
        var address = mapping.Element("addr")?.Value;
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new NotSupportedException($"Command '{parts[0]}' is disabled for device {DeviceId}.");
        }

        var protocolCommandName = deviceOverride?.Attribute("protocmd")?.Value ?? RequiredAttribute(command, "protocmd");
        if (!_protocolCommands.TryGetValue(protocolCommandName, out var protocolCommand))
        {
            throw new NotSupportedException($"Protocol {ProtocolName} does not define '{protocolCommandName}'.");
        }

        var script = protocolCommand.Element("send")?.Value;
        if (string.IsNullOrWhiteSpace(script))
        {
            throw new NotSupportedException($"Protocol command '{protocolCommandName}' has an empty definition.");
        }

        var length = int.Parse(mapping.Element("len")?.Value ?? "0", CultureInfo.InvariantCulture);
        var unit = mapping.Element("unit")?.Value ?? command.Element("unit")?.Value;
        var bit = int.Parse(mapping.Element("bit")?.Value ?? "0", CultureInfo.InvariantCulture);
        var argument = parts.Length == 2 ? parts[1] : string.Empty;
        var isWrite = protocolCommandName == "setaddr";
        var sendBytes = PrepareSendBytes(parts[0], protocolCommandName, unit, argument, length, timeProvider);

        if (length < 0 || length > (ProtocolName == "P300" ? 245 : 250) || bit is < 0 or > 7)
        {
            throw new InvalidDataException($"Invalid length or bit position for '{parts[0]}'.");
        }

        var instructions = Compile(script, address, length, unit, sendBytes, []);
        var timeout = int.Parse(protocolCommand.Element("recvTimeout")?.Value ?? "4000", CultureInfo.InvariantCulture);
        var attempts = int.Parse(protocolCommand.Element("retry")?.Value ?? "1", CultureInfo.InvariantCulture);
        if (timeout is <= 0 or > 30000 || attempts is < 1 or > 3)
        {
            throw new InvalidDataException($"Invalid timeout or retry count for '{protocolCommandName}'.");
        }

        return new OptolinkCommand(input, instructions, bit, isWrite, TimeSpan.FromMilliseconds(timeout),
            isWrite ? 1 : attempts, mapping.Element("precommand")?.Value);
    }

    private byte[] PrepareSendBytes(string input, string protocolCommandName, string? unit, string argument,
        int length, TimeProvider timeProvider)
    {
        var isWrite = protocolCommandName == "setaddr";
        if (isWrite)
        {
            return new OptolinkValueCodec(Units).Encode(unit, argument, length, timeProvider);
        }
        if (protocolCommandName.Contains("testaddr", StringComparison.Ordinal))
        {
            var sendBytes = ParseHex(argument);
            var expectedAddressBytes = ProtocolName == "GWG" ? 1 : 2;
            if (sendBytes.Length != expectedAddressBytes)
            {
                throw new ArgumentException($"Supply {expectedAddressBytes} hexadecimal address byte(s) for '{input}'.", nameof(input));
            }
            return sendBytes;
        }
        if (argument.Length > 0)
        {
            throw new ArgumentException($"Command '{input}' does not accept arguments.", nameof(input));
        }
        return [];
    }

    private List<OptolinkInstruction> Compile(string script, string address, int length, string? unit,
        byte[] sendBytes, HashSet<string> activeMacros)
    {
        List<OptolinkInstruction> result = [];
        foreach (var line in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (_macros.TryGetValue(tokens[0], out var macro))
            {
                if (!activeMacros.Add(tokens[0]))
                {
                    throw new InvalidDataException($"Recursive protocol macro '{tokens[0]}'.");
                }

                result.AddRange(Compile(macro + " " + string.Join(' ', tokens.Skip(1)), address, length, unit, sendBytes, activeMacros));
                activeMacros.Remove(tokens[0]);
                continue;
            }

            var expanded = line.Replace("$addr", address, StringComparison.Ordinal)
                .Replace("$hexlen", length.ToString("X2", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("$len", length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("$unit", unit ?? string.Empty, StringComparison.Ordinal)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result.Add(CreateInstruction(expanded, sendBytes, line));
        }
        return result;
    }

    private OptolinkInstruction CreateInstruction(string[] expanded, byte[] sendBytes, string line)
    {
        switch (expanded[0])
        {
            case "SEND" when expanded.Length > 1 && expanded[1] == "BYTES":
                return new OptolinkInstruction("SEND", sendBytes, 0, null);
            case "SEND":
            case "WAIT":
                return new OptolinkInstruction(expanded[0], ParseHex(string.Join(' ', expanded.Skip(1))), 0, null);
            case "RECV":
            case "PAUSE":
                var count = int.Parse(expanded[1], CultureInfo.InvariantCulture);
                if (count is <= 0 or > 30000 || expanded[0] == "RECV" && count > 250)
                {
                    throw new InvalidDataException($"Invalid protocol instruction '{line}'.");
                }
                return new OptolinkInstruction(expanded[0], [], count, expanded.Length > 2 ? expanded[2] : null);
            default:
                throw new NotSupportedException($"Undefined protocol macro/instruction '{expanded[0]}' in {ProtocolName}.");
        }
    }

    internal static byte[] ParseHex(string text)
    {
        var compact = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        if (compact.Length == 0 || compact.Length > 500)
        {
            throw new ArgumentException("Expected between 1 and 250 hexadecimal bytes.", nameof(text));
        }
        return Convert.FromHexString(compact);
    }

    private static XDocument LoadDocument(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static string RequiredAttribute(XElement element, string name) => element.Attribute(name)?.Value
        ?? throw new InvalidDataException($"Missing '{name}' attribute on {element.Name}.");

    private static string RequiredElement(XElement element, string name) => element.Element(name)?.Value
        ?? throw new InvalidDataException($"Missing '{name}' element on {element.Name}.");
}

internal sealed record OptolinkInstruction(string Operation, byte[] Bytes, int Count, string? Unit);

internal sealed record OptolinkCommand(string Input, IReadOnlyList<OptolinkInstruction> Instructions,
    int Bit, bool IsWrite, TimeSpan Timeout, int Attempts, string? Precommand);
