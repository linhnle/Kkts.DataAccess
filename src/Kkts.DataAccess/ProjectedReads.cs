using System.Reflection;
using System.Text.Json.Serialization;

namespace Kkts.DataAccess;

public enum ReadProjectionMode
{
    Required,
    AllowEntityMaterialization,
}

public sealed record EntityOutputBinding(string Alias, PropertyInfo Property);

public interface IEntityProjectedReadSession<T> : IEntityReadSession<T>
    where T : class
{
    Task<IReadOnlyList<ProjectedReadRow>> PageProjectedAsync(
        EntityReadPlan<T> plan,
        int offset,
        int limit,
        CancellationToken cancellation
    );
    Task<ProjectedReadRow?> SingleProjectedAsync(
        EntityReadPlan<T> plan,
        CancellationToken cancellation
    );
}

public interface IEntityProjectedSeekReadSession<T> : IEntityProjectedReadSession<T>
    where T : class
{
    Task<IReadOnlyList<ProjectedReadRow>> SeekProjectedAsync(
        EntitySeekPlan<T> plan,
        int limit,
        CancellationToken cancellation
    );
}

/// <summary>Trusted scalar snapshots, never a public DTO or client projection expression.</summary>
public sealed class ProjectedReadRow
{
    public IReadOnlyList<object?> OutputValues { get; }

    [JsonIgnore]
    public IReadOnlyList<object?> ContinuationValues { get; }

    public ProjectedReadRow(
        IEnumerable<object?> outputValues,
        IEnumerable<object?>? continuationValues = null
    )
    {
        ArgumentNullException.ThrowIfNull(outputValues);
        OutputValues = Array.AsReadOnly(outputValues.Select(Snapshot).ToArray());
        ContinuationValues = Array.AsReadOnly(
            (continuationValues ?? []).Select(Snapshot).ToArray()
        );
    }

    internal static object? Snapshot(object? value) => value is Array array ? array.Clone() : value;

    internal void Validate<T>(EntityReadPlan<T> plan, EntitySeekPlan<T>? seek = null)
        where T : class
    {
        ValidateValues(
            OutputValues,
            plan.OutputBindings.Select(binding => binding.Property).ToArray()
        );
        ValidateValues(ContinuationValues, seek?.ContinuationBindings ?? []);
    }

    internal static void ValidateValues(
        IReadOnlyList<object?> values,
        IReadOnlyList<PropertyInfo> properties
    )
    {
        if (values.Count != properties.Count)
            throw new InvalidOperationException(
                "Projected scalar cardinality does not match the trusted bindings."
            );
        for (var i = 0; i < values.Count; i++)
        {
            var property = properties[i];
            var type = property.PropertyType;
            var scalar = Nullable.GetUnderlyingType(type) ?? type;
            if (!LiteralConverter.IsSupported(scalar))
                throw new InvalidOperationException("Projected binding is not a supported scalar.");
            if (values[i] == null)
            {
                if (
                    type.IsValueType
                        ? Nullable.GetUnderlyingType(type) == null
                        : new NullabilityInfoContext().Create(property).ReadState
                            == NullabilityState.NotNull
                )
                    throw new InvalidOperationException(
                        "Null is incompatible with the projected scalar binding."
                    );
            }
            else if (values[i]!.GetType() != scalar)
                throw new InvalidOperationException(
                    "Projected scalar type does not match the trusted binding."
                );
        }
    }
}
