# Kkts.DataAccess.Sqlite

SQLite policy persistence and EF Core entity-read integration for Kkts.DataAccess.

## When to use

Use this package for either or both of two distinct roles:

- `SqlitePolicyStore`: policy drafts, immutable published versions, activation,
  and audit history.
- `SqliteEntityReads`: protected entity reads against SQLite through EF Core.

The policy store can also accompany SQL Server or MySQL entity adapters.
Installing an entity provider does not move policies into that entity database.
Applications with a custom policy source and a different entity provider do not
need this package.

## Requirements and installation

Targets .NET 10. NuGet installs core, the shared EF integration, and
Microsoft.EntityFrameworkCore.Sqlite 10.0.12 transitively.

```sh
dotnet add package Kkts.DataAccess.Sqlite
```

## Quick start

These helpers provision the policy store explicitly and separately validate an
entity adapter. The host supplies the policy connection string, entity schema,
and an `IDbContextFactory<TContext>` configured with `UseSqlite` and mapped
application entities. They may use separate database files. This is composition
code, not a complete application or policy-publication example.

```csharp
using Kkts.DataAccess;
using Kkts.DataAccess.EntityFrameworkCore;
using Kkts.DataAccess.Sqlite;
using Microsoft.EntityFrameworkCore;

public static class SqliteSetup
{
    public static async Task<SqlitePolicyStore> InitializePoliciesAsync(
        string policyConnectionString, CancellationToken cancellation)
    {
        var store = new SqlitePolicyStore(policyConnectionString);
        await store.InitializeAsync(cancellation);
        return store;
    }

    public static Task<EfEntityReadAdapter<TEntity>> CreateEntityReadsAsync<TEntity, TContext>(
        ResourceSchema<TEntity> schema,
        IDbContextFactory<TContext> factory,
        CancellationToken cancellation)
        where TEntity : class
        where TContext : DbContext =>
        SqliteEntityReads.CreateAsync<TEntity, TContext>(
            schema, factory, ReadOperations.All, cancellation);
}
```

Pass the store and adapter to a protected repository, with a compiler,
server-owned mandatory constraints and trusted context. Initializing a policy
database does not publish an active policy or grant access.

## Behavior and responsibilities

- Hosts own database paths, access controls, entity schema migrations, and
  authenticated administrator authorization for policy changes.
- Entity registration checks configuration/model/provider identity; it does
  not initialize the policy database or migrate entity tables.
- Retrieval uses mapped scalar database projection by default. Unsupported
  translations fail rather than evaluating authorization locally.
- Snapshot exports require explicit snapshot capability, an ordinary mapped
  main-database table, host-provisioned WAL, an explicit private-cache connection,
  and no dirty reads. Defaults do not promise a snapshot.
- Preserve draft/active revision checks when publishing or rolling back policies.

## Related packages and documentation

- [Shared EF integration](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.EntityFrameworkCore/README.md)
  and [optional DI setup](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.DependencyInjection/README.md).
- [Policy lifecycle](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/lifecycle.md),
  [provider requirements](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/providers.md),
  and [working host](https://github.com/linhnle/Kkts.DataAccess/tree/main/examples/EmployeeApp).
