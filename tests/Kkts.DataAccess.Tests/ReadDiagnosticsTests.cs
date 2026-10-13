using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Xunit;
using Employee = Kkts.DataAccess.Tests.CompilerTests.Employee;

namespace Kkts.DataAccess.Tests;

internal sealed class DiagnosticRecorder : IDisposable
{
    static readonly AsyncLocal<DiagnosticRecorder?> Active = new();
    readonly ActivityListener activities;
    readonly MeterListener metrics = new();
    readonly DiagnosticRecorder? previous = Active.Value;
    internal readonly ConcurrentQueue<Activity> Activities = new();
    internal readonly ConcurrentQueue<Measurement> Measurements = new();
    internal readonly ConcurrentDictionary<string, Instrument> Instruments = new();
    internal Action<Measurement>? OnMeasurement;

    internal sealed record Measurement(
        string Name,
        string? Unit,
        double Value,
        Dictionary<string, object?> Tags
    );

    internal DiagnosticRecorder()
    {
        Active.Value = this;
        activities = new()
        {
            ShouldListenTo = source => source.Name == ReadDiagnostics.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                Active.Value == this ? ActivitySamplingResult.AllData : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (Active.Value == this)
                    Activities.Enqueue(activity);
            },
        };
        ActivitySource.AddActivityListener(activities);
        metrics.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ReadDiagnostics.Name)
            {
                Instruments[instrument.Name] = instrument;
                listener.EnableMeasurementEvents(instrument);
            }
        };
        metrics.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => Record(instrument, value, tags)
        );
        metrics.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => Record(instrument, value, tags)
        );
        metrics.Start();
    }

    void Record(
        Instrument instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags
    )
    {
        if (Active.Value != this)
            return;
        var copy = new Dictionary<string, object?>();
        foreach (var tag in tags)
            copy.Add(tag.Key, tag.Value);
        var measurement = new Measurement(instrument.Name, instrument.Unit, value, copy);
        Measurements.Enqueue(measurement);
        OnMeasurement?.Invoke(measurement);
    }

    internal Measurement[] Find(string name) => Measurements.Where(m => m.Name == name).ToArray();

    internal double Total(string name, string key, string value) =>
        Find(name).Where(m => Equals(m.Tags.GetValueOrDefault(key), value)).Sum(m => m.Value);

    internal void AssertPrivate(params string[] sentinels)
    {
        var emitted = string.Join(
            "|",
            Activities
                .SelectMany(activity =>
                    activity
                        .TagObjects.Select(tag => $"{tag.Key}={tag.Value}")
                        .Concat(
                            activity.Events.SelectMany(e =>
                                e.Tags.Select(t => $"{e.Name}:{t.Key}={t.Value}")
                            )
                        )
                        .Append(activity.StatusDescription ?? "")
                        .Append(activity.DisplayName)
                )
                .Concat(Measurements.SelectMany(m => m.Tags.Select(t => $"{t.Key}={t.Value}")))
        );
        foreach (var sentinel in sentinels)
            Assert.DoesNotContain(sentinel, emitted, StringComparison.Ordinal);
        var keys = new[] { "operation", "provider", "stage", "outcome", "code", "cache" };
        Assert.All(Activities.SelectMany(a => a.TagObjects), tag => Assert.Contains(tag.Key, keys));
        Assert.All(Measurements.SelectMany(m => m.Tags), tag => Assert.Contains(tag.Key, keys));
        Assert.All(
            Activities,
            a =>
            {
                Assert.Empty(a.Events);
                Assert.Null(a.StatusDescription);
            }
        );
    }

    public void Dispose()
    {
        metrics.Dispose();
        activities.Dispose();
        Active.Value = previous;
    }
}

