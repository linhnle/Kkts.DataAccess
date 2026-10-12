using System.Data.Common;
using Kkts.DataAccess.EntityFrameworkCore;
using Kkts.DataAccess.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Employee = Kkts.DataAccess.Tests.CompilerTests.Employee;

namespace Kkts.DataAccess.Tests;

public class SqliteSnapshotTests : IAsyncLifetime
{
    readonly string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");

    string Connection(string cache = "Private") => $"Data Source={path};Cache={cache}";

    DbContextOptions Options(string cache = "Private") =>
        new DbContextOptionsBuilder().UseSqlite(Connection(cache)).Options;

    class Database(DbContextOptions options) : DbContext(options)
    {
        public bool Disposed;

        public override void Dispose()
        {
            Disposed = true;
            base.Dispose();
        }

        protected override void OnModelCreating(ModelBuilder builder) =>
            builder.Entity<Employee>().ToTable("Employees").HasKey(row => row.Id);

        public override async ValueTask DisposeAsync()
        {
            Disposed = true;
            await base.DisposeAsync();
        }
    }

    sealed class FilteredDatabase(DbContextOptions options) : Database(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Employee>().HasQueryFilter(row => row.Id > 0);
        }
    }

    sealed class ViewDatabase(DbContextOptions options) : Database(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Employee>().ToView("Employees");
        }
    }

    sealed class DirtyRead : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA read_uncommitted=1";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    sealed class RetryingFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryingStrategy(dependencies);
    }

    sealed class RetryingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.FromMilliseconds(1))
    {
        protected override bool ShouldRetryOn(Exception exception) => true;
    }

    sealed class Source : IActivePolicySource
    {
        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        ) =>
            Task.FromResult(
                CompilerTests.Capture(
                    CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
                )
            );
    }

    public async Task InitializeAsync()
    {
        await using var db = new Database(Options());
        await db.Database.EnsureCreatedAsync();
        db.Add(
            new Employee
            {
                Id = 1,
                TenantId = 1,
                Name = "safe",
            }
        );
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("wal")]
    [InlineData("shared")]
    [InlineData("default")]
    [InlineData("dirty")]
    [InlineData("filter")]
    [InlineData("view")]
    [InlineData("actual-view")]
    [InlineData("retry")]
    public async Task Missing_prerequisites_fail_before_delivery_without_setting_changes(
        string setting
    )
    {
        if (setting == "wal")
        {
            await using var db = new Database(Options());
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE");
        }
        if (setting == "actual-view")
        {
            await using var db = new Database(Options());
            await db.Database.ExecuteSqlRawAsync(
                "DROP TABLE Employees; CREATE VIEW Employees AS SELECT 1 AS Id,1 AS TenantId,'safe' AS Name"
            );
        }
        var options = new DbContextOptionsBuilder(
            Options(
                setting switch
                {
                    "shared" => "Shared",
                    "default" => "Default",
                    _ => "Private",
                }
            )
        );
        if (setting == "dirty")
            options.AddInterceptors(new DirtyRead());
        if (setting == "retry")
            options.ReplaceService<IExecutionStrategyFactory, RetryingFactory>();
        var contexts = new List<Database>();
        EfEntityReadAdapter<Employee> Create() =>
            SqliteEntityReads.Create<Employee>(
                CompilerTests.Schema,
                () =>
                {
                    Database context = setting switch
                    {
                        "filter" => new FilteredDatabase(options.Options),
                        "view" => new ViewDatabase(options.Options),
                        _ => new Database(options.Options),
                    };
                    contexts.Add(context);
                    return context;
                },
                ReadOperations.All | ReadOperations.Snapshot
            );
        if (setting is "filter" or "view" or "retry")
        {
            var failure = Assert.Throws<ProviderUnsupportedException>(Create);
            Assert.Contains(
                failure.Diagnostics,
                diagnostic => diagnostic.Path == "/operations/snapshot"
            );
            Assert.True(Assert.Single(contexts).Disposed);
            return;
        }
        var adapter = Create();
        contexts.Clear();
        var repo = new AuthorizedReadRepository<Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            new Source(),
            adapter,
            () => CompilerTests.Context(),
            "scope",
            export: new(dataConsistency: ExportDataConsistency.Snapshot)
        );
        await using var iterator = repo.ExportAsync(new()).GetAsyncEnumerator();
        await Assert.ThrowsAsync<ProviderUnsupportedException>(async () =>
            await iterator.MoveNextAsync()
        );
        Assert.True(Assert.Single(contexts).Disposed);
        if (setting == "wal")
        {
            await using var c = new SqliteConnection(Connection());
            await c.OpenAsync();
            using var command = c.CreateCommand();
            command.CommandText = "PRAGMA journal_mode";
            Assert.Equal("delete", await command.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task Facade_exposes_snapshot_configuration_with_unchanged_default_construction()
    {
        var store = new SqlitePolicyStore(Connection());
        await store.InitializeAsync();
        var admin = new AdministrationContext("admin", "scope", true);
        await store.SaveDraftAsync(
            admin,
            "scope",
            "Employee",
            PolicyJson.Serialize(
                CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
            ),
            0
        );
        await store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            1,
            0
        );
        foreach (
            var configuration in new ExportReadConfiguration?[]
            {
                null,
                new(ExportPolicyConsistency.AbortOnPolicyChange, ExportDataConsistency.Snapshot),
            }
        )
        {
            var reads = new AuthorizedReads<Employee>(
                CompilerTests.Schema,
                CompilerTests.Compiler(),
                store,
                () => new Database(Options()),
                () => CompilerTests.Context(),
                "scope",
                export: configuration
            );
            int rows = 0;
            await foreach (var batch in reads.ExportAsync(new(Selection: ["Name"])))
                rows += batch.Records.Count;
            Assert.Equal(1, rows);
        }
    }
}
