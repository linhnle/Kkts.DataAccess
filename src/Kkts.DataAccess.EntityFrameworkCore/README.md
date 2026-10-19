# Kkts.DataAccess.EntityFrameworkCore

Shared EF Core relational adapters for protected reads, scalar projection, and
keyset traversal.

## When to use

Use this package when composing an EF relational adapter directly. Most
applications should install `Kkts.DataAccess.Sqlite`, `.SqlServer`, or `.MySql`
instead: each brings this shared integration transitively and supplies its
provider identity and snapshot implementation.

This package alone supplies neither a database driver nor policy persistence.
Custom non-EF adapters need only core.

## Requirements and installation

Targets .NET 10. NuGet installs `Kkts.DataAccess` 0.1.0 and
Microsoft.EntityFrameworkCore.Relational 10.0.12 transitively.
A host must also reference and configure its chosen EF database provider.

```sh
dotnet add package Kkts.DataAccess.EntityFrameworkCore
```

## Quick start

This reusable helper takes an application-defined entity schema and
`IDbContextFactory<TContext>`. The factory must create independently owned
contexts with the entity mapped and a relational provider configured.
`providerName` must exactly match that provider's EF identity; supplying a name
does not add support for an untested provider.

```csharp
using Kkts.DataAccess;
using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public static class RelationalReads
{
    public static Task<EfEntityReadAdapter<TEntity>> CreateAsync<TEntity, TContext>(
        ResourceSchema<TEntity> schema,
        IDbContextFactory<TContext> factory,
        string providerName,
        CancellationToken cancellation)
        where TEntity : class
        where TContext : DbContext =>
        EfEntityReadAdapter<TEntity>.RegisterAsync(
            schema, factory, providerName,
            operations: ReadOperations.All,
            cancellation: cancellation);
}
```

Pass the validated adapter to a protected repository or the optional
[DI integration](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.DependencyInjection/README.md).
The adapter is not an authorization entry point on its own: repositories must
apply policies and trusted context before opening sessions.

## Behavior and responsibilities

- Registration validates the model and provider identity without provisioning
  a database or executing an application read.
- Contexts created by the factory are owned and disposed by the adapter.
  Do not return a shared request context as an independently owned context.
- SQL translation, collation, scalar types, projection, and seek support remain
  provider/model-specific. Translation errors propagate without local
  authorization evaluation or unrestricted-query fallback.
- `ReadOperations.All` covers reads, not snapshot capability. The helper above
  does not configure snapshots; use a concrete provider's setup and prerequisites
  when export-wide snapshots are needed.

## Related packages and documentation

- [Core contracts](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess/README.md).
- [SQLite](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.Sqlite/README.md),
  [SQL Server](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.SqlServer/README.md),
  and [MySQL](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.MySql/README.md).
- [Provider capabilities and snapshot prerequisites](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/providers.md)
  and [adapter contracts](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/repositories.md).
