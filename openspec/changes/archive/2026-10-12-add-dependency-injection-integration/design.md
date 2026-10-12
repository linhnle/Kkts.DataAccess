# Design

## Context

See [proposal.md](./proposal.md). Core repositories accept synchronous `Func<TrustedContext>` and adapters accept `Func<DbContext>`. Export has a separate capture path and retains context once. EF registration inspects a temporary model context; policy/entity stores remain independent. Hosts currently create these dependencies manually in EmployeeApp.

## Goals / Non-Goals

**Goals:** safe scopes and awaited capture, preserved direct construction, and explicit factory/session ownership.

**Non-goals:** HTTP identity conventions in core, active-policy publication at startup, auto database migrations, or implicit singleton identity.

## Decisions

### Packages and fluent registrations

Add core-only `Kkts.DataAccess.DependencyInjection` referencing core and the repository-compatible Microsoft.Extensions DI/options/hosting abstractions. Keep EF factory creation in the EF package; generic fluent registration accepts adapter factories from the host, avoiding a DI-package-to-EF dependency. ASP.NET remains in its optional integration package.

A fluent resource builder requires a schema/compiler/source/adapter and trusted-context registration, and configures existing read/projection/cursor/export options. Identify registrations by entity type plus explicit resource key; reject ambiguous unkeyed multiple resources instead of last-registration-wins. One resource can use the ordinary generic interface; multiple resources use explicit keyed resolution.

Schemas, context registries, immutable options, and identity-free compilers are singleton per registration. Repository and trusted-context provider are scoped. Adapter lifetimes match registered factories; no singleton resolves scoped identity/DbContext dependencies. Host scope and cursor subject ID are derived server-side and snapped with the operation identity, never request payload fields.

### Async core seam

Add `ITrustedContextProvider.GetAsync(CancellationToken)` returning ValueTask of a TrustedContext snapshot. Introduce an overload accepting this interface; retain synchronous constructor delegates via a small synchronous-provider wrapper, not `.Result`/GetAwaiter blocking. Route ordinary capture and stamped export capture through the same awaited context helper once per operation.

Capture/check cancellation before entity access. Exports acquire context on first advancement, retain the snapshot, and do not resolve request services after their scope has been disposed; document that enumeration lifetime must fit the host scope. A provider failure propagates rather than returning empty/default roles.

### Startup validation

Expose an explicit async registration validator and hosted startup adapter, using options validation for pure invariants. Inspect model/provider with a temporary owned factory-created context; no authorized sample data query, request context call, active policy requirement, or database provisioning. Structural mandatory/schema checks run once; later capability-profile validation attaches to this hook.

Runtime snapshot settings remain runtime checks; startup success is not a data-access/translation guarantee. Validate registration conflicts/options with structured configuration diagnostics from the errors prerequisite.

### EF factory ownership

Add generic factory overload using `IDbContextFactory<TContext>.CreateDbContextAsync(token)` in OpenAsync and async registration validation. Preserve synchronous direct factory overloads without ambiguous lambda signatures. Use a common internal owned async factory seam, disposing a created context if provider/setup checks fail. Session owns context exactly once; snapshot export keeps one session while best-effort uses per-batch sessions.

Never substitute a DI-resolved request DbContext for independently owned factory creation. Pooled factory contexts require reset/disposal tests; no new retry behavior is introduced.

## Risks / Trade-offs

- [Captive scope or export outlives request] -> Scoped repository/provider and startup lifetime checks, documented enumeration ownership, two-scope tests.
- [Constructor overload ambiguity] -> Interface-based async overload; retain distinct existing delegate signatures.
- [Startup mistaken for readiness/authorization] -> Explicit no-active-policy/no-sample-query contract and runtime failures preserved.

## Migration Plan

Apply structured errors first. Add async core seam and EF factory support, then optional DI package/options validator, then EmployeeApp registration example and tests. Direct constructors remain valid; hosts opt into the package. Provider capability validation extends the hook in its own change.
