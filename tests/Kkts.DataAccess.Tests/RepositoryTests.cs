using System.Text.Json;
using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Employee = Kkts.DataAccess.Tests.CompilerTests.Employee;

namespace Kkts.DataAccess.Tests;

public class RepositoryTests
{
    sealed class Source : IActivePolicySource
    {
        public CapturedPolicy Current = CompilerTests.Capture(
            CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
        );

        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(Current);
        }
    }

    sealed class RecordingAdapter : IEntityReadAdapter<Employee>
    {
        public int Opened,
            Disposed;
        public bool Fail;
        public Employee[] Rows =
        [
            new()
            {
                Id = 1,
                TenantId = 1,
                Name = "A",
            },
            new()
            {
                Id = 2,
                TenantId = 1,
                Name = "A",
            },
            new()
            {
                Id = 3,
                TenantId = 2,
                Name = "Hidden",
            },
        ];

        public ValueTask<IEntityReadSession<Employee>> OpenAsync(
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            Opened++;
            return ValueTask.FromResult<IEntityReadSession<Employee>>(new Session(this));
        }

        sealed class Session(RecordingAdapter owner) : IEntityReadSession<Employee>
        {
            IEnumerable<Employee> Query(EntityReadPlan<Employee> plan)
            {
                if (owner.Fail)
                    throw new InvalidOperationException("Adapter failure");
                // Recording test double only; production adapters execute predicates in their provider.
                return owner.Rows.Where(plan.Predicate.Compile());
            }

            public Task<long> CountAsync(EntityReadPlan<Employee> p, CancellationToken c) =>
                Task.FromResult(Query(p).LongCount());

            public Task<Employee?> SingleAsync(EntityReadPlan<Employee> p, CancellationToken c) =>
                Task.FromResult(Query(p).SingleOrDefault());

            public Task<IReadOnlyList<Employee>> PageAsync(
                EntityReadPlan<Employee> p,
                int offset,
                int limit,
                CancellationToken c
            )
            {
                IOrderedEnumerable<Employee>? sorted = null;
                foreach (var s in p.Sort)
                {
                    Func<Employee, object?> key = e => s.Property.GetValue(e);
                    sorted =
                        sorted == null
                            ? (
                                s.Descending
                                    ? Query(p).OrderByDescending(key)
                                    : Query(p).OrderBy(key)
                            )
                            : (s.Descending ? sorted.ThenByDescending(key) : sorted.ThenBy(key));
                }
                return Task.FromResult<IReadOnlyList<Employee>>(
                    sorted!.Skip(offset).Take(limit).ToArray()
                );
            }

            public ValueTask DisposeAsync()
            {
                owner.Disposed++;
                return ValueTask.CompletedTask;
            }
        }
    }

    static AuthorizedReadRepository<Employee> Repository(Source source, RecordingAdapter adapter) =>
        new(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            source,
            adapter,
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 1)
        );

    [Fact]
    public async Task Core_repository_preserves_validation_results_and_captured_export()
    {
        var source = new Source();
        var adapter = new RecordingAdapter();
        var repo = Repository(source, adapter);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            repo.ListAsync(new(Selection: ["Salary"]))
        );
        Assert.Equal(0, adapter.Opened);
        var page = await repo.ListAsync(new(Sort: [new("Name")], Limit: 1));
        Assert.Equal(2, page.Total);
        Assert.Equal(1, page.Records[0].GetProperty("Id").GetInt32());
        Assert.Equal(2, page.Records[0].EnumerateObject().Count());
        Assert.Equal(2, await repo.CountAsync(new(Limit: 1)));
        Assert.Null(
            (await repo.DetailAsync(JsonSerializer.SerializeToElement(3), new(Limit: 1))).Record
        );
        Assert.Null(
            (await repo.DetailAsync(JsonSerializer.SerializeToElement(99), new(Limit: 1))).Record
        );
        Assert.Equal(
            "{}",
            (await repo.ListAsync(new(Selection: [], Limit: 1))).Records[0].GetRawText()
        );
        await using (var export = repo.ExportAsync(new(Limit: 1)).GetAsyncEnumerator())
        {
            Assert.True(await export.MoveNextAsync());
            source.Current = CompilerTests.Capture(CompilerTests.Definition(), 2);
            Assert.True(await export.MoveNextAsync());
            Assert.Equal(1, export.Current.PolicyVersion);
        }
        Assert.Equal(adapter.Opened, adapter.Disposed);
        await Assert.ThrowsAsync<AuthorizationException>(() => repo.CountAsync(new(Limit: 1)));
    }

    [Fact]
    public async Task Core_repository_disposes_failure_and_stops_cancelled_export()
    {
        var adapter = new RecordingAdapter { Fail = true };
        var repo = Repository(new Source(), adapter);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ListAsync(new(Limit: 1)));
        Assert.Equal(1, adapter.Disposed);
        adapter.Fail = false;
        using var cancellation = new CancellationTokenSource();
        await using var export = repo.ExportAsync(new(Limit: 1), cancellation.Token)
            .GetAsyncEnumerator();
        Assert.True(await export.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await export.MoveNextAsync()
        );
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Ef_provider_mismatch_disposes_owned_context()
    {
        var db = new TrackedDatabase(
            new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options
        );
        var adapter = new EfEntityReadAdapter<Employee>(
            () => db,
            "Microsoft.EntityFrameworkCore.SqlServer"
        );
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await adapter.OpenAsync());
        Assert.True(db.Disposed);
    }

    [Fact]
    public void Registration_rejects_mismatch_before_querying()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Kkts.DataAccess.SqlServer.SqlServerEntityReads.Create<Employee>(() =>
                new DbContext(
                    new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options
                )
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            Kkts.DataAccess.MySql.MySqlEntityReads.Create<Employee>(() =>
                new DbContext(
                    new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options
                )
            )
        );
    }

    sealed class TrackedDatabase(DbContextOptions options) : DbContext(options)
    {
        public bool Disposed;

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }
    }
}
