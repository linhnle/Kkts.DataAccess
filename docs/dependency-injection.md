# Optional dependency injection integration

`Kkts.DataAccess.DependencyInjection` targets .NET 10 and references core plus
Microsoft.Extensions DI/options/hosting. It does not reference EF or ASP.NET.
Direct repository and SQLite facade construction remain supported; core has no
DI or ASP.NET dependency.

## Resource registration

Each resource has an explicit key and a complete, server-owned configuration.
For a SQLite host using the types in [EmployeeApp](../examples/EmployeeApp):

```csharp
using Kkts.DataAccess;
using Kkts.DataAccess.DependencyInjection;
using Kkts.DataAccess.Sqlite;
using Microsoft.EntityFrameworkCore;

services.AddHttpContextAccessor();
services.AddPooledDbContextFactory<EmployeeDatabase>(
    options => options.UseSqlite(entityConnection));
services.AddAuthorizedReads<Employee>("employees", resource =>
    resource.WithSchema(EmployeeConfiguration.Schema)
        .WithCompiler(EmployeeConfiguration.Registry, EmployeeConfiguration.Mandatory)
        .WithPolicySource(new SqlitePolicyStore(policyConnection))
        .WithAdapter(provider => SqliteEntityReads.Create<Employee, EmployeeDatabase>(
            EmployeeConfiguration.Schema,
            provider.GetRequiredService<IDbContextFactory<EmployeeDatabase>>()))
        .WithTrustedContext<EmployeeTrustedContextProvider>()
        .WithScope(trusted => "tenant-" + trusted.Values["tenant"])
        .WithOptions(new ResourceReadOptions
        {
            Limits = new ReadLimits(100, 10_000, 50),
            ProjectionMode = ReadProjectionMode.Required,
            Export = new ExportReadConfiguration(),
        }));
```

The example's trusted provider reads authenticated server claims and returns an
immutable `TrustedContext`; an application provider may instead await trusted
identity/tenant services. Never construct it from read-request JSON. Acquisition
is awaited once per operation, including counts and detail. Cancellation and
identity failures propagate before policy capture or entity-session access.

