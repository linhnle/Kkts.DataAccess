using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.Sqlite;

public sealed class AuthorizedReads<T>
    where T : class
{
    readonly ResourceSchema<T> schema;
    readonly PolicyCompiler<T> compiler;
    readonly SqlitePolicyStore store;
    readonly Func<DbContext> database;
    readonly Func<TrustedContext> context;
    readonly string scope;
    readonly ReadLimits limits;

    public AuthorizedReads(
        ResourceSchema<T> schema,
        PolicyCompiler<T> compiler,
        SqlitePolicyStore store,
        Func<DbContext> database,
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
            var filter = compiler.Predicate(request.Filter, trusted);
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
            return (selection, filter);
        }
        return (selection, e => true);
    }

    IQueryable<T> Ordered(IQueryable<T> query, SortField[]? sort)
    {
        var fields = (sort ?? []).ToList();
        if (fields.All(s => s.Field != schema.StableKey.Name))
            fields.Add(new(schema.StableKey.Name));
        for (var i = 0; i < fields.Count; i++)
        {
            var parameter = Expression.Parameter(typeof(T), "e");
            var property = schema.Resolve(fields[i].Field).Property;
            var selector = Expression.Lambda(Expression.Property(parameter, property), parameter);
            var method =
                (i == 0 ? "OrderBy" : "ThenBy") + (fields[i].Descending ? "Descending" : "");
            query = query.Provider.CreateQuery<T>(
                Expression.Call(
                    typeof(Queryable),
                    method,
                    [typeof(T), property.PropertyType],
                    query.Expression,
                    Expression.Quote(selector)
                )
            );
        }
        return query;
    }

    public async Task<ReadPage> ListAsync(
        ReadRequest request,
        CancellationToken cancellation = default
    )
    {
        var (bound, trusted) = await Capture(request.Action, cancellation);
        var validated = Validate(request, bound, trusted);
        await using var db = database();
        var query = db.Set<T>().AsNoTracking().Where(bound.Predicate).Where(validated.Filter);
        var count = await query.LongCountAsync(cancellation);
        var entities = await Ordered(query, request.Sort)
            .Skip(request.Offset)
            .Take(request.Limit)
            .ToListAsync(cancellation);
        return new(bound.Version, count, DtoMapper.Map(schema, entities, validated.Fields));
    }

    public async Task<long> CountAsync(
        ReadRequest request,
        CancellationToken cancellation = default
    )
    {
        var (bound, trusted) = await Capture(request.Action, cancellation);
        var validated = Validate(request, bound, trusted);
        await using var db = database();
        return await db.Set<T>()
            .AsNoTracking()
            .Where(bound.Predicate)
            .Where(validated.Filter)
            .LongCountAsync(cancellation);
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
        await using var db = database();
        var entity = await db.Set<T>()
            .AsNoTracking()
            .Where(bound.Predicate)
            .Where(validated.Filter)
            .Where(keyFilter)
            .SingleOrDefaultAsync(cancellation);
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
            await using var db = database();
            var query = db.Set<T>().AsNoTracking().Where(bound.Predicate).Where(validated.Filter);
            var rows = await Ordered(query, request.Sort)
                .Skip(offset)
                .Take(limits.ExportBatchSize)
                .ToListAsync(cancellation);
            if (rows.Count == 0)
                yield break;
            yield return new(bound.Version, DtoMapper.Map(schema, rows, validated.Fields));
            if (rows.Count < limits.ExportBatchSize)
                yield break;
        }
    }
}
