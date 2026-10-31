// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Vcontrol.Worker;

internal sealed class OptolinkValueCodec(IReadOnlyDictionary<string, XElement> units)
{
    public VclientReading Decode(string command, string? unitName, byte[] bytes, int bit)
    {
        var raw = unitName is null ? string.Join(' ', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)))
            : DecodeUnit(GetUnit(unitName), bytes, bit);
        var firstWord = raw.Split(' ', 2)[0];
        var value = double.TryParse(firstWord, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
        return new VclientReading { Command = command, Value = value, Raw = raw };
    }

    public byte[] Encode(string? unitName, string input, int length, TimeProvider timeProvider)
    {
        if (length is < 1 or > 250)
        {
            throw new InvalidDataException("A write command requires a length between 1 and 250 bytes.");
        }
        if (unitName is null)
        {
            return RequireLength(OptolinkConfiguration.ParseHex(input), length);
        }

        var unit = GetUnit(unitName);
        var type = unit.Element("type")?.Value;
        switch (type)
        {
            case "cycletime":
                return RequireLength(EncodeCycleTime(input), length);
            case "systime":
                return RequireLength(EncodeSystemTime(input, timeProvider), length);
            case "enum":
                var entry = unit.Elements("enum").FirstOrDefault(e => e.Attribute("text")?.Value == input
                    && e.Attribute("bytes") is not null)
                    ?? throw new ArgumentException($"Invalid value '{input}' for unit {unitName}.", nameof(input));
                return RequireLength(OptolinkConfiguration.ParseHex(entry.Attribute("bytes")!.Value), length);
        }

        var expression = Normalize(unit.Element("calc")?.Attribute("set")?.Value);
        if (expression is not ("V" or "V*2" or "V*10" or "V*100" or "V*1000" or "V*3600"))
        {
            throw new NotSupportedException($"Unit '{unitName}' has no supported write conversion; no bytes were sent.");
        }
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
        {
            throw new ArgumentException($"Expected a finite numeric value for unit {unitName}.", nameof(input));
        }
        var multiplier = expression == "V" ? 1 : int.Parse(expression[2..], CultureInfo.InvariantCulture);
        var scaled = value * multiplier;
        if (!double.IsFinite(scaled) || Math.Abs(scaled - Math.Round(scaled)) > 0.000001)
        {
            throw new ArgumentException($"Value '{input}' cannot be represented exactly by unit {unitName}.", nameof(input));
        }
        return EncodeInteger(scaled, type, length);
    }

    private static string DecodeUnit(XElement unit, byte[] bytes, int bit)
    {
        if (bytes.Length == 0)
        {
            throw new InvalidDataException("Empty Optolink value.");
        }
        var type = unit.Element("type")?.Value;
        switch (type)
        {
            case "enum":
                return DecodeEnum(unit, bytes);
            case "cycletime":
                return DecodeCycleTime(bytes);
            case "systime":
                return DecodeSystemTime(bytes);
            case "errstate":
                return DecodeErrorState(unit, bytes);
        }

        var expression = Normalize(unit.Element("calc")?.Attribute("get")?.Value);
        var integerExpression = Normalize(unit.Element("icalc")?.Attribute("get")?.Value);
        if (integerExpression is not null)
        {
            return DecodeIntegerExpression(unit, bytes, bit, integerExpression);
        }
        var value = DecodeNumericExpression(type, bytes, expression);
        return (value.ToString("F6", CultureInfo.InvariantCulture) + " " + (unit.Element("entity")?.Value ?? string.Empty)).TrimEnd();
    }

    private static string DecodeErrorState(XElement unit, byte[] bytes)
    {
        if (bytes.Length % 9 != 0)
        {
            throw new InvalidDataException("Error-state data must consist of 9-byte records.");
        }
        List<string> errors = [];
        for (var index = 0; index < bytes.Length; index += 9)
        {
            errors.Add($"{DecodeSystemTime(bytes[(index + 1)..(index + 9)])} {DecodeEnum(unit, [bytes[index]])} ({bytes[index]:X2})");
        }
        return string.Join('\n', errors);
    }

    private static string DecodeIntegerExpression(XElement unit, byte[] bytes, int bit, string expression)
    {
        if (expression == "(B0&(0x01<<BP))>>BP")
        {
            return DecodeEnum(unit, [(byte)((bytes[0] >> bit) & 1)]);
        }
        if (unit.Element("abbrev")?.Value == "SN"
            && expression == "((((((((((((B0-48)*10)+(B1-48))*10)+(B2-48))*10)+(B3-48))*10)+(B4-48))*10)+(B5-48))*10)+B6-48")
        {
            if (bytes.Length != 7 || bytes.Any(b => b is < 48 or > 57))
            {
                throw new InvalidDataException("A Sachnummer must contain seven ASCII digits.");
            }
            return int.Parse(Encoding.ASCII.GetString(bytes), CultureInfo.InvariantCulture).ToString("X8", CultureInfo.InvariantCulture);
        }
        throw new NotSupportedException($"Unsupported integer conversion '{expression}'.");
    }

    private static double DecodeNumericExpression(string? type, byte[] bytes, string? expression)
    {
        if (expression == "B1")
        {
            if (bytes.Length < 2)
            {
                throw new InvalidDataException("Conversion requires a second byte.");
            }
            return bytes[1];
        }
        if (expression == "(B1*100)+B0")
        {
            if (bytes.Length < 2)
            {
                throw new InvalidDataException("Brenner Stunden requires two bytes.");
            }
            return bytes[1] * 100 + bytes[0];
        }
        var value = DecodeInteger(type, bytes);
        return expression switch
        {
            "V" => value,
            "V/2" => value / 2,
            "V/10" => value / 10,
            "V/100" => value / 100,
            "V/1000" => value / 1000,
            "V/3600" => value / 3600,
            _ => throw new NotSupportedException($"Unsupported numeric conversion '{expression}'.")
        };
    }

    private XElement GetUnit(string name) => units.TryGetValue(name, out var unit) ? unit
        : throw new InvalidDataException($"Unknown Optolink unit '{name}'.");

    private static string? Normalize(string? value) => value is null ? null : string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    private static string DecodeEnum(XElement unit, byte[] bytes)
    {
        foreach (var entry in unit.Elements("enum"))
        {
            var hex = entry.Attribute("bytes")?.Value;
            if (hex is not null && OptolinkConfiguration.ParseHex(hex).AsSpan().SequenceEqual(bytes))
            {
                return entry.Attribute("text")?.Value ?? string.Empty;
            }
        }
        return unit.Elements("enum").FirstOrDefault(e => e.Attribute("bytes") is null)?.Attribute("text")?.Value
            ?? throw new InvalidDataException("No matching enumeration or default value.");
    }

    private static double DecodeInteger(string? type, byte[] bytes)
    {
        Span<byte> padded = stackalloc byte[4];
        padded.Clear();
        bytes.AsSpan(0, Math.Min(bytes.Length, 4)).CopyTo(padded);
        return type switch
        {
            "char" => unchecked((sbyte)padded[0]),
            "uchar" => padded[0],
            "short" => BinaryPrimitives.ReadInt16LittleEndian(padded),
            "ushort" => BinaryPrimitives.ReadUInt16LittleEndian(padded),
            "int" => BinaryPrimitives.ReadInt32LittleEndian(padded),
            "uint" => BinaryPrimitives.ReadUInt32LittleEndian(padded),
            _ => throw new NotSupportedException($"Unsupported numeric type '{type}'.")
        };
    }

    private static byte[] EncodeInteger(double value, string? type, int length)
    {
        var typeLength = type switch
        {
            "char" or "uchar" => 1,
            "short" or "ushort" => 2,
            "int" or "uint" => 4,
            _ => throw new NotSupportedException($"Unsupported numeric type '{type}'.")
        };
        if (length > typeLength)
        {
            throw new InvalidDataException("Write length exceeds the numeric unit width.");
        }
        var signed = type is "char" or "short" or "int";
        var minimum = signed ? -Math.Pow(2, length * 8 - 1) : 0;
        var maximum = Math.Pow(2, length * 8 - (signed ? 1 : 0)) - 1;
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Write value exceeds the configured register range.");
        }
        var result = new byte[length];
        var integer = (long)value;
        for (var index = 0; index < length; index++)
        {
            result[index] = unchecked((byte)(integer >> (index * 8)));
        }
        return result;
    }

    private static string DecodeCycleTime(byte[] bytes)
    {
        if (bytes.Length % 2 != 0)
        {
            throw new InvalidDataException("Cycle-time data must contain start/end pairs.");
        }
        List<string> periods = [];
        for (var index = 0; index < bytes.Length; index += 2)
        {
            periods.Add(bytes[index] == 255 ? $"{index / 2 + 1}:An:--     Aus:--"
                : $"{index / 2 + 1}:An:{FormatTime(bytes[index])}  Aus:{FormatTime(bytes[index + 1])}");
        }
        return string.Join('\n', periods);

        static string FormatTime(byte value) => string.Create(CultureInfo.InvariantCulture, $"{value >> 3:D2}:{(value & 7) * 10:D2}");
    }

    private static byte[] EncodeCycleTime(string input)
    {
        var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length is < 2 or > 8 || tokens.Length % 2 != 0)
        {
            throw new ArgumentException("Supply one to four start/end time pairs.", nameof(input));
        }
        var result = Enumerable.Repeat((byte)255, 8).ToArray();
        for (var index = 0; index < tokens.Length; index += 2)
        {
            var unused = tokens[index] is "-" or "--";
            if (unused != (tokens[index + 1] is "-" or "--"))
            {
                throw new ArgumentException("Unused cycle-time periods require '-- --'.", nameof(input));
            }
            if (!unused)
            {
                result[index] = ParseTime(tokens[index]);
                result[index + 1] = ParseTime(tokens[index + 1]);
            }
        }
        return result;

        static byte ParseTime(string text)
        {
            var parts = text.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var hour)
                || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var minute)
                || hour is < 0 or > 24 || minute is < 0 or > 59 || minute % 10 != 0 || hour == 24 && minute != 0)
            {
                throw new ArgumentException("Cycle times must be HH:mm, in ten-minute steps, from 00:00 to 24:00.", nameof(text));
            }
            return (byte)((hour << 3) | minute / 10);
        }
    }

    private static string DecodeSystemTime(byte[] bytes)
    {
        if (bytes.Length != 8)
        {
            throw new InvalidDataException("System time requires eight BCD bytes.");
        }
        var values = bytes.Select(DecodeBcd).ToArray();
        var offset = TimeZoneInfo.Local.BaseUtcOffset;
        var year = values[0] * 100 + values[1];
        if (year > 0 && values[2] > 0 && values[3] > 0)
        {
            var date = new DateTime(year, values[2], values[3], values[5], values[6], values[7], DateTimeKind.Unspecified);
            offset = TimeZoneInfo.Local.GetUtcOffset(date);
        }
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        offset = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture,
            $"{year:D4}-{values[2]:D2}-{values[3]:D2}T{values[5]:D2}:{values[6]:D2}:{values[7]:D2}{sign}{offset.Hours:D2}{offset.Minutes:D2}");
    }

    private static byte[] EncodeSystemTime(string input, TimeProvider timeProvider)
    {
        var date = timeProvider.GetLocalNow();
        if (input.Length > 0)
        {
            var normalized = input;
            if (input.Length >= 5 && input[^5] is '+' or '-' && input[^3] != ':')
            {
                normalized = input.Insert(input.Length - 2, ":");
            }
            if (!DateTimeOffset.TryParseExact(normalized, ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss'Z'"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                throw new ArgumentException("Expected ISO 8601 system time with a timezone offset.", nameof(input));
            }
            date = TimeZoneInfo.ConvertTime(parsed, timeProvider.LocalTimeZone);
        }
        return [EncodeBcd(date.Year / 100), EncodeBcd(date.Year % 100), EncodeBcd(date.Month), EncodeBcd(date.Day),
            EncodeBcd((int)date.DayOfWeek), EncodeBcd(date.Hour), EncodeBcd(date.Minute), EncodeBcd(date.Second)];
    }

    private static int DecodeBcd(byte value)
    {
        if ((value & 15) > 9 || (value >> 4) > 9)
        {
            throw new InvalidDataException("Invalid BCD system-time byte.");
        }
        return (value >> 4) * 10 + (value & 15);
    }

    private static byte EncodeBcd(int value) => (byte)((value / 10 << 4) | value % 10);

    private static byte[] RequireLength(byte[] bytes, int length) => bytes.Length == length ? bytes
        : throw new ArgumentException($"Expected {length} write bytes, received {bytes.Length}.", nameof(bytes));
}
