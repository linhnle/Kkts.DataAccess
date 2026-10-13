using System.Text;
using System.Text.Json;

namespace Kkts.DataAccess;

public sealed class PolicyValidator<T>(
    ResourceSchema<T> schema,
    ContextRegistry context,
    PolicyLimits? configuredLimits = null,
    IEnumerable<IReadProviderCapabilities>? capabilities = null
)
    where T : class
{
    readonly PolicyLimits limits = configuredLimits ?? new();
    readonly ExpressionsAdapter<T> expressions = new(schema, context, configuredLimits ?? new());
    readonly IReadOnlyList<IReadProviderCapabilities> profiles = Array.AsReadOnly(
        (capabilities ?? []).ToArray()
    );

    public IReadOnlyList<Diagnostic> Validate(PolicyDefinition policy)
    {
        if (
            profiles.Any(profile =>
                profile.Resource != schema.Resource || profile.SchemaVersion != schema.Version
            )
        )
            throw new ArgumentException(
                "A capability profile does not match the validator's schema."
            );
        var errors = new DiagnosticBuffer();
        var nodes = 0;
        if (policy.Resource != schema.Resource)
            errors.Add(
                new(DataAccessErrorCode.SchemaIncompatible, "/resource", "Resource mismatch.")
            );
        if (policy.SchemaVersion != schema.Version)
            errors.Add(
                new(
                    DataAccessErrorCode.SchemaIncompatible,
                    "/schemaVersion",
                    "Schema version mismatch."
                )
            );
        if (policy.Rules == null || policy.FieldEntries == null)
        {
            errors.Add(
                new(DataAccessErrorCode.InvalidEntry, "", "Rule and field arrays are required.")
            );
            return errors.AsReadOnly();
        }
        if (policy.Rules.Length + policy.FieldEntries.Length > limits.MaxEntries)
            errors.Add(new(DataAccessErrorCode.ComplexityExceeded, "", "Too many policy entries."));
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
                errors.Add(
                    new(
                        DataAccessErrorCode.InvalidEntry,
                        JsonPointer.Append(path, "id"),
                        "Unique entry id required."
                    )
                );
            if (resource != schema.Resource)
                errors.Add(
                    new(
                        DataAccessErrorCode.SchemaIncompatible,
                        JsonPointer.Append(path, "resource"),
                        "Resource mismatch."
                    )
                );
            if (!schema.Actions.Contains(action))
                errors.Add(
                    new(
                        DataAccessErrorCode.InvalidEntry,
                        JsonPointer.Append(path, "action"),
                        "Unsupported action."
                    )
                );
            if (!Enum.IsDefined(effect))
                errors.Add(
                    new(
                        DataAccessErrorCode.InvalidEntry,
                        JsonPointer.Append(path, "effect"),
                        "Unsupported effect."
                    )
                );
            if (subject == null || subject.Roles == null || subject.Groups == null)
                errors.Add(
                    new(
                        DataAccessErrorCode.InvalidSubject,
                        JsonPointer.Append(path, "subject"),
                        "Subject arrays required."
                    )
                );
            else
            {
                if (
                    subject.Roles.Length + subject.Groups.Length > limits.MaxCollection
                    || subject.Roles.Concat(subject.Groups).Any(string.IsNullOrWhiteSpace)
                )
                    errors.Add(
                        new(
                            DataAccessErrorCode.InvalidSubject,
                            JsonPointer.Append(path, "subject"),
                            "Invalid subject names or collection limit."
                        )
                    );
                if (subject.ContextCondition != null)
                    Node(
                        subject.ContextCondition,
                        JsonPointer.Append(path, "subject", "contextCondition"),
                        1,
                        true
                    );
            }
        }
        void Node(Condition c, string path, int depth, bool subject)
        {
            if (c == null)
            {
                errors.Add(new(DataAccessErrorCode.InvalidCondition, path, "Node required."));
                return;
            }
            if (++nodes > limits.MaxNodes || depth > limits.MaxDepth)
            {
                errors.Add(
                    new(
                        DataAccessErrorCode.ComplexityExceeded,
                        path,
                        "Condition complexity limit exceeded."
                    )
                );
                return;
            }
            var forms =
                (c.All != null ? 1 : 0)
                + (c.Any != null ? 1 : 0)
                + (c.Not != null ? 1 : 0)
                + (c.Field != null ? 1 : 0);
            if (forms != 1)
            {
                errors.Add(
                    new(
                        DataAccessErrorCode.InvalidCondition,
                        path,
                        "Exactly one node form required."
                    )
                );
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
                    errors.Add(
                        new(
                            DataAccessErrorCode.InvalidCondition,
                            path,
                            "Group cannot contain leaf properties."
                        )
                    );
                if (children != null)
                {
                    if (children.Length == 0 || children.Length > limits.MaxCollection)
                        errors.Add(
                            new(
                                DataAccessErrorCode.ComplexityExceeded,
                                JsonPointer.Append(path, c.All != null ? "all" : "any"),
                                "Invalid group size."
                            )
                        );
                    for (var i = 0; i < Math.Min(children.Length, limits.MaxNodes); i++)
                        Node(
                            children[i],
                            JsonPointer.Append(
                                path,
                                c.All != null ? "all" : "any",
                                i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            ),
                            depth + 1,
                            subject
                        );
                }
                else
                    Node(c.Not!, JsonPointer.Append(path, "not"), depth + 1, subject);
                return;
            }
            var location = JsonPointer.Append(path, "field");
            var code = DataAccessErrorCode.UnknownField;
            try
            {
                Type type;
                IReadOnlySet<string> operators;
                if (subject)
                {
                    if (!context.Keys.TryGetValue(c.Field!, out type!))
                        throw new InvalidRequestException("Unknown subject context field.");
                    operators = OperatorCatalog.For(type);
                }
                else
                {
                    var f = schema.Resolve(c.Field!);
                    type = f.Type;
                    operators = f.Operators;
                }
                location = JsonPointer.Append(path, "operator");
                code = DataAccessErrorCode.InvalidOperator;
                if (c.Operator == null || !operators.Contains(c.Operator))
                    throw new InvalidRequestException("Unsupported operator for field type.");
                if (!subject)
                    foreach (var profile in profiles)
                    {
                        var result = profile.CheckField(
                            c.Field!,
                            ProviderFieldUse.Predicate,
                            c.Operator
                        );
                        if (result.Status == CapabilityStatus.Unsupported)
                            errors.Add(
                                new(
                                    DataAccessErrorCode.ProviderUnsupported,
                                    location,
                                    result.Reason
                                )
                            );
                    }
                location = path;
                code = DataAccessErrorCode.InvalidCondition;
                if (c.Operator is "isNull" or "isNotNull")
                {
                    if (c.Literal.ValueKind != JsonValueKind.Undefined || c.Context != null)
                        throw new InvalidRequestException("Null predicates take no value.");
                    errors.AddRange(expressions.ValidateLeaf(c, path, subject));
                    return;
                }
                if (
                    (c.Literal.ValueKind != JsonValueKind.Undefined ? 1 : 0)
                        + (c.Context != null ? 1 : 0)
                    != 1
                )
                    throw new InvalidRequestException(
                        "Exactly one literal or trusted context reference required."
                    );
                if (c.Context != null)
                {
                    location = JsonPointer.Append(path, "context");
                    code = DataAccessErrorCode.InvalidContextReference;
                    if (
                        !context.Keys.TryGetValue(c.Context, out var valueType)
                        || (Nullable.GetUnderlyingType(type) ?? type)
                            != (Nullable.GetUnderlyingType(valueType) ?? valueType)
                    )
                        throw new InvalidRequestException(
                            "Unknown or incompatible context reference."
                        );
                    if (c.Operator == "in")
                        throw new InvalidRequestException(
                            "Collection context references are unsupported."
                        );
                }
                if (c.Literal.ValueKind != JsonValueKind.Undefined)
                {
                    location = JsonPointer.Append(path, "literal");
                    code = DataAccessErrorCode.InvalidLiteral;
                    var literal = c.Literal;
                    if (Encoding.UTF8.GetByteCount(literal.GetRawText()) > limits.MaxLiteralBytes)
                        throw new InvalidRequestException("Literal size limit exceeded.");
                    if (c.Operator == "in")
                    {
                        if (
                            literal.ValueKind != JsonValueKind.Array
                            || literal.GetArrayLength() == 0
                            || literal.GetArrayLength() > limits.MaxCollection
                        )
                            throw new InvalidRequestException("Invalid collection size.");
                        var index = 0;
                        foreach (var item in literal.EnumerateArray())
                        {
                            location = JsonPointer.Append(
                                path,
                                "literal",
                                (index++).ToString(
                                    System.Globalization.CultureInfo.InvariantCulture
                                )
                            );
                            LiteralConverter.Convert(item, type);
                        }
                    }
                    else
                        LiteralConverter.Convert(literal, type);
                }
                errors.AddRange(expressions.ValidateLeaf(c, path, subject));
            }
            catch (InvalidRequestException ex)
            {
                errors.Add(new(code, location, ex.Message));
            }
        }
        for (var i = 0; i < Math.Min(policy.Rules.Length, limits.MaxEntries); i++)
        {
            var r = policy.Rules[i];
            var path = JsonPointer.Append(
                "",
                "rules",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            if (r == null)
            {
                errors.Add(new(DataAccessErrorCode.InvalidEntry, path, "Rule required."));
                continue;
            }
            Header(r.Id, r.Resource, r.Action, r.Effect, r.Subject, path);
            if (r.Condition != null)
                Node(r.Condition, JsonPointer.Append(path, "condition"), 1, false);
        }
        for (var i = 0; i < Math.Min(policy.FieldEntries.Length, limits.MaxEntries); i++)
        {
            var entry = policy.FieldEntries[i];
            var path = JsonPointer.Append(
                "",
                "fieldEntries",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            if (entry == null)
            {
                errors.Add(new(DataAccessErrorCode.InvalidEntry, path, "Entry required."));
                continue;
            }
            Header(entry.Id, entry.Resource, entry.Action, entry.Effect, entry.Subject, path);
            if (entry.Fields == null)
            {
                errors.Add(
                    new(
                        DataAccessErrorCode.InvalidEntry,
                        JsonPointer.Append(path, "fields"),
                        "Field sets required."
                    )
                );
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
                    errors.Add(
                        new(
                            DataAccessErrorCode.InvalidEntry,
                            JsonPointer.Append(path, "fields", grant.Key.ToString()),
                            "Invalid operation or collection."
                        )
                    );
                    continue;
                }
                for (var index = 0; index < grant.Value.Length; index++)
                    try
                    {
                        var name = grant.Value[index];
                        if (!schema.Resolve(name).Eligibility.Contains(grant.Key))
                            throw new InvalidRequestException("Schema prohibits this operation.");
                        foreach (var profile in profiles)
                        {
                            var uses = new List<ProviderFieldUse>();
                            if (
                                grant.Key == FieldOperation.Select
                                && (
                                    profile.Operations
                                    & (
                                        ReadOperations.Offset
                                        | ReadOperations.Detail
                                        | ReadOperations.Cursor
                                        | ReadOperations.Export
                                    )
                                ) != 0
                            )
                                uses.Add(ProviderFieldUse.Output);
                            if (grant.Key == FieldOperation.Sort)
                            {
                                if (profile.Operations.HasFlag(ReadOperations.Offset))
                                    uses.Add(ProviderFieldUse.OffsetOrder);
                                if (
                                    (
                                        profile.Operations
                                        & (ReadOperations.Cursor | ReadOperations.Export)
                                    ) != 0
                                )
                                    uses.Add(ProviderFieldUse.SeekOrder);
                            }
                            foreach (var use in uses)
                            {
                                var result = profile.CheckField(name, use);
                                if (result.Status == CapabilityStatus.Unsupported)
                                    errors.Add(
                                        new(
                                            DataAccessErrorCode.ProviderUnsupported,
                                            JsonPointer.Append(
                                                path,
                                                "fields",
                                                grant.Key.ToString(),
                                                index.ToString(
                                                    System
                                                        .Globalization
                                                        .CultureInfo
                                                        .InvariantCulture
                                                )
                                            ),
                                            result.Reason
                                        )
                                    );
                            }
                        }
                    }
                    catch (InvalidRequestException ex)
                    {
                        errors.Add(
                            new(
                                DataAccessErrorCode.UnknownField,
                                JsonPointer.Append(
                                    path,
                                    "fields",
                                    grant.Key.ToString(),
                                    index.ToString(
                                        System.Globalization.CultureInfo.InvariantCulture
                                    )
                                ),
                                ex.Message
                            )
                        );
                    }
            }
        }
        return errors.AsReadOnly();
    }

    public void DemandValid(PolicyDefinition policy)
    {
        var errors = Validate(policy);
        if (errors.Count != 0)
            throw new InvalidRequestException("Policy validation failed.", errors);
    }

    public void DemandCondition(Condition condition, string inputRoot = "")
    {
        JsonPointer.Append(inputRoot);
        var errors = Validate(
            new(
                schema.Resource,
                schema.Version,
                [new("validation", schema.Resource, "Read", Effect.Allow, new([], []), condition)],
                []
            )
        );
        if (errors.Count != 0)
            throw new InvalidRequestException(
                "Condition validation failed.",
                errors.Select(d =>
                    d with
                    {
                        Path = d.Path.StartsWith("/rules/0/condition", StringComparison.Ordinal)
                            ? inputRoot + d.Path["/rules/0/condition".Length..]
                            : inputRoot,
                    }
                )
            );
    }
}
