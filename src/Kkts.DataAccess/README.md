# Kkts.DataAccess

Schema-controlled record- and field-level authorization for .NET data access,
with provider-neutral protected read contracts.

## When to use

Use this package to define resource schemas, compile policies, capture trusted
server context, and execute protected list, detail, count, cursor, and export
operations through an adapter. Use it directly for custom adapters or direct
repository construction. Installing a concrete provider package brings core
transitively; a separate core installation is then unnecessary.

This is library-enforced authorization, not native database row-level security.
The package does not include a database adapter, policy database, HTTP middleware,
or dependency injection integration.

## Requirements and installation

Targets .NET 10 (`net10.0`). NuGet installs Kkts.Expressions 3.1.0 and
Kkts.Dynamic 2.1.0 transitively.

```sh
dotnet add package Kkts.DataAccess
```

## Quick start

This schema and read helper are compilable building blocks, not a complete host.
The application must supply an `IAuthorizedReadRepository<Employee>` configured
with this schema, a compiler and mandatory server constraints, an active policy
source, an entity adapter, and a trusted context provider. Defining a schema
declares eligible operations; it does not grant access.

```csharp
using Kkts.DataAccess;

public sealed class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public static class EmployeeReads
{
    public static ResourceSchema<Employee> Schema { get; } = new(
        "employees",
        "1",
        [
            ResourceSchema<Employee>.Field("Id", e => e.Id,
                [FieldOperation.Select, FieldOperation.Filter, FieldOperation.Sort]),
            ResourceSchema<Employee>.Field("Name", e => e.Name,
                [FieldOperation.Select, FieldOperation.Filter, FieldOperation.Sort]),
        ],
        keyFields: ["Id"]);

    public static Task<ReadPage> ListNamesAsync(
        IAuthorizedReadRepository<Employee> repository,
        CancellationToken cancellation) =>
        repository.ListAsync(
            new ListReadRequest(Selection: ["Name"], Limit: 20, IncludeTotal: false),
            cancellation);
}
```

See the [repository setup](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/repositories.md)
and [working host](https://github.com/linhnle/Kkts.DataAccess/tree/main/examples/EmployeeApp)
for full composition.

## Behavior and responsibilities

- Policies combine mandatory constraints with allow/deny rules and independent
  field grants. Hosts own authentication, trusted context, and policy administration.
  Never accept identity or mandatory tenant constraints from read-request JSON.
- Default retrieval requires database scalar projection. Custom adapters must
  declare and implement the needed capabilities; unsupported operations fail
  explicitly rather than retrying unrestricted access.
- List/count/detail/export have distinct requests. Detail keys are exact
  alias-keyed objects; absent and inaccessible records both yield a null detail.
- Cursor tokens require an explicit protector and fresh authorization. Exports
  use private keyset boundaries; default traversal is not a database snapshot.
- This release supports reads and mapped scalar projection, not authorized
  writes, relationships, aggregation, or masking.

## Related packages and documentation

- [EF Core and entity providers](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/providers.md):
  `Kkts.DataAccess.EntityFrameworkCore`, `.Sqlite`, `.SqlServer`, and `.MySql`.
- [Optional dependency injection](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.DependencyInjection/README.md),
  [HTTP errors](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.AspNetCore/README.md),
  and [cursor protection](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.DataProtection/README.md).
- [Policy contract](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/policies.md)
  and [read operations](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/reads.md).
