using System.Text.Json;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class CompilerTests
{
    public class Employee
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int DepartmentId { get; set; }
        public string Status { get; set; } = "Active";
        public string Name { get; set; } = "";
        public decimal? Salary { get; set; }
    }

    public static readonly ResourceSchema<Employee> Schema = new(
        "Employee",
        "1",
        [
            ResourceSchema<Employee>.Field(
                "Id",
                e => e.Id,
                [FieldOperation.Select, FieldOperation.Sort]
            ),
            ResourceSchema<Employee>.Field("TenantId", e => e.TenantId, []),
            ResourceSchema<Employee>.Field(
                "DepartmentId",
                e => e.DepartmentId,
                [FieldOperation.Filter]
            ),
            ResourceSchema<Employee>.Field("Status", e => e.Status, [FieldOperation.Filter]),
            ResourceSchema<Employee>.Field(
                "Name",
                e => e.Name,
                [FieldOperation.Select, FieldOperation.Sort]
            ),
            ResourceSchema<Employee>.Field("Salary", e => e.Salary, [FieldOperation.Select]),
        ],
        ["Id"]
    );
    public static readonly ContextRegistry Registry = new(
        new Dictionary<string, Type> { { "tenant", typeof(int) }, { "department", typeof(int) } }
    );

    public static Condition Leaf(string field, string op, object? value) =>
        new()
        {
            Field = field,
            Operator = op,
            Literal = JsonSerializer.SerializeToElement(value),
        };

    public static Condition Reference(string field, string key) =>
        new()
        {
            Field = field,
            Operator = "eq",
            Context = key,
        };

    public static TrustedContext Context(
        int tenant = 1,
        int department = 10,
        string role = "manager"
    ) =>
        new(
            [role],
            [],
            new Dictionary<string, object?> { { "tenant", tenant }, { "department", department } }
        );

    public static PolicyDefinition Definition(params RecordRule[] rules) =>
        new(
            "Employee",
            "1",
            rules,
            [
                new(
                    "fields",
                    "Employee",
                    "Read",
                    Effect.Allow,
                    new(["manager"], []),
                    new()
                    {
                        { FieldOperation.Select, ["Id", "Name"] },
                        { FieldOperation.Filter, ["Status", "DepartmentId"] },
                        { FieldOperation.Sort, ["Name"] },
                    }
                ),
            ]
        );

    public static RecordRule Rule(string id, Effect effect, Condition? condition = null) =>
        new(id, "Employee", "Read", effect, new(["manager"], []), condition);

    public static CapturedPolicy Capture(PolicyDefinition p, long version = 1) =>
        new("scope", "Employee", "1", version, PolicyJson.Serialize(p));

    public static PolicyCompiler<Employee> Compiler() =>
        new(Schema, Registry, Reference("TenantId", "tenant"));

    [Fact]
    public void Mandatory_allow_deny_and_default_deny_compose()
    {
        var p = Definition(
            Rule("department", Effect.Allow, Reference("DepartmentId", "department")),
            Rule("deny", Effect.Deny, Leaf("Status", "eq", "Inactive"))
        );
        var bound = Compiler().Bind(Capture(p), Context(), "scope");
        var check = bound.Predicate.Compile();
        Assert.True(
            check(
                new()
                {
                    Id = 1,
                    TenantId = 1,
                    DepartmentId = 10,
                }
            )
        );
        Assert.False(
            check(
                new()
                {
                    Id = 1,
                    TenantId = 2,
                    DepartmentId = 10,
                }
            )
        );
        Assert.False(
            check(
                new()
                {
                    Id = 1,
                    TenantId = 1,
                    DepartmentId = 20,
                }
            )
        );
        Assert.False(
            check(
                new()
                {
                    Id = 1,
                    TenantId = 1,
                    DepartmentId = 10,
                    Status = "Inactive",
                }
            )
        );
        Assert.Throws<AuthorizationException>(() =>
            Compiler().Bind(Capture(Definition()), Context(), "scope")
        );
        Assert.Throws<AuthorizationException>(() =>
            Compiler().Bind(Capture(p), Context(role: "employee"), "scope")
        );
    }

    [Fact]
    public void Separate_fields_and_denies_do_not_grant_query_operations()
    {
        var p = Definition(Rule("all", Effect.Allow));
        p = p with
        {
            FieldEntries =
            [
                .. p.FieldEntries,
                new(
                    "hide",
                    "Employee",
                    "Read",
                    Effect.Deny,
                    new([], []),
                    new() { { FieldOperation.Select, ["Name"] } }
                ),
            ],
        };
        var b = Compiler().Bind(Capture(p), Context(), "scope");
        Assert.Equal(new[] { "Id" }, b.Fields[FieldOperation.Select].Order());
        Assert.DoesNotContain("Id", b.Fields[FieldOperation.Sort].ToArray());
        Assert.DoesNotContain("Name", b.Fields[FieldOperation.Filter].ToArray());
        var empty = Compiler().Bind(Capture(p with { FieldEntries = [] }, 2), Context(), "scope");
        Assert.All(empty.Fields.Values, Assert.Empty);
    }

    [Fact]
    public void Malformed_tree_unknown_paths_and_prohibited_grants_have_paths()
    {
        var validator = new PolicyValidator<Employee>(Schema, Registry);
        foreach (
            var c in new[]
            {
                new Condition { All = [] },
                new Condition { All = [Leaf("Id", "eq", 1)], Field = "Id" },
                Leaf("Manager.Id", "eq", 1),
                Leaf("Id", "contains", "x"),
                Leaf("Id", "eq", 1.5),
            }
        )
            Assert.Contains(
                validator.Validate(Definition(Rule("bad", Effect.Allow, c))),
                d => d.Path.StartsWith("/rules/0/condition")
            );
        var p = Definition(Rule("all", Effect.Allow));
        p = p with
        {
            FieldEntries =
            [
                new(
                    "bad",
                    "Employee",
                    "Read",
                    Effect.Allow,
                    new([], []),
                    new() { { FieldOperation.Sort, ["Salary"] } }
                ),
            ],
        };
        Assert.Contains(validator.Validate(p), d => d.Path.Contains("/fields/Sort"));
        Assert.NotEmpty(
            validator.Validate(
                p with
                {
                    Rules = [Rule("bad", Effect.Allow) with { Action = "Delete" }],
                }
            )
        );
    }

    [Fact]
    public void Roundtrip_preserves_explicit_null_and_rejects_combined_grants()
    {
        var p = Definition(Rule("null", Effect.Allow, Leaf("Salary", "eq", null)));
        var roundtrip = PolicyJson.Deserialize(PolicyJson.Serialize(p));
        Assert.Equal(JsonValueKind.Null, roundtrip.Rules[0].Condition!.Literal.ValueKind);
        var check = Compiler().Bind(Capture(roundtrip), Context(), "scope").Predicate.Compile();
        Assert.True(check(new() { TenantId = 1, Salary = null }));
        Assert.False(check(new() { TenantId = 1, Salary = 1 }));
        var json = PolicyJson
            .Serialize(p)
            .Replace("\"condition\":", "\"fields\":[\"Salary\"],\"condition\":");
        Assert.Throws<InvalidRequestException>(() => PolicyJson.Deserialize(json));
    }

    [Fact]
    public void Limits_and_missing_context_fail_before_execution()
    {
        var p = Definition(
            Rule("allow", Effect.Allow),
            Rule("deny", Effect.Deny, Reference("DepartmentId", "department"))
        );
        var missing = new TrustedContext(
            ["manager"],
            [],
            new Dictionary<string, object?> { { "tenant", 1 } }
        );
        Assert.Throws<AuthorizationException>(() => Compiler().Bind(Capture(p), missing, "scope"));
        var wrong = new TrustedContext(
            ["manager"],
            [],
            new Dictionary<string, object?> { { "tenant", "1" }, { "department", 10 } }
        );
        Assert.Throws<AuthorizationException>(() => Compiler().Bind(Capture(p), wrong, "scope"));
        var nested = Leaf("Id", "eq", 1);
        for (int i = 0; i < 15; i++)
            nested = new() { Not = nested };
        Assert.NotEmpty(
            new PolicyValidator<Employee>(Schema, Registry).Validate(
                Definition(Rule("deep", Effect.Allow, nested))
            )
        );
        Assert.NotEmpty(
            new PolicyValidator<Employee>(Schema, Registry, new(MaxLiteralBytes: 2)).Validate(
                Definition(Rule("big", Effect.Allow, Leaf("Name", "eq", "long")))
            )
        );
        Assert.Throws<AuthorizationException>(() =>
            Compiler().Bind(Capture(p), Context(), "scope", "Write")
        );
    }

    [Fact]
    public void Nested_multiple_allows_and_nullable_ordering_have_explicit_semantics()
    {
        var p = Definition(
            Rule("first", Effect.Allow, Leaf("Id", "eq", 1)),
            Rule(
                "second",
                Effect.Allow,
                new() { Any = [Leaf("Id", "eq", 2), Leaf("Id", "eq", 3)] }
            ),
            Rule("deny", Effect.Deny, new() { Not = Leaf("Status", "eq", "Active") })
        );
        var check = Compiler().Bind(Capture(p), Context(), "scope").Predicate.Compile();
        Assert.True(check(new() { Id = 1, TenantId = 1 }));
        Assert.True(check(new() { Id = 2, TenantId = 1 }));
        Assert.False(
            check(
                new()
                {
                    Id = 3,
                    TenantId = 1,
                    Status = "Inactive",
                }
            )
        );
        Assert.False(check(new() { Id = 4, TenantId = 1 }));
        var ordered = Compiler().Predicate(Leaf("Salary", "gt", 1m), Context()).Compile();
        Assert.False(ordered(new() { Salary = null }));
        Assert.True(ordered(new() { Salary = 2 }));
        var nullOrdered = Compiler().Predicate(Leaf("Salary", "gt", null), Context()).Compile();
        Assert.False(nullOrdered(new() { Salary = 2 }));
    }

    [Fact]
    public void All_complexity_limits_reject_before_compilation()
    {
        var tree = new Condition { All = [Leaf("Id", "eq", 1), Leaf("Id", "eq", 2)] };
        Assert.NotEmpty(
            new PolicyValidator<Employee>(Schema, Registry, new(MaxNodes: 2)).Validate(
                Definition(Rule("nodes", Effect.Allow, tree))
            )
        );
        Assert.NotEmpty(
            new PolicyValidator<Employee>(Schema, Registry, new(MaxCollection: 1)).Validate(
                Definition(Rule("collection", Effect.Allow, Leaf("Id", "in", new[] { 1, 2 })))
            )
        );
        Assert.NotEmpty(
            new PolicyValidator<Employee>(Schema, Registry, new(MaxEntries: 1)).Validate(
                Definition(Rule("entries", Effect.Allow))
            )
        );
    }

    [Fact]
    public async Task Cached_templates_never_share_request_identity()
    {
        var compiler = Compiler();
        var captured = Capture(
            Definition(Rule("department", Effect.Allow, Reference("DepartmentId", "department")))
        );
        await Task.WhenAll(
            Enumerable
                .Range(1, 30)
                .Select(i =>
                    Task.Run(() =>
                    {
                        var check = compiler
                            .Bind(captured, Context(i, i), "scope")
                            .Predicate.Compile();
                        Assert.True(check(new() { TenantId = i, DepartmentId = i }));
                        Assert.False(check(new() { TenantId = i + 1, DepartmentId = i }));
                    })
                )
        );
        Assert.Throws<SchemaIncompatibleException>(() =>
            compiler.Bind(captured with { SchemaVersion = "2" }, Context(), "scope")
        );
        Assert.Throws<RevisionConflictException>(() =>
            compiler.Bind(
                captured with
                {
                    Payload = PolicyJson.Serialize(Definition(Rule("different", Effect.Allow))),
                },
                Context(),
                "scope"
            )
        );
    }
}
