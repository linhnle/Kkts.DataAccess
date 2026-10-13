using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kkts.DataAccess;

public interface IContinuationTokenProtector
{
    byte[] Protect(byte[] payload);
    byte[] Unprotect(byte[] protectedPayload);
}

public sealed class InvalidContinuationTokenException()
    : DataAccessException(DataAccessErrorCode.InvalidContinuation, "Invalid continuation token.");

public sealed class CursorReadConfiguration
{
    readonly Func<TrustedContext, string>? trustedSubjectId;
    public IContinuationTokenProtector Protector { get; }
    public Func<string> SubjectId { get; }
    public string DatasetId { get; }
    public TimeSpan Lifetime { get; }
    public int MaxEncodedTokenSize { get; }
    public TimeProvider TimeProvider { get; }

    public CursorReadConfiguration(
        IContinuationTokenProtector protector,
        Func<TrustedContext, string> subjectId,
        string datasetId,
        TimeSpan? lifetime = null,
        int maxEncodedTokenSize = 16 * 1024,
        TimeProvider? timeProvider = null
    )
        : this(
            protector,
            () =>
                throw new InvalidOperationException(
                    "The subject requires a captured trusted context."
                ),
            datasetId,
            lifetime,
            maxEncodedTokenSize,
            timeProvider
        )
    {
        trustedSubjectId = subjectId ?? throw new ArgumentNullException(nameof(subjectId));
    }

    internal string GetSubject(TrustedContext context) =>
        trustedSubjectId is { } subject ? subject(context) : SubjectId();

    public CursorReadConfiguration(
        IContinuationTokenProtector protector,
        Func<string> subjectId,
        string datasetId,
        TimeSpan? lifetime = null,
        int maxEncodedTokenSize = 16 * 1024,
        TimeProvider? timeProvider = null
    )
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(subjectId);
        if (string.IsNullOrWhiteSpace(datasetId))
            throw new ArgumentException(
                "An opaque dataset identity is required.",
                nameof(datasetId)
            );
        Lifetime = lifetime ?? TimeSpan.FromMinutes(15);
        if (Lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (maxEncodedTokenSize < 1)
            throw new ArgumentOutOfRangeException(nameof(maxEncodedTokenSize));
        Protector = protector;
        SubjectId = subjectId;
        DatasetId = datasetId;
        MaxEncodedTokenSize = maxEncodedTokenSize;
        TimeProvider = timeProvider ?? TimeProvider.System;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CursorScalar(
    [property: JsonRequired] string Type,
    [property: JsonRequired] string? Value
);

internal static class CursorScalarCodec
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    internal static CursorScalar Encode(Type declared, object? value)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (!LiteralConverter.IsSupported(type))
            throw new ProviderUnsupportedException("Unsupported cursor scalar type.");
        if (value == null)
        {
            if (declared.IsValueType && Nullable.GetUnderlyingType(declared) == null)
                throw new ArgumentException("Null is incompatible with cursor scalar.");
            return new(type.FullName!, null);
        }
        if (!type.IsInstanceOfType(value))
            throw new ArgumentException("Cursor scalar type mismatch.");
        var encoded = value switch
        {
            string text => text,
            bool boolean => boolean ? "true" : "false",
            decimal number => string.Join(
                ",",
                decimal.GetBits(number).Select(bit => bit.ToString(Invariant))
            ),
            double number => BitConverter.DoubleToInt64Bits(number).ToString("X16", Invariant),
            float number => BitConverter.SingleToInt32Bits(number).ToString("X8", Invariant),
            DateTime date =>
                $"{date.Ticks.ToString(Invariant)},{((int)date.Kind).ToString(Invariant)}",
            DateTimeOffset date =>
                $"{date.Ticks.ToString(Invariant)},{date.Offset.Ticks.ToString(Invariant)}",
            Guid guid => guid.ToString("D"),
            Enum enumeration => System.Convert.ToString(
                System.Convert.ChangeType(enumeration, Enum.GetUnderlyingType(type), Invariant),
                Invariant
            )!,
            _ => System.Convert.ToString(value, Invariant)!,
        };
        return new(type.FullName!, encoded);
    }

