# Kkts.DataAccess.DependencyInjection

Dependency injection registration, scoped resource management, and startup
validation for protected reads.

## When to use

Use this optional package in Microsoft.Extensions.DependencyInjection/hosting
applications that need scoped or keyed authorized repositories. Direct
repository constructors remain supported without it.
The package does not reference EF Core or ASP.NET Core; choose an adapter and
policy source independently.

## Requirements and installation

Targets .NET 10. NuGet installs core plus Microsoft.Extensions.DependencyInjection,
Hosting.Abstractions, and Options 10.0.0 transitively.

```sh
dotnet add package Kkts.DataAccess.DependencyInjection
```

## Quick start

Call this helper from your host's service registration. Supply an
application-defined schema, context registry, mandatory condition, policy source,
and adapter. `TContextProvider` is your `ITrustedContextProvider` implementation
that acquires authenticated server context; its constructor dependencies must
be registered in DI. The scope callback must derive its value from that trusted
snapshot, never request JSON.

```csharp
using Kkts.DataAccess;
using Kkts.DataAccess.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

public static class ResourceRegistration
{
    public static IServiceCollection AddEmployeeReads<TEntity, TContextProvider>(
        IServiceCollection services,
        ResourceSchema<TEntity> schema,
        ContextRegistry registry,
        Condition mandatory,
        IActivePolicySource policies,
        IEntityReadAdapter<TEntity> adapter,
        Func<TrustedContext, string> policyScope)
        where TEntity : class
        where TContextProvider : class, ITrustedContextProvider =>
        services.AddAuthorizedReads<TEntity>("employees", resource =>
            resource.WithSchema(schema)
                .WithCompiler(registry, mandatory)
                .WithPolicySource(policies)
                .WithAdapter(adapter)
                .WithTrustedContext<TContextProvider>()
                .WithScope(policyScope)
                .WithOptions(new ResourceReadOptions
                {
                    ProjectionMode = ReadProjectionMode.Required,
                }));
}
```

The source and adapter instances in this example must be host-owned and safe
to share. Use the scoped/transient factory overloads instead when they depend
on scoped services. Installing this integration alone does not configure an EF
factory or database; see the
[full host setup](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/dependency-injection.md).

## Behavior and responsibilities

- Hosted startup performs asynchronous structural/model/provider validation
  without requiring request identity or an active published policy. It does not
  migrate databases or issue sample authorized reads. Non-hosted applications
  should invoke `IAuthorizedReadRegistrationValidator.ValidateAsync` explicitly.
- Repositories are scoped and keyed by resource. Unkeyed resolution is allowed
  only when one resource uses the entity type; otherwise use
  `GetRequiredKeyedService<IAuthorizedReadRepository<TEntity>>("employees")`
  from a scope.
- Trusted context is captured asynchronously once per operation; export retains
  that snapshot. Cancellation and identity failures propagate before entity access.
- Singleton compilers/factories must not capture request services. EF adapters
  require independently owned contexts, not a shared request `DbContext`.
- Cursor support additionally requires a protector, trusted subject callback,
  and dataset/options configuration together.

## Related packages and documentation

- [Core contracts](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess/README.md)
  and [provider setup](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/providers.md).
- [Optional cursor protection](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.DataProtection/README.md)
  and [ASP.NET Core errors](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.AspNetCore/README.md).
