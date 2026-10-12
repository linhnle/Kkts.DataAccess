using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Kkts.Expressions;

namespace Kkts.DataAccess;

/// <summary>Owns expression mechanics; callers retain policy and field authorization.</summary>
internal sealed class ExpressionsAdapter<T>
    where T : class
{
    readonly ResourceSchema<T> schema;
    readonly ContextRegistry registry;
    readonly PolicyLimits limits;
    readonly FrozenDictionary<string, string> fields;
    readonly FrozenDictionary<string, string> subjects;
    readonly ExpressionQueryContext policyContext;
    readonly ExpressionQueryContext subjectContext;

    internal ExpressionsAdapter(
        ResourceSchema<T> schema,
        ContextRegistry registry,
        PolicyLimits limits
    )
    {
        this.schema = schema;
        this.registry = registry;
        this.limits = limits;
        fields = Aliases(schema.Fields.Keys);
        subjects = Aliases(registry.Keys.Keys);
        var builder = new QuerySchema<T>();
        foreach (var field in schema.Fields.Values)
        {
            var parameter = Expression.Parameter(typeof(T), "entity");
            Register(
                builder,
                fields[field.Name],
                Expression.Property(parameter, field.Property),
                parameter
            );
        }
        policyContext = new(builder.Build(), Policy());
        subjectContext = CreateSubjectContext(new Dictionary<string, object?>());
    }

    ExpressionQueryContext CreateSubjectContext(IReadOnlyDictionary<string, object?> values)
    {
        var builder = new QuerySchema<TrustedContext>();
        foreach (var key in registry.Keys)
        {
            var parameter = Expression.Parameter(typeof(TrustedContext), "context");
            var type = Nullable.GetUnderlyingType(key.Value) ?? key.Value;
            values.TryGetValue(key.Key, out var value);
            Expression constant =
                value == null
                    ? Expression.Constant(
                        null,
                        type.IsValueType ? typeof(Nullable<>).MakeGenericType(type) : type
                    )
                    : Expression.Constant(value, type);
            Register(builder, subjects[key.Key], constant, parameter);
        }
        return new(builder.Build(), Policy());
    }

    static FrozenDictionary<string, string> Aliases(IEnumerable<string> names) =>
        names
            .Order(StringComparer.Ordinal)
            .Select((name, index) => (name, alias: "f" + index))
            .ToFrozenDictionary(x => x.name, x => x.alias, StringComparer.Ordinal);

    QueryPolicy Policy() =>
        new QueryPolicy(
            maxExpressionLength: 4096,
            maxParenthesisDepth: Math.Max(16, limits.MaxDepth * 4),
            maxAtomicConditions: Math.Max(4, limits.MaxNodes * 4),
            maxInItems: limits.MaxCollection,
            maxNavigationDepth: 0,
            allowCollectionAccess: false
        ).WithMaxFilterTreeDepth(Math.Max(16, limits.MaxDepth * 4));

    // Lift scalar selectors so explicit null predicates remain valid for nonnullable fields too.
    static void Register<TEntity>(
        QuerySchema<TEntity> builder,
        string alias,
        Expression body,
        ParameterExpression parameter
    )
    {
        if (body.Type.IsValueType && Nullable.GetUnderlyingType(body.Type) == null)
            body = Expression.Convert(body, typeof(Nullable<>).MakeGenericType(body.Type));
        typeof(ExpressionsAdapter<T>)
            .GetMethod(nameof(RegisterTyped), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(TEntity), body.Type)
            .Invoke(null, [builder, alias, body, parameter]);
    }

    static void RegisterTyped<TEntity, TValue>(
        QuerySchema<TEntity> builder,
        string alias,
        Expression body,
        ParameterExpression parameter
    ) => builder.Field(alias, Expression.Lambda<Func<TEntity, TValue>>(body, parameter));

    static string Operator(string op) =>
        op switch
        {
            "eq" or "isNull" => "=",
            "ne" or "isNotNull" => "!=",
            "lt" => "<",
            "le" => "<=",
            "gt" => ">",
            "ge" => ">=",
            "contains" => "contains",
            "startsWith" => "startsWith",
            "endsWith" => "endsWith",
            "in" => "in",
            _ => throw new AuthorizationException("Unsupported operator."),
        };

    internal IReadOnlyList<Diagnostic> ValidateLeaf(Condition condition, string path, bool subject)
    {
        var type = subject
            ? registry.Keys[condition.Field!]
            : schema.Resolve(condition.Field!).Type;
        var alias = (subject ? subjects : fields)[condition.Field!];
        var context = subject ? subjectContext : policyContext;
        // Values were strictly converted by PolicyValidator. Analyze their declared types, never runtime context.
        var variableType =
            type.IsValueType && Nullable.GetUnderlyingType(type) == null
                ? typeof(Nullable<>).MakeGenericType(type)
                : type;
        var variables = new ExpressionVariableSchema([
            ExpressionVariableDefinition.FromType("value", variableType),
        ]);
        var value = condition.Operator is "isNull" or "isNotNull"
            ? FilterValue.Null
            : FilterValue.Variable("value");
        if (condition.Operator == "in")
            value = FilterValue.Collection([value]);
        var tree = FilterNode.Condition(alias, Operator(condition.Operator!), value);
        var validated = context.ValidateFilterTree(tree, variables);
        if (!validated.Succeeded)
            return validated
                .Diagnostics.Select(d => DependencyDiagnostic(d.Code, path, condition))
                .ToArray();
        var formatted = context.FormatFilterTree(tree, variables);
        if (!formatted.Succeeded)
            return formatted
                .Diagnostics.Select(d => DependencyDiagnostic(d.Code, path, condition))
                .ToArray();
        var analysis = context.AnalyzeExpression(formatted.Result, variables);
        return analysis
            .Diagnostics.Select(d => DependencyDiagnostic(d.Code, path, condition))
            .ToArray();
    }

    internal static Diagnostic DependencyDiagnostic(
        string code,
        string path,
        Condition condition
    ) =>
        code switch
        {
            "unknown-property" => new(
                DataAccessErrorCode.UnknownProperty,
                JsonPointer.Append(path, "field"),
                "Unknown public field."
            ),
            "operator-not-applicable" => new(
                DataAccessErrorCode.OperatorNotApplicable,
                JsonPointer.Append(path, "operator"),
                "Unsupported operator for field type."
            ),
            "incompatible-operand" => new(
                DataAccessErrorCode.IncompatibleOperand,
                JsonPointer.Append(path, condition.Context != null ? "context" : "literal"),
                "Incompatible operand."
            ),
            _ => new(DataAccessErrorCode.InvalidCondition, path, "Invalid condition."),
        };

    internal Expression<Func<T, bool>> Build(
        Condition? condition,
        TrustedContext trusted,
        IEnumerable<string>? clientFields = null
    )
    {
        if (condition == null)
            return entity => true;
        var resolver = new VariableResolver();
        var tree = Lower(condition, trusted, resolver, false, new Counter());
        var context =
            clientFields == null
                ? policyContext
                : new ExpressionQueryContext(
                    policyContext.Schema,
                    Policy(),
                    clientFields.Select(name => fields[schema.Resolve(name).Name])
                );
        return Demand(context.TryBuildPredicate<T>(tree, resolver));
    }

    internal bool Subject(Condition condition, TrustedContext trusted)
    {
        var resolver = new VariableResolver();
        var tree = Lower(condition, trusted, resolver, true, new Counter());
        var values = new Dictionary<string, object?>();
        void Capture(Condition node)
        {
            if (node.Field != null)
                values[node.Field] = registry.Resolve(trusted, node.Field);
            foreach (var child in node.All ?? node.Any ?? [])
                Capture(child);
            if (node.Not != null)
                Capture(node.Not);
        }
        Capture(condition);
        return Demand(
                CreateSubjectContext(values).TryBuildPredicate<TrustedContext>(tree, resolver)
            )
            .Compile()(trusted);
    }

    static Expression<Func<TEntity, bool>> Demand<TEntity>(EvaluationResult<TEntity, bool> result)
    {
        if (!result.Succeeded)
            throw new AuthorizationException("Expression construction failed.", result.Exception);
        return result.Result;
    }

    sealed class Counter
    {
        internal int Value;
    }

    FilterNode Lower(
        Condition node,
        TrustedContext trusted,
        VariableResolver resolver,
        bool subject,
        Counter counter
    )
    {
        if (node.All != null)
            return FilterNode.And(
                node.All.Select(c => Lower(c, trusted, resolver, subject, counter)).ToArray()
            );
        if (node.Any != null)
            return FilterNode.Or(
                node.Any.Select(c => Lower(c, trusted, resolver, subject, counter)).ToArray()
            );
        if (node.Not != null)
            return FilterNode.Not(Lower(node.Not, trusted, resolver, subject, counter));
        var type = subject ? registry.Keys[node.Field!] : schema.Resolve(node.Field!).Type;
        var alias = (subject ? subjects : fields)[node.Field!];
        if (subject)
            registry.Resolve(trusted, node.Field!); // Eagerly demand every left operand, even under short circuit.
        if (node.Operator is "isNull" or "isNotNull")
            return FilterNode.Condition(alias, Operator(node.Operator), FilterValue.Null);
        FilterValue Value(object? value)
        {
            if (value == null)
                return FilterValue.Null;
            var key = "v" + counter.Value++;
            if (!resolver.TryAdd(key, value))
                throw new AuthorizationException("Cannot bind expression value.");
            return FilterValue.Variable(key);
        }
        if (node.Operator == "in")
            return FilterNode.Condition(
                alias,
                "in",
                FilterValue.Collection(
                    node.Literal.EnumerateArray()
                        .Select(literal => Value(LiteralConverter.Convert(literal, type)))
                        .ToArray()
                )
            );
        var bound =
            node.Context != null
                ? registry.Resolve(trusted, node.Context)
                : LiteralConverter.Convert(node.Literal, type);
        if (bound is string text && Encoding.UTF8.GetByteCount(text) > limits.MaxLiteralBytes)
            throw new AuthorizationException("Bound context exceeds value limit.");
        if (bound == null && type.IsValueType && Nullable.GetUnderlyingType(type) == null)
            throw new AuthorizationException("Null trusted context is incompatible with field.");
        var compare = FilterNode.Condition(alias, Operator(node.Operator!), Value(bound));
        if (node.Operator is "eq" or "ne")
            return compare;
        // Explicit guard preserves DataAccess's false-on-null string/ordered comparison semantics, including NOT.
        var notNull = FilterNode.Condition(alias, "!=", FilterValue.Null);
        if (bound == null)
            return FilterNode.And([notNull, FilterNode.Not(notNull)]);
        return FilterNode.And([notNull, compare]);
    }
}