public partial class RepositoryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Diagnostics_cover_each_read_once_and_preserve_listener_parity(
        bool projected,
        bool total
    )
    {
        async Task<(string Output, int Queries, int Opened, int Disposed)> Run()
        {
            var source = new Source();
            var adapter = new RecordingAdapter
            {
                SupportsProjection = projected,
                SupportsProjectedSeek = projected,
            };
            var context = new AsyncContext(async token =>
            {
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                return CompilerTests.Context();
            });
            var repository = new AuthorizedReadRepository<Employee>(
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
            var page = await repository.ListAsync(new(Limit: 1, IncludeTotal: total));
            var count = await repository.CountAsync(new());
            var detail = await repository.DetailAsync(
                new(JsonSerializer.SerializeToElement(new { Id = 1 }))
            );
            var cursor = await repository.ListCursorAsync(new(Limit: 1, IncludeTotal: total));
            var missing = await repository.DetailAsync(
                new(JsonSerializer.SerializeToElement(new { Id = 999 }))
            );
            Assert.Null(missing.Record);
            Assert.Equal(5, context.Captures);
            Assert.Equal(5, source.Captures);
            var queries =
                adapter.CountCalls
                + adapter.PageCalls
                + adapter.SingleCalls
                + adapter.SeekCalls
                + adapter.ProjectedPageCalls
                + adapter.ProjectedSingleCalls
                + adapter.ProjectedSeekCalls;
            return (
                JsonSerializer.Serialize(
                    new
                    {
                        page.Records,
                        count.Total,
                        detail.Record,
                        Cursor = cursor.Records,
                    }
                ),
                queries,
                adapter.Opened,
                adapter.Disposed
            );
        }

        var baseline = await Run();
        using var recorder = new DiagnosticRecorder();
        var observed = await Run();
        Assert.Equal(baseline, observed);
        Assert.Equal(total ? 7 : 5, observed.Queries);
        Assert.Equal(observed.Opened, observed.Disposed);
        Assert.Equal(5, recorder.Find("kda.operations").Length);
        Assert.Equal(5, recorder.Activities.Count(a => a.OperationName.StartsWith("kda.read.")));
        Assert.Equal(5, recorder.Activities.Count(a => a.OperationName == "kda.policy.capture"));
        Assert.Equal(5, recorder.Activities.Count(a => a.OperationName == "kda.policy.bind"));
        Assert.Equal(
            observed.Queries,
            recorder.Activities.Count(a => a.OperationName == "kda.entity.execute")
        );
        Assert.Equal(
            observed.Queries,
            recorder.Find("kda.stage.duration").Count(m => Equals(m.Tags["stage"], "execute"))
        );
        Assert.Equal(3, recorder.Activities.Count(a => a.OperationName == "kda.dto.map"));
        Assert.Equal(3, recorder.Find("kda.rows.returned").Sum(m => m.Value));
        Assert.All(
            recorder.Find("kda.operation.duration").Concat(recorder.Find("kda.stage.duration")),
            m =>
            {
                Assert.Equal("s", m.Unit);
                Assert.True(m.Value >= 0);
            }
        );
        Assert.All(
            recorder.Activities.Where(a => !a.OperationName.StartsWith("kda.read.")),
            a =>
                Assert.Contains(
                    recorder.Activities,
                    root =>
                        root.SpanId == a.ParentSpanId && root.OperationName.StartsWith("kda.read.")
                )
        );
        recorder.AssertPrivate();
    }

    [Fact]
    public async Task Diagnostics_never_emit_context_policy_token_row_or_exception_secrets()
    {
        const string secret = "SENTINEL-SECRET-DO-NOT-EMIT";
        using var recorder = new DiagnosticRecorder();
        using var parent = new Activity("host").Start();
        parent.AddBaggage("secret", secret);
        var source = new Source
        {
            Current = CompilerTests.Capture(
                CompilerTests.Definition(CompilerTests.Rule(secret, Effect.Allow))
            ),
        };
        var adapter = new RecordingAdapter();
        adapter.Rows[0].Name = secret;
        var repository = Repository(
            source,
            adapter,
            Cursor(() => secret),
            () =>
                new(
                    ["manager", secret],
                    [secret],
                    new Dictionary<string, object?>
                    {
                        ["tenant"] = 1,
                        ["department"] = 10,
                        ["secret"] = secret,
                    }
                )
        );
        var first = await repository.ListCursorAsync(new(Limit: 1));
        Assert.NotNull(first.NextContinuationToken);
        await repository.ListAsync(
            new(Filter: CompilerTests.Leaf("Status", "eq", secret), Limit: 1)
        );
        await repository.ListCursorAsync(
            new(Limit: 1, ContinuationToken: first.NextContinuationToken)
        );
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
            repository.ListCursorAsync(new(Limit: 1, ContinuationToken: secret))
        );
        adapter.OnCount = () =>
            throw new InvalidOperationException(secret + " SQL connection password");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CountAsync(new()));
        source.Current = source.Current with { Version = 2, Payload = secret };
        await Assert.ThrowsAsync<SchemaIncompatibleException>(() => repository.CountAsync(new()));
        recorder.AssertPrivate(secret, first.NextContinuationToken!, source.Current.Payload);
        Assert.Contains(
            recorder.Find("kda.operations"),
            m => Equals(m.Tags["code"], "infrastructure")
        );
        Assert.Contains(
            recorder.Find("kda.operations"),
            m => Equals(m.Tags["code"], "invalid_continuation")
        );
    }
}

