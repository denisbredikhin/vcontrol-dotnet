using AwesomeAssertions;
using System.Globalization;
using System.Xml.Linq;

namespace Vcontrol.Worker.Tests;

public sealed class OptolinkConfigurationTests
{
    public static IEnumerable<object[]> MappedReads()
    {
        var vito = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "OptolinkFixtures", "vito.xml"));
        foreach (var id in new[] { "20CB", "2098", "2094", "2053" })
        {
            foreach (var command in vito.Root!.Element("commands")!.Elements("command"))
            {
                var mapping = command.Elements("device").SingleOrDefault(d => d.Attribute("ID")?.Value == id) ?? command;
                var address = mapping.Element("addr")?.Value;
                var protocolCommand = mapping.Attribute("protocmd")?.Value ?? command.Attribute("protocmd")!.Value;
                if (string.IsNullOrEmpty(address) || address == "dummy" || !protocolCommand.StartsWith("get", StringComparison.Ordinal))
                {
                    continue;
                }
                yield return [id, command.Attribute("name")!.Value, address,
                    int.Parse(mapping.Element("len")!.Value, CultureInfo.InvariantCulture),
                    mapping.Element("unit")?.Value ?? command.Element("unit")!.Value, protocolCommand];
            }
        }
    }

    [Theory]
    [MemberData(nameof(MappedReads))]
    public void Every_enabled_read_mapping_preserves_address_length_and_unit(string id, string input, string address,
        int length, string unit, string protocolCommand)
    {
        var configuration = OptolinkTestData.Configuration(id);
        var opcode = protocolCommand switch
        {
            "getaddr" => id switch { "2053" => "CB", "20CB" => "01", _ => "F7" },
            "getpaddr" => "6E",
            "geteaddr" => "AE",
            "getxaddr" => "C5",
            _ => throw new InvalidOperationException($"Missing test vector for {protocolCommand}.")
        };
        var expected = (id == "20CB" ? "00" : "01") + opcode + address
            + length.ToString("X2", CultureInfo.InvariantCulture) + (id == "2053" ? "04" : "");

        var command = configuration.Prepare(input, TimeProvider.System);

        Convert.ToHexString(command.Instructions.Last(i => i.Operation == "SEND").Bytes).Should().Be(expected);
        command.Instructions.Last().Count.Should().Be(length);
        command.Instructions.Last().Unit.Should().Be(unit);
    }

    public static IEnumerable<object[]> MappedWrites()
    {
        var vito = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "OptolinkFixtures", "vito.xml"));
        foreach (var id in new[] { "20CB", "2098", "2094" })
        {
            foreach (var command in vito.Root!.Element("commands")!.Elements("command").Where(c => c.Attribute("protocmd")?.Value == "setaddr"))
            {
                var mapping = command.Elements("device").SingleOrDefault(d => d.Attribute("ID")?.Value == id) ?? command;
                var address = mapping.Element("addr")?.Value;
                if (string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }
                var unit = mapping.Element("unit")?.Value ?? command.Element("unit")!.Value;
                var argument = unit switch { "CT" => "06:00 22:00", "TI" => "2026-10-31T11:22:33+0000", "USV" => "Heizen", _ => "1" };
                yield return [id, command.Attribute("name")!.Value + " " + argument, address,
                    int.Parse(mapping.Element("len")!.Value, CultureInfo.InvariantCulture)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(MappedWrites))]
    public void Every_enabled_kw2_or_p300_write_preserves_register_width(string id, string input, string address, int length)
    {
        var configuration = OptolinkTestData.Configuration(id);
        var expectedHeader = (id == "20CB" ? "0002" : "01F4") + address + length.ToString("X2", CultureInfo.InvariantCulture);

        var command = configuration.Prepare(input, TimeProvider.System);

        var send = command.Instructions.Where(i => i.Operation == "SEND").TakeLast(2).ToArray();
        Convert.ToHexString(send[0].Bytes).Should().Be(expectedHeader);
        send[1].Bytes.Should().HaveCount(length);
        command.IsWrite.Should().BeTrue();
        command.Attempts.Should().Be(1);
    }

    [Theory]
    [InlineData("20CB", "P300", "0001080002", "UT")]
    [InlineData("2098", "KW2", "01F7080002", "UT")]
    [InlineData("2094", "KW2", "01F7080002", "UT")]
    [InlineData("2053", "GWG", "01CB6F0104", "UT1")]
    public void Configured_device_selects_protocol_and_register_override(string id, string protocol, string request, string unit)
    {
        var configuration = OptolinkTestData.Configuration(id);

        var command = configuration.Prepare("getTempA", TimeProvider.System);

        configuration.ProtocolName.Should().Be(protocol);
        configuration.DeviceId.Should().Be(id);
        Convert.ToHexString(command.Instructions.Last(i => i.Operation == "SEND").Bytes).Should().Be(request);
        command.Instructions.Last().Unit.Should().Be(unit);
    }

    [Theory]
    [InlineData("2098", "getUmschaltventil")]
    [InlineData("2053", "getBetriebArt")]
    [InlineData("2053", "setSystemTime")]
    [InlineData("20CB", "getExtBA")]
    public void Empty_or_absent_device_mapping_disables_command(string id, string input)
    {
        var configuration = OptolinkTestData.Configuration(id);

        var act = () => configuration.Prepare(input, TimeProvider.System);

        act.Should().Throw<NotSupportedException>().WithMessage("*disabled*");
    }

    [Theory]
    [InlineData("getBrennerStunden1", "01AE170204", "BH")]
    [InlineData("getPumpeStatusM1", "016E010104", "BST")]
    [InlineData("getVentilStatus", "016E010104", "BST")]
    [InlineData("getExtBA", "01C5000104", "BST")]
    public void Gwg_read_uses_configured_address_space(string input, string request, string unit)
    {
        var configuration = OptolinkTestData.Configuration("2053");

        var command = configuration.Prepare(input, TimeProvider.System);

        Convert.ToHexString(command.Instructions.Last(i => i.Operation == "SEND").Bytes).Should().Be(request);
        command.Instructions.Last().Unit.Should().Be(unit);
    }

    [Theory]
    [InlineData("get 6F", "01CB6F0104")]
    [InlineData("vget 6F", "01C76F0104")]
    [InlineData("bget 6F", "019E6F0104")]
    [InlineData("pget 6F", "016E6F0104")]
    [InlineData("eget 6F", "01AE6F0104")]
    [InlineData("xget 6F", "01C56F0104")]
    [InlineData("kmget 6F", "01436F0104")]
    public void Gwg_diagnostic_commands_compile_address_bytes(string input, string expected)
    {
        var configuration = OptolinkTestData.Configuration("2053");

        var command = configuration.Prepare(input, TimeProvider.System);

        Convert.ToHexString(command.Instructions.Where(i => i.Operation == "SEND").Skip(1).SelectMany(i => i.Bytes).ToArray())
            .Should().Be(expected);
        command.Instructions.Last().Count.Should().Be(1);
    }

    [Theory]
    [InlineData("20CB", "get 0800", "*empty definition*")]
    [InlineData("2053", "setTempWWsoll 45", "*SETADDR*")]
    [InlineData("2053", "setUmschaltventil 1", "*write conversion*")]
    public void Incomplete_protocol_definitions_report_explicit_limitations(string id, string input, string message)
    {
        var configuration = OptolinkTestData.Configuration(id);

        var act = () => configuration.Prepare(input, TimeProvider.System);

        act.Should().Throw<NotSupportedException>().WithMessage(message);
    }

    [Theory]
    [InlineData("missing", "*Unknown*")]
    [InlineData("getTempA 1", "*does not accept arguments*")]
    public void Invalid_command_input_is_rejected(string input, string message)
    {
        var configuration = OptolinkTestData.Configuration("2094");

        var act = () => configuration.Prepare(input, TimeProvider.System);

        act.Should().Throw<ArgumentException>().WithMessage(message);
    }

    [Fact]
    public void Diagnostic_address_requires_complete_hex_bytes()
    {
        var configuration = OptolinkTestData.Configuration("2094");

        var act = () => configuration.Prepare("get 0", TimeProvider.System);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Gwg_init_contains_eight_enable_sequences_with_delays()
    {
        var configuration = OptolinkTestData.Configuration("2053");

        var command = configuration.Prepare("init", TimeProvider.System);

        command.Instructions.Should().HaveCount(16);
        command.Instructions.Where(i => i.Operation == "SEND").Select(i => Convert.ToHexString(i.Bytes))
            .Should().Equal(Enumerable.Repeat("160000", 8));
        command.Instructions.Where(i => i.Operation == "PAUSE").Select(i => i.Count).Should().Equal(Enumerable.Repeat(100, 8));
    }

    [Fact]
    public void Xml_loader_resolves_local_xinclude()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "OptolinkFixtures", "vcontrold.xml");

        var configuration = OptolinkConfiguration.Load(path);

        configuration.DeviceId.Should().Be("20CB");
        configuration.Prepare("getTempKsoll", TimeProvider.System).Instructions[0].Bytes.Should().Equal([0, 1, 0x55, 0x5A, 2]);
    }
}
