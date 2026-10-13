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

public sealed class EfEntityReadAdapter<T>
    : IEntityReadAdapter<T>,
        IEntityReadRegistrationValidator<T>,
        IReadProviderCapabilitySource
    where T : class
{
    readonly Func<CancellationToken, ValueTask<DbContext>> factory;
    readonly string providerName;
    readonly EfSnapshotFactory? snapshotFactory;
    ResourceSchema<T>? resourceSchema;
    public ReadOperations Operations { get; private set; } =
        ReadOperations.All | ReadOperations.Snapshot;
    public IReadProviderCapabilities? Capabilities { get; private set; }

    public static EfEntityReadAdapter<T> Create<TContext>(
        ResourceSchema<T> schema,
        IDbContextFactory<TContext> factory,
        string providerName,
        ReadOperations operations = ReadOperations.All,
        EfSnapshotFactory? snapshotFactory = null
    )
        where TContext : DbContext =>
        Configure(Create(factory, providerName, snapshotFactory), schema, operations);

    static EfEntityReadAdapter<T> Configure(
        EfEntityReadAdapter<T> adapter,
        ResourceSchema<T> schema,
        ReadOperations operations
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (
            operations == ReadOperations.None
            || (operations & ~(ReadOperations.All | ReadOperations.Snapshot)) != 0
            || operations.HasFlag(ReadOperations.Snapshot)
                && !operations.HasFlag(ReadOperations.Export)
        )
            throw new ArgumentException("Invalid declared read operations.", nameof(operations));
        adapter.resourceSchema = schema;
        adapter.Operations = operations;
        return adapter;
    }

    void ValidateResource(DbContext db, ResourceSchema<T> schema)
    {
        var profile = EfModelCapabilities<T>.Profile(
            db,
            schema,
            Operations,
            snapshotFactory != null
        );
        var errors = profile.ValidateRegistration(schema);
        if (errors.Count != 0)
            throw new ProviderUnsupportedException(
                "The model does not support the declared resource operations.",
                errors
            );
        Capabilities = profile;
    }

    public EfEntityReadAdapter(
        Func<DbContext> factory,
        string providerName,
        EfSnapshotFactory? snapshotFactory = null
    )
        : this(OwnedFactory(factory), providerName, snapshotFactory) { }

    EfEntityReadAdapter(
        Func<CancellationToken, ValueTask<DbContext>> factory,
        string providerName,
        EfSnapshotFactory? snapshotFactory
    )
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider identity is required.", nameof(providerName));
        this.providerName = providerName;
        this.snapshotFactory = snapshotFactory;
    }

    static Func<CancellationToken, ValueTask<DbContext>> OwnedFactory(Func<DbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return token =>
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(factory());
        };
    }

    public static EfEntityReadAdapter<T> Create<TContext>(
        IDbContextFactory<TContext> factory,
        string providerName,
        EfSnapshotFactory? snapshotFactory = null
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new(
            async token => await factory.CreateDbContextAsync(token),
            providerName,
            snapshotFactory
        );
    }

    public static async Task<EfEntityReadAdapter<T>> RegisterAsync<TContext>(
        ResourceSchema<T> schema,
        IDbContextFactory<TContext> factory,
        string providerName,
        ReadOperations operations = ReadOperations.All,
        EfSnapshotFactory? snapshotFactory = null,
        CancellationToken cancellation = default
    )
        where TContext : DbContext
    {
        var adapter = Create(schema, factory, providerName, operations, snapshotFactory);
        await using var db = await adapter.factory(cancellation);
        cancellation.ThrowIfCancellationRequested();
        adapter.ValidateProvider(db);
        if (db.Model.FindEntityType(typeof(T)) == null)
            throw new InvalidOperationException("The resource entity is not mapped.");
        adapter.ValidateResource(db, schema);
        return adapter;
    }

    public async ValueTask ValidateAsync(
        ResourceSchema<T> schema,
        CancellationToken cancellation = default
    )
    {
        cancellation.ThrowIfCancellationRequested();
        await using var db = await factory(cancellation);
        cancellation.ThrowIfCancellationRequested();
        ValidateProvider(db);
        var model =
            db.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException("The resource entity is not mapped.");
        if (resourceSchema != null)
        {
            if (resourceSchema != schema)
                throw new InvalidOperationException(
                    "The adapter was configured with a different resource schema."
                );
            ValidateResource(db, schema);
        }
        else
        {
            foreach (var field in schema.Fields.Values)
                EfModelCapabilities<T>.Demand(EfModelCapabilities<T>.Mapping(db, field.Property));
        }
    }

    void ValidateProvider(DbContext db)
    {
        if (db.Database.ProviderName != providerName)
            throw new InvalidOperationException(
                "The entity provider does not match the registered provider."
            );
    }

    /// <summary>Validate provider metadata at host registration without opening a connection.</summary>
    public static EfEntityReadAdapter<T> Register(
        ResourceSchema<T> schema,
        Func<DbContext> factory,
        string providerName,
        ReadOperations operations = ReadOperations.All,
        EfSnapshotFactory? snapshotFactory = null
    )
    {
        var adapter = Configure(
            new EfEntityReadAdapter<T>(factory, providerName, snapshotFactory),
            schema,
            operations
        );
        using var db = factory();
        if (db.Database.ProviderName != providerName)
            throw new InvalidOperationException(
                $"Entity provider mismatch: expected {providerName}, received {db.Database.ProviderName}."
            );
        adapter.ValidateResource(db, schema);
        return adapter;
    }

    public async ValueTask<IEntityReadSession<T>> OpenAsync(
        CancellationToken cancellation = default
    )
    {
        cancellation.ThrowIfCancellationRequested();
        var db = await factory(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            ValidateProvider(db);
            if (resourceSchema != null)
                ValidateResource(db, resourceSchema);
            return new Session(db, snapshotFactory, resourceSchema, Operations, Capabilities);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    sealed class Session(
        DbContext db,
        EfSnapshotFactory? snapshotFactory,
        ResourceSchema<T>? schema,
        ReadOperations operations,
        IReadProviderCapabilities? capabilities
    )
        : IEntitySeekReadSession<T>,
            IEntityProjectedSeekReadSession<T>,
            IEntitySnapshotReadSession<T>,
            IEntityReadPlanValidator<T>,
            IReadProviderCapabilitySource
    {
        public ReadOperations Operations => operations;
        public IReadProviderCapabilities? Capabilities => capabilities;

        public ValueTask ValidatePlanAsync(
            EntityReadPlan<T> plan,
            ReadOperations operation,
            EntitySeekPlan<T>? seek = null,
            IReadOnlyList<IReadProviderCapabilities>? profiles = null,
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            if (!operations.HasFlag(operation))
                throw new ProviderUnsupportedException(
                    "The requested operation was disabled at registration."
                );
            if (schema != null && capabilities != null && profiles is { Count: > 0 })
            {
                var expected = profiles
                    .Where(profile => profile.Provider == capabilities.Provider)
                    .ToArray();
                if (
                    expected.Length != 0
                    && expected.All(profile => profile.Identity != capabilities.Identity)
                )
                    throw new ProviderUnsupportedException(
                        "The runtime model differs from its validated capability profile."
                    );
            }
            EfModelCapabilities<T>.ValidatePlan(db, plan, operation, seek);
            return ValueTask.CompletedTask;
        }

        bool initialized,
            readStarted;
        DbTransaction? nativeTransaction;
        IDbContextTransaction? transaction;
        DbConnection? snapshotConnection;
        EfSnapshotTransaction? snapshot;

        public async Task BeginSnapshotAsync(EntityReadPlan<T> plan, CancellationToken cancellation)
        {
            if (!operations.HasFlag(ReadOperations.Snapshot))
                throw new ProviderUnsupportedException(
                    "Snapshot export was disabled at registration."
                );
            if (initialized || readStarted)
                throw new InvalidOperationException(
                    "Snapshot initialization requires a fresh session."
                );
            initialized = true;
            if (snapshotFactory == null)
                throw new ProviderUnsupportedException(
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
                throw new ProviderUnsupportedException(
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
            EfModelCapabilities<T>.Demand(EfModelCapabilities<T>.SnapshotShape(db, true));
            _ = new EfProjection<T>(db, plan);
            EfSeekQuery<T>.ValidateOrder(db, plan);
        }

        IQueryable<T> Query(EntityReadPlan<T> plan)
        {
            EfModelCapabilities<T>.ValidatePlan(db, plan, ReadOperations.Count, null);
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
