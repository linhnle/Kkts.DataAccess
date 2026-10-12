using System.Collections.Frozen;

namespace Kkts.DataAccess;

public sealed class TrustedContext
{
    public FrozenSet<string> Roles { get; }
    public FrozenSet<string> Groups { get; }
    public FrozenDictionary<string, object?> Values { get; }

    // Host code constructs this snapshot from authenticated server identity. Read requests contain no context input.
    public TrustedContext(
        IEnumerable<string> roles,
        IEnumerable<string> groups,
        IReadOnlyDictionary<string, object?> values
    )
    {
        Roles = roles.ToFrozenSet(StringComparer.Ordinal);
        Groups = groups.ToFrozenSet(StringComparer.Ordinal);
        Values = values.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

public sealed class ContextRegistry
{
    public FrozenDictionary<string, Type> Keys { get; }

    public ContextRegistry(IReadOnlyDictionary<string, Type> keys)
    {
        if (
            keys.Any(k =>
                string.IsNullOrWhiteSpace(k.Key)
                || !LiteralConverter.IsSupported(Nullable.GetUnderlyingType(k.Value) ?? k.Value)
            )
        )
            throw new ArgumentException("Context keys must have supported scalar types.");
        Keys = keys.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public object? Resolve(TrustedContext context, string key)
    {
        if (
            !Keys.TryGetValue(key, out var type)
            || !context.Values.TryGetValue(key, out var value)
            || (value == null && type.IsValueType && Nullable.GetUnderlyingType(type) == null)
            || (
                value != null && !(Nullable.GetUnderlyingType(type) ?? type).IsInstanceOfType(value)
            )
        )
            throw new AuthorizationException(
                $"Missing or incorrectly typed trusted context '{key}'."
            );
        return value;
    }
}
