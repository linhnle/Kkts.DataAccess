using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kkts.Expressions;

namespace Kkts.DataAccess;

public sealed class AuthorizedReadRepository<T> : IAuthorizedReadRepository<T>
    where T : class
{
    readonly ResourceSchema<T> schema;
    readonly PolicyCompiler<T> compiler;
    readonly IActivePolicySource store;
    readonly IEntityReadAdapter<T> database;
    readonly Func<TrustedContext> context;
    readonly string scope;
    readonly ReadLimits limits;

    public AuthorizedReadRepository(
        ResourceSchema<T> schema,
        PolicyCompiler<T> compiler,
        IActivePolicySource store,
        IEntityReadAdapter<T> database,
        Func<TrustedContext> context,
        string scope,
        ReadLimits? limits = null
    )
    {
        this.schema = schema;
        this.compiler = compiler;
        this.store = store;
        this.database = database;
        this.context = context;
        this.scope = scope;
        this.limits = limits ?? new();
        if (
            this.limits.MaxPageSize < 1
            || this.limits.MaxOffset < 0
            || this.limits.ExportBatchSize < 1
            || this.limits.ExportBatchSize > this.limits.MaxPageSize
        )
            throw new ArgumentException("Invalid read limits.");
    }

    async Task<(BoundPolicy<T> Bound, TrustedContext Context)> Capture(
        string action,
        CancellationToken token
    )
    {
        if (action != "Read")
            throw new AuthorizationException(
                "Phase 1 supports Read only; writes and aggregation are unsupported."
            );
        var trusted = context();
        var captured = await store.CaptureAsync(scope, schema.Resource, token);
        return (compiler.Bind(captured, trusted, scope, action), trusted);
    }

    (string[] Fields, Expression<Func<T, bool>> Filter) Validate(
        ReadRequest request,
        BoundPolicy<T> bound,
        TrustedContext trusted
    )
    {
        if (
            request.Limit < 1
            || request.Limit > limits.MaxPageSize
            || request.Offset < 0
            || request.Offset > limits.MaxOffset
        )
            throw new AuthorizationException("Pagination limit exceeded.");
        if (
            (request.Selection?.Length ?? 0) > schema.Fields.Count
            || (request.Sort?.Length ?? 0) > schema.Fields.Count
        )
            throw new AuthorizationException("Too many field references.");
        void Demand(string field, FieldOperation operation)
        {
            var registered = schema.Resolve(field);
            if (
                !registered.Eligibility.Contains(operation)
                || !bound.Fields[operation].Contains(field)
            )
                throw new AuthorizationException($"Field '{field}' is forbidden for {operation}.");
        }
        var selection = (request.Selection ?? bound.Fields[FieldOperation.Select].Order().ToArray())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var name in selection)
            Demand(name, FieldOperation.Select);
        foreach (var sort in request.Sort ?? [])
            Demand(sort.Field, FieldOperation.Sort);
        if (request.Filter != null)
        {
            // Compiler validates structure/limits before recursion over client fields.
            compiler.DemandCondition(request.Filter);
            void Fields(Condition node)
            {
                if (node.Context != null)
                    throw new AuthorizationException(
                        "Client filters cannot reference authorization context."
                    );
                if (node.Field != null)
                    Demand(node.Field, FieldOperation.Filter);
                foreach (var child in node.All ?? node.Any ?? [])
                    Fields(child);
                if (node.Not != null)
                    Fields(node.Not);
            }
            Fields(request.Filter);
            return (
                selection,
                compiler.ClientPredicate(
                    request.Filter,
                    trusted,
                    bound.Fields[FieldOperation.Filter]
                )
            );
        }
        return (selection, e => true);
    }

    EntityReadPlan<T> Plan(Expression<Func<T, bool>> predicate, SortField[]? sort)
    {
        var fields = (sort ?? []).ToList();
        if (fields.All(s => s.Field != schema.StableKey.Name))
            fields.Add(new(schema.StableKey.Name));
        return new(
            predicate,
            fields.Select(s => new EntitySort(schema.Resolve(s.Field).Property, s.Descending))
        );
    }

    public async Task<ReadPage> ListAsync(
        ReadRequest request,
        CancellationToken cancellation = default
    )
    {
        var (bound, trusted) = await Capture(request.Action, cancellation);
        var validated = Validate(request, bound, trusted);
        await using var db = await database.OpenAsync(cancellation);
        var plan = Plan(bound.Predicate.And(validated.Filter), request.Sort);
        var count = await db.CountAsync(plan, cancellation);
        var entities = await db.PageAsync(plan, request.Offset, request.Limit, cancellation);
        return new(bound.Version, count, DtoMapper.Map(schema, entities, validated.Fields));
    }

    public async Task<long> CountAsync(
        ReadRequest request,
        CancellationToken cancellation = default
    )
    {
        var (bound, trusted) = await Capture(request.Action, cancellation);
        var validated = Validate(request, bound, trusted);
        await using var db = await database.OpenAsync(cancellation);
        return await db.CountAsync(
            Plan(bound.Predicate.And(validated.Filter), request.Sort),
            cancellation
        );
    }

    public async Task<ReadDetail> DetailAsync(
        JsonElement key,
        ReadRequest request,
        CancellationToken cancellation = default
    )
    {
        var (bound, trusted) = await Capture(request.Action, cancellation);
        var validated = Validate(request, bound, trusted);
        var keyFilter = compiler.Predicate(
            new()
            {
                Field = schema.StableKey.Name,
                Operator = "eq",
                Literal = key,
            },
            trusted
        );
        await using var db = await database.OpenAsync(cancellation);
        var entity = await db.SingleAsync(
            Plan(bound.Predicate.And(validated.Filter).And(keyFilter), request.Sort),
            cancellation
        );
        return new(
            bound.Version,
            entity == null ? null : DtoMapper.Map(schema, new[] { entity }, validated.Fields)[0]
        );
    }

    public async IAsyncEnumerable<ExportBatch> ExportAsync(
        ReadRequest request,
        [EnumeratorCancellation] CancellationToken cancellation = default
    )
    {
        var (bound, trusted) = await Capture(request.Action, cancellation);
        var validated = Validate(request, bound, trusted);
        // Pagination is validated but export covers every matching row from offset zero.
        for (var offset = 0; ; offset = checked(offset + limits.ExportBatchSize))
        {
            cancellation.ThrowIfCancellationRequested();
            await using var db = await database.OpenAsync(cancellation);
            var rows = await db.PageAsync(
                Plan(bound.Predicate.And(validated.Filter), request.Sort),
                offset,
                limits.ExportBatchSize,
                cancellation
            );
            if (rows.Count == 0)
                yield break;
            yield return new(bound.Version, DtoMapper.Map(schema, rows, validated.Fields));
            if (rows.Count < limits.ExportBatchSize)
                yield break;
        }
    }
}
