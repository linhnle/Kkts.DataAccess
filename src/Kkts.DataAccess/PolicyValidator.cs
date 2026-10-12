using System.Text;
using System.Text.Json;

namespace Kkts.DataAccess;

public sealed class PolicyValidator<T>(
    ResourceSchema<T> schema,
    ContextRegistry context,
    PolicyLimits? configuredLimits = null
)
    where T : class
{
    readonly PolicyLimits limits = configuredLimits ?? new();
    readonly ExpressionsAdapter<T> expressions = new(schema, context, configuredLimits ?? new());

    public IReadOnlyList<Diagnostic> Validate(PolicyDefinition policy)
    {
        var errors = new List<Diagnostic>();
        var nodes = 0;
        if (policy.Resource != schema.Resource)
            errors.Add(new("resource", "Resource mismatch."));
        if (policy.SchemaVersion != schema.Version)
            errors.Add(new("schemaVersion", "Schema version mismatch."));
        if (policy.Rules == null || policy.FieldEntries == null)
        {
            errors.Add(new("$", "Rule and field arrays are required."));
            return errors;
        }
        if (policy.Rules.Length + policy.FieldEntries.Length > limits.MaxEntries)
            errors.Add(new("$", "Too many policy entries."));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Header(
            string id,
            string resource,
            string action,
            Effect effect,
            Subject subject,
            string path
        )
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                errors.Add(new(path + ".id", "Unique entry id required."));
            if (resource != schema.Resource)
                errors.Add(new(path + ".resource", "Resource mismatch."));
            if (!schema.Actions.Contains(action))
                errors.Add(new(path + ".action", "Unsupported action."));
            if (!Enum.IsDefined(effect))
                errors.Add(new(path + ".effect", "Unsupported effect."));
            if (subject == null || subject.Roles == null || subject.Groups == null)
                errors.Add(new(path + ".subject", "Subject arrays required."));
            else
            {
                if (
                    subject.Roles.Length + subject.Groups.Length > limits.MaxCollection
                    || subject.Roles.Concat(subject.Groups).Any(string.IsNullOrWhiteSpace)
                )
                    errors.Add(
                        new(path + ".subject", "Invalid subject names or collection limit.")
                    );
                if (subject.ContextCondition != null)
                    Node(subject.ContextCondition, path + ".subject.contextCondition", 1, true);
            }
        }
        void Node(Condition c, string path, int depth, bool subject)
        {
            if (c == null)
            {
                errors.Add(new(path, "Node required."));
                return;
            }
            if (++nodes > limits.MaxNodes || depth > limits.MaxDepth)
            {
                errors.Add(new(path, "Condition complexity limit exceeded."));
                return;
            }
            var forms =
                (c.All != null ? 1 : 0)
                + (c.Any != null ? 1 : 0)
                + (c.Not != null ? 1 : 0)
                + (c.Field != null ? 1 : 0);
            if (forms != 1)
            {
                errors.Add(new(path, "Exactly one node form required."));
                return;
            }
            var children = c.All ?? c.Any;
            if (children != null || c.Not != null)
            {
                if (
                    c.Operator != null
                    || c.Literal.ValueKind != JsonValueKind.Undefined
                    || c.Context != null
                )
                    errors.Add(new(path, "Group cannot contain leaf properties."));
                if (children != null)
                {
                    if (children.Length == 0 || children.Length > limits.MaxCollection)
                        errors.Add(new(path, "Invalid group size."));
                    for (var i = 0; i < Math.Min(children.Length, limits.MaxNodes); i++)
                        Node(children[i], path + $"[{i}]", depth + 1, subject);
                }
                else
                    Node(c.Not!, path + ".not", depth + 1, subject);
                return;
            }
            try
            {
                Type type;
                IReadOnlySet<string> operators;
                if (subject)
                {
                    if (!context.Keys.TryGetValue(c.Field!, out type!))
                        throw new AuthorizationException("Unknown subject context field.");
                    operators = OperatorCatalog.For(type);
                }
                else
                {
                    var f = schema.Resolve(c.Field!);
                    type = f.Type;
                    operators = f.Operators;
                }
                if (c.Operator == null || !operators.Contains(c.Operator))
                    throw new AuthorizationException("Unsupported operator for field type.");
                if (c.Operator is "isNull" or "isNotNull")
                {
                    if (c.Literal.ValueKind != JsonValueKind.Undefined || c.Context != null)
                        throw new AuthorizationException("Null predicates take no value.");
                    errors.AddRange(expressions.ValidateLeaf(c, path, subject));
                    return;
                }
                if (
                    (c.Literal.ValueKind != JsonValueKind.Undefined ? 1 : 0)
                        + (c.Context != null ? 1 : 0)
                    != 1
                )
                    throw new AuthorizationException(
                        "Exactly one literal or trusted context reference required."
                    );
                if (c.Context != null)
                {
                    if (
                        !context.Keys.TryGetValue(c.Context, out var valueType)
                        || (Nullable.GetUnderlyingType(type) ?? type)
                            != (Nullable.GetUnderlyingType(valueType) ?? valueType)
                    )
                        throw new AuthorizationException(
                            "Unknown or incompatible context reference."
                        );
                    if (c.Operator == "in")
                        throw new AuthorizationException(
                            "Collection context references are unsupported."
                        );
                }
                if (c.Literal.ValueKind != JsonValueKind.Undefined)
                {
                    var literal = c.Literal;
                    if (Encoding.UTF8.GetByteCount(literal.GetRawText()) > limits.MaxLiteralBytes)
                        throw new AuthorizationException("Literal size limit exceeded.");
                    if (c.Operator == "in")
                    {
                        if (
                            literal.ValueKind != JsonValueKind.Array
                            || literal.GetArrayLength() == 0
                            || literal.GetArrayLength() > limits.MaxCollection
                        )
                            throw new AuthorizationException("Invalid collection size.");
                        foreach (var item in literal.EnumerateArray())
                            LiteralConverter.Convert(item, type);
                    }
                    else
                        LiteralConverter.Convert(literal, type);
                }
                errors.AddRange(expressions.ValidateLeaf(c, path, subject));
            }
            catch (AuthorizationException ex)
            {
                errors.Add(new(path, ex.Message));
            }
        }
        for (var i = 0; i < Math.Min(policy.Rules.Length, limits.MaxEntries); i++)
        {
            var r = policy.Rules[i];
            var path = $"rules[{i}]";
            if (r == null)
            {
                errors.Add(new(path, "Rule required."));
                continue;
            }
            Header(r.Id, r.Resource, r.Action, r.Effect, r.Subject, path);
            if (r.Condition != null)
                Node(r.Condition, path + ".condition", 1, false);
        }
        for (var i = 0; i < Math.Min(policy.FieldEntries.Length, limits.MaxEntries); i++)
        {
            var entry = policy.FieldEntries[i];
            var path = $"fieldEntries[{i}]";
            if (entry == null)
            {
                errors.Add(new(path, "Entry required."));
                continue;
            }
            Header(entry.Id, entry.Resource, entry.Action, entry.Effect, entry.Subject, path);
            if (entry.Fields == null)
            {
                errors.Add(new(path + ".fields", "Field sets required."));
                continue;
            }
            foreach (var grant in entry.Fields)
            {
                if (
                    !Enum.IsDefined(grant.Key)
                    || grant.Value == null
                    || grant.Value.Length > limits.MaxCollection
                )
                {
                    errors.Add(new(path + ".fields", "Invalid operation or collection."));
                    continue;
                }
                foreach (var name in grant.Value)
                    try
                    {
                        if (!schema.Resolve(name).Eligibility.Contains(grant.Key))
                            throw new AuthorizationException("Schema prohibits this operation.");
                    }
                    catch (AuthorizationException ex)
                    {
                        errors.Add(new(path + ".fields." + grant.Key, ex.Message));
                    }
            }
        }
        return errors;
    }

    public void DemandValid(PolicyDefinition policy)
    {
        var errors = Validate(policy);
        if (errors.Count != 0)
            throw new AuthorizationException(
                string.Join("; ", errors.Select(d => $"{d.Path}: {d.Message}"))
            );
    }

    public void DemandCondition(Condition condition)
    {
        DemandValid(
            new(
                schema.Resource,
                schema.Version,
                [new("validation", schema.Resource, "Read", Effect.Allow, new([], []), condition)],
                []
            )
        );
    }
}
