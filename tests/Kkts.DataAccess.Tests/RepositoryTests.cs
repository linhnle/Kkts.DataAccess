using System.Text.Json;
using Kkts.DataAccess.DataProtection;
using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Employee = Kkts.DataAccess.Tests.CompilerTests.Employee;

namespace Kkts.DataAccess.Tests;

public class RepositoryTests
{
    [Fact]
    public async Task Recording_extension_consumes_portable_protected_repository_scenarios()
    {
        var report =
            await new Testing.AdapterConformanceSuite<AdapterConformance.ConformanceEntity>().RunAsync(
                () =>
                    new AdapterConformance.RecordingFixture()
            );
        Assert.DoesNotContain(
            report.Results,
            result => result.Status == Testing.ConformanceStatus.Failed
        );
        Assert.False(report.Passed);
        Assert.Contains(
            report.Results,
            result =>
                result.ScenarioId == Testing.ConformanceScenario.DatabaseEvidence
                && result.Status == Testing.ConformanceStatus.NotRun
        );
    }

    internal sealed class AsyncContext(Func<CancellationToken, ValueTask<TrustedContext>> get)
        : ITrustedContextProvider
    {
        public int Captures;

        public ValueTask<TrustedContext> GetAsync(CancellationToken cancellation = default)
        {
            Captures++;
            return get(cancellation);
        }
    }

