using Kkts.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.EntityFrameworkCore;

public sealed class EfEntityReadAdapter<T> : IEntityReadAdapter<T>
    where T : class
{
    readonly Func<DbContext> factory;
    readonly string providerName;

    public EfEntityReadAdapter(Func<DbContext> factory, string providerName)
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider identity is required.", nameof(providerName));
        this.providerName = providerName;
    }

    /// <summary>Validate provider metadata at host registration without opening a connection.</summary>
    public static EfEntityReadAdapter<T> Register(Func<DbContext> factory, string providerName)
    {
        var adapter = new EfEntityReadAdapter<T>(factory, providerName);
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
            return new Session(db);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    sealed class Session(DbContext db)
        : IEntitySeekReadSession<T>,
            IEntityProjectedSeekReadSession<T>
    {
        IQueryable<T> Query(EntityReadPlan<T> plan) =>
            db.Set<T>().AsNoTracking().Where(plan.Predicate);

        public Task<long> CountAsync(EntityReadPlan<T> plan, CancellationToken cancellation) =>
            Query(plan).LongCountAsync(cancellation);

        public Task<T?> SingleAsync(EntityReadPlan<T> plan, CancellationToken cancellation) =>
            Query(plan).SingleOrDefaultAsync(cancellation);

        public async Task<ProjectedReadRow?> SingleProjectedAsync(
            EntityReadPlan<T> plan,
            CancellationToken cancellation
        )
        {
            var projection = new EfProjection<T>(db, plan);
            return await projection.SingleAsync(Query(plan), cancellation);
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
            return await projection.ReadAsync(query.Skip(offset).Take(limit), cancellation);
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
            return await projection.ReadAsync(query.Take(limit), cancellation);
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
            return await query.Skip(offset).Take(limit).ToListAsync(cancellation);
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
            return await query.Take(limit).ToListAsync(cancellation);
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
