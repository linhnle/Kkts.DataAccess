using System.Linq.Expressions;
using System.Reflection;

namespace Kkts.DataAccess;

public interface IAuthorizedReadRepository<T>
    where T : class
{
    Task<ReadPage> ListAsync(ListReadRequest request, CancellationToken cancellation = default);
    Task<CursorReadPage> ListCursorAsync(
        CursorListReadRequest request,
        CancellationToken cancellation = default
    );
    Task<ReadCount> CountAsync(CountReadRequest request, CancellationToken cancellation = default);
    Task<ReadDetail> DetailAsync(
        DetailReadRequest request,
        CancellationToken cancellation = default
    );
    IAsyncEnumerable<ExportBatch> ExportAsync(
        ExportReadRequest request,
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

public interface IEntitySeekReadSession<T> : IEntityReadSession<T>
    where T : class
{
    Task<IReadOnlyList<T>> SeekAsync(
        EntitySeekPlan<T> plan,
        int limit,
        CancellationToken cancellation
    );
}

/// <summary>Trusted authorized query and an immutable, typed continuation tuple.</summary>
public sealed class EntitySeekPlan<T>
    where T : class
{
    public EntityReadPlan<T> Read { get; }
    public IReadOnlyList<object?>? Boundary { get; }
    public IReadOnlyList<PropertyInfo> ContinuationBindings { get; }

    internal EntitySeekPlan(EntityReadPlan<T> read, IEnumerable<object?>? boundary)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (read.Sort.Count == 0)
            throw new ArgumentException("Seek ordering is required.", nameof(read));
        foreach (var sort in read.Sort)
        {
            var property = sort.Property;
            if (
                property.DeclaringType == null
                || !property.DeclaringType.IsAssignableFrom(typeof(T))
                || property.GetMethod is not { IsPublic: true, IsStatic: false }
                || property.GetIndexParameters().Length != 0
                || !LiteralConverter.IsSupported(
                    Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType
                )
            )
                throw new ArgumentException(
                    "Seek requires direct scalar property bindings.",
                    nameof(read)
                );
        }
        Read = read;
        ContinuationBindings = Array.AsReadOnly(read.Sort.Select(sort => sort.Property).ToArray());
        if (boundary == null)
            return;
        var values = boundary.Select(ProjectedReadRow.Snapshot).ToArray();
        if (values.Length != read.Sort.Count)
            throw new ArgumentException(
                "Seek boundary cardinality does not match ordering.",
                nameof(boundary)
            );
        for (var i = 0; i < values.Length; i++)
        {
            var property = read.Sort[i].Property;
            var type = property.PropertyType;
            var nullable = Nullable.GetUnderlyingType(type);
            if (values[i] == null)
            {
                if (
                    type.IsValueType
                        ? nullable == null
                        : new NullabilityInfoContext().Create(property).ReadState
                            == NullabilityState.NotNull
                )
                    throw new ArgumentException(
                        "Null is incompatible with the seek property.",
                        nameof(boundary)
                    );
            }
            else if (!(nullable ?? type).IsInstanceOfType(values[i]))
                throw new ArgumentException(
                    "Seek boundary scalar type does not match its property.",
                    nameof(boundary)
                );
        }
        Boundary = Array.AsReadOnly(values);
    }
}

/// <summary>A validated plan created only by the authorized repository.</summary>
public sealed class EntityReadPlan<T>
    where T : class
{
    public Expression<Func<T, bool>> Predicate { get; }
    public IReadOnlyList<EntitySort> Sort { get; }
    public IReadOnlyList<EntityOutputBinding> OutputBindings { get; }

    internal EntityReadPlan(
        Expression<Func<T, bool>> predicate,
        IEnumerable<EntitySort> sort,
        IEnumerable<EntityOutputBinding>? outputBindings = null
    )
    {
        Predicate = predicate;
        Sort = Array.AsReadOnly(sort.ToArray());
        OutputBindings = Array.AsReadOnly((outputBindings ?? []).ToArray());
    }
}
