# Kkts.DataAccess.SqlServer

SQL Server entity-read integration for Kkts.DataAccess using EF Core.

## When to use

Use this package when protected entities live in SQL Server. It supplies the
SQL Server adapter factory and provider-specific export snapshot setup.
It does not supply SQL Server policy persistence. Use a separate policy source;
the repository's host uses `SqlitePolicyStore` from the optional
`Kkts.DataAccess.Sqlite` package even when entities live in SQL Server.
Skip this package when using a different entity provider.

## Requirements and installation

Targets .NET 10. NuGet installs the shared EF integration, core, and
Microsoft.EntityFrameworkCore.SqlServer 10.0.12 transitively.
SQL Server 2022 is the repository's tested server baseline, not a claim of
compatibility with every server/model combination.

```sh
dotnet add package Kkts.DataAccess.SqlServer
```

## Quick start

The host supplies its entity schema and `IDbContextFactory<TContext>` configured
with `UseSqlServer`, mapped entities, and a trusted connection string. This
helper validates the adapter; it neither provisions SQL Server nor constructs
a complete authorized repository.

```csharp
using Kkts.DataAccess;
using Kkts.DataAccess.EntityFrameworkCore;
using Kkts.DataAccess.SqlServer;
using Microsoft.EntityFrameworkCore;

public static class SqlServerSetup
{
    public static Task<EfEntityReadAdapter<TEntity>> CreateAsync<TEntity, TContext>(
        ResourceSchema<TEntity> schema,
        IDbContextFactory<TContext> factory,
        CancellationToken cancellation)
        where TEntity : class
        where TContext : DbContext =>
        SqlServerEntityReads.CreateAsync<TEntity, TContext>(
            schema, factory, ReadOperations.All, cancellation);
}
```

Pass the validated adapter to a protected repository or optional DI registration,
along with a compiler, active policy source, mandatory constraints, and trusted
context. A raw adapter is not a substitute for the protected repository.

## Behavior and responsibilities

- Hosts own entity migrations, credentials, server settings, and policy storage.
  There is no implicit database provisioning.
- Factories must create independently owned contexts; the adapter disposes them.
- Database collation and mapped scalar/operator support affect translation and
  ordering. Translation failures propagate without local authorization fallback.
- Snapshot exports require declaring snapshot capability and fully enabling
  `ALLOW_SNAPSHOT_ISOLATION`; the mapped table must be ordinary, local,
  non-temporal, and not memory-optimized. Default exports are best-effort, not
  snapshots. The library does not enable database settings.

## Related packages and documentation

- [Core](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess/README.md),
  [shared EF integration](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.EntityFrameworkCore/README.md),
  and [optional SQLite policy store](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.Sqlite/README.md).
- [Optional DI integration](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.DependencyInjection/README.md),
  [provider setup](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/providers.md),
  and [export semantics](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/reads.md).
