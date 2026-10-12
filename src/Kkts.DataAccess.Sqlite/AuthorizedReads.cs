using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.Sqlite;

/// <summary>SQLite facade with operation-specific inputs delegating to the shared repository.</summary>
public sealed class AuthorizedReads<T> : IAuthorizedReadRepository<T>
    where T : class
{
    readonly AuthorizedReadRepository<T> repository;

    public AuthorizedReads(
        ResourceSchema<T> schema,
        PolicyCompiler<T> compiler,
        SqlitePolicyStore store,
        Func<DbContext> database,
        Func<TrustedContext> context,
        string scope,
        ReadLimits? limits = null,
        CursorReadConfiguration? cursor = null,
        ReadProjectionMode projectionMode = ReadProjectionMode.Required,
        ExportReadConfiguration? export = null
    )
        : this(
            schema,
            compiler,
            store,
            database,
            new SynchronousTrustedContextProvider(context),
            scope,
            limits,
            cursor,
            projectionMode,
            export
        ) { }

    public AuthorizedReads(
        ResourceSchema<T> schema,
        PolicyCompiler<T> compiler,
        SqlitePolicyStore store,
        Func<DbContext> database,
        ITrustedContextProvider context,
        string scope,
        ReadLimits? limits = null,
        CursorReadConfiguration? cursor = null,
        ReadProjectionMode projectionMode = ReadProjectionMode.Required,
        ExportReadConfiguration? export = null
    )
    {
        repository = new(
            schema,
            compiler,
            store,
            new EfEntityReadAdapter<T>(
                database,
                "Microsoft.EntityFrameworkCore.Sqlite",
                SqliteEntityReads.BeginSnapshot<T>
            ),
            context,
            scope,
            limits,
            cursor,
            projectionMode,
            export
        );
    }

    AuthorizedReads(AuthorizedReadRepository<T> repository) => this.repository = repository;

    public static async Task<AuthorizedReads<T>> CreateAsync<TContext>(
        ResourceSchema<T> schema,
        PolicyCompiler<T> compiler,
        SqlitePolicyStore store,
        IDbContextFactory<TContext> database,
        ITrustedContextProvider context,
        string scope,
        ReadLimits? limits = null,
        CursorReadConfiguration? cursor = null,
        ReadProjectionMode projectionMode = ReadProjectionMode.Required,
        ExportReadConfiguration? export = null,
        ReadOperations operations = ReadOperations.All,
        CancellationToken cancellation = default
    )
        where TContext : DbContext
    {
        if (export?.DataConsistency == ExportDataConsistency.Snapshot)
            operations |= ReadOperations.Snapshot;
        var adapter = SqliteEntityReads.Create<T, TContext>(schema, database, operations);
        await adapter.ValidateAsync(schema, cancellation);
        return new(
            new AuthorizedReadRepository<T>(
                schema,
                compiler,
                store,
                adapter,
                context,
                scope,
                limits,
                cursor,
                projectionMode,
                export
            )
        );
    }

    public Task<ReadPage> ListAsync(
        ListReadRequest request,
        CancellationToken cancellation = default
    ) => repository.ListAsync(request, cancellation);

    public Task<ReadCount> CountAsync(
        CountReadRequest request,
        CancellationToken cancellation = default
    ) => repository.CountAsync(request, cancellation);

    public Task<CursorReadPage> ListCursorAsync(
        CursorListReadRequest request,
        CancellationToken cancellation = default
    ) => repository.ListCursorAsync(request, cancellation);

    public Task<ReadDetail> DetailAsync(
        DetailReadRequest request,
        CancellationToken cancellation = default
    ) => repository.DetailAsync(request, cancellation);

    public IAsyncEnumerable<ExportBatch> ExportAsync(
        ExportReadRequest request,
        CancellationToken cancellation = default
    ) => repository.ExportAsync(request, cancellation);
}
