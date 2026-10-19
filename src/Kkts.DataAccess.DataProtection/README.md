# Kkts.DataAccess.DataProtection

Optional .NET 10 Data Protection integration for Kkts.DataAccess. Core has no
ASP.NET dependency. Register Data Protection once and pass a
`DataProtectionContinuationTokenProtector` to `CursorReadConfiguration`.
Use an opaque dataset label (not a connection string) and a server-derived
authenticated subject identifier. Token possession never replaces fresh
authorization or compatibility checks.

## When to use

Use this package for encrypted public forward-cursor continuation tokens.
Offset/detail/count reads need no public cursor protection, and exports use
private boundaries. It does not configure authentication or authorize token holders.

## Requirements and installation

Targets .NET 10. NuGet installs core and
Microsoft.AspNetCore.DataProtection.Extensions 10.0.12 transitively.
An ASP.NET Core web host is not required, but the host must configure Data
Protection services and key management.

```sh
dotnet add package Kkts.DataAccess.DataProtection
```

## Quick start

Call `Register` once during host setup with an application isolation name and a
host-managed key directory. Call `CreateCursor` with your service provider and
a callback that derives the authenticated subject from the operation's captured
trusted context. The host supplies that callback and an opaque dataset label,
not a connection string. Pass the resulting configuration to the protected
repository; these helpers do not construct a complete host.

```csharp
using Kkts.DataAccess;
using Kkts.DataAccess.DataProtection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

public static class CursorSetup
{
    public static void Register(
        IServiceCollection services, string applicationName, DirectoryInfo keyDirectory)
    {
        services.AddDataProtection()
            .SetApplicationName(applicationName)
            .PersistKeysToFileSystem(keyDirectory);
        services.AddSingleton<IContinuationTokenProtector>(provider =>
            new DataProtectionContinuationTokenProtector(
                provider.GetRequiredService<IDataProtectionProvider>()));
    }

    public static CursorReadConfiguration CreateCursor(
        IServiceProvider services,
        Func<TrustedContext, string> authenticatedSubject,
        string datasetId) =>
        new(services.GetRequiredService<IContinuationTokenProtector>(),
            authenticatedSubject, datasetId);
}
```

## Behavior and host responsibilities

The default traversal lifetime is fifteen minutes, with a 16-KiB maximum encoded
token and bounded decrypted payload. Host overrides must be positive; tests may
inject a TimeProvider. Lifetime does not slide on resume. Tokens use authenticated
encryption with a protocol-specific purpose; hidden boundary values do not appear
in plaintext. Do not log tokens, keys, or trusted context values.

Hosts own key persistence, access controls, encryption at rest, application
isolation, sharing across instances, rotation, and retirement. Retain decryptable
keys through token expiry for restart/rotation continuity. Instances must use
compatible keys and application configuration as well as matching current
authorization, query, subject, and dataset. Key-ring loss, incompatible isolation,
expiry, and tampering produce the same invalid-token error, never a restart.
Change dataset identity when routing, mappings, collation, or seek semantics change.
No fixed production keys are supplied, and exports use private boundaries without
requiring public-token configuration.

## Related packages and documentation

- [Core cursor contract](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/reads.md#forward-cursors).
- [Optional DI integration](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.DependencyInjection/README.md)
  for registering protection, trusted subject, and cursor options together.
- [Working host key setup](https://github.com/linhnle/Kkts.DataAccess/tree/main/examples/EmployeeApp).
