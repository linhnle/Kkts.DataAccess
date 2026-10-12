using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Text.Json;
using Kkts.Expressions;

namespace Kkts.DataAccess;

public sealed record CapturedPolicy(
    string Scope,
    string Resource,
    string SchemaVersion,
    long Version,
    string Payload
);

public sealed record BoundPolicy<T>(
    long Version,
    Expression<Func<T, bool>> Predicate,
    FrozenDictionary<FieldOperation, FrozenSet<string>> Fields,
    IReadOnlyList<string> ApplicableRules
)
    where T : class;

public sealed class PolicyCompiler<T>
    where T : class
{
    readonly ResourceSchema<T> schema;
    readonly ContextRegistry registry;
    readonly Condition mandatory;
    readonly PolicyLimits limits;
    readonly string compilerVersion;

    private sealed record CompilationTemplate(string Payload, PolicyDefinition Definition);

    readonly ConcurrentDictionary<
        (string Scope, string Resource, string Schema, long Version, string Compiler),
        CompilationTemplate
    > templates = new();

    public PolicyCompiler(
        ResourceSchema<T> schema,
        ContextRegistry registry,
        Condition mandatory,
        PolicyLimits? limits = null,
        string compilerVersion = "1"
    )
    {
        this.schema = schema;
        this.registry = registry;
        this.mandatory = JsonSerializer.Deserialize<Condition>(
            JsonSerializer.Serialize(mandatory, PolicyJson.Options),
            PolicyJson.Options
        )!;
        this.limits = limits ?? new();
        this.compilerVersion = compilerVersion;
        new PolicyValidator<T>(schema, registry, this.limits).DemandCondition(this.mandatory);
    }

    public BoundPolicy<T> Bind(
        CapturedPolicy captured,
        TrustedContext context,
        string scope,
        string action = "Read"
    )
    {
        if (action != "Read" || !schema.Actions.Contains(action))
            throw new AuthorizationException("Phase 1 supports Read only.");
        if (
            captured.Scope != scope
            || captured.Resource != schema.Resource
            || captured.SchemaVersion != schema.Version
            || captured.Version <= 0
        )
            throw new AuthorizationException("Incompatible captured policy.");
        var validator = new PolicyValidator<T>(schema, registry, limits);
        var key = (scope, schema.Resource, schema.Version, captured.Version, compilerVersion);
        var template = templates.GetOrAdd(
            key,
            _ =>
            {
                var definition = PolicyJson.Deserialize(captured.Payload);
                validator.DemandValid(definition);
                return new CompilationTemplate(captured.Payload, definition);
            }
        );
        if (template.Payload != captured.Payload)
            throw new AuthorizationException("Immutable policy version content changed.");
        // Privately owned validated AST is never exposed or mutated; request-bound expressions remain uncached.
        var definition = template.Definition;
        var mandatoryPredicate = Predicate(mandatory, context);
        var applicable = definition
            .Rules.Where(r => r.Action == action && Applies(r.Subject, context))
            .ToArray();
        // Bind every applicable condition, including denies, before evaluating grants.
        var conditions = applicable
            .Select(r => (r.Effect, Predicate: Predicate(r.Condition, context)))
            .ToArray();
        if (!conditions.Any(r => r.Effect == Effect.Allow))
            throw new AuthorizationException("No applicable Read grant.");
        Expression<Func<T, bool>> allow = e => false,
            deny = e => false;
        foreach (var r in conditions)
            if (r.Effect == Effect.Allow)
                allow = allow.Or(r.Predicate);
            else
                deny = deny.Or(r.Predicate);
        var negated = Expression.Lambda<Func<T, bool>>(Expression.Not(deny.Body), deny.Parameters);
        var entries = definition
            .FieldEntries.Where(e => e.Action == action && Applies(e.Subject, context))
            .ToArray();
        var sets = new Dictionary<FieldOperation, FrozenSet<string>>();
        foreach (var operation in Enum.GetValues<FieldOperation>())
        {
            var granted = entries
                .Where(e => e.Effect == Effect.Allow)
                .SelectMany(e => e.Fields.GetValueOrDefault(operation) ?? [])
                .ToHashSet(StringComparer.Ordinal);
            granted.ExceptWith(
                entries
                    .Where(e => e.Effect == Effect.Deny)
                    .SelectMany(e => e.Fields.GetValueOrDefault(operation) ?? [])
            );
            granted.IntersectWith(
                schema
                    .Fields.Values.Where(f => f.Eligibility.Contains(operation))
                    .Select(f => f.Name)
            );
            sets[operation] = granted.ToFrozenSet(StringComparer.Ordinal);
        }
        return new(
            captured.Version,
            mandatoryPredicate.And(allow).And(negated),
            sets.ToFrozenDictionary(),
            Array.AsReadOnly(applicable.Select(r => r.Id).ToArray())
        );
    }

    public Expression<Func<T, bool>> Predicate(Condition? condition, TrustedContext context)
    {
        if (condition != null)
            new PolicyValidator<T>(schema, registry, limits).DemandCondition(condition);
        var parameter = Expression.Parameter(typeof(T), "entity");
        return Expression.Lambda<Func<T, bool>>(
            Node(condition, parameter, context, false),
            parameter
        );
    }

    bool Applies(Subject subject, TrustedContext context)
    {
        if (
            subject.Roles.Length + subject.Groups.Length != 0
            && !subject.Roles.Any(context.Roles.Contains)
            && !subject.Groups.Any(context.Groups.Contains)
        )
            return false;
        return subject.ContextCondition == null
            || Expression
                .Lambda<Func<bool>>(Node(subject.ContextCondition, null, context, true))
                .Compile()();
    }

    Expression Node(
        Condition? c,
        ParameterExpression? parameter,
        TrustedContext context,
        bool subject
    )
    {
        if (c == null)
            return Expression.Constant(true);
        // Enumerate all children before composition: short circuit must not hide missing trusted context.
        if (c.All != null)
            return c
                .All.Select(n => Node(n, parameter, context, subject))
                .ToArray()
                .Aggregate(Expression.AndAlso);
        if (c.Any != null)
            return c
                .Any.Select(n => Node(n, parameter, context, subject))
                .ToArray()
                .Aggregate(Expression.OrElse);
        if (c.Not != null)
            return Expression.Not(Node(c.Not, parameter, context, subject));
        Expression left;
        Type type;
        if (subject)
        {
            type = registry.Keys[c.Field!];
            left = Constant(registry.Resolve(context, c.Field!), type);
        }
        else
        {
            var field = schema.Resolve(c.Field!);
            type = field.Type;
            left = Expression.Property(parameter!, field.Property);
        }
        var canNull = !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        var isNull = canNull
            ? Expression.Equal(left, Expression.Constant(null, type))
            : (Expression)Expression.Constant(false);
        if (c.Operator == "isNull")
            return isNull;
        if (c.Operator == "isNotNull")
            return Expression.Not(isNull);
        if (c.Operator == "in")
        {
            Expression found = Expression.Constant(false);
            foreach (var literal in c.Literal.EnumerateArray())
                found = Expression.OrElse(
                    found,
                    Expression.Equal(left, Constant(LiteralConverter.Convert(literal, type), type))
                );
            return found;
        }
        var value =
            c.Context != null
                ? registry.Resolve(context, c.Context)
                : LiteralConverter.Convert(c.Literal, type);
        if (
            value is string text
            && System.Text.Encoding.UTF8.GetByteCount(text) > limits.MaxLiteralBytes
        )
            throw new AuthorizationException("Bound context exceeds value limit.");
        if (value == null && !canNull)
            throw new AuthorizationException("Null trusted context is incompatible with field.");
        var right = Constant(value, type);
        if (c.Operator == "eq")
            return Expression.Equal(left, right);
        if (c.Operator == "ne")
            return Expression.NotEqual(left, right);
        if (value == null)
            return Expression.Constant(false);
        Expression compare = c.Operator switch
        {
            "lt" => Expression.LessThan(left, right),
            "le" => Expression.LessThanOrEqual(left, right),
            "gt" => Expression.GreaterThan(left, right),
            "ge" => Expression.GreaterThanOrEqual(left, right),
            "contains" => Expression.Call(
                left,
                typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
                right
            ),
            "startsWith" => Expression.Call(
                left,
                typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!,
                right
            ),
            "endsWith" => Expression.Call(
                left,
                typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!,
                right
            ),
            _ => throw new AuthorizationException("Unsupported operator."),
        };
        return canNull ? Expression.AndAlso(Expression.Not(isNull), compare) : compare;
    }

    static Expression Constant(object? value, Type type)
    {
        if (value == null)
            return Expression.Constant(null, type);
        var raw = Expression.Constant(value, Nullable.GetUnderlyingType(type) ?? type);
        return raw.Type == type ? raw : Expression.Convert(raw, type);
    }
}
