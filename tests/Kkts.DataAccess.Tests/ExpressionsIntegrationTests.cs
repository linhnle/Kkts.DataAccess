using System.Globalization;
using System.Linq.Expressions;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class ExpressionsIntegrationTests
{
    public enum State
    {
        Active,
        Inactive,
    }

    public sealed class Scalars
    {
        public int Id { get; set; }
        public byte Byte { get; set; }
        public sbyte SByte { get; set; }
        public short Short { get; set; }
        public ushort UShort { get; set; }
        public uint UInt { get; set; }
        public long Long { get; set; }
        public ulong ULong { get; set; }
        public decimal Decimal { get; set; }
        public float Float { get; set; }
        public double Double { get; set; }
        public bool Boolean { get; set; }
        public Guid Guid { get; set; }
        public DateTime Date { get; set; }
        public DateTimeOffset Offset { get; set; }
        public State State { get; set; }
        public State? NullableState { get; set; }
        public int? Nullable { get; set; }
        public string? Text { get; set; }
        public int Throwing =>
            throw new InvalidOperationException("Getter invoked during analysis");
    }

    static ResourceField Field<TValue>(string name) =>
        ResourceSchema<Scalars>.Field(
            name,
            Expression.Lambda<Func<Scalars, TValue>>(
                Expression.Property(Parameter, name),
                Parameter
            ),
            []
        );

    static readonly ParameterExpression Parameter = Expression.Parameter(typeof(Scalars), "row");
    static readonly ResourceSchema<Scalars> Schema = new(
        "Scalars",
        "1",
        typeof(Scalars)
            .GetProperties()
            .Select(p =>
                (ResourceField)
                    typeof(ExpressionsIntegrationTests)
                        .GetMethod(
                            nameof(Field),
                            System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Static
                        )!
                        .MakeGenericMethod(p.PropertyType)
                        .Invoke(null, [p.Name])!
            ),
        ["Id"]
    );
    static readonly ContextRegistry Registry = new(new Dictionary<string, Type>());
    static readonly TrustedContext Trusted = new([], [], new Dictionary<string, object?>());

    static PolicyCompiler<Scalars> Compiler(PolicyLimits? limits = null) =>
        new(Schema, Registry, CompilerTests.Leaf("Id", "eq", 1), limits);

    public static IEnumerable<object[]> Values()
    {
        yield return ["Id", 7];
        yield return ["Byte", (byte)7];
        yield return ["SByte", (sbyte)-7];
        yield return ["Short", (short)-7];
        yield return ["UShort", (ushort)7];
        yield return ["UInt", uint.MaxValue];
        yield return ["Long", long.MinValue];
        yield return ["ULong", ulong.MaxValue];
        yield return ["Decimal", 123456789.123456789m];
        yield return ["Float", 1.5f];
        yield return ["Double", 1.5d];
        yield return ["Boolean", true];
        yield return ["Guid", Guid.Parse("a40e2b14-0e8c-4c1b-b130-58f0f09a55f6")];
        yield return
        [
            "Date",
            new DateTime(2026, 10, 12, 1, 2, 3, DateTimeKind.Utc).AddTicks(1234567),
        ];
        yield return
        [
            "Offset",
            new DateTimeOffset(2026, 10, 12, 1, 2, 3, TimeSpan.FromHours(7)).AddTicks(1234567),
        ];
        yield return ["State", State.Active];
        yield return ["NullableState", State.Active];
        yield return ["Nullable", 7];
        yield return ["Text", "a'\"\\\ncontains $now and f0 = 1"];
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void Every_scalar_preserves_literal_and_typed_context_comparisons(
        string field,
        object value
    )
    {
        var property = typeof(Scalars).GetProperty(field)!;
        var row = new Scalars();
        property.SetValue(row, value);
        var compiler = Compiler();
        object jsonValue = value is State state ? state.ToString() : value;
        foreach (var op in OperatorCatalog.For(property.PropertyType))
        {
            Condition condition = op is "isNull" or "isNotNull"
                ? new() { Field = field, Operator = op }
                : CompilerTests.Leaf(field, op, op == "in" ? new[] { jsonValue } : jsonValue);
            var predicate = compiler.Predicate(condition, Trusted).Compile();
            var expected =
                op
                is "eq"
                    or "le"
                    or "ge"
                    or "in"
                    or "isNotNull"
                    or "contains"
                    or "startsWith"
                    or "endsWith";
            Assert.Equal(expected, predicate(row));
        }
        var registry = new ContextRegistry(
            new Dictionary<string, Type> { ["value"] = property.PropertyType }
        );
        var context = new TrustedContext(
            [],
            [],
            new Dictionary<string, object?> { ["value"] = value }
        );
        var bound = new PolicyCompiler<Scalars>(
            Schema,
            registry,
            CompilerTests.Leaf("Id", "eq", 1)
        );
        Assert.True(
            bound.Predicate(CompilerTests.Reference(field, "value"), context).Compile()(row)
        );
    }

    [Fact]
    public void Null_membership_and_negated_string_comparisons_preserve_truth_tables()
    {
        var compiler = Compiler();
        foreach (var field in new[] { "Nullable", "NullableState", "Text" })
        {
            Assert.True(
                compiler.Predicate(CompilerTests.Leaf(field, "eq", null), Trusted).Compile()(new())
            );
            Assert.False(
                compiler.Predicate(CompilerTests.Leaf(field, "ne", null), Trusted).Compile()(new())
            );
            Assert.True(
                compiler
                    .Predicate(CompilerTests.Leaf(field, "in", new object?[] { null }), Trusted)
                    .Compile()(new())
            );
        }
        foreach (var op in new[] { "contains", "startsWith", "endsWith" })
        {
            var condition = CompilerTests.Leaf("Text", op, "x");
            Assert.False(compiler.Predicate(condition, Trusted).Compile()(new()));
            Assert.True(compiler.Predicate(new() { Not = condition }, Trusted).Compile()(new()));
            Assert.False(
                compiler
                    .Predicate(CompilerTests.Leaf("Text", op, null), Trusted)
                    .Compile()(new() { Text = "x" })
            );
        }
        foreach (var op in new[] { "lt", "le", "gt", "ge" })
            Assert.False(
                compiler
                    .Predicate(CompilerTests.Leaf("Nullable", op, null), Trusted)
                    .Compile()(new() { Nullable = 2 })
            );
        foreach (var field in new[] { "Id", "Boolean", "Date", "State", "Guid" })
        {
            Assert.False(
                compiler
                    .Predicate(new() { Field = field, Operator = "isNull" }, Trusted)
                    .Compile()(new())
            );
            Assert.True(
                compiler
                    .Predicate(new() { Field = field, Operator = "isNotNull" }, Trusted)
                    .Compile()(new())
            );
        }
    }

    [Fact]
    public void Public_aliases_are_exact_and_client_contexts_do_not_grant_policy_fields()
    {
        var schema = new ResourceSchema<Scalars>(
            "Alias",
            "1",
            [
                ResourceSchema<Scalars>.Field("key", r => r.Id, []),
                ResourceSchema<Scalars>.Field(
                    "display name'; $now",
                    r => r.Text,
                    [FieldOperation.Filter],
                    ["eq"]
                ),
            ],
            ["key"]
        );
        var compiler = new PolicyCompiler<Scalars>(
            schema,
            Registry,
            CompilerTests.Leaf("key", "eq", 1)
        );
        var condition = CompilerTests.Leaf("display name'; $now", "eq", "quoted'\\text");
        Assert.True(
            compiler.Predicate(condition, Trusted).Compile()(new() { Text = "quoted'\\text" })
        );
        Assert.Throws<InvalidRequestException>(() =>
            compiler.Predicate(CompilerTests.Leaf("Text", "eq", "x"), Trusted)
        );
        Assert.Throws<InvalidRequestException>(() =>
            compiler.Predicate(CompilerTests.Leaf("Key", "eq", 1), Trusted)
        );
        Assert.Throws<InvalidRequestException>(() =>
            compiler.Predicate(CompilerTests.Leaf("display name'; $now", "contains", "x"), Trusted)
        );
        Assert.Throws<AuthorizationException>(() =>
            compiler.ClientPredicate(
                CompilerTests.Leaf("key", "eq", 1),
                Trusted,
                ["display name'; $now"]
            )
        );
        Assert.True(
            compiler
                .ClientPredicate(condition, Trusted, ["display name'; $now"])
                .Compile()(new() { Text = "quoted'\\text" })
        );
        Assert.Throws<ArgumentException>(() =>
            new ResourceSchema<Scalars>(
                "Alias",
                "1",
                [
                    ResourceSchema<Scalars>.Field("key", r => r.Id, []),
                    ResourceSchema<Scalars>.Field("KEY", r => r.Id, []),
                ],
                ["key"]
            )
        );
    }

    [Fact]
    public void Analysis_has_original_nested_paths_and_never_invokes_getters_or_context()
    {
        var validator = new PolicyValidator<Scalars>(Schema, Registry);
        PolicyDefinition Definition(Condition condition) =>
            new(
                "Scalars",
                "1",
                [new("rule", "Scalars", "Read", Effect.Allow, new([], []), condition)],
                []
            );
        Assert.Empty(validator.Validate(Definition(CompilerTests.Leaf("Throwing", "eq", 1))));
        var errors = validator.Validate(
            Definition(new() { All = [new() { Not = CompilerTests.Leaf("Id", "eq", "bad") }] })
        );
        Assert.Single(errors);
        Assert.Equal("/rules/0/condition/all/0/not/literal", errors[0].Path);
        var registry = new ContextRegistry(
            new Dictionary<string, Type> { ["missing"] = typeof(int) }
        );
        Assert.Empty(
            new PolicyValidator<Scalars>(Schema, registry).Validate(
                Definition(CompilerTests.Reference("Id", "missing"))
            )
        );
        Assert.Throws<AuthorizationException>(() =>
            new PolicyCompiler<Scalars>(
                Schema,
                registry,
                CompilerTests.Leaf("Id", "eq", 1)
            ).Predicate(CompilerTests.Reference("Id", "missing"), Trusted)
        );
    }

    [Fact]
    public void Subject_context_is_eagerly_bound_and_never_retained_between_users()
    {
        var registry = new ContextRegistry(
            new Dictionary<string, Type> { ["left"] = typeof(int), ["missing"] = typeof(int) }
        );
        var compiler = new PolicyCompiler<Scalars>(
            Schema,
            registry,
            CompilerTests.Leaf("Id", "eq", 1)
        );
        var subjectCondition = CompilerTests.Leaf("left", "eq", 2);
        PolicyDefinition Definition(Condition condition) =>
            new(
                "Scalars",
                "1",
                [new("rule", "Scalars", "Read", Effect.Allow, new([], [], condition), null)],
                []
            );
        var captured = new CapturedPolicy(
            "scope",
            "Scalars",
            "1",
            1,
            PolicyJson.Serialize(Definition(subjectCondition))
        );
        TrustedContext Context(int value) =>
            new([], [], new Dictionary<string, object?> { ["left"] = value });
        Assert.True(
            compiler.Bind(captured, Context(2), "scope").Predicate.Compile()(new() { Id = 1 })
        );
        Assert.Throws<AuthorizationException>(() => compiler.Bind(captured, Context(3), "scope"));
        var shortCircuit = new Condition
        {
            Any = [subjectCondition, CompilerTests.Leaf("missing", "eq", 1)],
        };
        var other = captured with
        {
            Version = 2,
            Payload = PolicyJson.Serialize(Definition(shortCircuit)),
        };
        Assert.Throws<AuthorizationException>(() => compiler.Bind(other, Context(2), "scope"));
    }

    [Fact]
    public void Translation_expansion_keeps_original_limits_and_invariant_conversion()
    {
        var limits = new PolicyLimits(MaxDepth: 2, MaxNodes: 3, MaxCollection: 2);
        var condition = new Condition
        {
            All =
            [
                CompilerTests.Leaf("Text", "contains", "x"),
                CompilerTests.Leaf("Nullable", "ge", 1),
            ],
        };
        Assert.True(
            Compiler(limits)
                .Predicate(condition, Trusted)
                .Compile()(new() { Text = "x", Nullable = 2 })
        );
        Assert.Throws<InvalidRequestException>(() =>
            Compiler(limits with { MaxNodes = 2 }).Predicate(condition, Trusted)
        );
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.True(
                Compiler()
                    .Predicate(CompilerTests.Leaf("Decimal", "eq", 123.45m), Trusted)
                    .Compile()(new() { Decimal = 123.45m })
            );
            Assert.Throws<InvalidRequestException>(() =>
                Compiler().Predicate(CompilerTests.Leaf("Double", "eq", 0.1m), Trusted)
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
