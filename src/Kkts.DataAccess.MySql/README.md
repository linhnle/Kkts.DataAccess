# Kkts.DataAccess.MySql

MySQL entity-read integration for Kkts.DataAccess using EF Core.

## When to use

Use this package when protected entities live in MySQL. It supplies the MySQL
adapter factory and provider-specific export snapshot setup.
It uses Oracle's `MySql.EntityFrameworkCore`, not Pomelo.
It does not supply MySQL policy persistence: use a separate policy source.
The repository's host uses `SqlitePolicyStore` from the optional
`Kkts.DataAccess.Sqlite` package even with MySQL entities.
Skip this package when using a different entity provider.

## Requirements and installation

Targets .NET 10. NuGet installs the shared EF integration, core, and Oracle
MySql.EntityFrameworkCore 10.0.9 transitively. The shared integration references
EF Core Relational 10.0.12. MySQL 8.4 is the tested server baseline; arbitrary
server/model combinations are not guaranteed.

```sh
dotnet add package Kkts.DataAccess.MySql
```

## Quick start

The host supplies its entity schema and `IDbContextFactory<TContext>` configured
with Oracle's `UseMySQL`, mapped entities, and a trusted connection string.
This helper validates the adapter; it does not provision MySQL or construct a
complete authorized repository.

```csharp
using Kkts.DataAccess;
using Kkts.DataAccess.EntityFrameworkCore;
using Kkts.DataAccess.MySql;
using Microsoft.EntityFrameworkCore;

public static class MySqlSetup
{
    public static Task<EfEntityReadAdapter<TEntity>> CreateAsync<TEntity, TContext>(
        ResourceSchema<TEntity> schema,
        IDbContextFactory<TContext> factory,
        CancellationToken cancellation)
        where TEntity : class
        where TContext : DbContext =>
        MySqlEntityReads.CreateAsync<TEntity, TContext>(
            schema, factory, ReadOperations.All, cancellation);
}
```

Pass the validated adapter to a protected repository or optional DI registration,
along with a compiler, active policy source, mandatory constraints, and trusted
context. The raw adapter alone does not enforce the repository's policy boundary.

## Behavior and responsibilities

- Hosts own entity migrations, credentials, server settings, and policy storage.
  Registration does not provision databases.
- Factories must create independently owned contexts; the adapter disposes them.
- Collation, scalar mappings, and operator translation remain provider-specific.
  Translation errors propagate without local authorization fallback.
- Snapshot exports require declaring snapshot capability and a verified ordinary
  InnoDB table. The provider uses a repeatable-read transaction and verifies its
  continuity. Default exports are best-effort, not snapshots.

## Related packages and documentation

- [Core](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess/README.md),
  [shared EF integration](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.EntityFrameworkCore/README.md),
  and [optional SQLite policy store](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.Sqlite/README.md).
- [Optional DI integration](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.DependencyInjection/README.md),
  [provider setup](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/providers.md),
  and [export semantics](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/reads.md).
