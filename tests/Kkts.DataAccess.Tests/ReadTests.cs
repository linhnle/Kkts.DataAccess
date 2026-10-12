using System.Text.Json;
using Kkts.DataAccess.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class ReadTests : IAsyncLifetime
{
    public class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        public bool Disposed { get; private set; }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<CompilerTests.Employee>().HasKey(e => e.Id);
            b.Entity<CompilerTests.Employee>().Property(e => e.Id).ValueGeneratedNever();
            b.Entity<Boundary>().HasKey(e => e.Id);
            b.Entity<Boundary>().Property(e => e.Id).ValueGeneratedNever();
            b.Entity<Boundary>().Property(e => e.Amount).HasPrecision(18, 2);
        }
    }

    public class Boundary
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public string? Text { get; set; }
        public decimal? Amount { get; set; }
        public DateTime Time { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public int Computed => Id + 1;
    }

    sealed class PolicySource(CapturedPolicy policy) : IActivePolicySource
    {
        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(policy);
        }
    }

    readonly string policyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
    readonly string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
    protected virtual DbContextOptions<Database> Options =>
        new DbContextOptionsBuilder<Database>().UseSqlite("Data Source=" + path).Options;
    SqlitePolicyStore Store => new("Data Source=" + policyPath);
    readonly AdministrationContext admin = new("admin", "scope", true);

    protected virtual string ProviderName => "Microsoft.EntityFrameworkCore.Sqlite";

    AuthorizedReadRepository<CompilerTests.Employee> Reads() =>
        new(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            Store,
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<CompilerTests.Employee>(
                () => new Database(Options),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 2)
        );

    public async Task InitializeAsync()
    {
        await using var db = new Database(Options);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(
            new CompilerTests.Employee
            {
                Id = 1,
                TenantId = 1,
                DepartmentId = 10,
                Name = "Same",
                Salary = 100,
            },
            new CompilerTests.Employee
            {
                Id = 2,
                TenantId = 1,
                DepartmentId = 10,
                Name = "Same",
                Salary = 200,
            },
            new CompilerTests.Employee
            {
                Id = 3,
                TenantId = 1,
                DepartmentId = 10,
                Name = "Same",
                Salary = 300,
            },
            new CompilerTests.Employee
            {
                Id = 4,
                TenantId = 2,
                DepartmentId = 10,
                Name = "Secret",
                Salary = 400,
            }
        );
        await db.SaveChangesAsync();
        await Store.InitializeAsync();
        await Store.SaveDraftAsync(
            admin,
            "scope",
            "Employee",
            PolicyJson.Serialize(
                CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
            ),
            0
        );
        await Store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            1,
            0
        );
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        using (var db = new Database(Options))
            db.Database.EnsureDeleted();
        File.Delete(path);
        File.Delete(policyPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Relational_list_detail_count_and_output_share_authorization()
    {
        var reads = Reads();
        var page = await reads.ListAsync(new(Sort: [new("Name")], Limit: 2));
        Assert.Equal(3, page.Total);
        Assert.Equal(new[] { 1, 2 }, page.Records.Select(r => r.GetProperty("Id").GetInt32()));
        Assert.All(
            page.Records,
            r =>
            {
                Assert.False(r.TryGetProperty("Salary", out _));
                Assert.False(r.TryGetProperty("TenantId", out _));
                Assert.Equal(2, r.EnumerateObject().Count());
            }
        );
        var next = await reads.ListAsync(new(Sort: [new("Name")], Offset: 2, Limit: 2));
        Assert.Equal(3, next.Records.Single().GetProperty("Id").GetInt32());
        Assert.Equal(3, await reads.CountAsync(new(Limit: 1)));
        Assert.Null(
            (await reads.DetailAsync(JsonSerializer.SerializeToElement(4), new(Limit: 1))).Record
        );
        Assert.Null(
            (await reads.DetailAsync(JsonSerializer.SerializeToElement(99), new(Limit: 1))).Record
        );
        Assert.NotNull(
            (await reads.DetailAsync(JsonSerializer.SerializeToElement(1), new(Limit: 1))).Record
        );
        var empty = await reads.ListAsync(new(Selection: [], Limit: 2));
        Assert.All(empty.Records, r => Assert.Equal("{}", r.GetRawText()));
    }

    [Fact]
    public async Task Forbidden_queries_and_unsupported_operations_fail_before_entity_database_access()
    {
        var reads = new AuthorizedReadRepository<CompilerTests.Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            Store,
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<CompilerTests.Employee>(
                () => throw new Exception("Provider executed"),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope"
        );
        foreach (
            var request in new[]
            {
                new ReadRequest(Selection: ["Salary"]),
                new ReadRequest(Filter: CompilerTests.Leaf("Salary", "eq", 100)),
                new ReadRequest(Sort: [new("Id")]),
                new ReadRequest(Selection: ["Manager.Id"]),
                new ReadRequest(Limit: 0),
                new ReadRequest(Action: "Write"),
                new ReadRequest(Action: "Aggregate"),
            }
        )
            await Assert.ThrowsAsync<AuthorizationException>(() => reads.ListAsync(request));
    }

    [Fact]
    public async Task Published_field_revocation_does_not_reuse_warmed_output_permissions()
    {
        var reads = Reads();
        var warm = await reads.ListAsync(new(Limit: 1));
        Assert.Equal(2, warm.Records[0].EnumerateObject().Count());
        var policy = CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow));
        policy = policy with
        {
            FieldEntries =
            [
                policy.FieldEntries[0] with
                {
                    Fields = new() { { FieldOperation.Select, ["Name"] } },
                },
            ],
        };
        await Store.SaveDraftAsync(admin, "scope", "Employee", PolicyJson.Serialize(policy), 1);
        await Store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            2,
            1
        );
        var page = await reads.ListAsync(new(Limit: 1));
        Assert.NotEqual(warm.PolicyVersion, page.PolicyVersion);
        Assert.Equal("{\"Name\":\"Same\"}", page.Records[0].GetRawText());
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            reads.ListAsync(new(Selection: ["Id"], Limit: 1))
        );
        var detail = await reads.DetailAsync(JsonSerializer.SerializeToElement(1), new(Limit: 1));
        Assert.Equal(page.PolicyVersion, detail.PolicyVersion);
        Assert.Equal("{\"Name\":\"Same\"}", detail.Record!.Value.GetRawText());
        var batches = 0;
        await foreach (var batch in reads.ExportAsync(new(Limit: 1)))
        {
            batches++;
            Assert.Equal(page.PolicyVersion, batch.PolicyVersion);
            Assert.All(batch.Records, row => Assert.Equal("{\"Name\":\"Same\"}", row.GetRawText()));
        }
        Assert.Equal(2, batches);
    }

    [Fact]
    public async Task Export_retains_captured_policy_and_supports_cancellation()
    {
        var reads = Reads();
        await using var export = reads.ExportAsync(new(Limit: 2)).GetAsyncEnumerator();
        Assert.True(await export.MoveNextAsync());
        var version = export.Current.PolicyVersion;
        Assert.Equal(2, export.Current.Records.Count);
        await Store.SaveDraftAsync(
            admin,
            "scope",
            "Employee",
            PolicyJson.Serialize(CompilerTests.Definition()),
            1
        );
        await Store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            2,
            1
        );
        Assert.True(await export.MoveNextAsync());
        Assert.Equal(version, export.Current.PolicyVersion);
        Assert.Single(export.Current.Records);
        Assert.False(await export.MoveNextAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => reads.CountAsync(new(Limit: 1)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reads.ListAsync(new(Limit: 1), cancellation.Token)
        );
    }

    [Fact]
    public async Task Provider_null_deny_collation_precision_and_ordering_boundaries()
    {
        var schema = new ResourceSchema<Boundary>(
            "Boundary",
            "1",
            [
                ResourceSchema<Boundary>.Field("Id", e => e.Id, [FieldOperation.Select]),
                ResourceSchema<Boundary>.Field("TenantId", e => e.TenantId, []),
                ResourceSchema<Boundary>.Field(
                    "Text",
                    e => e.Text,
                    [FieldOperation.Select, FieldOperation.Filter]
                ),
                ResourceSchema<Boundary>.Field(
                    "Amount",
                    e => e.Amount,
                    [FieldOperation.Select, FieldOperation.Sort]
                ),
                ResourceSchema<Boundary>.Field(
                    "Time",
                    e => e.Time,
                    [FieldOperation.Select, FieldOperation.Sort]
                ),
                ResourceSchema<Boundary>.Field("Computed", e => e.Computed, []),
            ],
            "Id"
        );
        var policy = new PolicyDefinition(
            "Boundary",
            "1",
            [
                new("allow", "Boundary", "Read", Effect.Allow, new([], []), null),
                new(
                    "deny",
                    "Boundary",
                    "Read",
                    Effect.Deny,
                    new([], []),
                    CompilerTests.Leaf("Id", "eq", 3)
                ),
            ],
            [
                new(
                    "fields",
                    "Boundary",
                    "Read",
                    Effect.Allow,
                    new([], []),
                    new()
                    {
                        [FieldOperation.Select] = ["Id", "Text", "Amount", "Time"],
                        [FieldOperation.Filter] = ["Text"],
                        [FieldOperation.Sort] = ["Amount", "Time"],
                    }
                ),
            ]
        );
        var captured = new CapturedPolicy(
            "scope",
            "Boundary",
            "1",
            1,
            PolicyJson.Serialize(policy)
        );
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        await using (var db = new Database(Options))
        {
            db.AddRange(
                new Boundary
                {
                    Id = 1,
                    TenantId = 1,
                    Text = null,
                    Amount = null,
                    Time = time,
                },
                new Boundary
                {
                    Id = 2,
                    TenantId = 1,
                    Text = "Mixed",
                    Amount = 123.45m,
                    Time = time.AddDays(1),
                },
                new Boundary
                {
                    Id = 3,
                    TenantId = 1,
                    Text = "Mixed",
                    Amount = 2m,
                    Time = time,
                },
                new Boundary
                {
                    Id = 4,
                    TenantId = 2,
                    Text = "Mixed",
                    Amount = 3m,
                    Time = time,
                }
            );
            await db.SaveChangesAsync();
        }
        var repo = new AuthorizedReadRepository<Boundary>(
            schema,
            new(schema, CompilerTests.Registry, CompilerTests.Reference("TenantId", "tenant")),
            new PolicySource(captured),
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<Boundary>(
                () => new Database(Options),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope"
        );
        Assert.Equal(1, await repo.CountAsync(new(Filter: CompilerTests.Leaf("Text", "eq", null))));
        var page = await repo.ListAsync(new(Sort: [new("Time")]));
        Assert.Equal(new[] { 1, 2 }, page.Records.Select(r => r.GetProperty("Id").GetInt32()));
        Assert.Equal(123.45m, page.Records[1].GetProperty("Amount").GetDecimal());
        var caseCount = await repo.CountAsync(
            new(Filter: CompilerTests.Leaf("Text", "eq", "mixed"))
        );
        Assert.Equal(ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" ? 0 : 1, caseCount);
        Assert.Equal(2, (await repo.ListAsync(new(Sort: [new("Amount")]))).Total);
        policy = policy with
        {
            Rules =
            [
                new(
                    "bad",
                    "Boundary",
                    "Read",
                    Effect.Allow,
                    new([], []),
                    CompilerTests.Leaf("Computed", "eq", 2)
                ),
            ],
        };
        var failed = new AuthorizedReadRepository<Boundary>(
            schema,
            new(schema, CompilerTests.Registry, CompilerTests.Reference("TenantId", "tenant")),
            new PolicySource(captured with { Version = 2, Payload = PolicyJson.Serialize(policy) }),
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<Boundary>(
                () => new Database(Options),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope"
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.ListAsync(new()));
    }

    [Fact]
    public async Task Structured_null_filters_and_alias_ordering_preserve_relational_results()
    {
        var schema = new ResourceSchema<Boundary>(
            "Boundary",
            "1",
            [
                ResourceSchema<Boundary>.Field("key", e => e.Id, [FieldOperation.Select]),
                ResourceSchema<Boundary>.Field("tenant", e => e.TenantId, []),
                ResourceSchema<Boundary>.Field(
                    "label",
                    e => e.Text,
                    [FieldOperation.Select, FieldOperation.Filter, FieldOperation.Sort]
                ),
                ResourceSchema<Boundary>.Field(
                    "amount",
                    e => e.Amount,
                    [FieldOperation.Select, FieldOperation.Filter]
                ),
                ResourceSchema<Boundary>.Field(
                    "time",
                    e => e.Time,
                    [FieldOperation.Filter, FieldOperation.Sort]
                ),
            ],
            "key"
        );
        var definition = new PolicyDefinition(
            "Boundary",
            "1",
            [
                new("allow", "Boundary", "Read", Effect.Allow, new([], []), null),
                new(
                    "deny",
                    "Boundary",
                    "Read",
                    Effect.Deny,
                    new([], []),
                    CompilerTests.Leaf("key", "eq", 4)
                ),
            ],
            [
                new(
                    "fields",
                    "Boundary",
                    "Read",
                    Effect.Allow,
                    new([], []),
                    new()
                    {
                        [FieldOperation.Select] = ["key", "label", "amount"],
                        [FieldOperation.Filter] = ["label", "amount", "time"],
                        [FieldOperation.Sort] = ["label", "time"],
                    }
                ),
            ]
        );
        var time = new DateTime(2026, 1, 1);
        var rows = new[]
        {
            new Boundary
            {
                Id = 1,
                TenantId = 1,
                Text = null,
                Amount = null,
                Time = time,
            },
            new Boundary
            {
                Id = 2,
                TenantId = 1,
                Text = "same",
                Amount = 2,
                Time = time,
            },
            new Boundary
            {
                Id = 3,
                TenantId = 1,
                Text = "same",
                Amount = 3,
                Time = time.AddDays(1),
            },
            new Boundary
            {
                Id = 4,
                TenantId = 1,
                Text = "denied",
                Amount = 4,
                Time = time,
            },
            new Boundary
            {
                Id = 5,
                TenantId = 2,
                Text = "same",
                Amount = 5,
                Time = time,
            },
        };
        await using (var db = new Database(Options))
        {
            db.AddRange(rows);
            await db.SaveChangesAsync();
        }
        var compiler = new PolicyCompiler<Boundary>(
            schema,
            CompilerTests.Registry,
            CompilerTests.Reference("tenant", "tenant")
        );
        var reads = new AuthorizedReadRepository<Boundary>(
            schema,
            compiler,
            new PolicySource(new("scope", "Boundary", "1", 1, PolicyJson.Serialize(definition))),
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<Boundary>(
                () => new Database(Options),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope",
            new(200, 1_000_000, 1)
        );
        var cases = new[]
        {
            (CompilerTests.Leaf("label", "eq", null), new[] { 1 }),
            (CompilerTests.Leaf("label", "ne", null), new[] { 2, 3 }),
            (CompilerTests.Leaf("amount", "eq", null), new[] { 1 }),
            (CompilerTests.Leaf("amount", "ne", null), new[] { 2, 3 }),
            (new Condition { Field = "label", Operator = "isNull" }, new[] { 1 }),
            (new Condition { Field = "label", Operator = "isNotNull" }, new[] { 2, 3 }),
            (new Condition { Field = "amount", Operator = "isNull" }, new[] { 1 }),
            (new Condition { Field = "amount", Operator = "isNotNull" }, new[] { 2, 3 }),
            (new Condition { Field = "time", Operator = "isNull" }, Array.Empty<int>()),
            (new Condition { Field = "time", Operator = "isNotNull" }, new[] { 1, 2, 3 }),
            (CompilerTests.Leaf("label", "in", new object?[] { null, "same" }), new[] { 1, 2, 3 }),
            (CompilerTests.Leaf("amount", "in", new object?[] { null, 2 }), new[] { 1, 2 }),
            (
                new Condition
                {
                    Not = CompilerTests.Leaf("amount", "in", new object?[] { null, 2 }),
                },
                new[] { 3 }
            ),
            (new Condition { Not = CompilerTests.Leaf("label", "contains", "sam") }, new[] { 1 }),
            (
                new Condition
                {
                    Not = new() { Not = CompilerTests.Leaf("label", "contains", "sam") },
                },
                new[] { 2, 3 }
            ),
            (
                new Condition
                {
                    Not = new()
                    {
                        Any =
                        [
                            CompilerTests.Leaf("label", "contains", "sam"),
                            new() { Not = CompilerTests.Leaf("amount", "eq", null) },
                        ],
                    },
                },
                new[] { 1 }
            ),
            (CompilerTests.Leaf("label", "ne", "same"), new[] { 1 }),
            (CompilerTests.Leaf("label", "startsWith", "sam"), new[] { 2, 3 }),
            (CompilerTests.Leaf("label", "endsWith", "ame"), new[] { 2, 3 }),
            (CompilerTests.Leaf("label", "contains", null), Array.Empty<int>()),
            (CompilerTests.Leaf("amount", "lt", null), Array.Empty<int>()),
            (CompilerTests.Leaf("amount", "le", null), Array.Empty<int>()),
            (CompilerTests.Leaf("amount", "gt", null), Array.Empty<int>()),
            (CompilerTests.Leaf("amount", "ge", null), Array.Empty<int>()),
            (CompilerTests.Leaf("amount", "ge", 2), new[] { 2, 3 }),
            (new Condition { Not = CompilerTests.Leaf("amount", "gt", null) }, new[] { 1, 2, 3 }),
        };
        foreach (var (filter, expected) in cases)
        {
            var predicate = compiler.Predicate(filter, CompilerTests.Context()).Compile();
            Assert.Equal(
                expected,
                rows.Where(row => row.TenantId == 1 && row.Id != 4)
                    .Where(predicate)
                    .Select(row => row.Id)
            );
            var request = new ReadRequest(Selection: ["key"], Filter: filter);
            var page = await reads.ListAsync(request);
            Assert.Equal(expected, page.Records.Select(row => row.GetProperty("key").GetInt32()));
            Assert.Equal(expected.Length, await reads.CountAsync(request));
        }
        var sorted = await reads.ListAsync(
            new(Selection: ["amount"], Sort: [new("label", true), new("label")])
        );
        Assert.Equal(
            new decimal?[] { 2, 3, null },
            sorted.Records.Select(row =>
                row.GetProperty("amount").ValueKind == JsonValueKind.Null
                    ? (decimal?)null
                    : row.GetProperty("amount").GetDecimal()
            )
        );
        Assert.All(sorted.Records, row => Assert.Single(row.EnumerateObject()));
        var multiKey = await reads.ListAsync(
            new(Selection: ["key"], Sort: [new("label"), new("time", true)])
        );
        Assert.Equal(
            new[] { 1, 3, 2 },
            multiKey.Records.Select(row => row.GetProperty("key").GetInt32())
        );
        var reverse = await reads.ListAsync(
            new(Selection: ["key"], Sort: [new("label", true), new("time")])
        );
        Assert.Equal(
            new[] { 2, 3, 1 },
            reverse.Records.Select(row => row.GetProperty("key").GetInt32())
        );
        var exported = new List<decimal?>();
        await foreach (
            var batch in reads.ExportAsync(new(Selection: ["amount"], Sort: [new("label", true)]))
        )
        {
            Assert.All(batch.Records, row => Assert.Single(row.EnumerateObject()));
            exported.AddRange(
                batch.Records.Select(row =>
                    row.GetProperty("amount").ValueKind == JsonValueKind.Null
                        ? (decimal?)null
                        : row.GetProperty("amount").GetDecimal()
                )
            );
        }
        Assert.Equal(new decimal?[] { 2, 3, null }, exported);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            reads.ListAsync(new(Sort: [new("key")]))
        );
    }

    [Fact]
    public async Task Real_provider_sessions_dispose_on_early_export_and_cancellation()
    {
        var sessions = new List<Database>();
        var adapter =
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<CompilerTests.Employee>(
                () =>
                {
                    var db = new Database(Options);
                    sessions.Add(db);
                    return db;
                },
                ProviderName
            );
        var repo = new AuthorizedReadRepository<CompilerTests.Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            Store,
            adapter,
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 1)
        );
        await repo.ListAsync(new(Limit: 1));
        Assert.True(sessions.Single().Disposed);
        await using (var export = repo.ExportAsync(new(Limit: 1)).GetAsyncEnumerator())
            Assert.True(await export.MoveNextAsync());
        Assert.All(sessions, db => Assert.True(db.Disposed));
        using var cancellation = new CancellationTokenSource();
        await using (
            var export = repo.ExportAsync(new(Limit: 1), cancellation.Token).GetAsyncEnumerator()
        )
        {
            Assert.True(await export.MoveNextAsync());
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await export.MoveNextAsync()
            );
        }
        Assert.All(sessions, db => Assert.True(db.Disposed));
    }
}
