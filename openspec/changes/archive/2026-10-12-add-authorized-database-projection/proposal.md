# Proposal

## Why

The EF adapter currently materializes complete entities and removes forbidden columns only during DTO mapping. Project authorized scalar output in the database to reduce data entering application memory while preserving the existing authorization and public JSON contracts.

## What Changes

- Extend trusted execution plans with immutable authorized output bindings and add projected page/detail/seek capabilities returning scalar rows instead of entity instances.
- Apply complete authorization, client filtering, ordering, and pagination in the provider, then select permitted output values without materializing entities or evaluating entity getters locally.
- Cover offset lists, detail, cursor lists, and keyset exports. Count remains a database aggregate and retains the versioned result/optional-total behavior from preceding changes.
- Permit minimal trusted continuation sidecars for cursor/export ordering values, including hidden tie-breakers. Keep them separate from DTO mapping and encrypted in public cursor tokens; ordinary filter/order dependencies do not enter the result.
- **BREAKING behavior**: require projection by default. Hosts may explicitly allow entity materialization for adapters lacking the relevant projected capability; projection execution failures never trigger entity-loading fallback.
- Preserve aliases, selected order, scalar/null serialization, omitted/empty selection, inaccessible-detail behavior, metadata, token compatibility, captured export policy, and resource disposal.
- Reuse the existing bounded DTO shape cache for value-based mapping rather than constructing partially populated entities.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `authorized-reads`: Extend permitted output guarantees to database projection, distinguish trusted continuation metadata from DTO fields, and preserve public result behavior.
- `entity-read-repositories`: Define projection-required host configuration, trusted projected capabilities, provider conformance, and explicit entity-materialization compatibility behavior.

## Impact

Implement and synchronize request separation, cursor/keyset pagination, then optional totals/versioned counts before this change. All three remain planned rather than implemented; their artifacts are not modified here. This change supersedes documentation that projection is deferred and that all retrieval paths may materialize hidden entity columns.

Affected code includes core execution/row contracts, repository orchestration, DTO shape mapping, shared EF execution, SQLite facade configuration, custom adapter examples, EmployeeApp, provider/read/mapper tests, and related documentation. SQL Server/MySQL/SQLite helpers continue using the shared EF adapter. No new dependency, policy/entity migration, or client executable projection expression is planned.

### Non-goals

Record-dependent fields, masking, relationship projection, arbitrary computed projections, database snapshots, changing cursor/policy semantics, removing Kkts.Dynamic, or promising that hidden ordering values never enter trusted continuation memory are excluded.
