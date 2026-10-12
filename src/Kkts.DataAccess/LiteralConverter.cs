using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Kkts.DataAccess;

public static class LiteralConverter
{
    public static bool IsSupported(Type type) =>
        type == typeof(string)
        || type == typeof(bool)
        || type == typeof(Guid)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type.IsEnum
        || new[]
        {
            typeof(byte),
            typeof(sbyte),
            typeof(short),
            typeof(ushort),
            typeof(int),
            typeof(uint),
            typeof(long),
            typeof(ulong),
            typeof(decimal),
            typeof(double),
            typeof(float),
        }.Contains(type);

    static bool ExactDecimal(string raw, decimal value)
    {
        if (raw.Length > 4096)
            return false;
        var parts = raw.Split(['e', 'E']);
        var exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        if (Math.Abs((long)exponent) > 4096)
            return false;
        var dot = parts[0].IndexOf('.');
        var scale = (dot < 0 ? 0 : parts[0].Length - dot - 1) - exponent;
        var original = BigInteger.Parse(parts[0].Replace(".", ""), CultureInfo.InvariantCulture);
        var bits = decimal.GetBits(value);
        var converted =
            ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        if (bits[3] < 0)
            converted = -converted;
        var convertedScale = (bits[3] >> 16) & 0x7f;
        var shared = Math.Max(scale, convertedScale);
        return original * BigInteger.Pow(10, shared - scale)
            == converted * BigInteger.Pow(10, shared - convertedScale);
    }

    static bool ExactBinary(decimal number, double result)
    {
        if (!double.IsFinite(result))
            return false;
        var decimalBits = decimal.GetBits(number);
        var numerator =
            ((BigInteger)(uint)decimalBits[2] << 64)
            | ((BigInteger)(uint)decimalBits[1] << 32)
            | (uint)decimalBits[0];
        if (decimalBits[3] < 0)
            numerator = -numerator;
        var denominator = BigInteger.Pow(10, (decimalBits[3] >> 16) & 0x7f);
        var bits = BitConverter.DoubleToInt64Bits(result);
        var exponent = (int)((bits >> 52) & 0x7ff);
        var mantissa = new BigInteger(bits & 0xfffffffffffffL);
        if (exponent != 0)
            mantissa += BigInteger.One << 52;
        if (bits < 0)
            mantissa = -mantissa;
        var shift = (exponent == 0 ? -1022 : exponent - 1023) - 52;
        return shift >= 0
            ? numerator == (mantissa << shift) * denominator
            : (numerator << -shift) == mantissa * denominator;
    }

    public static object? Convert(JsonElement value, Type declared)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (declared.IsValueType && Nullable.GetUnderlyingType(declared) == null)
                throw new InvalidRequestException(
                    diagnostics:
                    [
                        new(
                            DataAccessErrorCode.InvalidLiteral,
                            "",
                            "Null is incompatible with this field."
                        ),
                    ]
                );
            return null;
        }
        try
        {
            if (type == typeof(string) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
            if (
                type == typeof(bool)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            )
                return value.GetBoolean();
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()!;
                if (type == typeof(Guid) && Guid.TryParseExact(text, "D", out var guid))
                    return guid;
                if (
                    type == typeof(DateTime)
                    && DateTime.TryParseExact(
                        text,
                        "O",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var date
                    )
                )
                    return date;
                if (
                    type == typeof(DateTimeOffset)
                    && DateTimeOffset.TryParseExact(
                        text,
                        "O",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var offset
                    )
                )
                    return offset;
                if (type.IsEnum && Enum.GetNames(type).Contains(text, StringComparer.Ordinal))
                    return Enum.Parse(type, text);
            }
            if (value.ValueKind == JsonValueKind.Number)
            {
                var number = value.GetDecimal();
                if (!ExactDecimal(value.GetRawText(), number))
                    throw new InvalidRequestException(
                        diagnostics:
                        [
                            new(
                                DataAccessErrorCode.InvalidLiteral,
                                "",
                                "Numeric conversion would lose precision."
                            ),
                        ]
                    );
                if (type == typeof(decimal))
                    return number;
                if (type == typeof(double))
                {
                    var result = value.GetDouble();
                    if (double.IsFinite(result) && ExactBinary(number, result))
                        return result;
                }
                if (type == typeof(float))
                {
                    var result = value.GetSingle();
                    if (float.IsFinite(result) && ExactBinary(number, result))
                        return result;
                }
                if (
                    decimal.Truncate(number) == number
                    && type != typeof(double)
                    && type != typeof(float)
                    && type != typeof(bool)
                    && !type.IsEnum
                    && IsSupported(type)
                    && type != typeof(string)
                    && type != typeof(DateTime)
                    && type != typeof(DateTimeOffset)
                    && type != typeof(Guid)
                )
                    return System.Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex)
            when (ex is OverflowException or FormatException or InvalidOperationException)
        {
            throw new InvalidRequestException(
                "Literal must be a lossless value.",
                [new(DataAccessErrorCode.InvalidLiteral, "", "Literal must be a lossless value.")],
                ex
            );
        }
        throw new InvalidRequestException(
            "Literal must be a lossless value.",
            [new(DataAccessErrorCode.InvalidLiteral, "", "Literal must be a lossless value.")]
        );
    }
}
