# Design

## Context

See [proposal.md](proposal.md) for motivation and confirmed scope.

`ReadContracts.cs` defines one positional record with selection, filter, sort, offset, limit, and action. `AuthorizedReadRepository<T>.Validate` applies all of these checks to every operation. The SQLite facade delegates the same signatures; EmployeeApp binds this shared request for POST list/count/export and passes the GET route key separately for detail.

The trusted session already separates count, page, and single queries. EF count/single execution ignores plan sorting, so the input redesign needs no adapter extension. `ReadTests` is inherited by the SQL Server and MySQL acceptance classes. The HTTP script currently sends `{"limit":1}` to count, which must change.

The existing entity-repository spec promises source-compatible SQLite read methods. The delta explicitly replaces that promise while preserving constructor compatibility. A design artifact is warranted because this change crosses core, facade, transport, tests, and documentation.

## Goals / Non-Goals

**Goals:**
- Make irrelevant inputs unrepresentable in normal typed use and reject them at JSON boundaries.
- Preserve shared authorization enforcement without retaining a public catch-all request.
- Keep migration atomic across public repository surfaces and in-repository consumers.

**Non-Goals:**
- No adapter/session signature changes, new dependencies, or database migrations.
- No compatibility overloads or aliases that prolong the ambiguous contract.
- No new count/export options, compiler action semantics, or result contracts.

## Decisions

### Four flat request records

Define these public contracts in `ReadContracts.cs`:

| Type | Members |
|------|---------|
| `ListReadRequest` | `string[]? Selection = null`, `Condition? Filter = null`, `SortField[]? Sort = null`, `int Offset = 0`, `int Limit = 50` |
| `CountReadRequest` | `Condition? Filter = null` |
| `DetailReadRequest` | Required `JsonElement Key`, `string[]? Selection = null`, `Condition? Filter = null` |
| `ExportReadRequest` | `string[]? Selection = null`, `Condition? Filter = null`, `SortField[]? Sort = null` |

Each method takes its corresponding request and the existing optional cancellation token. Detail no longer takes a separate key parameter. Keep result records, `SortField`, and `ReadLimits` unchanged.

Alternatives: a shared base or nested query object would add indirection without eliminating validation responsibilities. Keeping detail's separate key would retain an inconsistent request boundary; putting it inside the request follows the selected contract.

### Strict transport contracts

Apply `JsonUnmappedMemberHandling.Disallow` to every request record, following the existing policy-record pattern. Use type-local attributes rather than global serializer settings, so policy administration and unrelated HTTP models do not change.

A detail key remains a JSON scalar validated using the existing typed compiler conversion. An omitted key represented as `JsonValueKind.Undefined` must fail explicitly before opening a session; null and incompatible values must also fail for the nonnullable stable key. Do not rely solely on a positional constructor parameter to enforce JSON presence. Preserve existing error pathways rather than introducing a new error taxonomy.

The alternative, ignoring unknown members, was rejected by the user: old action/pagination inputs must not appear to succeed. Example binding errors use the existing bad-request handling; authorization failures remain forbidden responses. GET detail builds the request from its authenticated route handler; no new detail endpoint is needed.

### Validate applicable components through private helpers

Extract field-demand, selection, filter, sorting, and list-pagination helpers inside the repository. Compose them directly in each method rather than translating requests back into a synthetic shared request.

- Selection preserves omitted/default, empty, ordinal distinct, and maximum-reference semantics.
- Filter preserves structural limits before recursion, every nested field grant check, and rejection of trusted-context references.
- Sort preserves independent grants, aliases, reference limits, directions, repeated keys, and stable-key completion.
- Pagination validates only list offset/limit against existing host limits.
- Detail validates its key with the existing host-key predicate, not a client field-grant demand.
- Count does not resolve selectable fields or demand sorting grants; detail has no sort validation.

Capture policy/context once per operation with no client action parameter, and bind to Read internally. Leave `PolicyCompiler.Bind` and schema action metadata unchanged, including rejection when Read is unsupported.

Alternatives: per-operation copies of the existing validator invite authorization drift; a flags-based validator makes forbidden combinations possible again. Small private helpers preserve one source of truth for each applicable check.

### Preserve execution and lifecycle behavior

List still counts before paging and returns an exact total. Count remains `Task<long>`. Detail remains a nullable protected DTO result. Export keeps one captured version, opens/disposes each batch session, and exports all matches from zero with the configured batch size.

Keep `EntityReadPlan`, adapter/session interfaces, and existing plan construction compatible. Count/detail can use plans with internal stable-key completion; these are not client sorting inputs and must not introduce Sort grants. Do not broaden the task into optimizing plans or changing offset traversal.

All validation required by an operation completes before opening an entity session. Export validation occurs on enumeration, preserving async-iterator timing. Provider exceptions propagate without local or unrestricted fallback.

### Migrate all public and example surfaces together

Update `IAuthorizedReadRepository<T>`, the core class, and `Sqlite.AuthorizedReads<T>` in one implementation sequence. Keep both constructors unchanged, and replace the facade's source-compatibility documentation.

EmployeeApp POST handlers bind the new list/count/export records. GET detail constructs `DetailReadRequest` from the serialized route id. Keep authentication, antiforgery checks, exact count response, NDJSON export, and detail 404 equivalence unchanged.

Tests that currently reuse one request across list/count need separate operation-specific values. Replace shared-request action tests with strict deserialization/API-surface tests, while retaining compiler-level action enforcement tests. Update the HTTP script to send `{}` for unfiltered count and add invalid-member cases.

## Risks / Trade-offs

- [Breaking consumers] -> Document four replacement types and before/after detail/count/export examples; remove old overloads deliberately.
- [Validation divergence] -> Use private shared helpers and recording-adapter assertions that invalid inputs open zero sessions.
- [Missing detail key slips through JSON defaults] -> Test absent, null, incompatible, and valid keys at both contract and repository boundaries.
- [JSON failures become misleading successes] -> Verify HTTP 400 for unsupported members and unchanged 403 for forbidden authorized-query fields.
- [Remote provider coverage silently omitted] -> Run the inherited read suite on configured isolated servers; report unavailable SQL Server/MySQL acceptance explicitly.
- [Deferred export errors or leaked sessions] -> Preserve iterator timing and test cancellation, stopped enumeration, provider failure, and one-version capture.

## Migration Plan

1. Add operation-specific contracts and migrate signatures, helpers, facade, and all typed callers together; delete `ReadRequest` and legacy overloads.
2. Add strict JSON and operation-validation tests; migrate the shared real-provider read suite and retain existing authorization/lifecycle assertions.
3. Update EmployeeApp binding and the HTTP acceptance payloads, then update README and read/repository/provider documentation with breaking migration examples.
4. Build the solution, run targeted contract/repository/read tests locally, and execute HTTP acceptance against a fresh synthetic database. Run inherited remote-provider suites when configured.

No persisted policy or entity data changes are required. If the API redesign is withdrawn before release, restore the prior API and matching callers as one coordinated change; do not mix old/new request surfaces or alter policy history.