    internal static object? Decode(Type declared, CursorScalar encoded)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (!LiteralConverter.IsSupported(type) || encoded.Type != type.FullName)
            throw new InvalidContinuationTokenException();
        var text = encoded.Value;
        if (text == null)
        {
            if (declared.IsValueType && Nullable.GetUnderlyingType(declared) == null)
                throw new InvalidContinuationTokenException();
            return null;
        }
        if (type == typeof(string))
            return text;
        if (type == typeof(bool))
            return text switch
            {
                "true" => true,
                "false" => false,
                _ => throw new FormatException(),
            };
        if (type == typeof(decimal))
        {
            var parts = text.Split(',');
            if (parts.Length != 4)
                throw new FormatException();
            return new decimal(parts.Select(p => int.Parse(p, Invariant)).ToArray());
        }
        if (type == typeof(double))
            return BitConverter.Int64BitsToDouble(
                long.Parse(text, NumberStyles.HexNumber, Invariant)
            );
        if (type == typeof(float))
            return BitConverter.Int32BitsToSingle(
                int.Parse(text, NumberStyles.HexNumber, Invariant)
            );
        if (type == typeof(Guid))
            return Guid.ParseExact(text, "D");
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            var parts = text.Split(',');
            if (parts.Length != 2)
                throw new FormatException();
            var ticks = long.Parse(parts[0], Invariant);
            if (type == typeof(DateTime))
                return new DateTime(ticks, (DateTimeKind)int.Parse(parts[1], Invariant));
            return new DateTimeOffset(ticks, TimeSpan.FromTicks(long.Parse(parts[1], Invariant)));
        }
        return type.IsEnum
            ? Enum.ToObject(
                type,
                System.Convert.ChangeType(text, Enum.GetUnderlyingType(type), Invariant)!
            )
            : System.Convert.ChangeType(text, type, Invariant);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CursorEnvelope(
    [property: JsonRequired] int Version,
    [property: JsonRequired] long Issued,
    [property: JsonRequired] long Expires,
    [property: JsonRequired] string Binding,
    [property: JsonRequired] long PolicyVersion,
    [property: JsonRequired] CursorScalar[] Boundary
);

internal static class CursorTokens
{
    internal static Condition? Snapshot(Condition? filter) =>
        filter == null
            ? null
            : JsonSerializer.Deserialize<Condition>(
                JsonSerializer.Serialize(filter, PolicyJson.Options),
                PolicyJson.Options
            )!;

    internal static string Binding<T>(
        ResourceSchema<T> schema,
        PolicyCompiler<T> compiler,
        BoundPolicy<T> bound,
        TrustedContext trusted,
        string scope,
        CursorReadConfiguration config,
        IReadOnlyList<string> selection,
        Condition? filter,
        EntityReadPlan<T> plan,
        int limit
    )
        where T : class
    {
        var subject = config.GetSubject(trusted);
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException(
                "A server-derived authenticated cursor subject is required."
            );
        var values = compiler
            .ContextRegistry.Keys.OrderBy(k => k.Key, StringComparer.Ordinal)
            .Select(k => new
            {
                Key = k.Key,
                Type = k.Value.AssemblyQualifiedName,
                Present = trusted.Values.ContainsKey(k.Key),
                Value = trusted.Values.TryGetValue(k.Key, out var value)
                    ? value == null
                        ? new CursorScalar(
                            (Nullable.GetUnderlyingType(k.Value) ?? k.Value).FullName!,
                            null
                        )
                        : CursorScalarCodec.Encode(k.Value, value)
                    : null,
            })
            .ToArray();
        var identity = JsonSerializer.SerializeToElement(
            new
            {
                Protocol = 2,
                scope,
                schema.Resource,
                SchemaVersion = schema.Version,
                config.DatasetId,
                subject,
                Entity = typeof(T).AssemblyQualifiedName,
                KeyFields = schema.KeyFields.Select(field => new
                {
                    field.Name,
                    Property = field.Property.Name,
                    DeclaringType = field.Property.DeclaringType!.AssemblyQualifiedName,
                    Type = field.Type.AssemblyQualifiedName,
                }),
                Fields = schema
                    .Fields.Values.OrderBy(f => f.Name, StringComparer.Ordinal)
                    .Select(f => new
                    {
                        f.Name,
                        Property = f.Property.Name,
                        DeclaringType = f.Property.DeclaringType!.AssemblyQualifiedName,
                        Type = f.Type.AssemblyQualifiedName,
                        f.Nullable,
                    }),
                selection,
                filter,
                limit,
                Order = plan.Sort.Select(s => new
                {
                    s.Property.Name,
                    s.Descending,
                    NullsFirst = !s.Descending,
                }),
                Roles = trusted.Roles.Order(StringComparer.Ordinal),
                Groups = trusted.Groups.Order(StringComparer.Ordinal),
                values,
                compiler.CursorBindingIdentity,
                PolicyVersion = bound.Version,
                Policy = bound.PolicyIdentity
                    ?? throw new InvalidOperationException(
                        "A captured policy identity is required for cursor binding."
                    ),
            },
            PolicyJson.Options
        );
        return Convert.ToHexString(SHA256.HashData(Canonical(identity)));
    }

