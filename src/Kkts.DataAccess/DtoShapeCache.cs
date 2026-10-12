using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Kkts.Dynamic;

namespace Kkts.DataAccess;

internal sealed class DtoShapeKey : IEquatable<DtoShapeKey>
{
    readonly record struct Binding(string PublicName, PropertyInfo Property);

    readonly Type entityType;
    readonly string resource;
    readonly string version;
    readonly Binding[] bindings;
    readonly int hashCode;

    DtoShapeKey(Type entityType, string resource, string version, Binding[] bindings)
    {
        this.entityType = entityType;
        this.resource = resource;
        this.version = version;
        this.bindings = bindings;
        var hash = new HashCode();
        hash.Add(entityType);
        hash.Add(resource, StringComparer.Ordinal);
        hash.Add(version, StringComparer.Ordinal);
        foreach (var binding in bindings)
            hash.Add(binding);
        hashCode = hash.ToHashCode();
    }

    internal static DtoShapeKey Create<T>(ResourceSchema<T> schema, IReadOnlyList<string> selection)
        where T : class
    {
        var names = selection.ToArray();
        var bindings = new Binding[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var field = schema.Resolve(names[i]);
            bindings[i] = new(field.Name, field.Property);
        }
        return new(typeof(T), schema.Resource, schema.Version, bindings);
    }

    public bool Equals(DtoShapeKey? other) =>
        other != null
        && entityType == other.entityType
        && resource == other.resource
        && version == other.version
        && bindings.AsSpan().SequenceEqual(other.bindings);

    public override bool Equals(object? obj) => obj is DtoShapeKey other && Equals(other);

    public override int GetHashCode() => hashCode;

    internal DtoShape Build(DynamicAssembly assembly)
    {
        var shape = assembly.DeclareClass(
            "AuthorizedDto",
            entityType,
            bindings.Select(binding => new PropertyBinding
            {
                EntityProperty = binding.Property.Name,
                DtoProperty = binding.PublicName,
                Mode = BindingMode.OneWayToDto,
            })
        );
        assembly.Build();
        return new(shape.GetBuiltType());
    }
}

internal sealed class DtoShape(Type type)
{
    internal Type Type { get; } = type;

    // A distinct resolver prevents equivalent options from sharing a global metadata cache.
    internal JsonSerializerOptions Options { get; } =
        new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    internal JsonElement Map(object entity)
    {
        // The public mapper ignores null, unlike Class.CreateDto.
        ArgumentNullException.ThrowIfNull(entity);
        var dto =
            Activator.CreateInstance(Type)
            ?? throw new InvalidOperationException($"Cannot construct DTO type '{Type}'.");
        Mapper.MapFromEntityToDto(dto, entity);
        return JsonSerializer.SerializeToElement(dto, Type, Options);
    }
}

internal sealed class DtoShapeCache
{
    internal const int DefaultCapacity = 128;
    readonly int capacity;
    readonly Func<DynamicAssembly> createAssembly;
    readonly object gate = new();
    readonly Dictionary<DtoShapeKey, DtoShape> entries = new();
    readonly Queue<DtoShapeKey> insertionOrder = new();
    int buildCount;

    internal DtoShapeCache(
        int capacity = DefaultCapacity,
        Func<DynamicAssembly>? createAssembly = null
    )
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        this.createAssembly = createAssembly ?? (() => new DynamicAssembly());
    }

    internal int Count
    {
        get
        {
            lock (gate)
                return entries.Count;
        }
    }

    internal int BuildCount
    {
        get
        {
            lock (gate)
                return buildCount;
        }
    }

    internal DtoShape GetOrBuild(DtoShapeKey key)
    {
        lock (gate)
        {
            if (entries.TryGetValue(key, out var existing))
                return existing;
            DtoShape shape;
            using (var assembly = createAssembly())
            {
                buildCount++;
                shape = key.Build(assembly);
            }
            if (entries.Count == capacity)
                entries.Remove(insertionOrder.Dequeue());
            entries.Add(key, shape);
            insertionOrder.Enqueue(key);
            return shape;
        }
    }
}
