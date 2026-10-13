using CustomReadAdapter;
using Kkts.DataAccess;
using Kkts.DataAccess.DataProtection;
using Kkts.DataAccess.Testing;
using Microsoft.AspNetCore.DataProtection;

namespace AdapterConformance;

public class ConformanceEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int GroupId { get; set; }
    public int LocalId { get; set; }
    public string Name { get; set; } = "";
    public int? Rank { get; set; }
    public bool Denied { get; set; }
    public string Secret { get; set; } = "hidden test payload";

    public ConformanceEntity Clone() => (ConformanceEntity)MemberwiseClone();

    public static ConformanceEntity From(ConformanceRecord row) =>
        new()
        {
            Id = row.Id,
            TenantId = row.TenantId,
            GroupId = row.GroupId,
            LocalId = row.LocalId,
            Name = row.Name,
            Rank = row.Rank,
            Denied = row.Denied,
        };
}

public enum RecordingFault
{
    None,
    IgnoreDeny,
    OverReturn,
    LeakDisposal,
    BrokenSidecars,
    DropSeek,
}

public sealed class RecordingFixture(
    RecordingFault fault = RecordingFault.None,
    ConformanceCapabilities? capabilities = null
) : IAdapterConformanceFixture<ConformanceEntity>
{
    readonly ConformanceCapabilities capabilities = capabilities ?? new(Snapshot: true);
    List<ConformanceEntity> rows = [];
    IContinuationTokenProtector? protector;
    int opened,
        disposed,
        countQueries,
        rowQueries;
    bool translationFailure;
    public bool TornDown { get; private set; }
    public ConformanceCapabilities Capabilities => capabilities;
    public ConformanceProvider Provider => ConformanceProvider.InMemory;
    public Version ProviderVersion => new(1, 0);
    public IContinuationTokenProtector ContinuationProtector =>
        protector ?? throw new InvalidOperationException("Fixture setup has not run.");
    public ConformanceBindings<ConformanceEntity> Bindings { get; } =
        new(
            row => row.Id,
            row => row.TenantId,
            row => row.GroupId,
            row => row.LocalId,
            row => row.Name,
            row => row.Rank,
            row => row.Denied
        );

    public ValueTask SetupAsync(
        IReadOnlyList<ConformanceRecord> records,
        OwnedFixtureResources ownership,
        CancellationToken cancellation
    )
    {
        cancellation.ThrowIfCancellationRequested();
        rows = records.Select(ConformanceEntity.From).ToList();
        protector = new DataProtectionContinuationTokenProtector(
            new EphemeralDataProtectionProvider()
        );
        return ValueTask.CompletedTask;
    }

    public IEntityReadAdapter<ConformanceEntity> CreateAdapter(
        ResourceSchema<ConformanceEntity> schema
    )
    {
        if (!capabilities.CompositeKeys && schema.KeyFields.Count > 1)
            throw new ProviderUnsupportedException(
                "Composite keys are absent in this test fixture."
            );
        return new CustomAdapter<ConformanceEntity>(token =>
        {
            token.ThrowIfCancellationRequested();
            opened++;
            var seek = capabilities.Seek && fault != RecordingFault.DropSeek;
            IEntityReadSession<ConformanceEntity> session = capabilities.Projection
                ? capabilities.Snapshot && seek
                    ? new SnapshotSession(this)
                    : seek
                        ? new ProjectedSeekSession(this)
                        : new ProjectedSession(this)
                : seek
                    ? new SeekSession(this)
                    : new Session(this);
            return ValueTask.FromResult(session);
        });
    }

    public ValueTask RenameAsync(int id, string name, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        rows.Single(row => row.Id == id).Name = name;
        return ValueTask.CompletedTask;
    }

    public ValueTask InjectTranslationFailureAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        translationFailure = true;
        return ValueTask.CompletedTask;
    }

    public ConformanceObservation Observe() => new(opened, disposed, countQueries, rowQueries);

    public ValueTask DisposeAsync()
    {
        TornDown = true;
        rows.Clear();
        protector = null;
        return ValueTask.CompletedTask;
    }

    class Session(RecordingFixture fixture) : IEntityReadSession<ConformanceEntity>
    {
        protected readonly RecordingFixture Fixture = fixture;
        protected IReadOnlyList<ConformanceEntity>? Snapshot;

        protected IEnumerable<ConformanceEntity> Query(EntityReadPlan<ConformanceEntity> plan)
        {
            var source = Snapshot ?? Fixture.rows;
            return Fixture.Fault == RecordingFault.IgnoreDeny
                ? source.Where(row => row.TenantId == 1 && row.GroupId < 10)
                : source.Where(plan.Predicate.Compile());
        }

        protected IEnumerable<ConformanceEntity> Order(EntityReadPlan<ConformanceEntity> plan)
        {
            IOrderedEnumerable<ConformanceEntity>? ordered = null;
            foreach (var sort in plan.Sort)
            {
                Func<ConformanceEntity, object?> key = row => sort.Property.GetValue(row);
                ordered =
                    ordered == null
                        ? sort.Descending
                            ? Query(plan).OrderByDescending(key)
                            : Query(plan).OrderBy(key)
                        : sort.Descending
                            ? ordered.ThenByDescending(key)
                            : ordered.ThenBy(key);
            }
            return ordered ?? throw new InvalidOperationException("Missing protected ordering.");
        }

        protected void RowQuery(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Fixture.rowQueries++;
            if (Fixture.translationFailure)
            {
                Fixture.translationFailure = false;
                throw new InvalidOperationException("Injected translation failure; no fallback.");
            }
        }

        protected int Bound(int limit) =>
            Fixture.Fault == RecordingFault.OverReturn ? limit + 1 : limit;

        protected IEnumerable<ConformanceEntity> SeekRows(EntitySeekPlan<ConformanceEntity> plan) =>
            Order(plan.Read)
                .Where(row =>
                {
                    if (plan.Boundary == null)
                        return true;
                    for (var index = 0; index < plan.Read.Sort.Count; index++)
                    {
                        var sort = plan.Read.Sort[index];
                        var compared = Comparer<object?>.Default.Compare(
                            sort.Property.GetValue(row),
                            plan.Boundary[index]
                        );
                        if (compared != 0)
                            return sort.Descending ? compared < 0 : compared > 0;
                    }
                    return false;
                });

        public Task<long> CountAsync(
            EntityReadPlan<ConformanceEntity> plan,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            Fixture.countQueries++;
            return Task.FromResult(Query(plan).LongCount());
        }

        public Task<IReadOnlyList<ConformanceEntity>> PageAsync(
            EntityReadPlan<ConformanceEntity> plan,
            int offset,
            int limit,
            CancellationToken token
        )
        {
            RowQuery(token);
            return Task.FromResult<IReadOnlyList<ConformanceEntity>>(
                Order(plan).Skip(offset).Take(Bound(limit)).ToArray()
            );
        }

        public Task<ConformanceEntity?> SingleAsync(
            EntityReadPlan<ConformanceEntity> plan,
            CancellationToken token
        )
        {
            RowQuery(token);
            return Task.FromResult(Query(plan).SingleOrDefault());
        }

        public ValueTask DisposeAsync()
        {
            if (Fixture.Fault != RecordingFault.LeakDisposal)
                Fixture.disposed++;
            return ValueTask.CompletedTask;
        }
    }

    sealed class SeekSession(RecordingFixture fixture)
        : Session(fixture),
            IEntitySeekReadSession<ConformanceEntity>
    {
        public Task<IReadOnlyList<ConformanceEntity>> SeekAsync(
            EntitySeekPlan<ConformanceEntity> plan,
            int limit,
            CancellationToken token
        )
        {
            RowQuery(token);
            return Task.FromResult<IReadOnlyList<ConformanceEntity>>(
                SeekRows(plan).Take(Bound(limit)).ToArray()
            );
        }
    }

    class ProjectedSession(RecordingFixture fixture)
        : Session(fixture),
            IEntityProjectedReadSession<ConformanceEntity>
    {
        protected ProjectedReadRow Project(
            ConformanceEntity row,
            EntityReadPlan<ConformanceEntity> plan,
            EntitySeekPlan<ConformanceEntity>? seek = null
        ) =>
            new(
                plan.OutputBindings.Select(binding => binding.Property.GetValue(row)),
                seek == null || Fixture.Fault == RecordingFault.BrokenSidecars
                    ? []
                    : seek.ContinuationBindings.Select(binding => binding.GetValue(row))
            );

        public Task<IReadOnlyList<ProjectedReadRow>> PageProjectedAsync(
            EntityReadPlan<ConformanceEntity> plan,
            int offset,
            int limit,
            CancellationToken token
        )
        {
            RowQuery(token);
            return Task.FromResult<IReadOnlyList<ProjectedReadRow>>(
                Order(plan)
                    .Skip(offset)
                    .Take(Bound(limit))
                    .Select(row => Project(row, plan))
                    .ToArray()
            );
        }

        public Task<ProjectedReadRow?> SingleProjectedAsync(
            EntityReadPlan<ConformanceEntity> plan,
            CancellationToken token
        )
        {
            RowQuery(token);
            var row = Query(plan).SingleOrDefault();
            return Task.FromResult(row == null ? null : Project(row, plan));
        }
    }

    class ProjectedSeekSession(RecordingFixture fixture)
        : ProjectedSession(fixture),
            IEntityProjectedSeekReadSession<ConformanceEntity>
    {
        public Task<IReadOnlyList<ProjectedReadRow>> SeekProjectedAsync(
            EntitySeekPlan<ConformanceEntity> plan,
            int limit,
            CancellationToken token
        )
        {
            RowQuery(token);
            return Task.FromResult<IReadOnlyList<ProjectedReadRow>>(
                SeekRows(plan)
                    .Take(Bound(limit))
                    .Select(row => Project(row, plan.Read, plan))
                    .ToArray()
            );
        }
    }

    sealed class SnapshotSession(RecordingFixture fixture)
        : ProjectedSeekSession(fixture),
            IEntitySnapshotReadSession<ConformanceEntity>
    {
        public Task BeginSnapshotAsync(
            EntityReadPlan<ConformanceEntity> plan,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            Snapshot = Fixture.rows.Select(row => row.Clone()).ToArray();
            return Task.CompletedTask;
        }
    }

    RecordingFault Fault => fault;
}
