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
        ReadProjectionMode projectionMode = ReadProjectionMode.Required
    )
    {
        repository = new(
            schema,
            compiler,
            store,
            new EfEntityReadAdapter<T>(database, "Microsoft.EntityFrameworkCore.Sqlite"),
            context,
            scope,
            limits,
            cursor,
            projectionMode
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
