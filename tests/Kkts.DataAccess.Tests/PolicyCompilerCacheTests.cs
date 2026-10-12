using Xunit;
using static Kkts.DataAccess.Tests.CompilerTests;

namespace Kkts.DataAccess.Tests;

public class PolicyCompilerCacheTests
{
    static PolicyCompiler<Employee> CompilerWithCapacity(int capacity) =>
        new(Schema, Registry, Reference("TenantId", "tenant"), cacheOptions: new(capacity));

    static CapturedPolicy Policy(long version) =>
        Capture(
            Definition(Rule("department", Effect.Allow, Reference("DepartmentId", "department"))),
            version
        );

    static void AssertState(PolicyCompiler<Employee> compiler, params long[] versions)
    {
        Assert.Equal(versions.Length, compiler.TemplateCount);
        Assert.Equal(versions, compiler.TemplateInsertionOrder);
    }

    [Fact]
    public void Options_are_positive_immutable_and_default_to_128_entries()
    {
        Assert.Equal(128, new PolicyCompilerCacheOptions().MaxEntries);
        Assert.Null(typeof(PolicyCompilerCacheOptions).GetProperty("MaxEntries")!.SetMethod);
        foreach (var invalid in new[] { 0, -1, int.MinValue })
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PolicyCompilerCacheOptions(invalid)
            );

        var compiler = Compiler();
        for (var version = 1; version <= 129; version++)
            compiler.Bind(Policy(version), Context(), "scope");
        AssertState(compiler, Enumerable.Range(2, 128).Select(i => (long)i).ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Retention_and_insertion_queue_remain_bounded_and_hits_do_not_reorder(int capacity)
    {
        var compiler = CompilerWithCapacity(capacity);
        for (var version = 1; version <= 40; version++)
        {
            compiler.Bind(Policy(version), Context(), "scope");
            var expected = Enumerable
                .Range(Math.Max(1, version - capacity + 1), Math.Min(version, capacity))
                .Select(i => (long)i)
                .ToArray();
            AssertState(compiler, expected);
            compiler.Bind(Policy(expected[0]), Context(), "scope");
            AssertState(compiler, expected);
        }
    }

    [Fact]
    public void Invalid_builds_at_capacity_preserve_healthy_entries_and_recover()
    {
        var compiler = CompilerWithCapacity(2);
        compiler.Bind(Policy(1), Context(), "scope");
        compiler.Bind(Policy(2), Context(), "scope");
        foreach (
            var payload in new[]
            {
                "{",
                PolicyJson.Serialize(
                    Definition(Rule("bad", Effect.Allow, Leaf("Unknown", "eq", 1)))
                ),
            }
        )
        {
            Assert.Throws<SchemaIncompatibleException>(() =>
                compiler.Bind(Policy(3) with { Payload = payload }, Context(), "scope")
            );
            AssertState(compiler, 1, 2);
        }
        compiler.Bind(Policy(3), Context(), "scope");
        AssertState(compiler, 2, 3);
    }

    [Fact]
    public void Resident_mismatch_is_rejected_without_reordering_or_replacing_the_owned_ast()
    {
        var compiler = CompilerWithCapacity(2);
        var original = Definition(
            Rule("department", Effect.Allow, Reference("DepartmentId", "department"))
        );
        var capture = Capture(original);
        var bound = compiler.Bind(capture, Context(), "scope");
        original.Rules[0] = Rule("changed", Effect.Allow, Leaf("Id", "eq", 9));
        original.FieldEntries[0].Fields[FieldOperation.Select] = [];
        compiler.Bind(Policy(2), Context(), "scope");
        Assert.Throws<RevisionConflictException>(() =>
            compiler.Bind(Capture(original), Context(), "scope")
        );
        AssertState(compiler, 1, 2);
        var rebound = compiler.Bind(capture, Context(), "scope");
        Assert.Same(bound.Definition, rebound.Definition);
        Assert.Equal(new[] { "Id", "Name" }, rebound.Fields[FieldOperation.Select].Order());
        Assert.True(rebound.Predicate.Compile()(new() { TenantId = 1, DepartmentId = 10 }));
        Assert.False(rebound.Predicate.Compile()(new() { TenantId = 1, DepartmentId = 20 }));
    }

    [Fact]
    public void Evicted_content_is_revalidated_without_a_historical_content_ledger()
    {
        var compiler = CompilerWithCapacity(1);
        var first = compiler.Bind(Policy(1), Context(), "scope");
        compiler.Bind(Policy(2), Context(), "scope");
        Assert.Throws<SchemaIncompatibleException>(() =>
            compiler.Bind(Policy(1) with { Payload = "{}" }, Context(), "scope")
        );
        AssertState(compiler, 2);
        var rebound = compiler.Bind(Policy(1), Context(2, 20), "scope");
        Assert.NotSame(first.Definition, rebound.Definition);
        Assert.Equal(first.PolicyIdentity, rebound.PolicyIdentity);
        Assert.True(rebound.Predicate.Compile()(new() { TenantId = 2, DepartmentId = 20 }));
        Assert.False(rebound.Predicate.Compile()(new() { TenantId = 1, DepartmentId = 10 }));

        compiler.Bind(Policy(2), Context(), "scope");
        // Only the authoritative source can reject changed content that is no longer resident.
        var changed = Capture(Definition(Rule("changed", Effect.Allow, Leaf("Id", "eq", 9))));
        var changedBound = compiler.Bind(changed, Context(), "scope");
        Assert.Equal(changed.Payload, changedBound.PolicyIdentity);
        Assert.Equal(new[] { "changed" }, changedBound.ApplicableRules);
        AssertState(compiler, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_same_and_mixed_versions_bind_fresh_users_and_grants(bool mixed)
    {
        var compiler = CompilerWithCapacity(2);
        var definition = Definition(
            Rule("department", Effect.Allow, Reference("DepartmentId", "department"))
        );
        definition = definition with
        {
            FieldEntries =
            [
                .. definition.FieldEntries,
                new(
                    "hide-name",
                    "Employee",
                    "Read",
                    Effect.Deny,
                    new(["restricted"], []),
                    new() { { FieldOperation.Select, ["Name"] } }
                ),
            ],
        };
        await Task.WhenAll(
            Enumerable
                .Range(1, 100)
                .Select(i =>
                    Task.Run(() =>
                    {
                        var trusted = new TrustedContext(
                            i % 2 == 0 ? ["manager", "restricted"] : ["manager"],
                            [],
                            new Dictionary<string, object?> { ["tenant"] = i, ["department"] = i }
                        );
                        var bound = compiler.Bind(
                            Capture(definition, mixed ? i % 7 + 1 : 1),
                            trusted,
                            "scope"
                        );
                        var check = bound.Predicate.Compile();
                        Assert.True(check(new() { TenantId = i, DepartmentId = i }));
                        Assert.False(check(new() { TenantId = i + 1, DepartmentId = i }));
                        Assert.False(check(new() { TenantId = i, DepartmentId = i + 1 }));
                        Assert.Equal(
                            i % 2 != 0,
                            bound.Fields[FieldOperation.Select].Contains("Name")
                        );
                        Assert.InRange(compiler.TemplateCount, 1, 2);
                        Assert.InRange(compiler.TemplateInsertionOrder.Count, 1, 2);
                    })
                )
        );
        Assert.Equal(mixed ? 2 : 1, compiler.TemplateCount);
        Assert.Equal(compiler.TemplateCount, compiler.TemplateInsertionOrder.Count);
    }
}
