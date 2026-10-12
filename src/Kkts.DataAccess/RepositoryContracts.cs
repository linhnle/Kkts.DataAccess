using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace Kkts.DataAccess;

public interface IAuthorizedReadRepository<T>
    where T : class
{
    Task<ReadPage> ListAsync(ReadRequest request, CancellationToken cancellation = default);
    Task<long> CountAsync(ReadRequest request, CancellationToken cancellation = default);
    Task<ReadDetail> DetailAsync(
        JsonElement key,
        ReadRequest request,
        CancellationToken cancellation = default
    );
    IAsyncEnumerable<ExportBatch> ExportAsync(
        ReadRequest request,
        CancellationToken cancellation = default
    );
}

public interface IActivePolicySource
{
    Task<CapturedPolicy> CaptureAsync(
        string scope,
        string resource,
        CancellationToken cancellation = default
    );
}

/// <summary>Trusted infrastructure contract. Never expose this adapter to client request handlers.</summary>
public interface IEntityReadAdapter<T>
    where T : class
{
    ValueTask<IEntityReadSession<T>> OpenAsync(CancellationToken cancellation = default);
}

public interface IEntityReadSession<T> : IAsyncDisposable
    where T : class
{
    Task<long> CountAsync(EntityReadPlan<T> plan, CancellationToken cancellation);
    Task<IReadOnlyList<T>> PageAsync(
        EntityReadPlan<T> plan,
        int offset,
        int limit,
        CancellationToken cancellation
    );
    Task<T?> SingleAsync(EntityReadPlan<T> plan, CancellationToken cancellation);
}

public sealed record EntitySort(PropertyInfo Property, bool Descending);

/// <summary>A validated plan created only by the authorized repository.</summary>
public sealed class EntityReadPlan<T>
    where T : class
{
    public Expression<Func<T, bool>> Predicate { get; }
    public IReadOnlyList<EntitySort> Sort { get; }

    internal EntityReadPlan(Expression<Func<T, bool>> predicate, IEnumerable<EntitySort> sort)
    {
        Predicate = predicate;
        Sort = Array.AsReadOnly(sort.ToArray());
    }
}