public partial class ExportLifecycleTests
{
    [Theory]
    [InlineData("disposed_early")]
    [InlineData("exhausted")]
    [InlineData("canceled")]
    [InlineData("duration_exceeded")]
    public async Task Diagnostics_short_batch_is_only_candidate_completion(string outcome)
    {
        using var recorder = new DiagnosticRecorder();
        using var caller = new CancellationTokenSource();
        var adapter = new Adapter();
        adapter.Rows = adapter.Rows.Take(1).ToArray();
        var clock = new Clock();
        var iterator = Repository(
                new Source(),
                adapter,
                new(maxDuration: TimeSpan.FromSeconds(1), timeProvider: clock)
            )
            .ExportAsync(new(), caller.Token)
            .GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        Assert.Empty(recorder.Find("kda.exports"));
        if (outcome == "duration_exceeded")
            clock.Advance(TimeSpan.FromSeconds(2));
        if (outcome == "canceled")
            caller.Cancel();
        if (outcome == "exhausted")
            Assert.False(await iterator.MoveNextAsync());
        else if (outcome != "disposed_early")
            Assert.NotNull(await Record.ExceptionAsync(() => iterator.MoveNextAsync().AsTask()));
        await iterator.DisposeAsync();
        Assert.Equal(outcome, Assert.Single(recorder.Find("kda.exports")).Tags["outcome"]);
        Assert.Single(adapter.Limits);
        Assert.Equal(adapter.Opened, adapter.Disposed);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("failed")]
    [InlineData("empty")]
    public async Task Diagnostics_first_advancement_failure_or_empty_export_is_terminal(
        string termination
    )
    {
        using var recorder = new DiagnosticRecorder();
        using var caller = new CancellationTokenSource();
        var adapter = new Adapter();
        var source = new Source();
        if (termination == "canceled")
            caller.Cancel();
        if (termination == "failed")
            source.OnCapture = _ => throw new InvalidOperationException("SECRET-capture");
        if (termination == "empty")
            adapter.Rows = [];
        var iterator = Repository(source, adapter)
            .ExportAsync(new(), caller.Token)
            .GetAsyncEnumerator();
        if (termination == "empty")
            Assert.False(await iterator.MoveNextAsync());
        else
            Assert.NotNull(await Record.ExceptionAsync(() => iterator.MoveNextAsync().AsTask()));
        await iterator.DisposeAsync();
        Assert.Equal(
            termination == "empty" ? "exhausted" : termination,
            Assert.Single(recorder.Find("kda.exports")).Tags["outcome"]
        );
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Equal(termination == "empty" ? 1 : 0, adapter.Limits.Count);
        recorder.AssertPrivate("SECRET-capture");
    }

