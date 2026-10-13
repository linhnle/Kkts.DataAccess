# Proposal

## Why

Every authorized read currently accepts the same request, causing count and detail to validate irrelevant options and export to validate pagination that it ignores. Before release, replace this ambiguous contract with operation-specific requests while preserving authorization and execution behavior.

## What Changes

- **BREAKING**: Replace `ReadRequest` with `ListReadRequest` (selection, filter, sort, offset, limit), `CountReadRequest` (filter), `DetailReadRequest` (required key, selection, optional filter), and `ExportReadRequest` (selection, filter, sort).
- **BREAKING**: Move the detail key into its request; update the repository interface, core implementation, and SQLite facade together. Remove old request types and method overloads rather than keeping a compatibility layer.
- **BREAKING**: Reject unmapped JSON members on each request, including client `action` values and operation-inapplicable options.
- Bind every repository operation internally to the Read action. Keep policy action metadata and compiler validation unchanged.
- Share applicable validation logic without introducing a public all-purpose request base. Count validates only filters; detail validates its key, selection, and filter; only list validates client pagination.
- Preserve exact list totals, existing result types, host-controlled export batch limits, captured-policy export behavior, stable ordering, cancellation, and session disposal.
- Migrate the example HTTP endpoints, test callers, acceptance script, and directly related documentation.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `authorized-reads`: Define operation-specific request shapes, strict JSON input, fixed Read binding, and validation limited to applicable inputs while preserving protected results.
- `entity-read-repositories`: Require matching request contracts across repository surfaces and replace the pre-release SQLite read-method source-compatibility promise with an explicit breaking migration.

## Impact

Affected code includes core read/repository contracts and validation, the SQLite delegation facade, EmployeeApp endpoints, repository/read/provider tests, and HTTP acceptance. Entity adapter/session contracts, database schemas, policy persistence, and compiler action semantics remain unchanged; no dependency additions are planned.

Existing consumers must replace the shared request, move detail keys into `DetailReadRequest`, and remove unsupported JSON options. Constructor compatibility for repository/facade registration is retained.

### Non-goals

Optional totals, `HasMore`, client export limits, cursor pagination, snapshot exports, projection, new result metadata, writes, and unrelated error-model changes are excluded. Export remains an all-matching-rows operation bounded per batch by host configuration.
