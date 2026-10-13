# Protected forward cursors

Optional .NET 10 Data Protection integration for Kkts.DataAccess. Core has no
ASP.NET dependency. Register Data Protection once and pass a
`DataProtectionContinuationTokenProtector` to `CursorReadConfiguration`.
Use an opaque dataset label (not a connection string) and a server-derived
authenticated subject identifier. Token possession never replaces fresh
authorization or compatibility checks.

```csharp
services.AddDataProtection()
    .SetApplicationName(applicationName)
    .PersistKeysToFileSystem(new DirectoryInfo(hostManagedKeyDirectory));
var protector = new DataProtectionContinuationTokenProtector(provider);
var cursor = new CursorReadConfiguration(protector, authenticatedSubjectId, datasetId);
```

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