    [Fact]
    public async Task Async_context_is_awaited_once_per_operation_and_export_is_lazy_and_stable()
    {
        var source = new Source();
        var adapter = new RecordingAdapter();
        var current = CompilerTests.Context();
        var context = new AsyncContext(async token =>
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            return current;
        });
        var repo = new AuthorizedReadRepository<Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            source,
            adapter,
            context,
            "scope",
            new(2, 100, 1),
            Cursor(),
            ReadProjectionMode.AllowEntityMaterialization
        );
        await repo.ListAsync(new(Limit: 1));
        await repo.CountAsync(new());
        await repo.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 1 })));
        await repo.ListCursorAsync(new(Limit: 1));
        Assert.Equal(4, context.Captures);
        await using var export = repo.ExportAsync(new()).GetAsyncEnumerator();
        Assert.Equal(4, context.Captures);
        Assert.True(await export.MoveNextAsync());
        current = CompilerTests.Context(tenant: 2);
        Assert.True(await export.MoveNextAsync());
        Assert.Equal(2, Assert.Single(export.Current.Records).GetProperty("Id").GetInt32());
        Assert.False(await export.MoveNextAsync());
        Assert.Equal(5, context.Captures);
        Assert.Equal(5, source.Captures);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("missing")]
    [InlineData("wrong-type")]
    [InlineData("null")]
    public async Task Async_context_failure_or_invalid_identity_opens_no_session(string failure)
    {
        var source = new Source();
        var adapter = new RecordingAdapter();
        using var cancellation = new CancellationTokenSource();
        var context = new AsyncContext(async token =>
        {
            await Task.Yield();
            if (failure == "failure")
                throw new InvalidOperationException("acquisition failed");
            if (failure == "cancel")
            {
                cancellation.Cancel();
                return CompilerTests.Context();
            }
            if (failure == "null")
                return null!;
            return new(
                ["manager"],
                [],
                new Dictionary<string, object?>
                {
                    ["tenant"] = failure == "wrong-type" ? "1" : null,
                    ["department"] = 10,
                }
            );
        });
        var repo = new AuthorizedReadRepository<Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            source,
            adapter,
            context,
            "scope"
        );
        var error = await Record.ExceptionAsync(() => repo.CountAsync(new(), cancellation.Token));
        Assert.NotNull(error);
        if (failure == "cancel")
            Assert.IsAssignableFrom<OperationCanceledException>(error);
        else if (failure == "failure")
            Assert.IsType<InvalidOperationException>(error);
        else
            Assert.IsType<AuthorizationException>(error);
        Assert.Equal(1, context.Captures);
        Assert.Equal(0, adapter.Opened);
    }

    [Fact]
    public async Task Async_context_does_not_block_and_shared_compiler_does_not_share_identity()
    {
        var adapter = new RecordingAdapter();
        var source = new Source();
        var compiler = CompilerTests.Compiler();
        var pending = new TaskCompletionSource<TrustedContext>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var first = new AuthorizedReadRepository<Employee>(
            CompilerTests.Schema,
            compiler,
            source,
            adapter,
            new AsyncContext(token => new(pending.Task.WaitAsync(token))),
            "scope"
        );
        var result = first.CountAsync(new());
        Assert.False(result.IsCompleted);
        Assert.Equal(0, adapter.Opened);
        pending.SetResult(CompilerTests.Context());
        Assert.Equal(2, (await result).Total);
        var second = new AuthorizedReadRepository<Employee>(
            CompilerTests.Schema,
            compiler,
            source,
            adapter,
            new AsyncContext(_ => ValueTask.FromResult(CompilerTests.Context(tenant: 2))),
            "scope"
        );
        Assert.Equal(1, (await second.CountAsync(new())).Total);
        Assert.Equal(2, (await first.CountAsync(new())).Total);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            first.CountAsync(new(), canceled.Token)
        );
    }

    sealed class Source : IActivePolicySource
    {
        public int Captures;
        public Action? OnCapture;
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
            Captures++;
            OnCapture?.Invoke();
            return Task.FromResult(Current);
        }
    }

    sealed class RecordingAdapter : IEntityReadAdapter<Employee>
    {
        public int Opened,
            Disposed;
        public int CountCalls,
            PageCalls,
            SeekCalls,
            SingleCalls;
        public List<int> SeekLimits = [];
        public List<int> PageLimits = [];
        public List<int> PageOffsets = [];
        public bool FailCount;
        public long? CountOverride;
        public Action? OnCount;
        public bool SupportsProjection;
        public bool SupportsProjectedSeek;
        public int ProjectedPageCalls,
            ProjectedSingleCalls,
            ProjectedSeekCalls;
        public Func<
            IReadOnlyList<ProjectedReadRow>,
            IReadOnlyList<ProjectedReadRow>
        >? ProjectedRows;
        public bool SupportsSeek = true;
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
            var session = new Session(this);
            if (SupportsProjectedSeek)
                return ValueTask.FromResult<IEntityReadSession<Employee>>(
                    new ProjectedSeekSession(this, session)
                );
            if (SupportsProjection)
                return ValueTask.FromResult<IEntityReadSession<Employee>>(
                    SupportsSeek
                        ? new ProjectedWithEntitySeekSession(this, session)
                        : new ProjectedSession(this, session)
                );
            return ValueTask.FromResult<IEntityReadSession<Employee>>(
                SupportsSeek ? session : new EntityOnlySession(session)
            );
        }

        class ProjectedSession(RecordingAdapter owner, Session session)
            : IEntityProjectedReadSession<Employee>
        {
            protected RecordingAdapter Owner => owner;
            protected Session EntitySession => session;

            protected IReadOnlyList<ProjectedReadRow> Project(
                IEnumerable<Employee> entities,
                EntityReadPlan<Employee> plan,
                EntitySeekPlan<Employee>? seek = null
            )
            {
                var rows = entities
                    .Select(entity => new ProjectedReadRow(
                        plan.OutputBindings.Select(binding => binding.Property.GetValue(entity)),
                        seek?.ContinuationBindings.Select(property => property.GetValue(entity))
                    ))
                    .ToArray();
                return owner.ProjectedRows?.Invoke(rows) ?? rows;
            }

            public Task<IReadOnlyList<ProjectedReadRow>> PageProjectedAsync(
                EntityReadPlan<Employee> plan,
                int offset,
                int limit,
                CancellationToken cancellation
            )
            {
                owner.ProjectedPageCalls++;
                owner.PageLimits.Add(limit);
                owner.PageOffsets.Add(offset);
                return Task.FromResult(Project(session.Order(plan).Skip(offset).Take(limit), plan));
            }

            public Task<ProjectedReadRow?> SingleProjectedAsync(
                EntityReadPlan<Employee> plan,
                CancellationToken cancellation
            )
            {
                owner.ProjectedSingleCalls++;
                return Task.FromResult(Project(session.Query(plan), plan).SingleOrDefault());
            }

            public Task<long> CountAsync(
                EntityReadPlan<Employee> plan,
                CancellationToken cancellation
            ) => session.CountAsync(plan, cancellation);

            public Task<IReadOnlyList<Employee>> PageAsync(
                EntityReadPlan<Employee> plan,
                int offset,
                int limit,
                CancellationToken cancellation
            ) => session.PageAsync(plan, offset, limit, cancellation);

            public Task<Employee?> SingleAsync(
                EntityReadPlan<Employee> plan,
                CancellationToken cancellation
            ) => session.SingleAsync(plan, cancellation);

            public ValueTask DisposeAsync() => session.DisposeAsync();
        }

        sealed class ProjectedWithEntitySeekSession(RecordingAdapter owner, Session session)
            : ProjectedSession(owner, session),
                IEntitySeekReadSession<Employee>
        {
            public Task<IReadOnlyList<Employee>> SeekAsync(
                EntitySeekPlan<Employee> plan,
                int limit,
                CancellationToken cancellation
            ) => EntitySession.SeekAsync(plan, limit, cancellation);
        }

        sealed class ProjectedSeekSession(RecordingAdapter owner, Session session)
            : ProjectedSession(owner, session),
                IEntityProjectedSeekReadSession<Employee>
        {
            public Task<IReadOnlyList<ProjectedReadRow>> SeekProjectedAsync(
                EntitySeekPlan<Employee> plan,
                int limit,
                CancellationToken cancellation
            )
            {
                Owner.ProjectedSeekCalls++;
                Owner.SeekLimits.Add(limit);
                return Task.FromResult(
                    Project(EntitySession.SeekRows(plan).Take(limit), plan.Read, plan)
                );
            }
        }

        sealed class EntityOnlySession(IEntityReadSession<Employee> session)
            : IEntityReadSession<Employee>
        {
            public Task<long> CountAsync(EntityReadPlan<Employee> plan, CancellationToken token) =>
                session.CountAsync(plan, token);

            public Task<Employee?> SingleAsync(
                EntityReadPlan<Employee> plan,
                CancellationToken token
            ) => session.SingleAsync(plan, token);

            public Task<IReadOnlyList<Employee>> PageAsync(
                EntityReadPlan<Employee> plan,
                int offset,
                int limit,
                CancellationToken token
            ) => session.PageAsync(plan, offset, limit, token);

            public ValueTask DisposeAsync() => session.DisposeAsync();
        }

        sealed class Session(RecordingAdapter owner) : IEntitySeekReadSession<Employee>
        {
            internal IEnumerable<Employee> Query(EntityReadPlan<Employee> plan)
            {
                if (owner.Fail)
                    throw new InvalidOperationException("Adapter failure");
                // Recording test double only; production adapters execute predicates in their provider.
                return owner.Rows.Where(plan.Predicate.Compile());
            }

            public Task<long> CountAsync(EntityReadPlan<Employee> p, CancellationToken c)
            {
                owner.CountCalls++;
                owner.OnCount?.Invoke();
                c.ThrowIfCancellationRequested();
                if (owner.FailCount)
                    throw new InvalidOperationException("Count failure");
                return Task.FromResult(owner.CountOverride ?? Query(p).LongCount());
            }

            public Task<Employee?> SingleAsync(EntityReadPlan<Employee> p, CancellationToken c)
            {
                owner.SingleCalls++;
                return Task.FromResult(Query(p).SingleOrDefault());
            }

            public Task<IReadOnlyList<Employee>> PageAsync(
                EntityReadPlan<Employee> p,
                int offset,
                int limit,
                CancellationToken c
            )
            {
                owner.PageCalls++;
                owner.PageLimits.Add(limit);
                owner.PageOffsets.Add(offset);
                return Task.FromResult<IReadOnlyList<Employee>>(
                    Order(p).Skip(offset).Take(limit).ToArray()
                );
            }

            internal IEnumerable<Employee> Order(EntityReadPlan<Employee> p)
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
                return sorted!;
            }

            public Task<IReadOnlyList<Employee>> SeekAsync(
                EntitySeekPlan<Employee> plan,
                int limit,
                CancellationToken cancellation
            )
            {
                cancellation.ThrowIfCancellationRequested();
                owner.SeekCalls++;
                owner.SeekLimits.Add(limit);
                return Task.FromResult<IReadOnlyList<Employee>>(
                    SeekRows(plan).Take(limit).ToArray()
                );
            }

            internal IEnumerable<Employee> SeekRows(EntitySeekPlan<Employee> plan) =>
                Order(plan.Read)
                    .Where(row =>
                    {
                        if (plan.Boundary == null)
                            return true;
                        for (var i = 0; i < plan.Read.Sort.Count; i++)
                        {
                            var sort = plan.Read.Sort[i];
                            var compared = Comparer<object?>.Default.Compare(
                                sort.Property.GetValue(row),
                                plan.Boundary[i]
                            );
                            if (compared != 0)
                                return sort.Descending ? compared < 0 : compared > 0;
                        }
                        return false;
                    });

            public ValueTask DisposeAsync()
            {
                owner.Disposed++;
                return ValueTask.CompletedTask;
            }
        }
    }

    static AuthorizedReadRepository<Employee> Repository(
        Source source,
        RecordingAdapter adapter,
        CursorReadConfiguration? cursor = null,
        Func<TrustedContext>? trusted = null,
        ReadProjectionMode projectionMode = ReadProjectionMode.AllowEntityMaterialization
    ) =>
        new(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            source,
            adapter,
            trusted ?? (() => CompilerTests.Context()),
            "scope",
            new(2, 100, 1),
            cursor,
            projectionMode
        );

    static CursorReadConfiguration Cursor(
        Func<string>? subject = null,
        CursorTokenTests.Clock? clock = null
    ) =>
        new(
            new DataProtectionContinuationTokenProtector(new EphemeralDataProtectionProvider()),
            subject ?? (() => "subject"),
            "dataset",
            timeProvider: clock
        );

    static ResourceSchema<Employee> CompositeSchema(IEnumerable<string>? keys = null) =>
        new(
            "Employee",
            "1",
            CompilerTests.Schema.Fields.Values.Append(
                ResourceSchema<Employee>.Field("LocalAlias", row => row.Id, [FieldOperation.Sort])
            ),
            keys ?? ["DepartmentId", "Id"]
        );

    static AuthorizedReadRepository<Employee> CompositeRepository(
        ResourceSchema<Employee> schema,
        Source source,
        RecordingAdapter adapter,
        CursorReadConfiguration? cursor = null
    ) =>
        new(
            schema,
            new(schema, CompilerTests.Registry, CompilerTests.Reference("TenantId", "tenant")),
            source,
            adapter,
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 1),
            cursor,
            ReadProjectionMode.AllowEntityMaterialization
        );

    [Theory]
    [InlineData("1", "/key")]
    [InlineData("{}", "/key/DepartmentId")]
    [InlineData("""{"DepartmentId":10}""", "/key/Id")]
    [InlineData("""{"DepartmentId":10,"Id":1,"Id":2}""", "/key/Id")]
    [InlineData("""{"DepartmentId":10,"Id":1,"extra":2}""", "/key/extra")]
    [InlineData("""{"departmentId":10,"Id":1}""", "/key/departmentId")]
    [InlineData("""{"DepartmentId":null,"Id":1}""", "/key/DepartmentId")]
    [InlineData("""{"DepartmentId":10,"Id":"1"}""", "/key/Id")]
    [InlineData("""{"DepartmentId":10,"Id":1.5}""", "/key/Id")]
    public async Task Composite_detail_invalid_members_open_no_session(string json, string path)
    {
        var adapter = new RecordingAdapter();
        var repo = CompositeRepository(CompositeSchema(), new Source(), adapter);
        var error = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            repo.DetailAsync(new(JsonDocument.Parse(json).RootElement))
        );
        Assert.Equal(path, Assert.Single(error.Diagnostics).Path);
        Assert.Equal(0, adapter.Opened);
    }

    [Fact]
    public async Task Composite_detail_is_joint_equality_without_implicit_field_grants()
    {
        var adapter = new RecordingAdapter
        {
            Rows =
            [
                new()
                {
                    DepartmentId = 10,
                    Id = 1,
                    TenantId = 1,
                    Name = "first",
                },
                new()
                {
                    DepartmentId = 20,
                    Id = 1,
                    TenantId = 1,
                    Name = "second",
                },
                new()
                {
                    DepartmentId = 10,
                    Id = 2,
                    TenantId = 1,
                    Name = "denied",
                    Status = "Inactive",
                },
                new()
                {
                    DepartmentId = 30,
                    Id = 1,
                    TenantId = 2,
                    Name = "hidden",
                },
            ],
        };
        var source = new Source
        {
            Current = CompilerTests.Capture(
                CompilerTests.Definition(
                    CompilerTests.Rule("allow", Effect.Allow),
                    CompilerTests.Rule(
                        "deny",
                        Effect.Deny,
                        CompilerTests.Leaf("Status", "eq", "Inactive")
                    )
                )
            ),
        };
        var repo = CompositeRepository(CompositeSchema(), source, adapter);
        var key = JsonSerializer.SerializeToElement(new { DepartmentId = 20, Id = 1 });
        var detail = await repo.DetailAsync(new(key, Selection: ["Name"]));
        Assert.Equal("second", detail.Record!.Value.GetProperty("Name").GetString());
        Assert.Equal(
            "{}",
            (await repo.DetailAsync(new(key, Selection: []))).Record!.Value.GetRawText()
        );
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            repo.DetailAsync(new(key, Filter: CompilerTests.Leaf("Id", "eq", 1)))
        );
        Assert.Null(
            (
                await repo.DetailAsync(
                    new(JsonSerializer.SerializeToElement(new { DepartmentId = 20, Id = 2 }))
                )
            ).Record
        );
        Assert.Null(
            (
                await repo.DetailAsync(
                    new(JsonSerializer.SerializeToElement(new { DepartmentId = 10, Id = 2 }))
                )
            ).Record
        );
        Assert.Null(
            (
                await repo.DetailAsync(
                    new(JsonSerializer.SerializeToElement(new { DepartmentId = 30, Id = 1 }))
                )
            ).Record
        );
        adapter.Rows = [adapter.Rows[1], adapter.Rows[1]];
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DetailAsync(new(key)));
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Composite_hidden_tuple_completes_partial_alias_and_repeated_order(
        bool projected
    )
    {
        var adapter = new RecordingAdapter
        {
            SupportsProjectedSeek = projected,
            Rows =
            [
                new()
                {
                    DepartmentId = 20,
                    Id = 1,
                    TenantId = 1,
                    Name = "third",
                },
                new()
                {
                    DepartmentId = 10,
                    Id = 1,
                    TenantId = 1,
                    Name = "second",
                },
                new()
                {
                    DepartmentId = 10,
                    Id = 2,
                    TenantId = 1,
                    Name = "first",
                },
            ],
        };
        var original = CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow));
        var entry = original.FieldEntries[0];
        entry.Fields[FieldOperation.Sort] = ["Name", "LocalAlias"];
        var source = new Source { Current = CompilerTests.Capture(original) };
        var config = Cursor();
        var repo = CompositeRepository(CompositeSchema(), source, adapter, config);
        SortField[] sort = [new("LocalAlias", true), new("LocalAlias", true)];
        var list = await repo.ListAsync(new(Selection: ["Name"], Sort: sort, Limit: 2));
        Assert.Equal(
            ["first", "second"],
            list.Records.Select(row => row.GetProperty("Name").GetString())
        );
        var names = new List<string?>();
        string? token = null;
        do
        {
            var page = await repo.ListCursorAsync(
                new(
                    Selection: ["Name"],
                    Sort: sort,
                    Limit: 1,
                    ContinuationToken: token,
                    IncludeTotal: false,
                    IncludeHasMore: true
                )
            );
            Assert.Null(page.Total);
            Assert.All(page.Records, row => Assert.Single(row.EnumerateObject()));
            names.AddRange(page.Records.Select(row => row.GetProperty("Name").GetString()));
            token = page.NextContinuationToken;
            Assert.Equal(token != null, page.HasMore);
        } while (token != null);
        Assert.Equal(["first", "second", "third"], names);
        var exported = new List<string?>();
        await foreach (var batch in repo.ExportAsync(new(Selection: ["Name"], Sort: sort)))
            exported.AddRange(batch.Records.Select(row => row.GetProperty("Name").GetString()));
        Assert.Equal(names, exported);
        var first = await repo.ListCursorAsync(new(Selection: ["Name"], Sort: sort, Limit: 1));
        var changed = CompositeRepository(
            CompositeSchema(["Id", "DepartmentId"]),
            source,
            adapter,
            config
        );
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
            changed.ListCursorAsync(
                new(
                    Selection: ["Name"],
                    Sort: sort,
                    Limit: 1,
                    ContinuationToken: first.NextContinuationToken
                )
            )
        );
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Theory]
    [InlineData("warm")]
    [InlineData("cold")]
    [InlineData("evicted")]
    public async Task Cursor_cache_paths_preserve_tokens_metadata_authorization_and_version(
        string path
    )
    {
        foreach (var includeTotal in new[] { false, true })
        foreach (var includeHasMore in new[] { false, true })
        {
            var source = new Source();
            var adapter = new RecordingAdapter();
            PolicyCompiler<Employee> NewCompiler() =>
                new(
                    CompilerTests.Schema,
                    CompilerTests.Registry,
                    CompilerTests.Reference("TenantId", "tenant"),
                    cacheOptions: new(1)
                );
            var compiler = NewCompiler();
            var config = Cursor(() =>
            {
                if (path == "evicted")
                    compiler.Bind(
                        source.Current with
                        {
                            Version = 2,
                        },
                        CompilerTests.Context(tenant: 2),
                        "scope"
                    );
                return "subject";
            });
            AuthorizedReadRepository<Employee> CreateRepository() =>
                new(
                    CompilerTests.Schema,
                    compiler,
                    source,
                    adapter,
                    () => CompilerTests.Context(),
                    "scope",
                    new(2, 100, 1),
                    config,
                    ReadProjectionMode.AllowEntityMaterialization
                );
            var first = await CreateRepository()
                .ListCursorAsync(
                    new(
                        Selection: ["Id"],
                        Limit: 1,
                        IncludeTotal: includeTotal,
                        IncludeHasMore: includeHasMore
                    )
                );
            Assert.Equal(1, first.PolicyVersion);
            Assert.Equal(1, Assert.Single(first.Records).GetProperty("Id").GetInt32());
            Assert.Equal(includeTotal ? 2L : (long?)null, first.Total);
            Assert.Equal(includeHasMore ? true : (bool?)null, first.HasMore);
            Assert.NotNull(first.NextContinuationToken);
            if (path == "cold")
                compiler = NewCompiler();
            else if (path == "evicted")
                Assert.Equal(new long[] { 2 }, compiler.TemplateInsertionOrder);

            var second = await CreateRepository()
                .ListCursorAsync(
                    new(
                        Selection: ["Id"],
                        Limit: 1,
                        ContinuationToken: first.NextContinuationToken,
                        IncludeTotal: !includeTotal,
                        IncludeHasMore: !includeHasMore
                    )
                );
            Assert.Equal(1, second.PolicyVersion);
            Assert.Equal(2, Assert.Single(second.Records).GetProperty("Id").GetInt32());
            Assert.Equal(!includeTotal ? 2L : (long?)null, second.Total);
            Assert.Equal(!includeHasMore ? false : (bool?)null, second.HasMore);
            Assert.Null(second.NextContinuationToken);
            Assert.Equal(1, adapter.CountCalls);
            Assert.Equal(2, source.Captures);

            source.Current = source.Current with { Version = 3 };
            var opened = adapter.Opened;
            await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
                CreateRepository()
                    .ListCursorAsync(
                        new(
                            Selection: ["Id"],
                            Limit: 1,
                            ContinuationToken: first.NextContinuationToken
                        )
                    )
            );
            Assert.Equal(opened, adapter.Opened);
            Assert.Equal(adapter.Opened, adapter.Disposed);
        }
    }

    [Theory]
    [InlineData("warm")]
    [InlineData("cold")]
    [InlineData("evicted")]
    public async Task Export_cache_paths_keep_captured_policy_and_context_after_eviction(
        string path
    )
    {
        var source = new Source();
        var adapter = new RecordingAdapter();
        var compiler = new PolicyCompiler<Employee>(
            CompilerTests.Schema,
            CompilerTests.Registry,
            CompilerTests.Reference("TenantId", "tenant"),
            cacheOptions: new(1)
        );
        if (path != "cold")
            compiler.Bind(source.Current, CompilerTests.Context(), "scope");
        if (path == "evicted")
            compiler.Bind(source.Current with { Version = 2 }, CompilerTests.Context(), "scope");
        var trusted = CompilerTests.Context();
        var repository = new AuthorizedReadRepository<Employee>(
            CompilerTests.Schema,
            compiler,
            source,
            adapter,
            () => trusted,
            "scope",
            new(2, 100, 1),
            projectionMode: ReadProjectionMode.AllowEntityMaterialization
        );
        await using var export = repository
            .ExportAsync(new(Selection: ["Id"]))
            .GetAsyncEnumerator();
        Assert.True(await export.MoveNextAsync());
        Assert.Equal(1, export.Current.PolicyVersion);
        Assert.Equal(1, Assert.Single(export.Current.Records).GetProperty("Id").GetInt32());

        trusted = CompilerTests.Context(tenant: 2);
        source.Current = CompilerTests.Capture(
            CompilerTests.Definition(
                CompilerTests.Rule("new", Effect.Allow, CompilerTests.Leaf("Id", "eq", 3))
            ),
            2
        );
        compiler.Bind(source.Current, trusted, "scope");
        Assert.Equal(new long[] { 2 }, compiler.TemplateInsertionOrder);
        Assert.True(await export.MoveNextAsync());
        Assert.Equal(1, export.Current.PolicyVersion);
        Assert.Equal(2, Assert.Single(export.Current.Records).GetProperty("Id").GetInt32());
        Assert.False(await export.MoveNextAsync());
        Assert.Equal(1, source.Captures);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Theory]
    [InlineData(ReadProjectionMode.Required, false, false)]
    [InlineData(ReadProjectionMode.Required, false, true)]
    [InlineData(ReadProjectionMode.Required, true, false)]
    [InlineData(ReadProjectionMode.Required, true, true)]
    [InlineData(ReadProjectionMode.AllowEntityMaterialization, false, false)]
    [InlineData(ReadProjectionMode.AllowEntityMaterialization, false, true)]
    [InlineData(ReadProjectionMode.AllowEntityMaterialization, true, false)]
    [InlineData(ReadProjectionMode.AllowEntityMaterialization, true, true)]
    public async Task Seek_capability_matrix_uses_only_explicit_absence_fallback(
        ReadProjectionMode mode,
        bool projectedSeek,
        bool entitySeek
    )
    {
        var adapter = new RecordingAdapter
        {
            SupportsProjection = true,
            SupportsProjectedSeek = projectedSeek,
            SupportsSeek = entitySeek,
        };
        var repo = Repository(new Source(), adapter, Cursor(), projectionMode: mode);
        if (!projectedSeek && (mode == ReadProjectionMode.Required || !entitySeek))
        {
            await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
                repo.ListCursorAsync(new(Limit: 1))
            );
            await using var export = repo.ExportAsync(new()).GetAsyncEnumerator();
            await Assert.ThrowsAsync<ProviderUnsupportedException>(async () =>
                await export.MoveNextAsync()
            );
            Assert.Equal(0, adapter.CountCalls);
            Assert.Equal(0, adapter.SeekCalls);
        }
        else
        {
            var page = await repo.ListCursorAsync(new(Selection: ["Id"], Limit: 1));
            Assert.Equal(1, Assert.Single(page.Records).GetProperty("Id").GetInt32());
            var ids = new List<int>();
            await foreach (var batch in repo.ExportAsync(new(Selection: ["Id"])))
                ids.Add(Assert.Single(batch.Records).GetProperty("Id").GetInt32());
            Assert.Equal(new[] { 1, 2 }, ids);
            Assert.Equal(projectedSeek ? 0 : 4, adapter.SeekCalls);
            Assert.Equal(projectedSeek ? 4 : 0, adapter.ProjectedSeekCalls);
        }
        Assert.Equal(0, adapter.PageCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Theory]
    [InlineData(ReadProjectionMode.Required)]
    [InlineData(ReadProjectionMode.AllowEntityMaterialization)]
    public async Task Projected_only_seek_adapter_handles_all_operations_without_entity_retries(
        ReadProjectionMode mode
    )
    {
        var adapter = new RecordingAdapter { SupportsProjectedSeek = true };
        var source = new Source();
        var repo = Repository(source, adapter, Cursor(), projectionMode: mode);
        var first = await repo.ListAsync(new(Selection: ["Name"], Limit: 1, IncludeHasMore: true));
        Assert.True(first.HasMore);
        Assert.Single(Assert.Single(first.Records).EnumerateObject());
        var detail = await repo.DetailAsync(
            new(JsonSerializer.SerializeToElement(new { Id = 1 }), Selection: [])
        );
        Assert.Equal("{}", detail.Record!.Value.GetRawText());
        Assert.Null(
            (await repo.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 3 })))).Record
        );
        var request = new CursorListReadRequest(Selection: [], Limit: 1, IncludeTotal: false);
        var cursor = await repo.ListCursorAsync(request);
        Assert.Equal("{}", Assert.Single(cursor.Records).GetRawText());
        Assert.Null(cursor.Total);
        Assert.NotNull(cursor.NextContinuationToken);
        var final = await repo.ListCursorAsync(
            request with
            {
                ContinuationToken = cursor.NextContinuationToken,
            }
        );
        Assert.Null(final.NextContinuationToken);
        var ids = new List<int>();
        await foreach (var batch in repo.ExportAsync(new(Selection: ["Id"])))
            ids.Add(Assert.Single(batch.Records).GetProperty("Id").GetInt32());
        Assert.Equal(new[] { 1, 2 }, ids);
        Assert.Equal(new ReadCount(1, 2), await repo.CountAsync(new()));
        Assert.Equal(0, adapter.PageCalls);
        Assert.Equal(0, adapter.SeekCalls);
        Assert.Equal(0, adapter.SingleCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        var pages = adapter.ProjectedPageCalls;
        adapter.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.ListAsync(new(Limit: 1, IncludeTotal: false))
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ListCursorAsync(request));
        Assert.Equal(pages + 1, adapter.ProjectedPageCalls);
        Assert.Equal(0, adapter.PageCalls);
        Assert.Equal(0, adapter.SeekCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Required_projection_checks_relevant_capabilities_before_counts_and_disposes()
    {
        var adapter = new RecordingAdapter();
        var repo = Repository(
            new Source(),
            adapter,
            Cursor(),
            projectionMode: ReadProjectionMode.Required
        );
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() => repo.ListAsync(new(Limit: 1)));
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
            repo.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 1 })))
        );
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
            repo.ListCursorAsync(new(Limit: 1))
        );
        await using (var export = repo.ExportAsync(new()).GetAsyncEnumerator())
            await Assert.ThrowsAsync<ProviderUnsupportedException>(async () =>
                await export.MoveNextAsync()
            );
        Assert.Equal(0, adapter.CountCalls);
        Assert.Equal(0, adapter.PageCalls);
        Assert.Equal(0, adapter.SeekCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(new ReadCount(1, 2), await repo.CountAsync(new()));
        adapter.SupportsProjection = true;
        await repo.ListAsync(new(Limit: 1));
        var counts = adapter.CountCalls;
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
            repo.ListCursorAsync(new(Limit: 1))
        );
        Assert.Equal(counts, adapter.CountCalls);
        Assert.Equal(1, adapter.ProjectedPageCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        var mode = typeof(AuthorizedReadRepository<Employee>)
            .GetConstructors()[0]
            .GetParameters()
            .Single(parameter => parameter.Name == "projectionMode");
        Assert.Equal(ReadProjectionMode.Required, mode.DefaultValue);
        Assert.Equal(
            ReadProjectionMode.Required,
            typeof(Sqlite.AuthorizedReads<Employee>)
                .GetConstructors()[0]
                .GetParameters()
                .Single(parameter => parameter.Name == "projectionMode")
                .DefaultValue
        );
        Assert.Throws<ArgumentException>(() =>
            Repository(new Source(), adapter, projectionMode: (ReadProjectionMode)99)
        );
    }

    [Theory]
    [InlineData(ReadProjectionMode.Required)]
    [InlineData(ReadProjectionMode.AllowEntityMaterialization)]
    public async Task Malformed_projection_fails_whole_page_or_batch_without_entity_retry(
        ReadProjectionMode mode
    )
    {
        var adapter = new RecordingAdapter { SupportsProjectedSeek = true };
        var repo = Repository(new Source(), adapter, Cursor(), projectionMode: mode);
        foreach (
            var invalid in new ProjectedReadRow[]
            {
                new([]),
                new([1L]),
                new([null]),
                new([1, "extra"]),
                new([1], [1]),
            }
        )
        {
            adapter.ProjectedRows = _ => [invalid];
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.ListAsync(new(Selection: ["Id"], Limit: 1, IncludeTotal: false))
            );
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.DetailAsync(
                    new(JsonSerializer.SerializeToElement(new { Id = 1 }), Selection: ["Id"])
                )
            );
        }
        foreach (
            var invalid in new ProjectedReadRow[]
            {
                new([1]),
                new([1], [1L]),
                new([1], [null]),
                new([1], [1, 2]),
            }
        )
        {
            adapter.ProjectedRows = _ => [invalid];
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.ListCursorAsync(new(Selection: ["Id"], Limit: 1, IncludeTotal: false))
            );
            await using var export = repo.ExportAsync(new(Selection: ["Id"])).GetAsyncEnumerator();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await export.MoveNextAsync()
            );
        }
        adapter.ProjectedRows = rows => rows.Concat([new ProjectedReadRow([1L])]).ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.ListAsync(
                new(Selection: ["Id"], Limit: 1, IncludeTotal: false, IncludeHasMore: true)
            )
        );
        Assert.Equal(0, adapter.PageCalls);
        Assert.Equal(0, adapter.SeekCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Projected_export_captures_bindings_before_yield_and_disposes_early_or_on_cancellation()
    {
        var source = new Source();
        var adapter = new RecordingAdapter { SupportsProjectedSeek = true };
        var repo = Repository(source, adapter, projectionMode: ReadProjectionMode.Required);
        string[] selection = ["Id"];
        await using (var export = repo.ExportAsync(new(Selection: selection)).GetAsyncEnumerator())
        {
            Assert.True(await export.MoveNextAsync());
            selection[0] = "Salary";
            source.Current = CompilerTests.Capture(CompilerTests.Definition(), 2);
            adapter.Rows[0].Id = 99;
            Assert.True(await export.MoveNextAsync());
            Assert.Equal(1, export.Current.PolicyVersion);
            Assert.Equal(2, Assert.Single(export.Current.Records).GetProperty("Id").GetInt32());
        }
        Assert.Equal(1, source.Captures);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        source.Current = CompilerTests.Capture(
            CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
        );
        using var cancellation = new CancellationTokenSource();
        await using (
            var export = repo.ExportAsync(new(Selection: ["Id"]), cancellation.Token)
                .GetAsyncEnumerator()
        )
        {
            Assert.True(await export.MoveNextAsync());
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await export.MoveNextAsync()
            );
        }
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(0, adapter.SeekCalls);
        Assert.Equal(0, adapter.PageCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Offset_metadata_executes_only_requested_work(bool total, bool hasMore)
    {
        var source = new Source();
        var adapter = new RecordingAdapter { FailCount = !total };
        var repo = Repository(source, adapter);
        var first = await repo.ListAsync(
            new(Selection: ["Id"], Limit: 1, IncludeTotal: total, IncludeHasMore: hasMore)
        );
        Assert.Equal(total ? 2L : (long?)null, first.Total);
        Assert.Equal(hasMore ? true : (bool?)null, first.HasMore);
        Assert.Equal(1, Assert.Single(first.Records).GetProperty("Id").GetInt32());
        var final = await repo.ListAsync(
            new(Selection: [], Offset: 1, Limit: 1, IncludeTotal: total, IncludeHasMore: hasMore)
        );
        Assert.Equal(hasMore ? false : (bool?)null, final.HasMore);
        Assert.Equal("{}", Assert.Single(final.Records).GetRawText());
        var beyond = await repo.ListAsync(
            new(Offset: 100, Limit: 1, IncludeTotal: total, IncludeHasMore: hasMore)
        );
        Assert.Empty(beyond.Records);
        Assert.Equal(hasMore ? false : (bool?)null, beyond.HasMore);
        var full = await repo.ListAsync(
            new(Limit: 2, IncludeTotal: total, IncludeHasMore: hasMore)
        );
        Assert.Equal(2, full.Records.Count);
        Assert.Equal(hasMore ? false : (bool?)null, full.HasMore);
        var empty = await repo.ListAsync(
            new(
                Filter: CompilerTests.Leaf("Status", "eq", "Inactive"),
                Limit: 1,
                IncludeTotal: total,
                IncludeHasMore: hasMore
            )
        );
        Assert.Equal(total ? 0L : (long?)null, empty.Total);
        Assert.Equal(hasMore ? false : (bool?)null, empty.HasMore);
        Assert.Empty(empty.Records);
        Assert.Equal(total ? 5 : 0, adapter.CountCalls);
        Assert.Equal(
            new[] { 1, 1, 1, 2, 1 }.Select(n => n + (hasMore ? 1 : 0)),
            adapter.PageLimits
        );
        Assert.Equal(new[] { 0, 1, 100, 0, 0 }, adapter.PageOffsets);
        Assert.Equal(5, source.Captures);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(0, adapter.SeekCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Cursor_metadata_reuses_lookahead_and_flags_can_change_on_resume(
        bool total,
        bool hasMore
    )
    {
        var source = new Source();
        var adapter = new RecordingAdapter { FailCount = !total };
        var repo = Repository(source, adapter, Cursor());
        var request = new CursorListReadRequest(
            Selection: ["Id"],
            Limit: 1,
            IncludeTotal: total,
            IncludeHasMore: hasMore
        );
        var first = await repo.ListCursorAsync(request);
        Assert.Equal(total ? 2L : (long?)null, first.Total);
        Assert.Equal(hasMore ? true : (bool?)null, first.HasMore);
        Assert.Equal(1, Assert.Single(first.Records).GetProperty("Id").GetInt32());
        Assert.NotNull(first.NextContinuationToken);
        Assert.Equal(total ? 1 : 0, adapter.CountCalls);
        adapter.FailCount = total;
        var final = await repo.ListCursorAsync(
            request with
            {
                ContinuationToken = first.NextContinuationToken,
                IncludeTotal = !total,
                IncludeHasMore = !hasMore,
            }
        );
        Assert.Equal(!total ? 2L : (long?)null, final.Total);
        Assert.Equal(!hasMore ? false : (bool?)null, final.HasMore);
        Assert.Equal(2, Assert.Single(final.Records).GetProperty("Id").GetInt32());
        Assert.Null(final.NextContinuationToken);
        Assert.Equal(1, adapter.CountCalls);
        Assert.Equal(new[] { 2, 2 }, adapter.SeekLimits);
        Assert.Equal(0, adapter.PageCalls);
        Assert.Equal(2, source.Captures);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Has_more_uses_page_observation_and_count_failures_never_become_success()
    {
        var adapter = new RecordingAdapter { CountOverride = 0 };
        var repo = Repository(new Source(), adapter, Cursor());
        Assert.True((await repo.ListAsync(new(Limit: 1, IncludeHasMore: true))).HasMore);
        Assert.True((await repo.ListCursorAsync(new(Limit: 1, IncludeHasMore: true))).HasMore);
        adapter.CountOverride = 999;
        Assert.False((await repo.ListAsync(new(Limit: 2, IncludeHasMore: true))).HasMore);
        Assert.False((await repo.ListCursorAsync(new(Limit: 2, IncludeHasMore: true))).HasMore);
        adapter.FailCount = true;
        var pages = adapter.PageCalls;
        var seeks = adapter.SeekCalls;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ListAsync(new(Limit: 1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.ListCursorAsync(new(Limit: 1))
        );
        Assert.Equal(pages, adapter.PageCalls);
        Assert.Equal(seeks, adapter.SeekCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task No_count_cursor_handles_empty_hidden_final_rows_and_seek_failures()
    {
        var adapter = new RecordingAdapter { FailCount = true };
        var repo = Repository(new Source(), adapter, Cursor());
        var request = new CursorListReadRequest(
            Selection: [],
            Limit: 1,
            IncludeTotal: false,
            IncludeHasMore: true
        );
        var first = await repo.ListCursorAsync(request);
        Assert.Null(first.Total);
        Assert.True(first.HasMore);
        Assert.Equal("{}", Assert.Single(first.Records).GetRawText());
        var final = await repo.ListCursorAsync(
            request with
            {
                ContinuationToken = first.NextContinuationToken,
            }
        );
        Assert.False(final.HasMore);
        Assert.Null(final.NextContinuationToken);
        Assert.Equal("{}", Assert.Single(final.Records).GetRawText());
        var empty = await repo.ListCursorAsync(
            request with
            {
                Filter = CompilerTests.Leaf("Status", "eq", "Inactive"),
            }
        );
        Assert.False(empty.HasMore);
        Assert.Null(empty.Total);
        Assert.Empty(empty.Records);
        adapter.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ListCursorAsync(request));
        Assert.Equal(0, adapter.CountCalls);
        Assert.Equal(0, adapter.PageCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Unrepresentable_lookahead_is_rejected_before_sessions()
    {
        var adapter = new RecordingAdapter();
        var repo = new AuthorizedReadRepository<Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            new Source(),
            adapter,
            () => CompilerTests.Context(),
            "scope",
            new(int.MaxValue, 100, 1),
            Cursor(),
            ReadProjectionMode.AllowEntityMaterialization
        );
        await Assert.ThrowsAsync<InvalidRequestException>(() =>
            repo.ListAsync(new(Limit: int.MaxValue, IncludeHasMore: true))
        );
        await Assert.ThrowsAsync<InvalidRequestException>(() =>
            repo.ListCursorAsync(new(Limit: int.MaxValue, IncludeTotal: false))
        );
        Assert.Equal(0, adapter.Opened);
        Assert.Equal(
            2,
            (await repo.ListAsync(new(Limit: int.MaxValue, IncludeTotal: false))).Records.Count
        );
        Assert.Equal(int.MaxValue, Assert.Single(adapter.PageLimits));
    }

    [Fact]
    public async Task Count_retains_capture_during_publication_and_disposes_cancellation()
    {
        var source = new Source();
        var adapter = new RecordingAdapter
        {
            OnCount = () => source.Current = CompilerTests.Capture(CompilerTests.Definition(), 2),
        };
        var repo = Repository(source, adapter);
        Assert.Equal(new ReadCount(1, 2), await repo.CountAsync(new()));
        Assert.Equal(1, source.Captures);
        Assert.Equal(1, adapter.CountCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        source.Current = CompilerTests.Capture(
            CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
        );
        using var cancellation = new CancellationTokenSource();
        adapter.OnCount = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repo.CountAsync(new(), cancellation.Token)
        );
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Cursor_lookahead_uses_last_returned_boundary_and_base_total()
    {
        var source = new Source();
        var adapter = new RecordingAdapter();
        var clock = new CursorTokenTests.Clock();
        var repo = Repository(source, adapter, Cursor(clock: clock));
        var request = new CursorListReadRequest(Selection: [], Sort: [new("Name")], Limit: 1);
        var first = await repo.ListCursorAsync(request);
        Assert.Equal(2, first.Total);
        Assert.Equal("{}", Assert.Single(first.Records).GetRawText());
        Assert.NotNull(first.NextContinuationToken);
        var final = await repo.ListCursorAsync(
            request with
            {
                ContinuationToken = first.NextContinuationToken,
            }
        );
        Assert.Equal(2, final.Total);
        Assert.Single(final.Records);
        Assert.Null(final.NextContinuationToken);
        Assert.Equal(2, source.Captures);
        Assert.Equal(2, adapter.CountCalls);
        Assert.Equal(new[] { 2, 2 }, adapter.SeekLimits);
        Assert.Equal(0, adapter.PageCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        clock.Now += TimeSpan.FromMinutes(15);
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
            repo.ListCursorAsync(request with { ContinuationToken = first.NextContinuationToken })
        );
        Assert.Equal(2, adapter.Opened);
    }

    [Fact]
    public async Task Cursor_rejects_changed_query_subject_context_policy_and_dataset_before_sessions()
    {
        var source = new Source();
        var adapter = new RecordingAdapter();
        var subject = "subject";
        var trusted = CompilerTests.Context();
        var config = Cursor(() => subject);
        var repo = Repository(source, adapter, config, () => trusted);
        var request = new CursorListReadRequest(Limit: 1);
        var first = await repo.ListCursorAsync(request);
        var resume = request with { ContinuationToken = first.NextContinuationToken };
        foreach (
            var changed in new[]
            {
                resume with
                {
                    Limit = 2,
                },
                resume with
                {
                    Selection = ["Name"],
                },
                resume with
                {
                    Sort = [new("Name", true)],
                },
                resume with
                {
                    Filter = CompilerTests.Leaf("Status", "eq", "Active"),
                },
                resume with
                {
                    ContinuationToken = "invalid",
                },
                resume with
                {
                    ContinuationToken = "",
                },
            }
        )
            await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
                repo.ListCursorAsync(changed)
            );
        subject = "another";
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
            repo.ListCursorAsync(resume)
        );
        subject = "subject";
        trusted = CompilerTests.Context(department: 20);
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
            repo.ListCursorAsync(resume)
        );
        trusted = new(["manager"], ["group"], CompilerTests.Context().Values);
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
            repo.ListCursorAsync(resume)
        );
        trusted = CompilerTests.Context();
        source.Current = source.Current with { Version = 2 };
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
            repo.ListCursorAsync(resume)
        );
        source.Current = source.Current with { Version = 1 };
        Assert.Null((await repo.ListCursorAsync(resume)).NextContinuationToken);
        var other = Repository(
            source,
            adapter,
            new(config.Protector, () => "subject", "other-dataset")
        );
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
            other.ListCursorAsync(resume)
        );
        Assert.Equal(2, adapter.Opened);
        Assert.Equal(2, adapter.CountCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Missing_configuration_capability_and_bad_limits_fail_without_queries()
    {
        var source = new Source();
        var adapter = new RecordingAdapter { SupportsSeek = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Repository(source, adapter).ListCursorAsync(new(Limit: 1))
        );
        Assert.Equal(0, adapter.Opened);
        var repo = Repository(source, adapter, Cursor());
        foreach (var limit in new[] { 0, -1, 3 })
            await Assert.ThrowsAsync<InvalidRequestException>(() =>
                repo.ListCursorAsync(new(Limit: limit))
            );
        Assert.Equal(0, adapter.Opened);
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
            repo.ListCursorAsync(new(Limit: 1))
        );
        await using (var export = repo.ExportAsync(new()).GetAsyncEnumerator())
            await Assert.ThrowsAsync<ProviderUnsupportedException>(async () =>
                await export.MoveNextAsync()
            );
        Assert.Equal(0, adapter.CountCalls);
        Assert.Equal(0, adapter.PageCalls);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(new ReadCount(1, 2), await repo.CountAsync(new()));
        Assert.Single((await repo.ListAsync(new(Limit: 1))).Records);
        Assert.NotNull(
            (await repo.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 1 })))).Record
        );
    }

    [Fact]
    public async Task Cursor_snapshots_inputs_before_policy_capture()
    {
        string[] selection = ["Id"];
        SortField[] sort = [new("Name")];
        var filter = new Condition { All = [CompilerTests.Leaf("Status", "eq", "Active")] };
        var source = new Source
        {
            OnCapture = () =>
            {
                selection[0] = "Salary";
                sort[0] = new("Id");
                filter.All![0] = CompilerTests.Leaf("Status", "eq", "Inactive");
            },
        };
        var adapter = new RecordingAdapter();
        var repo = Repository(source, adapter, Cursor());
        var page = await repo.ListCursorAsync(new(selection, filter, sort, 1));
        Assert.Equal(2, page.Total);
        Assert.Equal(1, Assert.Single(page.Records).GetProperty("Id").GetInt32());
        source.OnCapture = null;
        var next = await repo.ListCursorAsync(
            new(
                ["Id"],
                new() { All = [CompilerTests.Leaf("Status", "eq", "Active")] },
                [new("Name")],
                1,
                page.NextContinuationToken
            )
        );
        Assert.Equal(2, Assert.Single(next.Records).GetProperty("Id").GetInt32());
        Assert.Null(next.NextContinuationToken);
    }

    [Fact]
    public async Task Cursor_mutations_do_not_shift_remaining_fixed_sort_tuples()
    {
        var source = new Source();
        var adapter = new RecordingAdapter
        {
            Rows = Enumerable
                .Range(1, 5)
                .Select(id => new Employee
                {
                    Id = id,
                    TenantId = 1,
                    Name = "A",
                })
                .ToArray(),
        };
        var repo = Repository(source, adapter, Cursor());
        var request = new CursorListReadRequest(Limit: 2);
        var first = await repo.ListCursorAsync(request);
        Assert.Equal(new[] { 1, 2 }, first.Records.Select(row => row.GetProperty("Id").GetInt32()));
        adapter.Rows = adapter
            .Rows.Where(row => row.Id > 2)
            .Append(
                new()
                {
                    Id = 0,
                    TenantId = 1,
                    Name = "A",
                }
            )
            .Append(
                new()
                {
                    Id = 6,
                    TenantId = 1,
                    Name = "A",
                }
            )
            .ToArray();
        var next = await repo.ListCursorAsync(
            request with
            {
                ContinuationToken = first.NextContinuationToken,
            }
        );
        Assert.Equal(new[] { 3, 4 }, next.Records.Select(row => row.GetProperty("Id").GetInt32()));
        var final = await repo.ListCursorAsync(
            request with
            {
                ContinuationToken = next.NextContinuationToken,
            }
        );
        Assert.Equal(new[] { 5, 6 }, final.Records.Select(row => row.GetProperty("Id").GetInt32()));
        Assert.Null(final.NextContinuationToken);
        Assert.Equal(0, adapter.PageCalls);
    }

    [Fact]
    public async Task Empty_short_and_full_cursor_pages_preserve_metadata_and_disposal()
    {
        var adapter = new RecordingAdapter();
        var repo = Repository(new Source(), adapter, Cursor());
        var full = await repo.ListCursorAsync(new(Limit: 2));
        Assert.Equal(2, full.Total);
        Assert.Equal(2, full.Records.Count);
        Assert.Null(full.NextContinuationToken);
        var empty = await repo.ListCursorAsync(
            new(Limit: 1, Filter: CompilerTests.Leaf("Status", "eq", "Inactive"))
        );
        Assert.Equal(0, empty.Total);
        Assert.Empty(empty.Records);
        Assert.Null(empty.NextContinuationToken);
        adapter.Rows = [adapter.Rows[0]];
        var shortPage = await repo.ListCursorAsync(new(Limit: 2));
        Assert.Equal(1, shortPage.Total);
        Assert.Single(shortPage.Records);
        Assert.Null(shortPage.NextContinuationToken);
        adapter.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.ListCursorAsync(new(Limit: 1))
        );
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(0, adapter.PageCalls);
    }

    [Fact]
    public async Task Export_snapshots_request_and_boundary_before_yield_and_ignores_position_shifts()
    {
        var source = new Source();
        var adapter = new RecordingAdapter
        {
            Rows = Enumerable
                .Range(1, 5)
                .Select(id => new Employee
                {
                    Id = id,
                    TenantId = 1,
                    Name = "A",
                })
                .ToArray(),
        };
        var repo = Repository(source, adapter);
        string[] selection = ["Id"];
        SortField[] sort = [new("Name")];
        await using var export = repo.ExportAsync(new(selection, Sort: sort)).GetAsyncEnumerator();
        Assert.True(await export.MoveNextAsync());
        Assert.Equal(1, export.Current.Records[0].GetProperty("Id").GetInt32());
        selection[0] = "Salary";
        sort[0] = new("Name", true);
        adapter.Rows[0].Id = 99;
        adapter.Rows = adapter
            .Rows.Skip(1)
            .Append(
                new()
                {
                    Id = 0,
                    TenantId = 1,
                    Name = "A",
                }
            )
            .Append(
                new()
                {
                    Id = 6,
                    TenantId = 1,
                    Name = "A",
                }
            )
            .Append(
                new()
                {
                    Id = 7,
                    TenantId = 2,
                    Name = "Hidden",
                }
            )
            .ToArray();
        var remaining = new List<int>();
        while (await export.MoveNextAsync())
            remaining.Add(Assert.Single(export.Current.Records).GetProperty("Id").GetInt32());
        Assert.Equal(new[] { 2, 3, 4, 5, 6 }, remaining);
        Assert.Equal(1, source.Captures);
        Assert.Equal(0, adapter.PageCalls);
        Assert.All(adapter.SeekLimits, value => Assert.Equal(1, value));
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Invalid_operation_inputs_never_open_entity_sessions()
    {
        var adapter = new RecordingAdapter();
        var repo = Repository(new Source(), adapter);
        foreach (
            var request in new[]
            {
                new ListReadRequest(Selection: ["Salary"], Limit: 1),
                new ListReadRequest(Filter: CompilerTests.Leaf("Salary", "eq", 100), Limit: 1),
                new ListReadRequest(Sort: [new("Id")], Limit: 1),
            }
        )
            await Assert.ThrowsAsync<AuthorizationException>(() => repo.ListAsync(request));
        foreach (
            var request in new[]
            {
                new ListReadRequest(Selection: ["Manager.Id"], Limit: 1),
                new ListReadRequest(Filter: CompilerTests.Reference("Status", "tenant"), Limit: 1),
                new ListReadRequest(Limit: 0),
                new ListReadRequest(Limit: 3),
                new ListReadRequest(Offset: -1, Limit: 1),
                new ListReadRequest(Offset: 101, Limit: 1),
            }
        )
            await Assert.ThrowsAsync<InvalidRequestException>(() => repo.ListAsync(request));
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            repo.CountAsync(new(CompilerTests.Leaf("Salary", "eq", 100)))
        );
        foreach (
            var key in new[]
            {
                default(JsonElement),
                JsonSerializer.SerializeToElement<object?>(null),
                JsonSerializer.SerializeToElement("1"),
                JsonSerializer.SerializeToElement(1.5),
                JsonSerializer.SerializeToElement(new { id = 1 }),
            }
        )
            await Assert.ThrowsAsync<InvalidRequestException>(() => repo.DetailAsync(new(key)));
        var validKey = JsonSerializer.SerializeToElement(new { Id = 1 });
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            repo.DetailAsync(new(validKey, Selection: ["Salary"]))
        );
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            repo.DetailAsync(new(validKey, Filter: CompilerTests.Leaf("Id", "eq", 1)))
        );
        foreach (
            var request in new[]
            {
                new ExportReadRequest(Selection: ["Salary"]),
                new ExportReadRequest(Filter: CompilerTests.Leaf("Salary", "eq", 100)),
                new ExportReadRequest(Sort: [new("Id")]),
            }
        )
        {
            await using var export = repo.ExportAsync(request).GetAsyncEnumerator();
            await Assert.ThrowsAsync<AuthorizationException>(async () =>
                await export.MoveNextAsync()
            );
        }
        Assert.Equal(0, adapter.Opened);
    }

    [Fact]
    public async Task Request_diagnostics_locate_filter_selection_sort_and_key_before_entity_access()
    {
        var adapter = new RecordingAdapter();
        var repo = Repository(new Source(), adapter);
        var filter = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            repo.CountAsync(
                new(
                    new Condition
                    {
                        All =
                        [
                            CompilerTests.Leaf("Status", "eq", "Active"),
                            CompilerTests.Leaf("Status", "bad operator sentinel", "value sentinel"),
                        ],
                    }
                )
            )
        );
        Assert.Equal(DataAccessErrorCode.InvalidOperator, Assert.Single(filter.Diagnostics).Code);
        Assert.Equal("/filter/all/1/operator", filter.Diagnostics[0].Path);
        var selection = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            repo.ListAsync(new(Selection: ["Name", "unknown sentinel"], Limit: 1))
        );
        Assert.Equal("/selection/1", Assert.Single(selection.Diagnostics).Path);
        var sort = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            repo.ListAsync(new(Sort: [new("Name"), new("unknown sentinel")], Limit: 1))
        );
        Assert.Equal("/sort/1/field", Assert.Single(sort.Diagnostics).Path);
        var key = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            repo.DetailAsync(new(JsonSerializer.SerializeToElement("key sentinel")))
        );
        Assert.Equal("/key", Assert.Single(key.Diagnostics).Path);
        foreach (var error in new[] { filter, selection, sort, key })
            Assert.DoesNotContain("sentinel", JsonSerializer.Serialize(error.Diagnostics));
        Assert.Equal(0, adapter.Opened);
    }

    [Fact]
    public async Task Non_list_operations_ignore_default_page_limit_and_count_needs_no_output_grants()
    {
        var policy = CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow));
        var source = new Source
        {
            Current = CompilerTests.Capture(
                policy with
                {
                    FieldEntries =
                    [
                        policy.FieldEntries[0] with
                        {
                            Fields = new() { [FieldOperation.Filter] = ["Status"] },
                        },
                    ],
                }
            ),
        };
        var adapter = new RecordingAdapter();
        var repo = Repository(source, adapter);
        Assert.Equal(new ReadCount(1, 2), await repo.CountAsync(new()));
        Assert.Equal(1, source.Captures);
        Assert.Equal(
            2,
            (await repo.CountAsync(new(CompilerTests.Leaf("Status", "eq", "Active")))).Total
        );
        var detail = await repo.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 1 })));
        Assert.Equal("{}", detail.Record!.Value.GetRawText());
        var excluded = await repo.DetailAsync(
            new(
                JsonSerializer.SerializeToElement(new { Id = 1 }),
                Filter: CompilerTests.Leaf("Status", "eq", "Inactive")
            )
        );
        Assert.Null(excluded.Record);
        var page = await repo.ListAsync(new(Limit: 1));
        Assert.Equal(2, page.Total);
        Assert.Equal("{}", Assert.Single(page.Records).GetRawText());
        var captures = source.Captures;
        var exported = new List<JsonElement>();
        await foreach (var batch in repo.ExportAsync(new()))
            exported.AddRange(batch.Records);
        Assert.Equal(captures + 1, source.Captures);
        Assert.Equal(2, exported.Count);
        Assert.All(exported, row => Assert.Equal("{}", row.GetRawText()));
        Assert.Equal(adapter.Opened, adapter.Disposed);
        await Assert.ThrowsAsync<InvalidRequestException>(() => repo.ListAsync(new()));
    }

    [Fact]
    public async Task Detail_selection_filter_and_host_key_lookup_preserve_protected_results()
    {
        var repo = Repository(new Source(), new RecordingAdapter());
        var key = JsonSerializer.SerializeToElement(new { Id = 1 });
        var detail = await repo.DetailAsync(new(key, Selection: ["Id", "Name"]));
        Assert.Equal(
            new[] { "Id", "Name" },
            detail.Record!.Value.EnumerateObject().Select(p => p.Name)
        );
        Assert.Equal(
            "{}",
            (await repo.DetailAsync(new(key, Selection: []))).Record!.Value.GetRawText()
        );
        Assert.NotNull(
            (
                await repo.DetailAsync(
                    new(key, Filter: CompilerTests.Leaf("Status", "eq", "Active"))
                )
            ).Record
        );
        Assert.Null(
            (
                await repo.DetailAsync(
                    new(key, Filter: CompilerTests.Leaf("Status", "eq", "Inactive"))
                )
            ).Record
        );
        Assert.Equal(
            2,
            (await repo.CountAsync(new(CompilerTests.Leaf("Status", "eq", "Active")))).Total
        );
    }

    [Fact]
    public async Task Count_detail_and_export_failures_dispose_without_retry()
    {
        var adapter = new RecordingAdapter { Fail = true };
        var repo = Repository(new Source(), adapter);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CountAsync(new()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 1 })))
        );
        await using (var export = repo.ExportAsync(new()).GetAsyncEnumerator())
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await export.MoveNextAsync()
            );
        Assert.Equal(3, adapter.Opened);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Warm_shapes_do_not_cache_grants_or_change_captured_export_fields()
    {
        var source = new Source();
        var adapter = new RecordingAdapter();
        var repo = Repository(source, adapter);
        var broad = CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow));
        var narrow = broad with
        {
            FieldEntries =
            [
                broad.FieldEntries[0] with
                {
                    Fields = new() { { FieldOperation.Select, ["Name"] } },
                },
            ],
        };
        var warm = await repo.ListAsync(new(Limit: 1));
        Assert.Equal(new[] { "Id", "Name" }, warm.Records[0].EnumerateObject().Select(p => p.Name));
        await using var export = repo.ExportAsync(new()).GetAsyncEnumerator();
        Assert.True(await export.MoveNextAsync());
        source.Current = CompilerTests.Capture(narrow, 2);
        var opened = adapter.Opened;
        foreach (var selection in new[] { new[] { "Id" }, new[] { "Salary" } })
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                repo.ListAsync(new(Selection: selection, Limit: 1))
            );
        Assert.Equal(opened, adapter.Opened);
        var page = await repo.ListAsync(new(Limit: 1));
        Assert.Equal(2, page.PolicyVersion);
        Assert.Equal("{\"Name\":\"A\"}", page.Records[0].GetRawText());
        var detail = await repo.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 1 })));
        Assert.Equal(2, detail.PolicyVersion);
        Assert.Equal("{\"Name\":\"A\"}", detail.Record!.Value.GetRawText());
        Assert.True(await export.MoveNextAsync());
        Assert.Equal(1, export.Current.PolicyVersion);
        Assert.Equal(
            new[] { "Id", "Name" },
            export.Current.Records[0].EnumerateObject().Select(p => p.Name)
        );
        Assert.False(await export.MoveNextAsync());
        await foreach (var batch in repo.ExportAsync(new()))
        {
            Assert.Equal(2, batch.PolicyVersion);
            Assert.All(batch.Records, row => Assert.Equal("{\"Name\":\"A\"}", row.GetRawText()));
        }
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Core_repository_preserves_validation_results_and_captured_export()
    {
        var source = new Source();
        var adapter = new RecordingAdapter();
        var repo = Repository(source, adapter);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            repo.ListAsync(new(Selection: ["Salary"], Limit: 1))
        );
        Assert.Equal(0, adapter.Opened);
        var page = await repo.ListAsync(new(Sort: [new("Name")], Limit: 1));
        Assert.Equal(2, page.Total);
        Assert.Equal(1, page.Records[0].GetProperty("Id").GetInt32());
        Assert.Equal(2, page.Records[0].EnumerateObject().Count());
        Assert.Equal(new ReadCount(1, 2), await repo.CountAsync(new()));
        Assert.Null(
            (await repo.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 3 })))).Record
        );
        Assert.Null(
            (await repo.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 99 })))).Record
        );
        Assert.Equal(
            "{}",
            (await repo.ListAsync(new(Selection: [], Limit: 1))).Records[0].GetRawText()
        );
        await using (var export = repo.ExportAsync(new()).GetAsyncEnumerator())
        {
            Assert.True(await export.MoveNextAsync());
            source.Current = CompilerTests.Capture(CompilerTests.Definition(), 2);
            Assert.True(await export.MoveNextAsync());
            Assert.Equal(1, export.Current.PolicyVersion);
        }
        Assert.Equal(adapter.Opened, adapter.Disposed);
        await Assert.ThrowsAsync<AuthorizationException>(() => repo.CountAsync(new()));
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
        await using var export = repo.ExportAsync(new(), cancellation.Token).GetAsyncEnumerator();
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
            Kkts.DataAccess.SqlServer.SqlServerEntityReads.Create<Employee>(
                CompilerTests.Schema,
                () =>
                    new DbContext(
                        new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options
                    )
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            Kkts.DataAccess.MySql.MySqlEntityReads.Create<Employee>(
                CompilerTests.Schema,
                () =>
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
