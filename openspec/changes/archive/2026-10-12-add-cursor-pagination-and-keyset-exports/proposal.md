# Proposal

## Why

Increasing offsets make long exports progressively more expensive and let changes before a page boundary shift traversal. Add protected forward cursors and provider-executed keyset traversal without implying a database snapshot or allowing a token to replace authorization.

## What Changes

- Add a distinct cursor-list request and method alongside the offset-list API introduced by `split-read-requests-by-operation`. Cursor results include permitted DTOs, exact total, policy version, and an opaque next token.
- Bind tokens to the effective sort tuple and unique stable key, scope/resource/schema, query, dataset, authenticated caller/context, and policy version. Re-capture current authorization on every request; reject mismatched versions and incompatible or malformed tokens before entity execution.
- Add an optional Data Protection integration package providing authenticated encryption with host-managed persistent keys. Keep core independent of ASP.NET and fail explicitly when cursor protection is unconfigured.
- Convert `ExportAsync` to internal keyset traversal, retaining host-controlled batches, one captured policy/context, existing batch results, cancellation, and disposal. Export requests and batches do not expose continuation tokens.
- Add an opt-in trusted seek-session capability and provider-aware lexicographic comparisons. Existing offset/count/detail session methods remain unchanged.
- **BREAKING behavior**: export requires seek-capable adapters and supported seek ordering; unsupported adapters/orderings fail explicitly rather than reverting to offsets or local evaluation.
- Preserve independent field grants; hidden tie-breakers and nonselected sort values remain internal or encrypted.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `authorized-reads`: Add protected forward cursor lists and internal keyset export requirements; extend bounded deterministic pagination to continuation boundaries.
- `entity-read-repositories`: Add trusted seek capability, provider comparison conformance, and explicit unsupported-capability behavior across core and SQLite facade.

## Impact

Implement and synchronize the prerequisite request-contract change first; it is planned, not present in source today. This proposal does not alter its artifacts or duplicate its overlapping requirement changes.

Affected areas include core read/seek/token contracts, repository orchestration, EF/provider adapters, an optional `Kkts.DataAccess.DataProtection` package, SQLite facade, EmployeeApp, tests, documentation, solution registration, and dependency locks. The protection package uses the matching .NET 10 Data Protection dependency; core retains its existing database/framework independence. Hosts configure key persistence, application isolation, dataset identity, and authenticated caller binding.

No policy/entity migration or snapshot transaction is introduced.

### Non-goals

Replacing offset lists, backward cursors, resumable exports, optional totals, client export limits, record projection, composite stable keys, policy-pinned replay across requests, immediate revocation of running exports, and guarantees against changes to sort values are excluded.
