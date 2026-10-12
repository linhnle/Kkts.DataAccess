using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;

namespace Kkts.DataAccess;

public enum FieldOperation
{
    Select,
    Filter,
    Sort,
    Aggregate,
    Write,
}

public sealed record AdministrationContext(string Actor, string Scope, bool IsAdministrator)
{
    public void Demand(string scope)
    {
        if (!IsAdministrator || string.IsNullOrWhiteSpace(Actor) || Scope != scope)
            throw new AuthorizationException("Administrator access to this scope is required.");
    }
}

public sealed record FieldMetadata(
    string Name,
    string Type,
    bool Nullable,
    IReadOnlyList<string> Operators,
    IReadOnlyList<FieldOperation> Eligibility
);

public sealed record SchemaMetadata(
    string Resource,
    string Version,
    IReadOnlyList<string> Actions,
    IReadOnlyList<FieldMetadata> Fields
);

public sealed class ResourceField
{
    public string Name { get; }
    public PropertyInfo Property { get; }
    public Type Type => Property.PropertyType;
    public bool Nullable { get; }
    public FrozenSet<string> Operators { get; }
    public FrozenSet<FieldOperation> Eligibility { get; }

    internal ResourceField(
        string name,
        PropertyInfo property,
        IEnumerable<FieldOperation> eligibility,
        IEnumerable<string> operators
    )
    {
        Name = name;
        Property = property;
        Nullable =
            System.Nullable.GetUnderlyingType(Type) != null
            || (
                !Type.IsValueType
                && new NullabilityInfoContext().Create(property).ReadState
                    != NullabilityState.NotNull
            );
        Operators = operators.ToFrozenSet(StringComparer.Ordinal);
        Eligibility = eligibility.ToFrozenSet();
    }
}

public sealed class ResourceSchema<T>
    where T : class
{
    public string Resource { get; }
    public string Version { get; }
    public FrozenDictionary<string, ResourceField> Fields { get; }
    public FrozenSet<string> Actions { get; }
    public ResourceField StableKey { get; }

    public ResourceSchema(
        string resource,
        string version,
        IEnumerable<ResourceField> fields,
        string stableKey,
        IEnumerable<string>? actions = null
    )
    {
        if (string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Resource and version are required.");
        Resource = resource;
        Version = version;
        var values = fields.ToArray();
        if (
            values.Length == 0
            || values.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != values.Length
        )
            throw new ArgumentException("Public field names must be unique and nonempty.");
        if (
            values.Any(f =>
                f.Property.DeclaringType == null
                || !f.Property.DeclaringType.IsAssignableFrom(typeof(T))
            )
        )
            throw new ArgumentException("Field belongs to a different entity type.");
        Fields = values.ToFrozenDictionary(f => f.Name, StringComparer.Ordinal);
        Actions = (actions ?? ["Read"]).ToFrozenSet(StringComparer.Ordinal);
        if (Actions.Count == 0 || Actions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Actions are required.");
        StableKey = Resolve(stableKey);
        if (StableKey.Nullable || StableKey.Type == typeof(bool))
            throw new ArgumentException("Stable key must be nonnullable and orderable.");
    }

    public ResourceField Resolve(string name) =>
        name != null && Fields.TryGetValue(name, out var field)
            ? field
            : throw new InvalidRequestException(
                diagnostics: [new(DataAccessErrorCode.UnknownField, "", "Unknown public field.")]
            );

    public SchemaMetadata Metadata(AdministrationContext admin, string scope)
    {
        admin.Demand(scope);
        return new(
            Resource,
            Version,
            Array.AsReadOnly(Actions.Order().ToArray()),
            Array.AsReadOnly(
                Fields
                    .Values.OrderBy(f => f.Name)
                    .Select(f => new FieldMetadata(
                        f.Name,
                        (System.Nullable.GetUnderlyingType(f.Type) ?? f.Type).Name,
                        f.Nullable,
                        Array.AsReadOnly(f.Operators.Order().ToArray()),
                        Array.AsReadOnly(f.Eligibility.Order().ToArray())
                    ))
                    .ToArray()
            )
        );
    }

    public static ResourceField Field<TValue>(
        string name,
        Expression<Func<T, TValue>> binding,
        IEnumerable<FieldOperation> eligibility,
        IEnumerable<string>? operators = null
    )
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || name.Contains('.')
            || binding.Body
                is not MemberExpression
                {
                    Member: PropertyInfo property,
                    Expression: ParameterExpression
                }
            || property.GetMethod is not { IsPublic: true, IsStatic: false }
            || property.GetIndexParameters().Length != 0
        )
            throw new ArgumentException($"Invalid direct public property binding for '{name}'.");
        var type =
            System.Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (!LiteralConverter.IsSupported(type))
            throw new ArgumentException($"Unsupported scalar binding '{name}'.");
        var permitted = OperatorCatalog.For(type);
        var requested = (operators ?? permitted).ToArray();
        if (requested.Length == 0 || requested.Any(op => !permitted.Contains(op)))
            throw new ArgumentException($"Invalid operators for '{name}'.");
        var ops = eligibility.ToArray();
        if (ops.Any(op => !Enum.IsDefined(op)))
            throw new ArgumentException($"Invalid eligibility for '{name}'.");
        return new(name, property, ops, requested);
    }
}

public static class OperatorCatalog
{
    public static FrozenSet<string> For(Type type)
    {
        type = System.Nullable.GetUnderlyingType(type) ?? type;
        string[] equality = ["eq", "ne", "isNull", "isNotNull", "in"];
        if (type == typeof(string))
            return equality
                .Concat(["contains", "startsWith", "endsWith"])
                .ToFrozenSet(StringComparer.Ordinal);
        if (type == typeof(bool) || type == typeof(Guid) || type.IsEnum)
            return equality.ToFrozenSet(StringComparer.Ordinal);
        return equality.Concat(["lt", "le", "gt", "ge"]).ToFrozenSet(StringComparer.Ordinal);
    }
}
