using System.Linq.Expressions;
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

    sealed class Session(DbContext db) : IEntityReadSession<T>
    {
        IQueryable<T> Query(EntityReadPlan<T> plan) =>
            db.Set<T>().AsNoTracking().Where(plan.Predicate);

        public Task<long> CountAsync(EntityReadPlan<T> plan, CancellationToken cancellation) =>
            Query(plan).LongCountAsync(cancellation);

        public Task<T?> SingleAsync(EntityReadPlan<T> plan, CancellationToken cancellation) =>
            Query(plan).SingleOrDefaultAsync(cancellation);

        public async Task<IReadOnlyList<T>> PageAsync(
            EntityReadPlan<T> plan,
            int offset,
            int limit,
            CancellationToken cancellation
        )
        {
            var query = Query(plan);
            for (var i = 0; i < plan.Sort.Count; i++)
            {
                var sort = plan.Sort[i];
                var parameter = Expression.Parameter(typeof(T), "e");
                var selector = Expression.Lambda(
                    Expression.Property(parameter, sort.Property),
                    parameter
                );
                var method =
                    (i == 0 ? "OrderBy" : "ThenBy") + (sort.Descending ? "Descending" : "");
                query = query.Provider.CreateQuery<T>(
                    Expression.Call(
                        typeof(Queryable),
                        method,
                        [typeof(T), sort.Property.PropertyType],
                        query.Expression,
                        Expression.Quote(selector)
                    )
                );
            }
            return await query.Skip(offset).Take(limit).ToListAsync(cancellation);
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
