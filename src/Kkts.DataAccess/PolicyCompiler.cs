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
    readonly Condition mandatory;
    readonly string compilerVersion;
    readonly ExpressionsAdapter<T> expressions;
    readonly PolicyValidator<T> validator;

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
        this.mandatory = JsonSerializer.Deserialize<Condition>(
            JsonSerializer.Serialize(mandatory, PolicyJson.Options),
            PolicyJson.Options
        )!;
        var configuredLimits = limits ?? new();
        this.compilerVersion = compilerVersion;
        expressions = new(schema, registry, configuredLimits);
        validator = new(schema, registry, configuredLimits);
        validator.DemandCondition(this.mandatory);
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
            validator.DemandCondition(condition);
        return expressions.Build(condition, context);
    }

    internal void DemandCondition(Condition condition) => validator.DemandCondition(condition);

    internal Expression<Func<T, bool>> ClientPredicate(
        Condition condition,
        TrustedContext context,
        IEnumerable<string> permittedFields
    ) => expressions.Build(condition, context, permittedFields);

    bool Applies(Subject subject, TrustedContext context)
    {
        if (
            subject.Roles.Length + subject.Groups.Length != 0
            && !subject.Roles.Any(context.Roles.Contains)
            && !subject.Groups.Any(context.Groups.Contains)
        )
            return false;
        return subject.ContextCondition == null
            || expressions.Subject(subject.ContextCondition, context);
    }
}
