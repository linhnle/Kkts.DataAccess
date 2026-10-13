# Design

## Context

See proposal.md for motivation. AuthorizedReads<T> currently lives in Kkts.DataAccess.Sqlite, captures policies directly through SqlitePolicyStore and constructs EF Core queries from a Func<DbContext>. It owns validation, ordering, counts, materialization, DTO mapping and export. The core has no EF dependency. SqlitePolicyStore also declares draft/audit/conflict types in the SQLite namespace. ReadTests and TranslationTests use actual SQLite; EmployeeApp currently configures one database for policies and entities. Existing specs require authorization before execution and forbid query handles escaping.

## Goals / Non-Goals

Goals: keep authorization in one facade, isolate entity execution behind a typed boundary, make provider registration small and independently testable, and retain existing SQLite construction.

Non-goals: repository CRUD, exposing IQueryable to client-facing consumers, migrating policy persistence, automatic entity schema migration, distributed policy/entity transactions or promising identical database collations.

## Decisions

### Separate the protected facade from trusted execution

Add a public IAuthorizedReadRepository<T> in core with existing list/detail/count/export signatures. Move the shared authorized orchestrator to core and accept an IActivePolicySource exposing only CaptureAsync plus a trusted entity-read adapter. The adapter accepts a validated immutable execution plan (combined typed predicate, resolved sort selectors/stable key, bounds) and returns materialized entities only inside the trusted integration boundary; it exposes no GetAll/IQueryable bypass API. Plan construction stays internal to orchestration, with an accessible read-only plan contract for external adapter implementers. Adapter references and host DbContexts are trusted infrastructure, never client results. DTO mapping remains in the facade using Kkts.Dynamic 2.1.0; predicate composition retains Kkts.Expressions 3.1.0.

Alternative: a conventional public CRUD repository would expose unnecessary writes and risk unauthorized query paths. Merely moving AuthorizedReads into another namespace would keep policy-store and EF coupling and offer no non-EF extension point.

### Shared EF execution with small provider registrations

Create Kkts.DataAccess.EntityFrameworkCore for async no-tracking execution, stable ordering and session disposal. SQLite, SqlServer and MySql packages register this executor with explicit provider identity, factory and capability metadata; avoid copying four read implementations. The existing SQLite package continues policy storage and provides its entity registration plus a compatibility AuthorizedReads<T> wrapper delegating to the shared facade. SqlitePolicyStore implements IActivePolicySource without relocating its public audit/draft types or changing its SQL.

Use Microsoft EF Core SQL Server aligned with the current EF Core 10 line. Use Oracle MySql.EntityFrameworkCore for MySQL, with its supported EF Core 10 line verified from package metadata and a restore/build smoke check before pinning. Do not assume Pomelo 9 is compatible with EF Core 10. Exact patch versions are an implementation verification task, not an unverified promise. If a compatible release cannot restore/build, stop and surface that dependency blocker rather than downgrade the shared EF line or omit MySQL. Official compatibility references: https://dev.mysql.com/doc/connector-net/en/connector-net-entityframework-core.html and https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/releases.

Alternative: handwritten SQL dialects duplicate expression translation and increase risk; EF already implements the current query path. Keep a provider-neutral adapter contract for future non-EF implementations.

### Explicit host configuration and resource lifetime

Separate EmployeeApp PolicyDatabase (SQLite) from EntityProvider/EntityDatabase. Preserve Database as the legacy SQLite default when new settings are absent. Use host-supplied mapped DbContext factories with explicit asynchronous session ownership, including export disposal. Hosts own entity migrations and collation choices; the sample can seed its isolated fixture database explicitly. Do not log connection credentials. Select MySQL 8.4 and SQL Server 2022 as initial test server baselines, with exact images/builds recorded by implementation; MariaDB is not claimed.

### Capabilities and conformance

Validate provider identity/configuration at registration, and reject known unsupported plan features before entity execution where detectable. Translation errors still propagate; never use Compile/AsEnumerable as a fallback. Document database collation behavior instead of rewriting predicates to silently change existing semantics. Test ASCII baseline and explicit case/collation cases, null comparisons, decimal/date ordering restrictions, precision and stable keys on each real provider. The host's schema alone does not guarantee every provider translation.

Use shared conformance tests parameterized by adapters. Keep SQLite policy fixtures separate from entity fixtures even in SQLite tests. SQL Server/MySQL suites require disposable isolated databases via containers or supplied test connections; CI must run both suites as required jobs. Local absence can be reported as not run, never counted as passing. On Apple Silicon, remote or compatible CI SQL Server is acceptable; no emulated/in-memory substitute qualifies.

## Risks / Trade-offs

- EF/MySQL version compatibility → verify official metadata and locked restore/build before integrating; report blockers.
- Collation/precision differences → publish a provider support matrix and test boundaries, fail on unsupported translation.
- Public compatibility wrapper causes ambiguous type names → document fully qualified preferred API, preserve old namespace and constructor tests.
- Offset export under concurrent entity mutation → preserve documented lack of snapshot guarantee; policy capture remains stable.
- Separate databases cannot share an atomic data snapshot → capture immutable SQLite policy first; no distributed transaction claim.
- Resource leaks on partial enumeration → async disposal/cancellation tests for every executor.

## Migration Plan

Add core contracts and EF executor first; make SQLite facade delegate without caller changes. Add SQL Server/MySQL registrations and real-provider tests. Switch EmployeeApp to explicit independent configuration and keep legacy default behavior. Pack and consume all adapters from an isolated sample. No policy migration is required. Rollback switches hosts to the old SQLite facade/package and original SQLite entity configuration; existing policy tables and published content remain usable.