    internal static byte[] Canonical(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(writer, value);
        return stream.ToArray();
    }

    static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (
                var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            )
            {
                writer.WritePropertyName(property.Name);
                Write(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
                Write(writer, item);
            writer.WriteEndArray();
        }
        else
            value.WriteTo(writer);
    }

    internal static CursorEnvelope New(
        CursorReadConfiguration config,
        string binding,
        long policyVersion
    )
    {
        var now = config.TimeProvider.GetUtcNow();
        return new(1, now.UtcTicks, now.Add(config.Lifetime).UtcTicks, binding, policyVersion, []);
    }

    internal static string Protect<T>(
        CursorReadConfiguration config,
        CursorEnvelope envelope,
        EntitySeekPlan<T> boundary
    )
        where T : class
    {
        if (boundary.Boundary == null)
            throw new ArgumentException("A continuation boundary is required.", nameof(boundary));
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            envelope with
            {
                Boundary = boundary
                    .Read.Sort.Select(
                        (s, i) =>
                            CursorScalarCodec.Encode(s.Property.PropertyType, boundary.Boundary[i])
                    )
                    .ToArray(),
            }
        );
        if (payload.Length > config.MaxEncodedTokenSize)
            throw new InvalidOperationException(
                "Generated continuation payload exceeds the host limit."
            );
        var token = Convert
            .ToBase64String(config.Protector.Protect(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        if (token.Length > config.MaxEncodedTokenSize)
            throw new InvalidOperationException(
                "Generated continuation token exceeds the host limit."
            );
        return token;
    }

    internal static (CursorEnvelope Envelope, EntitySeekPlan<T> Seek) Unprotect<T>(
        CursorReadConfiguration config,
        string token,
        string binding,
        long policyVersion,
        EntityReadPlan<T> read
    )
        where T : class
    {
        if (
            token.Length == 0
            || token.Length > config.MaxEncodedTokenSize
            || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
            || token.Length % 4 == 1
        )
            throw new InvalidContinuationTokenException();
        byte[] encoded;
        try
        {
            encoded = Convert.FromBase64String(
                token.Replace('-', '+').Replace('_', '/')
                    + new string('=', (4 - token.Length % 4) % 4)
            );
        }
        catch (FormatException)
        {
            throw new InvalidContinuationTokenException();
        }
        var payload = config.Protector.Unprotect(encoded);
        if (payload.Length > config.MaxEncodedTokenSize)
            throw new InvalidContinuationTokenException();
        try
        {
            var envelope =
                JsonSerializer.Deserialize<CursorEnvelope>(payload)
                ?? throw new InvalidContinuationTokenException();
            var now = config.TimeProvider.GetUtcNow().UtcTicks;
            if (
                envelope.Version != 1
                || envelope.Binding != binding
                || envelope.PolicyVersion != policyVersion
                || envelope.Issued > now
                || envelope.Issued < 0
                || envelope.Expires > DateTimeOffset.MaxValue.UtcTicks
                || envelope.Expires <= now
                || envelope.Expires <= envelope.Issued
                || envelope.Boundary == null
                || envelope.Boundary.Length != read.Sort.Count
                || envelope.Boundary.Any(v => v == null)
            )
                throw new InvalidContinuationTokenException();
            var values = read
                .Sort.Select(
                    (sort, i) =>
                        CursorScalarCodec.Decode(sort.Property.PropertyType, envelope.Boundary[i])
                )
                .ToArray();
            return (envelope, new(read, values));
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or FormatException
                        or ArgumentException
                        or OverflowException
            )
        {
            throw new InvalidContinuationTokenException();
        }
    }
}
