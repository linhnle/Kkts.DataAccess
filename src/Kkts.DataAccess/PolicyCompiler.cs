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
    where T : class
{
    internal PolicyDefinition? Definition { get; init; }
    internal string? PolicyIdentity { get; init; }
}

public sealed class PolicyCompiler<T>
    where T : class
{
    readonly ResourceSchema<T> schema;
    readonly Condition mandatory;
    readonly string compilerVersion;
    readonly ExpressionsAdapter<T> expressions;
    readonly PolicyValidator<T> validator;
    readonly PolicyLimits limits;
    public IReadOnlyList<IReadProviderCapabilities> Capabilities { get; }
    public string ValidationIdentity { get; }
    internal ContextRegistry ContextRegistry { get; }
    internal string CursorBindingIdentity =>
        JsonSerializer.Serialize(
            new
            {
                compilerVersion,
                mandatory,
                ValidationIdentity,
            },
            PolicyJson.Options
        );

    private sealed record CompilationTemplate(string Payload, PolicyDefinition Definition);

    readonly object templateGate = new();
    readonly int templateCapacity;
    readonly Dictionary<
        (
            string Scope,
            string Resource,
            string Schema,
            long Version,
            string Compiler,
            string Validation
        ),
        CompilationTemplate
    > templates = new();
    readonly Queue<(
        string Scope,
        string Resource,
        string Schema,
        long Version,
        string Compiler,
        string Validation
    )> templateInsertionOrder = new();

    internal int TemplateCount
    {
        get
        {
            lock (templateGate)
                return templates.Count;
        }
    }

    internal IReadOnlyList<long> TemplateInsertionOrder
    {
        get
        {
            lock (templateGate)
                return templateInsertionOrder.Select(key => key.Version).ToArray();
        }
    }

    public PolicyCompiler(
        ResourceSchema<T> schema,
        ContextRegistry registry,
        Condition mandatory,
        PolicyLimits? limits = null,
        string compilerVersion = "1",
        IEnumerable<IReadProviderCapabilities>? capabilities = null,
        PolicyCompilerCacheOptions? cacheOptions = null
    )
    {
        this.schema = schema;
        ContextRegistry = registry;
        this.mandatory = JsonSerializer.Deserialize<Condition>(
            JsonSerializer.Serialize(mandatory, PolicyJson.Options),
            PolicyJson.Options
        )!;
        var configuredLimits = limits ?? new();
        this.limits = configuredLimits;
        this.compilerVersion = compilerVersion;
        templateCapacity = (cacheOptions ?? new()).MaxEntries;
        expressions = new(schema, registry, configuredLimits);
        Capabilities = Array.AsReadOnly(
            (capabilities ?? [])
                .DistinctBy(profile => profile.Identity)
                .OrderBy(profile => profile.Identity, StringComparer.Ordinal)
                .ToArray()
        );
        ValidationIdentity = string.Join(":", Capabilities.Select(profile => profile.Identity));
        validator = new(schema, registry, configuredLimits, Capabilities);
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
            throw new SchemaIncompatibleException("Incompatible captured policy.");
        var key = (
            scope,
            schema.Resource,
            schema.Version,
            captured.Version,
            compilerVersion,
            ValidationIdentity
        );
        CompilationTemplate template;
        bool hit = false,
            built = false,
            evicted = false;
        Exception? cacheError = null;
        try
        {
            lock (templateGate)
            {
                if (templates.TryGetValue(key, out var existing))
                {
                    hit = true;
                    if (existing.Payload != captured.Payload)
                        throw new RevisionConflictException(
                            "Immutable policy version content changed."
                        );
                    template = existing;
                }
                else
                {
                    built = true;
                    try
                    {
                        var parsedDefinition = PolicyJson.Deserialize(captured.Payload);
                        validator.DemandValid(parsedDefinition);
                        template = new CompilationTemplate(captured.Payload, parsedDefinition);
                    }
                    catch (InvalidRequestException ex)
                    {
                        throw new SchemaIncompatibleException(
                            "The captured policy is invalid.",
                            ex
                        );
                    }
                    if (templates.Count == templateCapacity)
                    {
                        templates.Remove(templateInsertionOrder.Dequeue());
                        evicted = true;
                    }
                    templates.Add(key, template);
                    templateInsertionOrder.Enqueue(key);
                }
            }
        }
        catch (Exception ex)
        {
            cacheError = ex;
            throw;
        }
        finally
        {
            ReadDiagnostics.Cache("template", hit, built, evicted, cacheError);
        }
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
        )
        {
            Definition = definition,
            PolicyIdentity = template.Payload,
        };
    }

    internal void DemandCapabilities(BoundPolicy<T> bound, IReadProviderCapabilities profile)
    {
        if (Capabilities.Any(existing => existing.Identity == profile.Identity))
            return;
        var providerValidator = new PolicyValidator<T>(schema, ContextRegistry, limits, [profile]);
        try
        {
            providerValidator.DemandCondition(mandatory);
            providerValidator.DemandValid(
                bound.Definition
                    ?? throw new InvalidOperationException(
                        "A captured definition is required for provider validation."
                    )
            );
        }
        catch (InvalidRequestException exception)
        {
            throw new SchemaIncompatibleException(
                "The captured policy or mandatory predicate is incompatible with the provider.",
                exception
            );
        }
    }

    public Expression<Func<T, bool>> Predicate(Condition? condition, TrustedContext context)
    {
        if (condition != null)
            validator.DemandCondition(condition);
        return expressions.Build(condition, context);
    }

    internal void DemandCondition(Condition condition, string inputRoot = "/filter") =>
        validator.DemandCondition(condition, inputRoot);

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