    [Theory]
    [InlineData("exhausted", false)]
    [InlineData("row_cap", false)]
    [InlineData("disposed_early", false)]
    [InlineData("policy_changed", false)]
    [InlineData("duration_exceeded", false)]
    [InlineData("canceled", false)]
    [InlineData("failed", false)]
    [InlineData("exhausted", true)]
    [InlineData("row_cap", true)]
    [InlineData("disposed_early", true)]
    [InlineData("policy_changed", true)]
    [InlineData("duration_exceeded", true)]
    [InlineData("canceled", true)]
    [InlineData("failed", true)]
    public async Task Diagnostics_export_terminal_is_once_after_owned_cleanup(
        string outcome,
        bool snapshot
    )
    {
        using var recorder = new DiagnosticRecorder();
        using var caller = new CancellationTokenSource();
        var source = new Source();
        var adapter = new Adapter { SnapshotCapable = true };
        var clock = new Clock();
        var repository = Repository(
            source,
            adapter,
            new(
                ExportPolicyConsistency.AbortOnPolicyChange,
                snapshot ? ExportDataConsistency.Snapshot : ExportDataConsistency.BestEffort,
                maxRows: outcome is "row_cap" or "duration_exceeded" ? 4 : null,
                maxDuration: TimeSpan.FromSeconds(1),
                timeProvider: clock
            )
        );
        using var parent = new Activity("host").Start();
        var iterator = repository.ExportAsync(new(), caller.Token).GetAsyncEnumerator();
        Assert.Empty(recorder.Activities);
        Assert.Empty(recorder.Find("kda.operations"));
        Assert.True(await iterator.MoveNextAsync());
        Assert.Same(parent, Activity.Current);
        using (var unrelated = new Activity("consumer").Start())
            Assert.Equal(parent.Id, unrelated.ParentId);
        Assert.Empty(recorder.Find("kda.exports"));
        switch (outcome)
        {
            case "policy_changed":
                source.Revision++;
                break;
            case "duration_exceeded":
                clock.Advance(TimeSpan.FromSeconds(2));
                break;
            case "canceled":
                caller.Cancel();
                break;
            case "failed":
                adapter.OnQuery = _ => throw new InvalidOperationException("SECRET-provider-error");
                break;
        }
        recorder.OnMeasurement = m =>
        {
            if (m.Name == "kda.exports")
                Assert.Equal(adapter.Opened, adapter.Disposed);
        };
        if (outcome != "disposed_early")
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                while (await iterator.MoveNextAsync()) { }
            });
            if (outcome is "row_cap" or "exhausted")
                Assert.Null(error);
            else
                Assert.NotNull(error);
        }
        await iterator.DisposeAsync();
        await iterator.DisposeAsync();
        Assert.Equal(adapter.Opened, adapter.Disposed);
        Assert.Same(parent, Activity.Current);
        var terminal = Assert.Single(recorder.Find("kda.exports"));
        Assert.Equal(outcome, terminal.Tags["outcome"]);
        Assert.Equal("{export}", terminal.Unit);
        Assert.Single(recorder.Find("kda.operations"));
        var root = Assert.Single(recorder.Activities, a => a.OperationName == "kda.read.export");
        Assert.Equal(parent.SpanId, root.ParentSpanId);
        Assert.All(
            recorder.Activities.Where(a => a != root),
            a => Assert.Equal(root.SpanId, a.ParentSpanId)
        );
        Assert.Equal(
            adapter.Limits.Count,
            recorder.Activities.Count(a => a.OperationName == "kda.entity.execute")
        );
        if (
            outcome
            is "duration_exceeded"
                or "row_cap"
                or "disposed_early"
                or "policy_changed"
                or "canceled"
        )
            Assert.Single(adapter.Limits);
        Assert.Equal(
            outcome == "exhausted" ? 10 : 4,
            recorder.Find("kda.rows.returned").Sum(m => m.Value)
        );
        recorder.AssertPrivate("SECRET-provider-error");
    }

    [Fact]
    public async Task Diagnostics_unadvanced_and_abandoned_exports_do_not_invent_completion()
    {
        using var recorder = new DiagnosticRecorder();
        var adapter = new Adapter();
        var repository = Repository(new Source(), adapter);
        var unadvanced = repository.ExportAsync(new()).GetAsyncEnumerator();
        await unadvanced.DisposeAsync();
        Assert.Empty(recorder.Measurements);
        Assert.Empty(recorder.Activities);
        var abandoned = repository.ExportAsync(new()).GetAsyncEnumerator();
        Assert.True(await abandoned.MoveNextAsync());
        Assert.Empty(recorder.Find("kda.exports"));
        Assert.DoesNotContain(recorder.Activities, a => a.OperationName == "kda.read.export");
        await abandoned.DisposeAsync();
        Assert.Equal("disposed_early", Assert.Single(recorder.Find("kda.exports")).Tags["outcome"]);
    }

    [Fact]
    public async Task Diagnostics_cleanup_failure_replaces_candidate_success()
    {
        using var recorder = new DiagnosticRecorder();
        var adapter = new Adapter
        {
            SnapshotCapable = true,
            OnDispose = _ => throw new InvalidOperationException("SECRET-cleanup"),
        };
        var iterator = Repository(
                new Source(),
                adapter,
                new(dataConsistency: ExportDataConsistency.Snapshot, maxRows: 4)
            )
            .ExportAsync(new())
            .GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            iterator.MoveNextAsync().AsTask()
        );
        await iterator.DisposeAsync();
        Assert.Equal("failed", Assert.Single(recorder.Find("kda.exports")).Tags["outcome"]);
        recorder.AssertPrivate("SECRET-cleanup");
    }
}

