# Proposal

## Why

Validation diagnostics currently contain only paths/messages, while unrelated failures share AuthorizationException and the example maps them all to 403. Hosts need stable categories and codes without parsing messages or disclosing policy, identity, or database internals.

## What Changes

- Introduce transport-neutral typed errors and immutable diagnostics with stable codes and precise JSON Pointer paths.
- Distinguish invalid request, access denied, missing policy, incompatible schema, revision conflict, unsupported provider operation, invalid cursor, and export lifecycle failures.
- **BREAKING**: migrate diagnostic paths and public exception contracts; retain human-readable messages as non-contractual text.
- Add an optional ASP.NET Core integration package for safe Problem Details mapping, including malformed JSON and already-started streaming responses.
- Preserve protected detail not-found equivalence and propagate unexpected infrastructure failures without mislabeling them as access denial.

## Capabilities

### New Capabilities

- `structured-errors`: Stable failure codes, path diagnostics, and safe optional HTTP translation.

### Modified Capabilities

None. Existing fail-closed and diagnostic requirements remain; this adds their machine-readable contract.

## Impact

Core schema/literal/compiler/validator/read/token errors, SQLite policy errors, provider failures, EmployeeApp middleware/designer, tests, and documentation migrate together. New `Kkts.DataAccess.AspNetCore` keeps ASP.NET dependencies out of core. No database migration. This is the foundation for the later DI, capability, telemetry, and conformance proposals; it has no prerequisite among this batch.
