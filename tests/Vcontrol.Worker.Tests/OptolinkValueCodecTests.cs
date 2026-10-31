using AwesomeAssertions;

namespace Vcontrol.Worker.Tests;

public sealed class OptolinkValueCodecTests
{
    [Theory]
    [InlineData("UT", "CBFF", -5.3, "-5.300000 Grad Celsius")]
    [InlineData("UTH", "D204", 12.34, "12.340000 Grad Celsius")]
    [InlineData("UN", "0F00", 1.5, "1.500000")]
    [InlineData("UT1", "F6", -5, "-5.000000 Grad Celsius")]
    [InlineData("UT1U", "FE", 127, "127.000000 Grad Celsius")]
    [InlineData("UTI", "2D", 45, "45.000000 Grad Celsius")]
    [InlineData("ST", "FF", -1, "-1.000000")]
    [InlineData("CO", "78563412", 305419896, "305419896.000000")]
    [InlineData("CO", "3412", 4660, "4660.000000")]
    [InlineData("VS", "F401", 500, "500.000000 l/h")]
    [InlineData("COL", "D2040000", 1.234, "1.234000")]
    [InlineData("PR", "C800", 100, "100.000000 %")]
    [InlineData("PR1", "64", 100, "100.000000 %")]
    [InlineData("PR2", "0064", 100, "100.000000 %")]
    [InlineData("PR3", "C8", 100, "100.000000 %")]
    [InlineData("CS", "201C0000", 2, "2.000000 Stunden")]
    [InlineData("BH", "220C", 1234, "1234.000000 h")]
    [InlineData("BST", "04", 1, "1")]
    [InlineData("HKT", "04", 0, "drehzahlgeregelt")]
    [InlineData("RT", "03", 2, "2")]
    [InlineData("BA", "03", 0, "H+WW")]
    [InlineData("USV", "01", 0, "Heizen")]
    [InlineData("ESG", "00", 0, "Keine Stoerung")]
    [InlineData("SN", "31323334353637", 0, "0012D687")]
    [InlineData("DT", "2098", 0, "V200KW2 ID=2098 Protokoll:KW")]
    public void Mapped_units_decode_known_wire_values(string unit, string hex, double value, string raw)
    {
        var codec = new OptolinkValueCodec(OptolinkTestData.Configuration().Units);

        var reading = codec.Decode("reading", unit, Convert.FromHexString(hex), 2);

        reading.Value.Should().Be(value);
        reading.Raw.Should().Be(raw);
        reading.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("UT", "-5.3", 2, "CBFF")]
    [InlineData("UTI", "45", 1, "2D")]
    [InlineData("ST", "-128", 1, "80")]
    [InlineData("ST", "127", 1, "7F")]
    [InlineData("CO", "32767", 2, "FF7F")]
    [InlineData("CO", "-2147483648", 4, "00000080")]
    [InlineData("RT", "1", 1, "01")]
    [InlineData("BA", "H+WW", 1, "03")]
    [InlineData("USV", "Heizen", 1, "01")]
    public void Mapped_writes_encode_exact_register_bytes(string unit, string input, int length, string hex)
    {
        var codec = new OptolinkValueCodec(OptolinkTestData.Configuration().Units);

        var bytes = codec.Encode(unit, input, length, TimeProvider.System);

        Convert.ToHexString(bytes).Should().Be(hex);
    }

    [Theory]
    [InlineData("UTI", "256", 1)]
    [InlineData("UTI", "-1", 1)]
    [InlineData("ST", "-129", 1)]
    [InlineData("ST", "128", 1)]
    [InlineData("CO", "65536", 2)]
    [InlineData("UT", "0.01", 2)]
    [InlineData("UT", "NaN", 2)]
    [InlineData("UT", "Infinity", 2)]
    [InlineData("UT", "junk", 2)]
    [InlineData("RT", "99", 1)]
    public void Invalid_numeric_or_enum_writes_are_rejected(string unit, string input, int length)
    {
        var codec = new OptolinkValueCodec(OptolinkTestData.Configuration().Units);

        var act = () => codec.Encode(unit, input, length, TimeProvider.System);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Write_width_cannot_exceed_the_unit_type()
    {
        var codec = new OptolinkValueCodec(OptolinkTestData.Configuration().Units);

        var act = () => codec.Encode("UTI", "45", 2, TimeProvider.System);

        act.Should().Throw<InvalidDataException>().WithMessage("*unit width*");
    }

    [Fact]
    public void Schedule_encodes_time_pairs_and_unused_slots()
    {
        var codec = new OptolinkValueCodec(OptolinkTestData.Configuration().Units);

        var bytes = codec.Encode("CT", "06:00 22:00 -- -- 00:00 24:00", 8, TimeProvider.System);

        Convert.ToHexString(bytes).Should().Be("30B0FFFF00C0FFFF");
    }

    [Fact]
    public void Schedule_decodes_german_compatible_raw_text()
    {
        var codec = new OptolinkValueCodec(OptolinkTestData.Configuration().Units);

        var reading = codec.Decode("schedule", "CT", Convert.FromHexString("30B0FFFFFFFFFFFF"), 0);

        reading.Raw.Should().Be("1:An:06:00  Aus:22:00\n2:An:--     Aus:--\n3:An:--     Aus:--\n4:An:--     Aus:--");
        reading.Value.Should().Be(0);
    }

    [Theory]
    [InlineData("06:05 22:00")]
    [InlineData("24:10 22:00")]
    [InlineData("25:00 22:00")]
    [InlineData("06:00")]
    [InlineData("-- 22:00")]
    [InlineData("00:00 01:00 02:00 03:00 04:00 05:00 06:00 07:00 08:00 09:00")]
    public void Invalid_schedule_writes_are_rejected(string input)
    {
        var codec = new OptolinkValueCodec(OptolinkTestData.Configuration().Units);

        var act = () => codec.Encode("CT", input, 8, TimeProvider.System);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("", "2026103106112233")]
    [InlineData("2026-10-31T11:22:33+0000", "2026103106112233")]
    [InlineData("2026-10-31T13:22:33+02:00", "2026103106112233")]
    public void System_time_encodes_bcd_using_host_timezone(string input, string hex)
    {
        var codec = new OptolinkValueCodec(OptolinkTestData.Configuration().Units);
        var clock = new FixedClock();

        var bytes = codec.Encode("TI", input, 8, clock);

        Convert.ToHexString(bytes).Should().Be(hex);
    }

    [Fact]
    public void Error_history_preserves_timestamp_and_error_description()
    {
        var codec = new OptolinkValueCodec(OptolinkTestData.Configuration().Units);

        var reading = codec.Decode("error", "ES", Convert.FromHexString("D12026103106112233"), 0);

        reading.Raw.Should().StartWith("2026-10-31T11:22:33").And.EndWith(" Brennerstoerung (D1)");
        reading.Error.Should().BeNull();
    }

    [Fact]
    public void Malformed_bcd_is_rejected()
    {
        var codec = new OptolinkValueCodec(OptolinkTestData.Configuration().Units);

        var act = () => codec.Decode("time", "TI", Convert.FromHexString("202A103106112233"), 0);

        act.Should().Throw<InvalidDataException>().WithMessage("*BCD*");
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 31, 11, 22, 33, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