public partial class ReadDiagnosticsTests
{
    [Fact]
    public async Task Bcl_listener_setup_compiles_and_instrument_contract_is_versioned()
    {
        using var recorder = new DiagnosticRecorder();
        using var activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReadDiagnostics.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity => Console.WriteLine(activity.OperationName),
        };
        ActivitySource.AddActivityListener(activities);
        using var metrics = new MeterListener();
        metrics.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ReadDiagnostics.Name)
                listener.EnableMeasurementEvents(instrument);
        };
        metrics.SetMeasurementEventCallback<long>(
            (instrument, value, tags, state) => Console.WriteLine($"{instrument.Name}: {value}")
        );
        metrics.SetMeasurementEventCallback<double>(
            (instrument, value, tags, state) => Console.WriteLine($"{instrument.Name}: {value}")
        );
        metrics.Start();
        await ReadDiagnostics.Read("count", "custom", () => Task.FromResult(1));
        Assert.Equal("Kkts.DataAccess", ReadDiagnostics.Name);
        Assert.Equal("1.0.0", ReadDiagnostics.Version);
        var expected = new Dictionary<string, string>
        {
            ["kda.operation.duration"] = "s",
            ["kda.stage.duration"] = "s",
            ["kda.operations"] = "{operation}",
            ["kda.exports"] = "{export}",
            ["kda.rows.returned"] = "{row}",
            ["kda.cache.lookups"] = "{lookup}",
            ["kda.cache.builds"] = "{build}",
            ["kda.cache.evictions"] = "{eviction}",
        };
        Assert.Equal(expected.Count, recorder.Instruments.Count);
        foreach (var (name, unit) in expected)
        {
            var instrument = recorder.Instruments[name];
            Assert.Equal(unit, instrument.Unit);
            Assert.Equal(ReadDiagnostics.Version, instrument.Meter.Version);
            if (unit == "s")
                Assert.IsType<Histogram<double>>(instrument);
            else
                Assert.IsType<Counter<long>>(instrument);
        }
        Assert.Equal(ReadDiagnostics.Version, Assert.Single(recorder.Activities).Source.Version);
        foreach (var code in Enum.GetValues<DataAccessErrorCode>())
            Assert.NotEqual(
                "unknown",
                ReadDiagnostics.Code(new DataAccessException(code, "SECRET"))
            );
    }

    [Fact]
    public async Task Cache_callbacks_are_outside_locks_and_concurrent_totals_match_actual_builds()
    {
        using var recorder = new DiagnosticRecorder();
        var compiler = new PolicyCompiler<Employee>(
            CompilerTests.Schema,
            CompilerTests.Registry,
            CompilerTests.Reference("TenantId", "tenant"),
            cacheOptions: new(1)
        );
        var policy = CompilerTests.Capture(
            CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
        );
        var dto = new DtoShapeCache(1);
        var key = DtoShapeKey.Create(CompilerTests.Schema, ["Id"]);
        recorder.OnMeasurement = measurement =>
        {
            if (measurement.Name.StartsWith("kda.cache."))
                Assert.True(
                    Task.Factory.StartNew(
                            () =>
                            {
                                _ = compiler.TemplateCount;
                                _ = dto.Count;
                            },
                            CancellationToken.None,
                            TaskCreationOptions.LongRunning,
                            TaskScheduler.Default
                        )
                        .Wait(TimeSpan.FromSeconds(5))
                );
        };
        await Task.WhenAll(
            Enumerable
                .Range(0, 16)
                .Select(_ =>
                    Task.Run(() =>
                    {
                        compiler.Bind(policy, CompilerTests.Context(), "scope");
                        dto.GetOrBuild(key);
                    })
                )
        );
        Assert.Equal(15, recorder.Total("kda.cache.lookups", "outcome", "hit") / 2);
        Assert.Equal(2, recorder.Total("kda.cache.lookups", "outcome", "miss"));
        Assert.Equal(2, recorder.Total("kda.cache.builds", "outcome", "success"));
        compiler.Bind(policy with { Version = 2 }, CompilerTests.Context(), "scope");
        dto.GetOrBuild(DtoShapeKey.Create(CompilerTests.Schema, ["Name"]));
        Assert.Equal(2, recorder.Find("kda.cache.evictions").Sum(m => m.Value));
        Assert.Throws<SchemaIncompatibleException>(() =>
            compiler.Bind(
                policy with
                {
                    Version = 3,
                    Payload = "SECRET-payload",
                },
                CompilerTests.Context(),
                "scope"
            )
        );
        var failing = new DtoShapeCache(createAssembly: () =>
            throw new InvalidOperationException("SECRET-build")
        );
        Assert.Throws<InvalidOperationException>(() => failing.GetOrBuild(key));
        Assert.Equal(2, recorder.Total("kda.cache.builds", "outcome", "failed"));
        Assert.Equal(1, compiler.TemplateCount);
        Assert.Equal(1, dto.Count);
        recorder.AssertPrivate("SECRET-payload", "SECRET-build");
    }
}

