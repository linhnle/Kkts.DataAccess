using System.Data.Common;
using Kkts.DataAccess.EntityFrameworkCore;
using Kkts.DataAccess.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;
using Employee = Kkts.DataAccess.Tests.CompilerTests.Employee;

namespace Kkts.DataAccess.Tests;

public class FactoryIntegrationTests : IAsyncLifetime
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"kda-factory-{Guid.NewGuid():N}.db");
    readonly Commands commands = new();
    DbContextOptions<Database> Options =>
        new DbContextOptionsBuilder<Database>()
            .UseSqlite($"Data Source={path};Cache=Private")
            .AddInterceptors(commands)
            .Options;

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        public int Disposals;

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<Employee>().ToTable("Employees");

        public override void Dispose()
        {
            Disposals++;
            base.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            Disposals++;
            await base.DisposeAsync();
        }
    }

    sealed class Commands : DbCommandInterceptor
    {
        public int Queries;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Queries++;
            return ValueTask.FromResult(result);
        }
    }

    sealed class Factory(DbContextOptions<Database> options) : IDbContextFactory<Database>
    {
        public List<Database> Created = [];
        public CancellationToken LastToken;
        public Action? AfterCreate;

        public Database CreateDbContext() =>
            throw new InvalidOperationException("Synchronous creation must not run.");

        public async Task<Database> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            LastToken = cancellationToken;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var database = new Database(options);
            Created.Add(database);
            AfterCreate?.Invoke();
            return database;
        }
    }

    internal sealed class Source : IActivePolicySource
    {
        public int Captures;
        public string? LastScope;

        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            Captures++;
            LastScope = scope;
            return Task.FromResult(
                CompilerTests.Capture(
                    CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
                ) with
                {
                    Scope = scope,
                }
            );
        }
    }

    AuthorizedReadRepository<Employee> Repository(
        IEntityReadAdapter<Employee> adapter,
        ExportDataConsistency consistency = ExportDataConsistency.BestEffort
    ) =>
        new(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            new Source(),
            adapter,
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 1),
            export: new(dataConsistency: consistency)
        );

    public async Task InitializeAsync()
    {
        await using var database = new Database(Options);
        await database.Database.EnsureCreatedAsync();
        await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
        database.AddRange(
            new Employee
            {
                Id = 1,
                TenantId = 1,
                Name = "one",
            },
            new Employee
            {
                Id = 2,
                TenantId = 1,
                Name = "two",
            },
            new Employee
            {
                Id = 3,
                TenantId = 2,
                Name = "hidden",
            }
        );
        await database.SaveChangesAsync();
        commands.Queries = 0;
    }

    public Task DisposeAsync()
    {
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Async_registration_disposes_temporary_contexts_without_application_queries()
    {
        var factory = new Factory(Options);
        using var token = new CancellationTokenSource();
        var adapter = await SqliteEntityReads.CreateAsync<Employee, Database>(
            CompilerTests.Schema,
            factory,
            cancellation: token.Token
        );
        await adapter.ValidateAsync(CompilerTests.Schema, token.Token);
        Assert.Equal(2, factory.Created.Count);
        Assert.All(factory.Created, context => Assert.Equal(1, context.Disposals));
        Assert.Equal(token.Token, factory.LastToken);
        Assert.Equal(0, commands.Queries);
    }

    [Fact]
    public async Task Direct_facade_and_async_factory_facade_preserve_authorized_reads()
    {
        var store = new SqlitePolicyStore($"Data Source={path}");
        await store.InitializeAsync();
        var admin = new AdministrationContext("setup", "scope", true);
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
        var factory = new Factory(Options);
        var context = new RepositoryTests.AsyncContext(_ =>
            ValueTask.FromResult(CompilerTests.Context())
        );
        var asynchronous = await AuthorizedReads<Employee>.CreateAsync(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            store,
            factory,
            context,
            "scope",
            new(2, 100, 1)
        );
        Assert.Equal(0, context.Captures);
        Assert.Equal(0, commands.Queries);
        Assert.Equal(1, Assert.Single(factory.Created).Disposals);
        Assert.Equal(2, (await asynchronous.CountAsync(new())).Total);
        Assert.Equal(1, context.Captures);
        Assert.Equal(2, factory.Created.Count);
        Assert.All(factory.Created, database => Assert.Equal(1, database.Disposals));
        var direct = new AuthorizedReads<Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            store,
            () => new Database(Options),
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 1)
        );
        Assert.Equal(2, (await direct.CountAsync(new())).Total);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_mismatch_disposes_owned_context_exactly_once(bool startup)
    {
        var factory = new Factory(Options);
        if (startup)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                EfEntityReadAdapter<Employee>.RegisterAsync(
                    CompilerTests.Schema,
                    factory,
                    "Microsoft.EntityFrameworkCore.SqlServer"
                )
            );
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                EfEntityReadAdapter<Employee>
                    .Create(factory, "Microsoft.EntityFrameworkCore.SqlServer")
                    .OpenAsync()
                    .AsTask()
            );
        Assert.Equal(1, Assert.Single(factory.Created).Disposals);
        Assert.Equal(0, commands.Queries);
    }

    [Fact]
    public async Task Cancellation_after_creation_disposes_owned_context_and_never_opens_a_session()
    {
        using var cancellation = new CancellationTokenSource();
        var factory = new Factory(Options) { AfterCreate = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SqliteEntityReads
                .Create<Employee, Database>(CompilerTests.Schema, factory)
                .OpenAsync(cancellation.Token)
                .AsTask()
        );
        Assert.Equal(1, Assert.Single(factory.Created).Disposals);
    }

    [Fact]
    public async Task Normal_reads_and_best_effort_batches_own_distinct_contexts_not_request_context()
    {
        await using var requestContext = new Database(Options);
        var factory = new Factory(Options);
        var repo = Repository(
            SqliteEntityReads.Create<Employee, Database>(CompilerTests.Schema, factory)
        );
        await repo.ListAsync(new(Limit: 1));
        await repo.CountAsync(new());
        await repo.DetailAsync(new(System.Text.Json.JsonSerializer.SerializeToElement(1)));
        var beforeExport = factory.Created.Count;
        var rows = 0;
        await foreach (var batch in repo.ExportAsync(new()))
            rows += batch.Records.Count;
        Assert.Equal(2, rows);
        Assert.True(factory.Created.Count > beforeExport + 1);
        Assert.Equal(factory.Created.Count, factory.Created.Distinct().Count());
        Assert.All(factory.Created, context => Assert.Equal(1, context.Disposals));
        Assert.Equal(0, requestContext.Disposals);
    }

    [Fact]
    public async Task Snapshot_export_owns_one_context_until_early_disposal()
    {
        var factory = new Factory(Options);
        var repo = Repository(
            SqliteEntityReads.Create<Employee, Database>(
                CompilerTests.Schema,
                factory,
                ReadOperations.All | ReadOperations.Snapshot
            ),
            ExportDataConsistency.Snapshot
        );
        var iterator = repo.ExportAsync(new()).GetAsyncEnumerator();
        try
        {
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal(0, Assert.Single(factory.Created).Disposals);
            Assert.True(await iterator.MoveNextAsync());
            Assert.Single(factory.Created);
        }
        finally
        {
            await iterator.DisposeAsync();
        }
        Assert.Equal(1, Assert.Single(factory.Created).Disposals);
    }

    [Fact]
    public async Task Snapshot_setup_failure_disposes_factory_context_without_replay()
    {
        var factory = new Factory(Options);
        var adapter = EfEntityReadAdapter<Employee>.Create(
            factory,
            "Microsoft.EntityFrameworkCore.Sqlite",
            (_, _) => throw new InvalidOperationException("snapshot setup failed")
        );
        await using var iterator = Repository(adapter, ExportDataConsistency.Snapshot)
            .ExportAsync(new())
            .GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            iterator.MoveNextAsync().AsTask()
        );
        Assert.Equal(1, Assert.Single(factory.Created).Disposals);
    }

    [Fact]
    public async Task Pooled_factory_returns_contexts_and_resets_tracking_between_sessions()
    {
        var factory = new PooledDbContextFactory<Database>(Options, poolSize: 1);
        await using (var first = await factory.CreateDbContextAsync())
            first.Attach(new Employee { Id = 100, TenantId = 2 });
        var adapter = SqliteEntityReads.Create<Employee, Database>(CompilerTests.Schema, factory);
        await adapter.ValidateAsync(CompilerTests.Schema);
        Assert.Equal(2, (await Repository(adapter).CountAsync(new())).Total);
        await using var next = await factory.CreateDbContextAsync();
        Assert.Empty(next.ChangeTracker.Entries());
    }
}
