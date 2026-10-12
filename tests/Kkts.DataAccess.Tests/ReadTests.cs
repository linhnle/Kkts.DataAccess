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