public partial class ReadTests
{
    [Fact]
    public async Task Diagnostics_real_provider_observations_match_commands_and_disposal_with_and_without_listeners()
    {
        await SeedComposite();
        async Task<(string Result, int Queries, int Sessions)> Run()
        {
            var sessions = new List<Database>();
            var commands = new SeekCommands();
            var repository = CompositeReads(sessions, commands);
            var page = await repository.ListAsync(new(Limit: 1, IncludeTotal: false));
            var count = await repository.CountAsync(new());
            var cursor = await repository.ListCursorAsync(new(Limit: 1));
            var detail = await repository.DetailAsync(
                new(JsonSerializer.SerializeToElement(new { GroupId = 1, LocalId = 1 }))
            );
            var batches = new List<ExportBatch>();
            await foreach (var batch in repository.ExportAsync(new()))
                batches.Add(batch);
            Assert.All(sessions, session => Assert.True(session.Disposed));
            return (
                JsonSerializer.Serialize(
                    new
                    {
                        page.Records,
                        count.Total,
                        detail.Record,
                        Cursor = cursor.Records,
                        Batches = batches.Select(b => b.Records),
                    }
                ),
                commands.Sql.Count,
                sessions.Count
            );
        }
        var baseline = await Run();
        using var recorder = new DiagnosticRecorder();
        var observed = await Run();
        Assert.Equal(baseline, observed);
        Assert.Equal(
            observed.Queries,
            recorder.Activities.Count(a => a.OperationName == "kda.entity.execute")
        );
        Assert.Equal(5, recorder.Find("kda.operations").Length);
        Assert.Single(recorder.Find("kda.exports"));
        var provider = ReadDiagnostics.Provider(ProviderName);
        Assert.NotEqual("custom", provider);
        Assert.All(
            recorder.Find("kda.operations"),
            m => Assert.Equal(provider, m.Tags["provider"])
        );
        recorder.AssertPrivate(
            CompositeSchema.Resource,
            CompositeSchema.Version,
            "composite-dataset"
        );
    }
}

public partial class ReadDiagnosticsTests
{
    [Fact]
    public async Task Custom_provider_codes_and_resource_churn_have_finite_series()
    {
        using var recorder = new DiagnosticRecorder();
        var cache = new DtoShapeCache(1);
        for (var i = 0; i < 40; i++)
        {
            var secret = "SECRET-identity-" + i;
            var schema = DtoMapperTests.Schema(version: secret, resource: secret, alias: secret);
            cache.GetOrBuild(DtoShapeKey.Create(schema, ["id"]));
            var provider = ReadDiagnostics.Provider(secret);
            Assert.Equal("custom", provider);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReadDiagnostics.Read<int>(
                    "list",
                    provider,
                    () => throw new InvalidOperationException(secret)
                )
            );
            recorder.AssertPrivate(secret);
        }
        Assert.Equal(
            5,
            recorder
                .Measurements.Select(m =>
                    m.Name + string.Join("|", m.Tags.Select(t => $"{t.Key}={t.Value}"))
                )
                .Distinct()
                .Count()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DataAccessException((DataAccessErrorCode)999, "SECRET-custom-code")
        );
        Assert.Equal(
            "invalid_request",
            ReadDiagnostics.Code(
                new InvalidRequestException(
                    "SECRET",
                    [new((DataAccessErrorCode)999, "SECRET", "SECRET")]
                )
            )
        );
        Assert.Equal("infrastructure", ReadDiagnostics.Code(new Exception("SECRET-custom-code")));
        Assert.Equal("sqlite", ReadDiagnostics.Provider("Microsoft.EntityFrameworkCore.Sqlite"));
        Assert.Equal(
            "sqlserver",
            ReadDiagnostics.Provider("Microsoft.EntityFrameworkCore.SqlServer")
        );
        Assert.Equal("mysql", ReadDiagnostics.Provider("MySql.EntityFrameworkCore"));
    }
}
