using System.Text.Json;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class CursorTokenTests
{
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    sealed class IdentityProtector : IContinuationTokenProtector
    {
        public byte[] Protect(byte[] payload) => payload;

        public byte[] Unprotect(byte[] payload) => payload;
    }

    [Theory]
    [MemberData(
        nameof(ExpressionsIntegrationTests.Values),
        MemberType = typeof(ExpressionsIntegrationTests)
    )]
    public void Registered_scalars_round_trip_without_client_literal_conversion(
        string field,
        object value
    )
    {
        var type = typeof(ExpressionsIntegrationTests.Scalars).GetProperty(field)!.PropertyType;
        Assert.Equal(value, CursorScalarCodec.Decode(type, CursorScalarCodec.Encode(type, value)));
    }

    [Fact]
    public void Codec_preserves_decimal_representation_float_bits_and_temporal_precision()
    {
        foreach (
            var value in new[]
            {
                1.2300m,
                decimal.MaxValue,
                decimal.MinValue,
                new decimal(0, 0, 0, true, 2),
            }
        )
            Assert.Equal(
                decimal.GetBits(value),
                decimal.GetBits(
                    Assert.IsType<decimal>(
                        CursorScalarCodec.Decode(
                            typeof(decimal),
                            CursorScalarCodec.Encode(typeof(decimal), value)
                        )
                    )
                )
            );
        foreach (
            var bits in new[]
            {
                long.MinValue,
                0L,
                0x3FB999999999999A,
                0x7FF8000000000001,
                0x7FF0000000000000,
            }
        )
        {
            var value = BitConverter.Int64BitsToDouble(bits);
            Assert.Equal(
                bits,
                BitConverter.DoubleToInt64Bits(
                    Assert.IsType<double>(
                        CursorScalarCodec.Decode(
                            typeof(double),
                            CursorScalarCodec.Encode(typeof(double), value)
                        )
                    )
                )
            );
        }
        foreach (var bits in new[] { int.MinValue, 0, 0x3DCCCCCD, 0x7FC00001, 0x7F800000 })
        {
            var value = BitConverter.Int32BitsToSingle(bits);
            Assert.Equal(
                bits,
                BitConverter.SingleToInt32Bits(
                    Assert.IsType<float>(
                        CursorScalarCodec.Decode(
                            typeof(float),
                            CursorScalarCodec.Encode(typeof(float), value)
                        )
                    )
                )
            );
        }
        foreach (
            var kind in new[] { DateTimeKind.Utc, DateTimeKind.Local, DateTimeKind.Unspecified }
        )
        {
            var value = new DateTime(638000000000000001, kind);
            var result = Assert.IsType<DateTime>(
                CursorScalarCodec.Decode(
                    typeof(DateTime),
                    CursorScalarCodec.Encode(typeof(DateTime), value)
                )
            );
            Assert.Equal(value.Ticks, result.Ticks);
            Assert.Equal(kind, result.Kind);
        }
        var offset = new DateTimeOffset(638000000000000001, TimeSpan.FromHours(7));
        var decoded = Assert.IsType<DateTimeOffset>(
            CursorScalarCodec.Decode(
                typeof(DateTimeOffset),
                CursorScalarCodec.Encode(typeof(DateTimeOffset), offset)
            )
        );
        Assert.Equal(offset.Ticks, decoded.Ticks);
        Assert.Equal(offset.Offset, decoded.Offset);
        Assert.Null(
            CursorScalarCodec.Decode(typeof(int?), CursorScalarCodec.Encode(typeof(int?), null))
        );
        Assert.Throws<NotSupportedException>(() =>
            CursorScalarCodec.Encode(typeof(object), new object())
        );
        Assert.Throws<ArgumentException>(() => CursorScalarCodec.Encode(typeof(int), 1L));
        Assert.Throws<InvalidContinuationTokenException>(() =>
            CursorScalarCodec.Decode(typeof(int), new("System.Int64", "1"))
        );
    }

    static EntityReadPlan<CompilerTests.Employee> Read() =>
        new(row => true, [new(typeof(CompilerTests.Employee).GetProperty("Id")!, false)]);

    [Fact]
    public void Token_lifetime_does_not_slide_and_expiry_is_exact()
    {
        var clock = new Clock();
        var config = new CursorReadConfiguration(
            new IdentityProtector(),
            () => "subject",
            "dataset",
            timeProvider: clock
        );
        var envelope = CursorTokens.New(config, "binding", 1);
        var token = CursorTokens.Protect(
            config,
            envelope,
            new EntitySeekPlan<CompilerTests.Employee>(Read(), [1])
        );
        clock.Now += TimeSpan.FromMinutes(10);
        var resumed = CursorTokens.Unprotect(config, token, "binding", 1, Read());
        var next = CursorTokens.Protect(
            config,
            resumed.Envelope,
            new EntitySeekPlan<CompilerTests.Employee>(Read(), [2])
        );
        clock.Now += TimeSpan.FromMinutes(5);
        Assert.Throws<InvalidContinuationTokenException>(() =>
            CursorTokens.Unprotect(config, next, "binding", 1, Read())
        );
        clock.Now -= TimeSpan.FromTicks(1);
        Assert.Equal(
            2,
            CursorTokens.Unprotect(config, next, "binding", 1, Read()).Seek.Boundary![0]
        );
    }

    [Fact]
    public void Malformed_envelopes_types_and_bindings_fail_generically()
    {
        var config = new CursorReadConfiguration(
            new IdentityProtector(),
            () => "s",
            "d",
            timeProvider: new Clock()
        );
        var valid = CursorTokens.New(config, "binding", 1) with
        {
            Boundary = [new("System.Int32", "1")],
        };
        foreach (
            var envelope in new[]
            {
                valid with
                {
                    Version = 2,
                },
                valid with
                {
                    Binding = "different",
                },
                valid with
                {
                    PolicyVersion = 2,
                },
                valid with
                {
                    Boundary = [],
                },
                valid with
                {
                    Boundary = [new("System.Int64", "1")],
                },
                valid with
                {
                    Boundary = [new("System.Int32", "NaN")],
                },
                valid with
                {
                    Boundary = [new("System.Int32", null)],
                },
                valid with
                {
                    Issued = long.MaxValue,
                },
                valid with
                {
                    Expires = valid.Issued,
                },
            }
        )
        {
            var token = Convert
                .ToBase64String(JsonSerializer.SerializeToUtf8Bytes(envelope))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            Assert.Equal(
                "Invalid continuation token.",
                Assert
                    .Throws<InvalidContinuationTokenException>(() =>
                        CursorTokens.Unprotect(config, token, "binding", 1, Read())
                    )
                    .Message
            );
        }
        foreach (var token in new[] { "", "!", "A", "a=", "e30", new string('a', 16385) })
            Assert.Throws<InvalidContinuationTokenException>(() =>
                CursorTokens.Unprotect(config, token, "binding", 1, Read())
            );
    }

    [Fact]
    public void Encoded_and_decrypted_limits_and_generation_overflow_are_explicit()
    {
        var normal = new CursorReadConfiguration(
            new IdentityProtector(),
            () => "s",
            "d",
            timeProvider: new Clock()
        );
        var envelope = CursorTokens.New(normal, "binding", 1);
        var boundary = new EntitySeekPlan<CompilerTests.Employee>(Read(), [1]);
        var token = CursorTokens.Protect(normal, envelope, boundary);
        var exact = new CursorReadConfiguration(
            new IdentityProtector(),
            () => "s",
            "d",
            maxEncodedTokenSize: token.Length,
            timeProvider: new Clock()
        );
        Assert.Equal(token, CursorTokens.Protect(exact, envelope, boundary));
        Assert.Equal(
            1,
            CursorTokens.Unprotect(exact, token, "binding", 1, Read()).Seek.Boundary![0]
        );
        var shortLimit = new CursorReadConfiguration(
            new IdentityProtector(),
            () => "s",
            "d",
            maxEncodedTokenSize: token.Length - 1,
            timeProvider: new Clock()
        );
        Assert.Throws<InvalidOperationException>(() =>
            CursorTokens.Protect(shortLimit, envelope, boundary)
        );
        Assert.Throws<InvalidContinuationTokenException>(() =>
            CursorTokens.Unprotect(shortLimit, token, "binding", 1, Read())
        );
        var expanded = new CursorReadConfiguration(
            new ExpandingProtector(),
            () => "s",
            "d",
            maxEncodedTokenSize: 100
        );
        Assert.Throws<InvalidContinuationTokenException>(() =>
            CursorTokens.Unprotect(expanded, "AA", "binding", 1, Read())
        );
        var raw = JsonSerializer.SerializeToUtf8Bytes(
            envelope with
            {
                Boundary = [new("System.Int32", "1")],
            }
        );
        var exactPayload = new CursorReadConfiguration(
            new PayloadProtector(raw),
            () => "s",
            "d",
            maxEncodedTokenSize: raw.Length,
            timeProvider: new Clock()
        );
        Assert.Equal(
            1,
            CursorTokens.Unprotect(exactPayload, "AA", "binding", 1, Read()).Seek.Boundary![0]
        );
        var oversizedPayload = new CursorReadConfiguration(
            new PayloadProtector(raw),
            () => "s",
            "d",
            maxEncodedTokenSize: raw.Length - 1,
            timeProvider: new Clock()
        );
        Assert.Throws<InvalidContinuationTokenException>(() =>
            CursorTokens.Unprotect(oversizedPayload, "AA", "binding", 1, Read())
        );
        Assert.Throws<InvalidOperationException>(() =>
            CursorTokens.Unprotect(
                new(new FailingProtector(), () => "s", "d"),
                "AA",
                "b",
                1,
                Read()
            )
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CursorReadConfiguration(new IdentityProtector(), () => "s", "d", TimeSpan.Zero)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CursorReadConfiguration(
                new IdentityProtector(),
                () => "s",
                "d",
                maxEncodedTokenSize: 0
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new CursorReadConfiguration(new IdentityProtector(), () => "s", "")
        );
    }

    sealed class ExpandingProtector : IContinuationTokenProtector
    {
        public byte[] Protect(byte[] payload) => payload;

        public byte[] Unprotect(byte[] payload) => new byte[101];
    }

    sealed class PayloadProtector(byte[] payload) : IContinuationTokenProtector
    {
        public byte[] Protect(byte[] value) => [0];

        public byte[] Unprotect(byte[] value) => payload;
    }

    sealed class FailingProtector : IContinuationTokenProtector
    {
        public byte[] Protect(byte[] payload) =>
            throw new InvalidOperationException("Configuration failure");

        public byte[] Unprotect(byte[] payload) =>
            throw new InvalidOperationException("Configuration failure");
    }

    [Fact]
    public void Fingerprints_bind_schema_property_policy_context_and_subject_without_requiring_unused_keys()
    {
        var trusted = CompilerTests.Context();
        var registry = new ContextRegistry(
            new Dictionary<string, Type>
            {
                ["tenant"] = typeof(int),
                ["department"] = typeof(int),
                ["unused"] = typeof(string),
            }
        );
        var schema = CompilerTests.Schema;
        var captured = CompilerTests.Capture(
            CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
        );
        var config = new CursorReadConfiguration(
            new IdentityProtector(),
            () => "subject",
            "dataset"
        );
        string Binding(
            ResourceSchema<CompilerTests.Employee> resource,
            CapturedPolicy policy,
            TrustedContext context,
            string version = "1",
            string scope = "scope"
        )
        {
            var compiler = new PolicyCompiler<CompilerTests.Employee>(
                resource,
                registry,
                CompilerTests.Reference("TenantId", "tenant"),
                compilerVersion: version
            );
            var bound = compiler.Bind(policy, context, scope);
            return CursorTokens.Binding(
                resource,
                compiler,
                bound,
                context,
                scope,
                config,
                ["Id", "Name"],
                null,
                new(row => true, [new(resource.StableKey.Property, false)]),
                1
            );
        }
        var original = Binding(schema, captured, trusted);
        var missing = new TrustedContext(
            ["manager"],
            [],
            new Dictionary<string, object?> { ["tenant"] = 1, ["department"] = 10 }
        );
        Assert.Equal(original, Binding(schema, captured, missing));
        var nullValue = new TrustedContext(
            ["manager"],
            [],
            new Dictionary<string, object?>
            {
                ["tenant"] = 1,
                ["department"] = 10,
                ["unused"] = null,
            }
        );
        Assert.NotEqual(original, Binding(schema, captured, nullValue));
        var wrong = new TrustedContext(
            ["manager"],
            [],
            new Dictionary<string, object?>
            {
                ["tenant"] = 1,
                ["department"] = 10,
                ["unused"] = new object(),
            }
        );
        Assert.Throws<ArgumentException>(() => Binding(schema, captured, wrong));
        Assert.NotEqual(original, Binding(schema, captured, trusted, version: "2"));
        Assert.NotEqual(
            original,
            Binding(schema, captured with { Scope = "other" }, trusted, scope: "other")
        );
        var altered = new ResourceSchema<CompilerTests.Employee>(
            schema.Resource,
            schema.Version,
            schema
                .Fields.Values.Where(f => f.Name != "Name")
                .Append(
                    ResourceSchema<CompilerTests.Employee>.Field(
                        "Name",
                        row => row.Status,
                        [FieldOperation.Select, FieldOperation.Sort]
                    )
                ),
            "Id"
        );
        Assert.NotEqual(original, Binding(altered, captured, trusted));
        var schemaVersion = new ResourceSchema<CompilerTests.Employee>(
            schema.Resource,
            "2",
            schema.Fields.Values,
            "Id"
        );
        Assert.NotEqual(
            original,
            Binding(
                schemaVersion,
                captured with
                {
                    SchemaVersion = "2",
                    Payload = PolicyJson.Serialize(
                        PolicyJson.Deserialize(captured.Payload) with
                        {
                            SchemaVersion = "2",
                        }
                    ),
                },
                trusted
            )
        );
        var changedPolicy = PolicyJson.Deserialize(captured.Payload);
        changedPolicy = changedPolicy with
        {
            Rules = [changedPolicy.Rules[0] with { Id = "different" }],
        };
        Assert.NotEqual(
            original,
            Binding(
                schema,
                captured with
                {
                    Payload = PolicyJson.Serialize(changedPolicy),
                },
                trusted
            )
        );
    }

    [Fact]
    public void Canonical_json_sorts_members_but_preserves_array_order_and_request_snapshot()
    {
        Assert.Equal(
            CursorTokens.Canonical(JsonSerializer.SerializeToElement(new { a = 1, b = 2 })),
            CursorTokens.Canonical(JsonDocument.Parse("""{ "b": 2, "a": 1 }""").RootElement)
        );
        Assert.NotEqual(
            CursorTokens.Canonical(JsonSerializer.SerializeToElement(new[] { 1, 2 })),
            CursorTokens.Canonical(JsonSerializer.SerializeToElement(new[] { 2, 1 }))
        );
        var filter = new Condition { All = [CompilerTests.Leaf("Status", "eq", "Active")] };
        var snapshot = CursorTokens.Snapshot(filter)!;
        filter.All![0] = CompilerTests.Leaf("Status", "eq", "Inactive");
        Assert.Equal("Active", snapshot.All![0].Literal.GetString());
    }
}
