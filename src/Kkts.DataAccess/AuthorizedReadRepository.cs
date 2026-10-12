using System.Linq.Expressions;
using System.Runtime.CompilerServices;
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
    readonly CursorReadConfiguration? cursor;
    readonly ReadProjectionMode projectionMode;

    public AuthorizedReadRepository(
        ResourceSchema<T> schema,
        PolicyCompiler<T> compiler,
        IActivePolicySource store,
        IEntityReadAdapter<T> database,
        Func<TrustedContext> context,
        string scope,
        ReadLimits? limits = null,
        CursorReadConfiguration? cursor = null,
        ReadProjectionMode projectionMode = ReadProjectionMode.Required
    )
    {
        this.schema = schema;
        this.compiler = compiler;
        this.store = store;
        this.database = database;
        this.context = context;
        this.scope = scope;
        this.limits = limits ?? new();
        this.cursor = cursor;
        if (!Enum.IsDefined(projectionMode))
            throw new ArgumentException("Invalid read projection mode.", nameof(projectionMode));
        this.projectionMode = projectionMode;
        if (
            this.limits.MaxPageSize < 1
            || this.limits.MaxOffset < 0
            || this.limits.ExportBatchSize < 1
            || this.limits.ExportBatchSize > this.limits.MaxPageSize
        )
            throw new ArgumentException("Invalid read limits.");
    }

    async Task<(BoundPolicy<T> Bound, TrustedContext Context)> Capture(CancellationToken token)
    {
        var trusted = context();
        var captured = await store.CaptureAsync(scope, schema.Resource, token);
        return (compiler.Bind(captured, trusted, scope, "Read"), trusted);
    }

    void ValidatePagination(ListReadRequest request)
    {
        if (
            request.Limit < 1
            || request.Limit > limits.MaxPageSize
            || request.Offset < 0
            || request.Offset > limits.MaxOffset
        )
            throw new AuthorizationException("Pagination limit exceeded.");
    }

    void Demand(string field, FieldOperation operation, BoundPolicy<T> bound)
    {
        var registered = schema.Resolve(field);
        if (!registered.Eligibility.Contains(operation) || !bound.Fields[operation].Contains(field))
            throw new AuthorizationException($"Field '{field}' is forbidden for {operation}.");
    }

    string[] ValidateSelection(string[]? requested, BoundPolicy<T> bound)
    {
        if ((requested?.Length ?? 0) > schema.Fields.Count)
            throw new AuthorizationException("Too many field references.");
        var selection = (requested ?? bound.Fields[FieldOperation.Select].Order().ToArray())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var name in selection)
            Demand(name, FieldOperation.Select, bound);
        return selection;
    }

    void ValidateSort(SortField[]? requested, BoundPolicy<T> bound)
    {
        if ((requested?.Length ?? 0) > schema.Fields.Count)
            throw new AuthorizationException("Too many field references.");
        foreach (var sort in requested ?? [])
            Demand(sort.Field, FieldOperation.Sort, bound);
    }

    Expression<Func<T, bool>> ValidateFilter(
        Condition? filter,
        BoundPolicy<T> bound,
        TrustedContext trusted
    )
    {
        if (filter != null)
        {
            // Compiler validates structure/limits before recursion over client fields.
            compiler.DemandCondition(filter);
            void Fields(Condition node)
            {
                if (node.Context != null)
                    throw new AuthorizationException(
                        "Client filters cannot reference authorization context."
                    );
                if (node.Field != null)
                    Demand(node.Field, FieldOperation.Filter, bound);
                foreach (var child in node.All ?? node.Any ?? [])
                    Fields(child);
                if (node.Not != null)
                    Fields(node.Not);
            }
            Fields(filter);
            return compiler.ClientPredicate(filter, trusted, bound.Fields[FieldOperation.Filter]);
        }
        return e => true;
    }

    EntityReadPlan<T> Plan(
        Expression<Func<T, bool>> predicate,
        SortField[]? sort,
        IReadOnlyList<string>? selection = null
    )
    {
        var fields = (sort ?? []).ToList();
        if (fields.All(s => s.Field != schema.StableKey.Name))
            fields.Add(new(schema.StableKey.Name));
        return new(
            predicate,
            fields.Select(s => new EntitySort(schema.Resolve(s.Field).Property, s.Descending)),
            selection?.Select(name => new EntityOutputBinding(name, schema.Resolve(name).Property))
        );
    }

    void DemandProjection(bool capable)
    {
        if (!capable && projectionMode == ReadProjectionMode.Required)
            throw new NotSupportedException(
                "The entity adapter does not support the required projected retrieval."
            );
    }

    static void ValidateRows(
        IReadOnlyList<ProjectedReadRow> rows,
        EntityReadPlan<T> plan,
        EntitySeekPlan<T>? seek = null
    )
    {
        foreach (var row in rows)
        {
            if (row == null)
                throw new InvalidOperationException("The projected adapter returned a null row.");
            row.Validate(plan, seek);
        }
    }

    public async Task<ReadPage> ListAsync(
        ListReadRequest request,
        CancellationToken cancellation = default
    )
    {
        var (bound, trusted) = await Capture(cancellation);
        ValidatePagination(request);
        var fetchLimit = FetchLimit(request.Limit, request.IncludeHasMore);
        var fields = ValidateSelection(request.Selection, bound);
        var filter = ValidateFilter(request.Filter, bound, trusted);
        ValidateSort(request.Sort, bound);
        await using var db = await database.OpenAsync(cancellation);
        var projected = db as IEntityProjectedReadSession<T>;
        DemandProjection(projected != null);
        var plan = Plan(bound.Predicate.And(filter), request.Sort, fields);
        long? count = request.IncludeTotal ? await db.CountAsync(plan, cancellation) : null;
        if (projected != null)
        {
            var rows = await projected.PageProjectedAsync(
                plan,
                request.Offset,
                fetchLimit,
                cancellation
            );
            ValidateRows(rows, plan);
            var records = TrimPage(rows, request.Limit, fetchLimit);
            return new(
                bound.Version,
                count,
                DtoMapper.MapValues(schema, records, fields),
                request.IncludeHasMore ? rows.Count > request.Limit : null
            );
        }
        var entities = await db.PageAsync(plan, request.Offset, fetchLimit, cancellation);
        var returned = TrimPage(entities, request.Limit, fetchLimit);
        return new(
            bound.Version,
            count,
            DtoMapper.Map(schema, returned, fields),
            request.IncludeHasMore ? entities.Count > request.Limit : null
        );
    }

    static int FetchLimit(int limit, bool lookahead)
    {
        if (lookahead && limit == int.MaxValue)
            throw new AuthorizationException("Pagination lookahead limit exceeded.");
        return lookahead ? checked(limit + 1) : limit;
    }

    static TRow[] TrimPage<TRow>(IReadOnlyList<TRow> rows, int limit, int fetchLimit)
    {
        if (rows.Count > fetchLimit)
            throw new InvalidOperationException(
                "The entity adapter exceeded the requested row bound."
            );
        return rows.Take(limit).ToArray();
    }

    public async Task<ReadCount> CountAsync(
        CountReadRequest request,
        CancellationToken cancellation = default
    )
    {
        var (bound, trusted) = await Capture(cancellation);
        var filter = ValidateFilter(request.Filter, bound, trusted);
        await using var db = await database.OpenAsync(cancellation);
        var count = await db.CountAsync(Plan(bound.Predicate.And(filter), null), cancellation);
        return new(bound.Version, count);
    }

    public async Task<CursorReadPage> ListCursorAsync(
        CursorListReadRequest request,
        CancellationToken cancellation = default
    )
    {
        var configured =
            cursor
            ?? throw new InvalidOperationException(
                "Cursor protection and host bindings are not configured."
            );
        if (request.Filter != null)
            compiler.DemandCondition(request.Filter);
        request = request with
        {
            Selection = request.Selection?.ToArray(),
            Sort = request.Sort?.ToArray(),
            Filter = CursorTokens.Snapshot(request.Filter),
        };
        var (bound, trusted) = await Capture(cancellation);
        if (
            request.Limit < 1
            || request.Limit > limits.MaxPageSize
            || request.Limit == int.MaxValue
        )
            throw new AuthorizationException("Cursor pagination limit exceeded.");
        var fetchLimit = FetchLimit(request.Limit, true);
        var fields = ValidateSelection(request.Selection, bound);
        var filter = ValidateFilter(request.Filter, bound, trusted);
        ValidateSort(request.Sort, bound);
        var plan = Plan(bound.Predicate.And(filter), request.Sort, fields);
        var binding = CursorTokens.Binding(
            schema,
            compiler,
            bound,
            trusted,
            scope,
            configured,
            fields,
            request.Filter,
            plan,
            request.Limit
        );
        var envelope =
            request.ContinuationToken == null
                ? CursorTokens.New(configured, binding, bound.Version)
                : null;
        EntitySeekPlan<T> seek;
        if (request.ContinuationToken != null)
            (envelope, seek) = CursorTokens.Unprotect(
                configured,
                request.ContinuationToken,
                binding,
                bound.Version,
                plan
            );
        else
            seek = new(plan, null);
        await using var db = await database.OpenAsync(cancellation);
        var projected = db as IEntityProjectedSeekReadSession<T>;
        DemandProjection(projected != null);
        var session = db as IEntitySeekReadSession<T>;
        if (projected == null && session == null)
            throw new NotSupportedException("The entity adapter does not support seek retrieval.");
        long? count = request.IncludeTotal ? await db.CountAsync(plan, cancellation) : null;
        if (projected != null)
        {
            var scalars = await projected.SeekProjectedAsync(seek, fetchLimit, cancellation);
            ValidateRows(scalars, plan, seek);
            var records = TrimPage(scalars, request.Limit, fetchLimit);
            var continuation =
                scalars.Count > request.Limit
                    ? CursorTokens.Protect(
                        configured,
                        envelope!,
                        new EntitySeekPlan<T>(plan, records[^1].ContinuationValues)
                    )
                    : null;
            return new(
                bound.Version,
                count,
                DtoMapper.MapValues(schema, records, fields),
                continuation,
                request.IncludeHasMore ? scalars.Count > request.Limit : null
            );
        }
        var rows = await session!.SeekAsync(seek, fetchLimit, cancellation);
        var returned = TrimPage(rows, request.Limit, fetchLimit);
        var next =
            rows.Count > request.Limit
                ? CursorTokens.Protect(configured, envelope!, Boundary(plan, returned[^1]))
                : null;
        return new(
            bound.Version,
            count,
            DtoMapper.Map(schema, returned, fields),
            next,
            request.IncludeHasMore ? rows.Count > request.Limit : null
        );
    }

    static EntitySeekPlan<T> Boundary(EntityReadPlan<T> read, T row) =>
        new(read, read.Sort.Select(sort => sort.Property.GetValue(row)));

    public async Task<ReadDetail> DetailAsync(
        DetailReadRequest request,
        CancellationToken cancellation = default
    )
    {
        var (bound, trusted) = await Capture(cancellation);
        var fields = ValidateSelection(request.Selection, bound);
        var filter = ValidateFilter(request.Filter, bound, trusted);
        var keyFilter = compiler.Predicate(
            new()
            {
                Field = schema.StableKey.Name,
                Operator = "eq",
                Literal = request.Key,
            },
            trusted
        );
        await using var db = await database.OpenAsync(cancellation);
        var projected = db as IEntityProjectedReadSession<T>;
        DemandProjection(projected != null);
        var plan = Plan(bound.Predicate.And(filter).And(keyFilter), null, fields);
        if (projected != null)
        {
            var row = await projected.SingleProjectedAsync(plan, cancellation);
            if (row == null)
                return new(bound.Version, null);
            row.Validate(plan);
            return new(bound.Version, DtoMapper.MapValues(schema, [row], fields)[0]);
        }
        var entity = await db.SingleAsync(plan, cancellation);
        return new(
            bound.Version,
            entity == null ? null : DtoMapper.Map(schema, new[] { entity }, fields)[0]
        );
    }

    public async IAsyncEnumerable<ExportBatch> ExportAsync(
        ExportReadRequest request,
        [EnumeratorCancellation] CancellationToken cancellation = default
    )
    {
        if (request.Filter != null)
            compiler.DemandCondition(request.Filter);
        request = request with
        {
            Selection = request.Selection?.ToArray(),
            Sort = request.Sort?.ToArray(),
            Filter = CursorTokens.Snapshot(request.Filter),
        };
        var (bound, trusted) = await Capture(cancellation);
        var fields = ValidateSelection(request.Selection, bound);
        var filter = ValidateFilter(request.Filter, bound, trusted);
        ValidateSort(request.Sort, bound);
        var plan = Plan(bound.Predicate.And(filter), request.Sort, fields);
        var seek = new EntitySeekPlan<T>(plan, null);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            await using var db = await database.OpenAsync(cancellation);
            var projected = db as IEntityProjectedSeekReadSession<T>;
            DemandProjection(projected != null);
            if (projected != null)
            {
                var scalars = await projected.SeekProjectedAsync(
                    seek,
                    limits.ExportBatchSize,
                    cancellation
                );
                ValidateRows(scalars, plan, seek);
                var records = TrimPage(scalars, limits.ExportBatchSize, limits.ExportBatchSize);
                if (records.Length == 0)
                    yield break;
                seek = new(plan, records[^1].ContinuationValues);
                yield return new(bound.Version, DtoMapper.MapValues(schema, records, fields));
                if (records.Length < limits.ExportBatchSize)
                    yield break;
                continue;
            }
            var session =
                db as IEntitySeekReadSession<T>
                ?? throw new NotSupportedException(
                    "The entity adapter does not support seek retrieval."
                );
            var rows = await session.SeekAsync(seek, limits.ExportBatchSize, cancellation);
            if (rows.Count > limits.ExportBatchSize)
                throw new InvalidOperationException(
                    "The seek adapter exceeded the requested row bound."
                );
            if (rows.Count == 0)
                yield break;
            seek = Boundary(plan, rows[^1]);
            yield return new(bound.Version, DtoMapper.Map(schema, rows, fields));
            if (rows.Count < limits.ExportBatchSize)
                yield break;
        }
    }
}
