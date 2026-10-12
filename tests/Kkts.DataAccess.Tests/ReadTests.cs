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
        protected override void OnModelCreating(ModelBuilder b) =>
            b.Entity<CompilerTests.Employee>().HasKey(e => e.Id);
    }

    readonly string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
    DbContextOptions<Database> Options =>
        new DbContextOptionsBuilder<Database>().UseSqlite("Data Source=" + path).Options;
    SqlitePolicyStore Store => new("Data Source=" + path);
    readonly AdministrationContext admin = new("admin", "scope", true);

    AuthorizedReads<CompilerTests.Employee> Reads() =>
        new(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            Store,
            () => new Database(Options),
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
        File.Delete(path);
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
        var reads = new AuthorizedReads<CompilerTests.Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            Store,
            () => throw new Exception("Provider executed"),
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
}
