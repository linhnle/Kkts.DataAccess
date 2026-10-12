using System.Text.Json;
using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.Sqlite;

/// <summary>Source-compatible SQLite facade delegating to the provider-neutral repository.</summary>
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
        ReadLimits? limits = null
    )
    {
        repository = new(
            schema,
            compiler,
            store,
            new EfEntityReadAdapter<T>(database, "Microsoft.EntityFrameworkCore.Sqlite"),
            context,
            scope,
            limits
        );
    }

    public Task<ReadPage> ListAsync(
        ReadRequest request,
        CancellationToken cancellation = default
    ) => repository.ListAsync(request, cancellation);

    public Task<long> CountAsync(ReadRequest request, CancellationToken cancellation = default) =>
        repository.CountAsync(request, cancellation);

    public Task<ReadDetail> DetailAsync(
        JsonElement key,
        ReadRequest request,
        CancellationToken cancellation = default
    ) => repository.DetailAsync(key, request, cancellation);

    public IAsyncEnumerable<ExportBatch> ExportAsync(
        ReadRequest request,
        CancellationToken cancellation = default
    ) => repository.ExportAsync(request, cancellation);
}
