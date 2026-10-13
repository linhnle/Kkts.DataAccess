# Proposal

## Why

Authorized entity reads currently live in the SQLite package and depend directly on SqlitePolicyStore, although their query execution already uses EF Core. A generic repository boundary will let hosts select entity database providers without duplicating authorization enforcement or moving policy storage.

## What Changes

- Introduce a provider-neutral, read-only generic repository behind the authorized facade, with closed list/detail/count/export results.
- Separate policy capture from entity query execution; keep drafts, publication, audit history and rollback in SQLite.
- Supply shared EF Core execution and concrete SQLite, SQL Server and MySQL registration adapters.
- Preserve validation before entity access, mandatory predicates, permitted DTO output, deterministic pagination, cancellation and captured-policy exports.
- Preserve existing SQLite callers through a compatibility facade while documenting the preferred provider-neutral API.
- Add provider-specific capability validation and real database acceptance suites; never substitute in-memory execution for unsupported translation.

## Capabilities

### New Capabilities

- `entity-read-repositories`: Provider-neutral repository contract, provider selection, lifecycle ownership and SQLite/SQL Server/MySQL conformance for authorized entity reads.

### Modified Capabilities

None. Existing authorized-read and policy-administration behavior remains authoritative; the new capability extends supported entity backends without changing those contracts.

## Impact

Core repository and policy-capture contracts, shared EF Core adapter, provider registration packages, existing SQLite AuthorizedReads compatibility surface, EmployeeApp configuration, solution/package references, test infrastructure and documentation. Kkts.Expressions 3.1.0 and Kkts.Dynamic 2.1.0 remain pinned. Entity provider dependencies must have a verified compatible target/EF Core matrix; policy storage retains its existing SQLite schema and transaction semantics.

## Non-goals

Generic CRUD, writes, aggregation, navigation traversal, alternate policy-store providers, distributed transactions between policy/entity stores, and identical collation or snapshot isolation across database engines.