`WithCompiler` accepts optional `cacheOptions: new PolicyCompilerCacheOptions(MaxEntries: 32)`.
The immutable configuration is captured at registration and used by the shared
resource compiler. Omitted options retain the default 128-entry FIFO capacity;
see [compiler retention](policies.md#bounded-compiler-templates) for lifetime and
authoritative-source content guarantees.

Scope callbacks receive that operation's captured context, not a second request
identity lookup. Cursor subjects use the same rule:

```csharp
resource
    .WithCursorProtection(provider =>
        provider.GetRequiredService<IContinuationTokenProtector>())
    .WithCursorSubject(trusted => (string)trusted.Values["subject"]!)
    .WithOptions(new ResourceReadOptions
    {
        Cursor = new ResourceCursorOptions(
            DatasetId: "employee-dataset-v1",
            Lifetime: TimeSpan.FromMinutes(15)),
    });
```

Register a real protector, such as the optional Data Protection implementation,
with identity-free dependencies. `subject` above is a server-supplied snapshot
value, not a client field. The protector, subject callback and cursor metadata
must be configured together. Reject missing subject values in the trusted
provider or callback; empty subjects are denied. Read projection/export settings
remain host-only. See [cursor protection](reads.md#forward-cursors).

## Lifetimes and keyed resolution

- Schema, context registry, immutable options and identity-free compiler are
  singleton **per resource**. The mandatory condition is copied at registration;
  retaining and mutating the builder cannot change the registered configuration.
- Repository and trusted-context provider are scoped. A provider must not retain
  identity from a different request. `WithTrustedContext<TProvider>()` constructs
  a scoped provider with its registered constructor dependencies.
- Adapter and policy-source resolver factories default to scoped and may
  explicitly use `ServiceLifetime.Transient`. Resolver-backed singleton factories
  are rejected, even when container scope validation is disabled. For a safe,
  identity-free shared adapter/source, use the instance overload and let the host
  manage that instance's lifetime. Do not capture a request service in it.
- Use container `ValidateScopes` and `ValidateOnBuild` as well. The library cannot
  inspect arbitrary closure captures or validate every host factory dependency.

For exactly one resource of an entity type, unkeyed and keyed resolution return
the same scoped repository:

```csharp
var repository = scope.ServiceProvider
    .GetRequiredService<IAuthorizedReadRepository<Employee>>();
var keyed = scope.ServiceProvider
    .GetRequiredKeyedService<IAuthorizedReadRepository<Employee>>("employees");
```

Multiple resources of the same entity type require explicit keys. Unkeyed
resolution throws a structured ambiguity error rather than choosing the last
registration. Duplicate `(entity type, resource key)` registrations are rejected.
Different entity types may reuse a key without sharing their registry/options,
policy source, provider or cursor protection bindings.

An export is lazy: it acquires identity on first advancement, retains the captured
context and policy scope throughout enumeration, and does not reacquire identity
between batches. Start, consume and dispose the enumerator **within the owning
DI scope**. For a background export, create a dedicated scope and configure a
server-owned identity provider suitable for that job; never pass a request-scoped
repository to work that outlives its request.

## EF ownership

Provider helpers accept `IDbContextFactory<TContext>` and use
`CreateDbContextAsync(cancellation)` for independently owned contexts. The factory
may be pooled. Do not replace it with a DI-resolved request `DbContext`.

Each ordinary/best-effort session owns and disposes its factory-created context.
Snapshot exports retain one session/context until completion, failure,
cancellation or early enumerator disposal. Provider/setup failures after creation
also dispose the owned context. Pooled contexts are returned/reset on disposal;
the application remains responsible for custom mutable state in its own contexts.
Synchronous `Func<DbContext>` factories and direct facade constructors remain
valid. See [provider registration](providers.md).

## Startup validation versus runtime

`AddAuthorizedReads` registers one hosted startup validator for all resources.
In a host without hosted-service startup, explicitly await:

```csharp
await provider.GetRequiredService<IAuthorizedReadRegistrationValidator>()
    .ValidateAsync(cancellation);
```

Validation checks required configuration, read/projection/cursor invariants,
mandatory schema/context structure and registered options validators. EF adapters
implement the core `IEntityReadRegistrationValidator<T>` seam: they inspect the
provider and entity/scalar mappings using a temporary owned context and dispose
it asynchronously. Custom adapters can implement that interface or use
`ValidateWith(async (services, cancellation) => { /* host metadata checks */ })`.
Hooks must be identity-free and must honor cancellation.

Schema-aware EF validation captures an immutable provider/model profile before
creating the resource's singleton compiler. Repository resolution requires completed
startup validation; compiler resolution requires completed metadata validation.
Non-host consumers must await the validator before resolving either. This is an
intentional startup-order migration, not synchronous blocking of async factories.
Explicit `WithCapabilities(profileA, profileB)` targets are copied and merged
with the adapter profile, never overwritten. Policy validation must satisfy every
target; compiler template and cursor binding identities include that frozen set.
See [capability scope and policy validation](provider-capabilities.md).

Startup does **not**:

- Acquire trusted request context, derive a tenant scope/subject, or capture an
  active policy. A draft-only or unpublished store can start successfully.
- Run an authorized sample query, open an entity read session, provision a
  database, migrate, publish policies or enable snapshot settings.
- Prove arbitrary query translation, database connectivity, every adapter capability,
  transaction prerequisites or later policy/context validity.

Identity-free adapter/protector factories are resolved during validation, so those
host factories must not themselves perform provisioning or request lookups.
Runtime checks remain authoritative; there are no unrestricted-query fallbacks.
`DataAccessConfigurationException` carries finite configuration diagnostic codes,
paths and optional internal causes. It is a host/configuration error, **not** a
client `invalid_request` error. Keep startup diagnostics in operator logs rather
than sending internal details to clients. See [structured runtime errors](errors.md).

EmployeeApp opts into scoped/keyed resolution and pooled factory ownership in
[Program.cs](../examples/EmployeeApp/Program.cs). Its explicit synthetic
provisioning/seeding belongs to the example host, not the registration validator.
