# Design

## Context

See [proposal.md](./proposal.md). Existing tests are xUnit-specific, rely on internal plan access/shared CompilerTests fixtures, and inherit EF SQLite tests for remote providers. They already cover authorization, reader columns/materialization, seek/cursors, and snapshots; extract reusable behavior without moving provider setup/drivers into a neutral kit.

## Goals / Non-Goals

**Goals:** portable reusable checks through real protected operations, owned fixtures, and auditable scenario evidence.

**Non-goals:** an adapter certification, production authorization middleware, automatic database provisioning/deletion, or exhaustive arbitrary SQL/policy verification.

## Decisions

### Test-only package and fixture seam

Add `Kkts.DataAccess.Testing` under src with core dependency only and clear test-only packaging/docs. Public async scenario methods return structured results or throw a dedicated assertion failure; callers adapt them to their preferred runner. No runner assertion library, EF/driver, DI, ASP.NET, or friend-access dependency.

Fixture contracts construct isolated repositories/schemas/policy sources/contexts for scenario-defined records and implement explicit owned teardown. Use generic entity/scalar accessors or fixture bindings rather than require one production entity type. Scenarios exercise repository operations to obtain plans naturally; never make internal plan constructors public simply for the kit.

Host callbacks implement seeding/mutation/fault injection and evidence collection; callbacks are trusted testing infrastructure, not executable expressions from clients. Cancellation propagates, setup failures are distinct from assertions, and teardown runs in finally without targeting caller-unowned resources.

### Scenarios and honest results

Define stable scenario IDs, declared required/optional capabilities, and Passed/Failed/Unsupported/NotRun results. A missing mandatory scenario/evidence prevents the overall suite result from passing. Unsupported optional capabilities run explicit unsupported-operation rejection checks; they do not masquerade as successful snapshot/seek tests. Claimed capability failures are Failed, never skipped.

Core scenarios cover mandatory AND allow-minus-deny, independent field grants, hidden query fields, implicit object key lookup, count options, paging, output cardinality, bounded seek, context/version isolation, cancellation/disposal, and deliberate translation failures with no fallback. Optional modules cover projection materialization, full composite-key traversal, encrypted cursor compatibility, and four export consistency combinations/caps/deadlines.

Evidence records identify fixture/provider/version and tested scenarios without storing connection strings, secrets, rows, or policy payloads. Real-provider proof uses fixture-owned reader/materialization/query interception; absent evidence is NotRun. Output-only in-memory checks cannot establish that authorization ran in the database.

### Existing suite integration

Apply errors, composite keys, and provider capabilities first. Refactor current recording/relational tests to call kit checks without deleting provider-specific negative tests or shrinking coverage. Keep SQL Server/MySQL provisioning/environment checks in their test projects; unavailable servers remain explicit not-run/failure evidence.

Add an extension-author sample from CustomReadAdapter and a minimal non-xUnit consumer that compiles/runs the kit. Include intentionally broken adapters (ignore deny, leak sidecars, over-return, fail disposal) and assert the kit detects faults. Do not weaken assertions to make existing adapters pass.

## Risks / Trade-offs

- [Overgeneric fixture API] -> Start from existing bounded scenarios; expose only callbacks/accessors needed for those checks.
- [False confidence from passing tests] -> Reports and docs explicitly limit claims to executed fixtures and evidence, including negative controls.
- [Destructive test cleanup] -> Fixture-provided exact resource handles and ownership; no generic wildcard/database-wide cleanup.

## Migration Plan

Land final prerequisite contracts, introduce the package and basic tests, then optional/evidence modules, existing provider consumers, and extension docs/sample. Pack and test package consumption before publication. No production package references the kit; no databases are provisioned by the package itself.
