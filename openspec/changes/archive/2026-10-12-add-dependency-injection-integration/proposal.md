# Proposal

## Why

Hosts manually construct schemas, compilers, policy sources, and repositories and provide synchronous context/database delegates. Supply validated registrations with asynchronous server-derived context and EF factory ownership without coupling core to ASP.NET.

## What Changes

- Add a fluent optional DI package, per-resource registrations, options/startup validation, and explicit service lifetimes.
- Add an asynchronous trusted-context provider, capturing one context per operation/export without sync-over-async.
- Add EF `IDbContextFactory<TContext>` support using asynchronous creation and owned disposal.
- Preserve direct construction and explicit host configuration for projection, cursor, and export modes.
- Reject contradictory registrations/options at startup; do not impersonate users or automatically migrate/provision databases.

## Capabilities

### New Capabilities

- `host-integration`: Fluent DI registration, startup validation, async context capture, and EF factory integration.

### Modified Capabilities

None. Existing identity/ownership semantics remain.

## Impact

New `Kkts.DataAccess.DependencyInjection`, a core async context seam, shared EF factories, SQLite facade, EmployeeApp, tests, and docs. Depends on `add-structured-errors` for actionable validation. Capability validation will extend the registration hook later; this proposal does not require that later change or active published policy at startup.
