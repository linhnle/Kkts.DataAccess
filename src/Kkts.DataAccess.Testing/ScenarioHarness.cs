using System.Text.Json;

namespace Kkts.DataAccess.Testing;

internal sealed class ScenarioHarness<T>
    where T : class
{
    internal readonly AuthorizedReadRepository<T> Repository;
    internal readonly TestPolicySource Source = new();
    internal int Tenant = 1;
    internal string Role = "tester";

    internal ScenarioHarness(
        IAdapterConformanceFixture<T> fixture,
        bool composite = false,
        ReadProjectionMode? projection = null,
        ExportReadConfiguration? export = null
    )
    {
        var bindings = fixture.Bindings;
        var schema = new ResourceSchema<T>(
            "Conformance",
            "1",
            [
                ResourceSchema<T>.Field("Id", bindings.Id, [FieldOperation.Select]),
                ResourceSchema<T>.Field("TenantId", bindings.TenantId, []),
                ResourceSchema<T>.Field("GroupId", bindings.GroupId, []),
                ResourceSchema<T>.Field("LocalId", bindings.LocalId, []),
                ResourceSchema<T>.Field("LocalAlias", bindings.LocalId, [FieldOperation.Sort]),
                ResourceSchema<T>.Field(
                    "Name",
                    bindings.Name,
                    [FieldOperation.Select, FieldOperation.Sort]
                ),
                ResourceSchema<T>.Field(
                    "Rank",
                    bindings.Rank,
                    [FieldOperation.Filter, FieldOperation.Sort]
                ),
                ResourceSchema<T>.Field("Denied", bindings.Denied, []),
            ],
            composite ? ["GroupId", "LocalId"] : ["Id"]
        );
        var registry = new ContextRegistry(
            new Dictionary<string, Type> { ["tenant"] = typeof(int) }
        );
        Repository = new(
            schema,
            new(
                schema,
                registry,
                new Condition
                {
                    Field = "TenantId",
                    Operator = "eq",
                    Context = "tenant",
                }
            ),
            Source,
            fixture.CreateAdapter(schema),
            () =>
                new TrustedContext(
                    [Role],
                    [],
                    new Dictionary<string, object?> { ["tenant"] = Tenant }
                ),
            "conformance",
            new(2, 100, 1),
            new(
                fixture.ContinuationProtector,
                () => "test-subject",
                "isolated-conformance-dataset"
            ),
            projection
                ?? (
                    fixture.Capabilities.Projection
                        ? ReadProjectionMode.Required
                        : ReadProjectionMode.AllowEntityMaterialization
                ),
            export
        );
    }

    internal static JsonElement Key(int id, bool composite = false)
    {
        var record = ConformanceRecord.Seed.Single(record => record.Id == id);
        return composite
            ? JsonSerializer.SerializeToElement(new { record.GroupId, record.LocalId })
            : JsonSerializer.SerializeToElement(new { record.Id });
    }

    internal sealed class TestPolicySource : IActivationAwarePolicySource
    {
        long version = 1;
        long revision = 1;
        bool denyName;

        internal void Activate() => revision++;

        internal void DenyName()
        {
            denyName = true;
            version++;
            revision++;
        }

        CapturedPolicy Policy()
        {
            Subject subject = new(["tester"], []);
            var definition = new PolicyDefinition(
                "Conformance",
                "1",
                [
                    new(
                        "allow",
                        "Conformance",
                        "Read",
                        Effect.Allow,
                        subject,
                        new Condition
                        {
                            Field = "GroupId",
                            Operator = "lt",
                            Literal = JsonSerializer.SerializeToElement(10),
                        }
                    ),
                    new(
                        "deny",
                        "Conformance",
                        "Read",
                        Effect.Deny,
                        subject,
                        new Condition
                        {
                            Field = "Denied",
                            Operator = "eq",
                            Literal = JsonSerializer.SerializeToElement(true),
                        }
                    ),
                ],
                [
                    new(
                        "fields",
                        "Conformance",
                        "Read",
                        Effect.Allow,
                        subject,
                        new()
                        {
                            [FieldOperation.Select] = ["Id", "Name"],
                            [FieldOperation.Filter] = ["Rank"],
                            [FieldOperation.Sort] = ["Name", "Rank", "LocalAlias"],
                        }
                    ),
                    new(
                        "denied-fields",
                        "Conformance",
                        "Read",
                        Effect.Deny,
                        subject,
                        new() { [FieldOperation.Select] = denyName ? ["Name"] : [] }
                    ),
                ]
            );
            return new(
                "conformance",
                "Conformance",
                "1",
                version,
                PolicyJson.Serialize(definition)
            );
        }

        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(Policy());
        }

        public Task<CapturedPolicyActivation> CaptureActivationAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(new CapturedPolicyActivation(Policy(), new(version, revision)));
        }

        public Task<PolicyActivationStamp> ReadActivationAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(new PolicyActivationStamp(version, revision));
        }
    }
}

internal sealed class ScenarioClock : TimeProvider
{
    long timestamp;
    readonly List<ScenarioTimer> timers = [];
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => timestamp;

    internal void Advance(TimeSpan elapsed)
    {
        timestamp += elapsed.Ticks;
        foreach (var timer in timers.ToArray())
            timer.Fire(timestamp);
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        var timer = new ScenarioTimer(this, callback, state);
        timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    sealed class ScenarioTimer(ScenarioClock clock, TimerCallback callback, object? state) : ITimer
    {
        long? due;
        bool disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (disposed)
                return false;
            if (period != Timeout.InfiniteTimeSpan)
                throw new ArgumentException("Conformance timers must be one-shot.", nameof(period));
            due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.timestamp + dueTime.Ticks;
            return true;
        }

        internal void Fire(long now)
        {
            if (disposed || due == null || now < due)
                return;
            due = null;
            callback(state);
        }

        public void Dispose()
        {
            disposed = true;
            clock.timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
