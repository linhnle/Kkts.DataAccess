using Kkts.Expressions;
using Xunit;
using Employee = Kkts.DataAccess.Tests.CompilerTests.Employee;

namespace Kkts.DataAccess.Tests;

public class ExportLifecycleTests
{
    [Theory]
    [InlineData(ExportPolicyConsistency.CapturedPolicy)]
    [InlineData(ExportPolicyConsistency.AbortOnPolicyChange)]
    public async Task Async_export_capture_is_lazy_awaited_and_retained_independently_of_activation(
        ExportPolicyConsistency consistency
    )
    {
        var source = new Source();
        var adapter = new Adapter();
        var identity = CompilerTests.Context();
        var pending = new TaskCompletionSource<TrustedContext>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var context = new RepositoryTests.AsyncContext(token => new(pending.Task.WaitAsync(token)));
        var repo = new AuthorizedReadRepository<Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            source,
            adapter,
            context,
            "scope",
            new(2, 100, 1),
            export: new(policyConsistency: consistency)
        );
        await using var export = repo.ExportAsync(new()).GetAsyncEnumerator();
        Assert.Equal(0, context.Captures);
        var move = export.MoveNextAsync().AsTask();
        Assert.False(move.IsCompleted);
        Assert.Equal(0, adapter.Opened);
        pending.SetResult(identity);
        Assert.True(await move);
        identity = CompilerTests.Context(tenant: 2);
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.SetResult(identity);
        Assert.True(await export.MoveNextAsync());
        Assert.Equal(2, Assert.Single(export.Current.Records).GetProperty("Id").GetInt32());
        Assert.Equal(1, context.Captures);
        if (consistency == ExportPolicyConsistency.AbortOnPolicyChange)
        {
            source.Revision++;
            await Assert.ThrowsAsync<ExportPolicyChangedException>(() =>
                export.MoveNextAsync().AsTask()
            );
            Assert.Equal(1, source.StampedCaptures);
        }
        else
        {
            Assert.True(await export.MoveNextAsync());
            Assert.Equal(1, source.Captures);
        }
    }

    sealed class Source : IActivationAwarePolicySource
    {
        public CapturedPolicy Policy = CompilerTests.Capture(
            CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
        );
        public long Revision = 1;
        public int Captures,
            StampedCaptures,
            Checks;
        public Func<CancellationToken, Task>? OnCapture,
            OnCheck;
        public bool NullCapture,
            NullStamp;

        public async Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            Captures++;
            if (OnCapture != null)
                await OnCapture(cancellation);
            cancellation.ThrowIfCancellationRequested();
            return Policy;
        }

        public async Task<CapturedPolicyActivation> CaptureActivationAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            StampedCaptures++;
            if (OnCapture != null)
                await OnCapture(cancellation);
            cancellation.ThrowIfCancellationRequested();
            return NullCapture ? null! : new(Policy, new(Policy.Version, Revision));
        }

        public async Task<PolicyActivationStamp> ReadActivationAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            Checks++;
            if (OnCheck != null)
                await OnCheck(cancellation);
            cancellation.ThrowIfCancellationRequested();
            return NullStamp ? null! : new(Policy.Version, Revision);
        }
    }

    sealed class CapturedOnly(Source source) : IActivePolicySource
    {
        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        ) => source.CaptureAsync(scope, resource, cancellation);
    }

    sealed class Adapter : IEntityReadAdapter<Employee>
    {
        public Employee[] Rows = Enumerable
            .Range(1, 10)
            .Select(id => new Employee
            {
                Id = id,
                TenantId = 1,
                Name = $"row{id}",
            })
            .ToArray();
        public bool SnapshotCapable;
        public int Opened,
            Disposed,
            Begun,
            Active;
        public List<int> Limits = [];
        public Action<CancellationToken>? OnOpen,
            OnQuery,
            OnBegin,
            OnDispose;
        public Func<IReadOnlyList<ProjectedReadRow>, IReadOnlyList<ProjectedReadRow>>? MapRows;
        public bool IgnoreQueryCancellation;

        public ValueTask<IEntityReadSession<Employee>> OpenAsync(
            CancellationToken cancellation = default
        )
        {
            OnOpen?.Invoke(cancellation);
            Opened++;
            Active++;
            return ValueTask.FromResult<IEntityReadSession<Employee>>(
                SnapshotCapable ? new SnapshotSession(this) : new Session(this)
            );
        }

        class Session(Adapter owner) : IEntityProjectedSeekReadSession<Employee>
        {
            protected Employee[]? Snapshot;

            public Task<IReadOnlyList<ProjectedReadRow>> SeekProjectedAsync(
                EntitySeekPlan<Employee> seek,
                int limit,
                CancellationToken token
            )
            {
                owner.Limits.Add(limit);
                var rows = (Snapshot ?? owner.Rows)
                    .Where(seek.Read.Predicate.Compile())
                    .OrderBy(row => row.Id)
                    .Where(row => seek.Boundary == null || row.Id > (int)seek.Boundary[^1]!)
                    .Take(limit)
                    .Select(row => new ProjectedReadRow(
                        seek.Read.OutputBindings.Select(binding => binding.Property.GetValue(row)),
                        seek.ContinuationBindings.Select(property => property.GetValue(row))
                    ))
                    .ToArray();
                owner.OnQuery?.Invoke(token);
                if (!owner.IgnoreQueryCancellation)
                    token.ThrowIfCancellationRequested();
                return Task.FromResult(owner.MapRows?.Invoke(rows) ?? rows);
            }

            public Task<long> CountAsync(EntityReadPlan<Employee> plan, CancellationToken token) =>
                throw new InvalidOperationException("Export must not count.");

            public Task<IReadOnlyList<Employee>> PageAsync(
                EntityReadPlan<Employee> plan,
                int offset,
                int limit,
                CancellationToken token
            ) => throw new InvalidOperationException("No offsets.");

            public Task<Employee?> SingleAsync(
                EntityReadPlan<Employee> plan,
                CancellationToken token
            ) => throw new InvalidOperationException("No entity reads.");

            public Task<IReadOnlyList<ProjectedReadRow>> PageProjectedAsync(
                EntityReadPlan<Employee> plan,
                int offset,
                int limit,
                CancellationToken token
            ) => throw new InvalidOperationException("No pages.");

            public Task<ProjectedReadRow?> SingleProjectedAsync(
                EntityReadPlan<Employee> plan,
                CancellationToken token
            ) => throw new InvalidOperationException("No detail.");

            public ValueTask DisposeAsync()
            {
                owner.Disposed++;
                owner.Active--;
                owner.OnDispose?.Invoke(CancellationToken.None);
                return ValueTask.CompletedTask;
            }

            protected void Begin(EntityReadPlan<Employee> plan, CancellationToken token)
            {
                if (Snapshot != null)
                    throw new InvalidOperationException("Already initialized.");
                owner.OnBegin?.Invoke(token);
                token.ThrowIfCancellationRequested();
                owner.Begun++;
                Snapshot = owner
                    .Rows.Select(row => new Employee
                    {
                        Id = row.Id,
                        TenantId = row.TenantId,
                        Name = row.Name,
                    })
                    .ToArray();
            }
        }

        sealed class SnapshotSession(Adapter owner)
            : Session(owner),
                IEntitySnapshotReadSession<Employee>
        {
            public Task BeginSnapshotAsync(EntityReadPlan<Employee> plan, CancellationToken token)
            {
                Begin(plan, token);
                return Task.CompletedTask;
            }
        }
    }

    internal sealed class Clock : TimeProvider
    {
        long ticks;
        readonly List<Timer> timers = [];
        public int Timers => timers.Count;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(ticks);

        public void Advance(TimeSpan duration)
        {
            ticks += duration.Ticks;
            foreach (var timer in timers.ToArray())
                timer.Fire(ticks);
        }

        public void AdvanceTimestampsOnly(TimeSpan duration) => ticks += duration.Ticks;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new Timer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        sealed class Timer(Clock owner, TimerCallback callback, object? state) : ITimer
        {
            long? due;
            TimeSpan period;
            bool disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (disposed)
                    return false;
                this.period = period;
                due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.ticks + dueTime.Ticks;
                return true;
            }

            public void Fire(long now)
            {
                if (disposed || due == null || now < due)
                    return;
                due = period == Timeout.InfiniteTimeSpan ? null : now + period.Ticks;
                callback(state);
            }

            public void Dispose()
            {
                disposed = true;
                owner.timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    static AuthorizedReadRepository<Employee> Repository(
        IActivePolicySource source,
        Adapter adapter,
        ExportReadConfiguration? export = null,
        int batch = 4,
        Func<TrustedContext>? context = null
    ) =>
        new(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            source,
            adapter,
            context ?? (() => CompilerTests.Context()),
            "scope",
            new(20, 100, batch),
            export: export
        );

    static ExportReadConfiguration Abort(
        ExportDataConsistency data = ExportDataConsistency.BestEffort
    ) => new(ExportPolicyConsistency.AbortOnPolicyChange, data);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Modes_are_independent_and_snapshot_never_requires_entity_seek(
        bool abort,
        bool snapshot
    )
    {
        var source = new Source();
        var adapter = new Adapter { SnapshotCapable = snapshot };
        int contexts = 0;
        var repo = Repository(
            source,
            adapter,
            new(
                abort
                    ? ExportPolicyConsistency.AbortOnPolicyChange
                    : ExportPolicyConsistency.CapturedPolicy,
                snapshot ? ExportDataConsistency.Snapshot : ExportDataConsistency.BestEffort
            ),
            context: () =>
            {
                contexts++;
                return CompilerTests.Context();
            }
        );
        await using var iterator = repo.ExportAsync(new(Selection: ["Id", "Name"]))
            .GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(snapshot ? 1 : 0, adapter.Active);
        adapter.Rows[4].Name = "changed";
        source.Revision += 2; // A publish followed by rollback to the original immutable version.
        if (abort)
        {
            await Assert.ThrowsAsync<ExportPolicyChangedException>(async () =>
                await iterator.MoveNextAsync()
            );
            Assert.Single(adapter.Limits);
        }
        else
        {
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal(
                snapshot ? "row5" : "changed",
                iterator.Current.Records[0].GetProperty("Name").GetString()
            );
            Assert.Equal(1, iterator.Current.PolicyVersion);
            Assert.Equal(0, source.Checks);
        }
        await iterator.DisposeAsync();
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(snapshot ? 1 : 0, adapter.Begun);
        Assert.Equal(abort ? 1 : 0, source.StampedCaptures);
        Assert.Equal(abort ? 0 : 1, source.Captures);
        Assert.Equal(1, contexts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Change_during_query_or_scalar_validation_suppresses_batch(bool duringMapping)
    {
        var source = new Source();
        var adapter = new Adapter();
        if (duringMapping)
            adapter.MapRows = rows =>
            {
                source.Revision++;
                return rows;
            };
        else
            adapter.OnQuery = _ => source.Revision++;
        await using var iterator = Repository(source, adapter, Abort())
            .ExportAsync(new())
            .GetAsyncEnumerator();
        await Assert.ThrowsAsync<ExportPolicyChangedException>(async () =>
            await iterator.MoveNextAsync()
        );
        Assert.Equal(2, source.Checks);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Single(adapter.Limits);
        Assert.Equal(1, source.StampedCaptures);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("stamp")]
    [InlineData("failure")]
    [InlineData("missing")]
    [InlineData("version")]
    public async Task Invalid_or_failed_source_never_releases_unchecked_output(string failure)
    {
        var source = new Source();
        var adapter = new Adapter();
        Exception expected = new InvalidOperationException("check failed");
        switch (failure)
        {
            case "capture":
                source.NullCapture = true;
                break;
            case "stamp":
                source.NullStamp = true;
                break;
            case "missing":
                expected = new PolicyMissingException();
                source.OnCapture = _ => throw expected;
                break;
            case "failure":
                source.OnCheck = _ => throw expected;
                break;
            case "version":
                adapter.OnQuery = _ => source.Policy = source.Policy with { Version = 2 };
                break;
        }
        await using var iterator = Repository(source, adapter, Abort())
            .ExportAsync(new())
            .GetAsyncEnumerator();
        var error = await Record.ExceptionAsync(async () => await iterator.MoveNextAsync());
        Assert.NotNull(error);
        Assert.Equal(expected.GetType(), error.GetType());
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(1, source.StampedCaptures);
        Assert.Equal(0, source.Captures);
    }

    [Fact]
    public async Task Optional_capabilities_fail_explicitly_only_when_requested()
    {
        var source = new Source();
        var adapter = new Adapter();
        await using (
            var iterator = Repository(new CapturedOnly(source), adapter, Abort())
                .ExportAsync(new())
                .GetAsyncEnumerator()
        )
            await Assert.ThrowsAsync<ProviderUnsupportedException>(async () =>
                await iterator.MoveNextAsync()
            );
        Assert.Equal(0, adapter.Opened);
        await using (
            var iterator = Repository(
                    source,
                    adapter,
                    new(dataConsistency: ExportDataConsistency.Snapshot)
                )
                .ExportAsync(new())
                .GetAsyncEnumerator()
        )
            await Assert.ThrowsAsync<ProviderUnsupportedException>(async () =>
                await iterator.MoveNextAsync()
            );
        Assert.Empty(adapter.Limits);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        await using (
            var iterator = Repository(new CapturedOnly(source), adapter)
                .ExportAsync(new())
                .GetAsyncEnumerator()
        )
            Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Theory]
    [InlineData(1, false, new[] { 1 })]
    [InlineData(4, false, new[] { 4 })]
    [InlineData(6, false, new[] { 4, 2 })]
    [InlineData(6, true, new[] { 4, 2 })]
    [InlineData(10, false, new[] { 4, 4, 2 })]
    [InlineData(12, false, new[] { 4, 4, 4 })]
    public async Task Row_caps_bound_each_query_and_complete_without_a_probe(
        long cap,
        bool empty,
        int[] limits
    )
    {
        var adapter = new Adapter();
        var source = new Source();
        var repo = Repository(
            source,
            adapter,
            new(ExportPolicyConsistency.AbortOnPolicyChange, maxRows: cap)
        );
        int delivered = 0;
        await foreach (var batch in repo.ExportAsync(new(Selection: empty ? [] : ["Id"])))
        {
            delivered += batch.Records.Count;
            Assert.Equal(0, adapter.Active);
            if (empty)
                Assert.All(batch.Records, row => Assert.Equal("{}", row.GetRawText()));
        }
        Assert.Equal(Math.Min(cap, 10), delivered);
        Assert.Equal(limits, adapter.Limits);
        Assert.Equal(limits.Length * 2, source.Checks);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Over_return_fails_before_delivery_and_disposes()
    {
        var adapter = new Adapter { MapRows = rows => rows.Concat(rows).ToArray() };
        await using var iterator = Repository(new Source(), adapter, new(maxRows: 2))
            .ExportAsync(new())
            .GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await iterator.MoveNextAsync()
        );
        Assert.Equal(new[] { 2 }, adapter.Limits);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Fact]
    public async Task Deadline_starts_on_advancement_and_is_independent_for_each_enumerator()
    {
        var clock = new Clock();
        var adapter = new Adapter();
        var source = new Source();
        var enumerable = Repository(
                source,
                adapter,
                new(maxDuration: TimeSpan.FromSeconds(10), timeProvider: clock)
            )
            .ExportAsync(new());
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, clock.Timers);
        await using var first = enumerable.GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        clock.Advance(TimeSpan.FromSeconds(5));
        await using var second = enumerable.GetAsyncEnumerator();
        Assert.True(await second.MoveNextAsync());
        Assert.Equal(2, clock.Timers);
        clock.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ExportDurationExceededException>(async () =>
            await first.MoveNextAsync()
        );
        Assert.True(await second.MoveNextAsync());
        Assert.Equal(1, clock.Timers);
        await second.DisposeAsync();
        Assert.Equal(0, clock.Timers);
        Assert.Equal(2, source.Captures);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Theory]
    [InlineData("capture", false)]
    [InlineData("check", false)]
    [InlineData("open", false)]
    [InlineData("begin", true)]
    [InlineData("query", false)]
    [InlineData("late", false)]
    [InlineData("mapping", false)]
    public async Task Deadline_cancels_every_phase_and_rejects_noncooperative_late_results(
        string phase,
        bool snapshot
    )
    {
        var clock = new Clock();
        var source = new Source();
        var adapter = new Adapter
        {
            SnapshotCapable = snapshot,
            IgnoreQueryCancellation = phase == "late",
        };
        Action<CancellationToken> expire = token =>
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(token.IsCancellationRequested);
        };
        switch (phase)
        {
            case "capture":
                source.OnCapture = token =>
                {
                    expire(token);
                    return Task.CompletedTask;
                };
                break;
            case "check":
                source.OnCheck = token =>
                {
                    expire(token);
                    return Task.CompletedTask;
                };
                break;
            case "open":
                adapter.OnOpen = expire;
                break;
            case "begin":
                adapter.OnBegin = expire;
                break;
            case "query":
            case "late":
                adapter.OnQuery = expire;
                break;
            case "mapping":
                adapter.MapRows = rows =>
                {
                    clock.Advance(TimeSpan.FromSeconds(1));
                    return rows;
                };
                break;
        }
        var repo = Repository(
            source,
            adapter,
            new(
                ExportPolicyConsistency.AbortOnPolicyChange,
                snapshot ? ExportDataConsistency.Snapshot : ExportDataConsistency.BestEffort,
                maxDuration: TimeSpan.FromSeconds(1),
                timeProvider: clock
            )
        );
        await using var iterator = repo.ExportAsync(new()).GetAsyncEnumerator();
        await Assert.ThrowsAsync<ExportDurationExceededException>(async () =>
            await iterator.MoveNextAsync()
        );
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(0, clock.Timers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pause_expiry_is_an_error_even_after_capped_or_short_batch(bool capped)
    {
        var clock = new Clock();
        var adapter = new Adapter { SnapshotCapable = true };
        if (!capped)
            adapter.Rows = adapter.Rows[..2];
        var repo = Repository(
            new Source(),
            adapter,
            new(
                dataConsistency: ExportDataConsistency.Snapshot,
                maxRows: capped ? 4 : null,
                maxDuration: TimeSpan.FromSeconds(1),
                timeProvider: clock
            )
        );
        await using var iterator = repo.ExportAsync(new()).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(1, adapter.Active);
        clock.Advance(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<ExportDurationExceededException>(async () =>
            await iterator.MoveNextAsync()
        );
        Assert.Single(adapter.Limits);
        Assert.Equal(0, adapter.Active);
        Assert.Equal(0, clock.Timers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_wins_over_deadline(bool expire)
    {
        var clock = new Clock();
        using var caller = new CancellationTokenSource();
        var adapter = new Adapter
        {
            OnQuery = _ =>
            {
                caller.Cancel();
                if (expire)
                    clock.Advance(TimeSpan.FromSeconds(1));
            },
        };
        var repo = Repository(
            new Source(),
            adapter,
            new(maxDuration: TimeSpan.FromSeconds(1), timeProvider: clock)
        );
        await using var iterator = repo.ExportAsync(new(), caller.Token).GetAsyncEnumerator();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await iterator.MoveNextAsync()
        );
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(0, clock.Timers);
    }

    [Fact]
    public async Task Unrelated_failure_is_not_replaced_by_expired_deadline()
    {
        var clock = new Clock();
        var original = new InvalidOperationException("original query error");
        var adapter = new Adapter
        {
            OnQuery = _ =>
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                throw original;
            },
        };
        await using var iterator = Repository(
                new Source(),
                adapter,
                new(maxDuration: TimeSpan.FromSeconds(1), timeProvider: clock)
            )
            .ExportAsync(new())
            .GetAsyncEnumerator();
        Assert.Same(
            original,
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await iterator.MoveNextAsync()
            )
        );
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(0, clock.Timers);
    }

    [Fact]
    public async Task Monotonic_elapsed_check_rejects_expiry_even_when_timer_delivery_is_late()
    {
        var clock = new Clock();
        var adapter = new Adapter
        {
            OnQuery = token =>
            {
                clock.AdvanceTimestampsOnly(TimeSpan.FromSeconds(1));
                Assert.False(token.IsCancellationRequested);
            },
        };
        await using var iterator = Repository(
                new Source(),
                adapter,
                new(maxDuration: TimeSpan.FromSeconds(1), timeProvider: clock)
            )
            .ExportAsync(new())
            .GetAsyncEnumerator();
        await Assert.ThrowsAsync<ExportDurationExceededException>(async () =>
            await iterator.MoveNextAsync()
        );
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(0, clock.Timers);
    }

    [Fact]
    public async Task Early_disposal_releases_snapshot_and_timer_without_canceled_cleanup_tokens()
    {
        var clock = new Clock();
        var adapter = new Adapter
        {
            SnapshotCapable = true,
            OnDispose = token => Assert.False(token.IsCancellationRequested),
        };
        using var caller = new CancellationTokenSource();
        var repo = Repository(
            new Source(),
            adapter,
            new(
                dataConsistency: ExportDataConsistency.Snapshot,
                maxDuration: TimeSpan.FromSeconds(1),
                timeProvider: clock
            )
        );
        await using var iterator = repo.ExportAsync(new(), caller.Token).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        caller.Cancel();
        clock.Advance(TimeSpan.FromSeconds(1));
        await iterator.DisposeAsync();
        Assert.Single(adapter.Limits);
        Assert.Equal(0, adapter.Active);
        Assert.Equal(0, clock.Timers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_query_source_failure_disposes_before_delivery(bool snapshot)
    {
        var source = new Source();
        var failure = new InvalidOperationException("authoritative read failure");
        var adapter = new Adapter
        {
            SnapshotCapable = snapshot,
            OnQuery = _ => source.OnCheck = _ => throw failure,
        };
        await using var iterator = Repository(
                source,
                adapter,
                Abort(snapshot ? ExportDataConsistency.Snapshot : ExportDataConsistency.BestEffort)
            )
            .ExportAsync(new())
            .GetAsyncEnumerator();
        Assert.Same(
            failure,
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await iterator.MoveNextAsync()
            )
        );
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(1, source.StampedCaptures);
    }

    public class MappingRow
    {
        public int Id { get; set; }
        public Action? OnMap { get; set; }
        public string Name
        {
            get
            {
                OnMap?.Invoke();
                return "safe";
            }
        }
    }

    sealed class MappingAdapter(MappingRow row) : IEntityReadAdapter<MappingRow>
    {
        public int Disposed;

        public ValueTask<IEntityReadSession<MappingRow>> OpenAsync(
            CancellationToken token = default
        ) => ValueTask.FromResult<IEntityReadSession<MappingRow>>(new Session(this, row));

        sealed class Session(MappingAdapter owner, MappingRow row)
            : IEntitySeekReadSession<MappingRow>
        {
            public Task<IReadOnlyList<MappingRow>> SeekAsync(
                EntitySeekPlan<MappingRow> plan,
                int limit,
                CancellationToken token
            ) => Task.FromResult<IReadOnlyList<MappingRow>>([row]);

            public Task<long> CountAsync(
                EntityReadPlan<MappingRow> plan,
                CancellationToken token
            ) => throw new InvalidOperationException();

            public Task<IReadOnlyList<MappingRow>> PageAsync(
                EntityReadPlan<MappingRow> plan,
                int offset,
                int limit,
                CancellationToken token
            ) => throw new InvalidOperationException();

            public Task<MappingRow?> SingleAsync(
                EntityReadPlan<MappingRow> plan,
                CancellationToken token
            ) => throw new InvalidOperationException();

            public ValueTask DisposeAsync()
            {
                owner.Disposed++;
                return ValueTask.CompletedTask;
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_DTO_mapping_cannot_deliver_after_policy_change_or_deadline(bool expire)
    {
        var schema = new ResourceSchema<MappingRow>(
            "Mapping",
            "1",
            [
                ResourceSchema<MappingRow>.Field("Id", row => row.Id, [FieldOperation.Select]),
                ResourceSchema<MappingRow>.Field("Name", row => row.Name, [FieldOperation.Select]),
            ],
            "Id"
        );
        var policy = new PolicyDefinition(
            "Mapping",
            "1",
            [new("allow", "Mapping", "Read", Effect.Allow, new(["manager"], []), null)],
            [
                new(
                    "fields",
                    "Mapping",
                    "Read",
                    Effect.Allow,
                    new(["manager"], []),
                    new() { { FieldOperation.Select, ["Id", "Name"] } }
                ),
            ]
        );
        var source = new Source
        {
            Policy = new("scope", "Mapping", "1", 1, PolicyJson.Serialize(policy)),
        };
        var clock = new Clock();
        var adapter = new MappingAdapter(
            new()
            {
                Id = 1,
                OnMap = () =>
                {
                    if (expire)
                        clock.Advance(TimeSpan.FromSeconds(1));
                    else
                        source.Revision++;
                },
            }
        );
        var compiler = new PolicyCompiler<MappingRow>(
            schema,
            new(new Dictionary<string, Type>()),
            CompilerTests.Leaf("Id", "gt", 0)
        );
        var repo = new AuthorizedReadRepository<MappingRow>(
            schema,
            compiler,
            source,
            adapter,
            () => CompilerTests.Context(),
            "scope",
            projectionMode: ReadProjectionMode.AllowEntityMaterialization,
            export: new(
                ExportPolicyConsistency.AbortOnPolicyChange,
                maxDuration: TimeSpan.FromSeconds(1),
                timeProvider: clock
            )
        );
        await using var iterator = repo.ExportAsync(new(Selection: ["Name"])).GetAsyncEnumerator();
        var error = await Record.ExceptionAsync(async () => await iterator.MoveNextAsync());
        Assert.IsType(
            expire ? typeof(ExportDurationExceededException) : typeof(ExportPolicyChangedException),
            error
        );
        Assert.Equal(1, adapter.Disposed);
        Assert.Equal(0, clock.Timers);
    }
}
