using System.Data;
using System.Data.Common;
using System.Transactions;
using Kkts.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kkts.DataAccess.EntityFrameworkCore;

public delegate Task<EfSnapshotTransaction> EfSnapshotFactory(
    DbContext database,
    CancellationToken cancellation
);

public sealed class EfSnapshotTransaction
{
    public DbTransaction Transaction { get; }
    public Func<CancellationToken, Task>? VerifyAsync { get; }

    public EfSnapshotTransaction(
        DbTransaction transaction,
        Func<CancellationToken, Task>? verifyAsync = null
    )
    {
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        VerifyAsync = verifyAsync;
    }
}

public sealed class EfEntityReadAdapter<T> : IEntityReadAdapter<T>
    where T : class
{
    readonly Func<DbContext> factory;
    readonly string providerName;
    readonly EfSnapshotFactory? snapshotFactory;

    public EfEntityReadAdapter(
        Func<DbContext> factory,
        string providerName,
        EfSnapshotFactory? snapshotFactory = null
    )
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider identity is required.", nameof(providerName));
        this.providerName = providerName;
        this.snapshotFactory = snapshotFactory;
    }

    /// <summary>Validate provider metadata at host registration without opening a connection.</summary>
    public static EfEntityReadAdapter<T> Register(
        Func<DbContext> factory,
        string providerName,
        EfSnapshotFactory? snapshotFactory = null
    )
    {
        var adapter = new EfEntityReadAdapter<T>(factory, providerName, snapshotFactory);
        using var db = factory();
        if (db.Database.ProviderName != providerName)
            throw new InvalidOperationException(
                $"Entity provider mismatch: expected {providerName}, received {db.Database.ProviderName}."
            );
        return adapter;
    }

    public async ValueTask<IEntityReadSession<T>> OpenAsync(
        CancellationToken cancellation = default
    )
    {
        cancellation.ThrowIfCancellationRequested();
        var db = factory();
        try
        {
            if (db.Database.ProviderName != providerName)
                throw new InvalidOperationException(
                    $"Entity provider mismatch: expected {providerName}, received {db.Database.ProviderName}."
                );
            return new Session(db, snapshotFactory);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    sealed class Session(DbContext db, EfSnapshotFactory? snapshotFactory)
        : IEntitySeekReadSession<T>,
            IEntityProjectedSeekReadSession<T>,
            IEntitySnapshotReadSession<T>
    {
        bool initialized,
            readStarted;
        DbTransaction? nativeTransaction;
        IDbContextTransaction? transaction;
        DbConnection? snapshotConnection;
        EfSnapshotTransaction? snapshot;

        public async Task BeginSnapshotAsync(EntityReadPlan<T> plan, CancellationToken cancellation)
        {
            if (initialized || readStarted)
                throw new InvalidOperationException(
                    "Snapshot initialization requires a fresh session."
                );
            initialized = true;
            if (snapshotFactory == null)
                throw new NotSupportedException(
                    "Register a provider-owned snapshot factory for snapshot exports."
                );
            if (
                Transaction.Current != null
                || db.Database.CurrentTransaction != null
                || db.Database.GetEnlistedTransaction() != null
                || db.Database.GetDbConnection().State != ConnectionState.Closed
            )
                throw new InvalidOperationException(
                    "Snapshot exports cannot join host-owned connections or transactions."
                );
            if (db.Database.CreateExecutionStrategy().RetriesOnFailure)
                throw new NotSupportedException(
                    "Snapshot exports do not support automatic execution retries."
                );
            ValidateSnapshotModel(db, plan);
            cancellation.ThrowIfCancellationRequested();
            await db.Database.OpenConnectionAsync(cancellation);
            snapshotConnection = db.Database.GetDbConnection();
            snapshot = await snapshotFactory(db, cancellation);
            nativeTransaction = snapshot.Transaction;
            if (nativeTransaction.Connection != snapshotConnection)
                throw new InvalidOperationException(
                    "Snapshot transaction must own the entity connection."
                );
            transaction =
                await db.Database.UseTransactionAsync(nativeTransaction, cancellation)
                ?? throw new InvalidOperationException("Snapshot transaction enlistment failed.");
        }

        static void ValidateSnapshotModel(DbContext db, EntityReadPlan<T> plan)
        {
            var entity =
                db.Model.FindEntityType(typeof(T))
                ?? throw new NotSupportedException("Snapshot entity is not mapped.");
            var tableName = entity.GetTableName();
            var table =
                tableName == null
                    ? null
                    : db.Model.GetRelationalModel().FindTable(tableName, entity.GetSchema());
            if (
                table == null
                || entity.GetViewName() != null
                || entity.GetSqlQuery() != null
                || entity.GetDeclaredQueryFilters().Any()
                || entity.BaseType != null
                || entity.GetDerivedTypes().Any()
                || entity.IsOwned()
                || entity.FindPrimaryKey() == null
                || entity.GetTableMappings().Count() != 1
                || table.EntityTypeMappings.Count() != 1
            )
                throw new NotSupportedException(
                    "Snapshots require an ordinary single-table resource without query filters or shared mappings."
                );
            _ = new EfProjection<T>(db, plan);
            EfSeekQuery<T>.ValidateOrder(db, plan);
        }

        IQueryable<T> Query(EntityReadPlan<T> plan)
        {
            readStarted = true;
            if (
                initialized
                && (
                    transaction == null
                    || nativeTransaction?.Connection != snapshotConnection
                    || snapshotConnection?.State != ConnectionState.Open
                    || db.Database.GetDbConnection() != snapshotConnection
                    || db.Database.CurrentTransaction != transaction
                )
            )
                throw new InvalidOperationException(
                    "Export snapshot was lost; it cannot be reopened or replayed."
                );
            return db.Set<T>().AsNoTracking().Where(plan.Predicate);
        }

        async Task<TResult> Read<TResult>(
            Func<Task<TResult>> operation,
            CancellationToken cancellation
        )
        {
            if (snapshot?.VerifyAsync is { } verifyBefore)
                await verifyBefore(cancellation);
            var result = await operation();
            if (snapshot?.VerifyAsync is { } verifyAfter)
                await verifyAfter(cancellation);
            return result;
        }

        public Task<long> CountAsync(EntityReadPlan<T> plan, CancellationToken cancellation) =>
            Read(() => Query(plan).LongCountAsync(cancellation), cancellation);

        public Task<T?> SingleAsync(EntityReadPlan<T> plan, CancellationToken cancellation) =>
            Read(() => Query(plan).SingleOrDefaultAsync(cancellation), cancellation);

        public async Task<ProjectedReadRow?> SingleProjectedAsync(
            EntityReadPlan<T> plan,
            CancellationToken cancellation
        )
        {
            var projection = new EfProjection<T>(db, plan);
            return await Read(
                () => projection.SingleAsync(Query(plan), cancellation),
                cancellation
            );
        }

        public async Task<IReadOnlyList<ProjectedReadRow>> PageProjectedAsync(
            EntityReadPlan<T> plan,
            int offset,
            int limit,
            CancellationToken cancellation
        )
        {
            var projection = new EfProjection<T>(db, plan);
            var query = Query(plan)
                .OrderBy(
                    plan.Sort.Select(sort => new OrderByInfo
                    {
                        Property = sort.Property.Name,
                        Descending = sort.Descending,
                    })
                );
            return await Read(
                () => projection.ReadAsync(query.Skip(offset).Take(limit), cancellation),
                cancellation
            );
        }

        public async Task<IReadOnlyList<ProjectedReadRow>> SeekProjectedAsync(
            EntitySeekPlan<T> plan,
            int limit,
            CancellationToken cancellation
        )
        {
            if (limit < 1)
                throw new ArgumentOutOfRangeException(nameof(limit));
            var projection = new EfProjection<T>(db, plan.Read, plan);
            var query = EfSeekQuery<T>.Apply(db, Query(plan.Read), plan);
            return await Read(
                () => projection.ReadAsync(query.Take(limit), cancellation),
                cancellation
            );
        }

        public async Task<IReadOnlyList<T>> PageAsync(
            EntityReadPlan<T> plan,
            int offset,
            int limit,
            CancellationToken cancellation
        )
        {
            var query = Query(plan);
            query = query.OrderBy(
                plan.Sort.Select(sort => new OrderByInfo
                {
                    Property = sort.Property.Name,
                    Descending = sort.Descending,
                })
            );
            return await Read(
                () => query.Skip(offset).Take(limit).ToListAsync(cancellation),
                cancellation
            );
        }

        public async Task<IReadOnlyList<T>> SeekAsync(
            EntitySeekPlan<T> plan,
            int limit,
            CancellationToken cancellation
        )
        {
            if (limit < 1)
                throw new ArgumentOutOfRangeException(nameof(limit));
            var query = Query(plan.Read);
            query = EfSeekQuery<T>.Apply(db, query, plan);
            return await Read(() => query.Take(limit).ToListAsync(cancellation), cancellation);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (transaction != null)
                    await transaction.DisposeAsync();
            }
            finally
            {
                try
                {
                    if (nativeTransaction != null)
                        await nativeTransaction.DisposeAsync();
                }
                finally
                {
                    await db.DisposeAsync();
                }
            }
        }
    }
}
