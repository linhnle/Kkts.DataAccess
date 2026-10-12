# Design

## Context

See [proposal.md](proposal.md) for motivation and confirmed scope.

Current source still has one `ReadRequest`, nonnullable page totals, and `CountAsync` returning `long`. List executes a count before paging through one owned session. The EF adapter's existing `CountAsync` and bounded `PageAsync` already provide the required execution primitives.

The prerequisite request redesign introduces separate contracts without changing results. The cursor proposal then introduces exact-total cursor pages and mandatory `Limit + 1` lookahead for next-token production. Both are planning-complete but unimplemented. This change follows them in order and intentionally supersedes their exact-total/count-shape guarantees.

The delta preserves the complete request-redesign consistency block and modifies the cursor block from its prerequisite. Until that cursor block is synchronized into main specs, validation can report that archive would refuse its MODIFIED header. This is a declared ordering constraint, not permission to synchronize unfinished work.

Tests include recording adapters and real EF SQLite execution, with inherited SQL Server/MySQL read suites. EmployeeApp exposes count results directly, and HTTP acceptance currently expects a bare number; those consumers must migrate with the public result.

## Goals / Non-Goals

**Goals:**
- Verify avoided count work, not just a null result field.
- Keep exact totals and observed continuation independent under concurrent data changes.
- Share small lookahead/mapping helpers where offset and cursor orchestration overlap.

**Non-Goals:**
- No new session methods, additional dependencies, approximate counts, or inferred metadata defaults.
- No new authorization grant type or snapshot guarantee.
- No weakening of token compatibility, field grants, or existing provider failure behavior.

## Decisions

### Independent request options and explicit nullable results

Append `bool IncludeTotal = true` and `bool IncludeHasMore = false` to `ListReadRequest` and `CursorListReadRequest`. Preserve all existing input members, defaults, strict unmapped-member handling, and cancellation signatures. Do not add these flags to count/detail/export requests.

Change `ReadPage.Total` and `CursorReadPage.Total` to `long?`. Append `bool? HasMore = null` to both result records, preserving the existing positional member order for records/token fields. Requested totals contain the provider's exact count; disabled totals are null. Requested has-more is true/false; disabled has-more is null even if cursor lookahead already knows the answer.

Annotate nullable metadata for explicit JSON inclusion, including when a host uses ordinary ignore-null/default serializer settings. Cover default and web JSON naming in tests. Do not change serializer settings globally. An exact zero is a value, not missing metadata.

Alternatives: three exclusive modes prevent requesting total and has-more together; a nonnullable boolean incorrectly conflates unrequested metadata with a final page. The user selected independent booleans with backward-behavior-preserving defaults.

### Offset execution matrix

After existing validation and one policy capture, build the complete authorized client query and use one owned session:

| IncludeTotal | IncludeHasMore | Entity count calls | Page fetch bound | Total | HasMore |
|--------------|----------------|--------------------|------------------|-------|---------|
| false | false | 0 | Limit | null | null |
| true | false | 1 | Limit | exact | null |
| false | true | 0 | Limit + 1 | null | observed bool |
| true | true | 1 | Limit + 1 | exact | observed bool |

When enabled, count the base query before paging, retaining current sequencing. When disabled, never invoke session count, `Any`, or another count-shaped aggregate in its place. Obtain has-more only from whether the bounded materialized page contains the extra authorized row.

Trim the extra row before DTO mapping. The host limit applies to returned rows; a validated single-row internal allowance does not grant extra client page size. Validate checked `Limit + 1` arithmetic before opening a session whenever lookahead is needed. Reject unrepresentable lookahead explicitly rather than clamping or silently disabling it. Offset bounds remain unchanged.

An empty page or exact-full final page returns false when requested. A count failure in enabled mode propagates; it does not turn into null/zero success. Disabled mode must work when a test adapter deliberately rejects any count invocation.

Alternatives: deriving `HasMore` from `Total > Offset + Records.Count` can be wrong when count and page observe different entity states and would require count work. A separate existence query adds work and a second observation.

### Cursor execution reuses mandatory lookahead

Cursor seek remains `Limit + 1` for every flag combination because next-token generation depends on it. `IncludeHasMore` controls exposing the observed boolean, not whether the cursor protocol performs lookahead. `IncludeTotal=false` suppresses only the base count.

If requested, has-more is exactly the same observation that triggers a next token. Never fetch another lookahead row/query, and continue using the last returned row, not the extra row, as the boundary. Count the authorized client query without the boundary when exact total is enabled.

Exclude both flags from token query fingerprints; snapshot them for response execution separately. Keep all row-query, limit, selection, identity/context, dataset, binding, expiry, and active-policy checks unchanged. The existing protection purpose/envelope needs no redesign: flags were not part of the earlier fingerprint and remain absent. Tests must resume tokens while changing either flag and still reject actual query/context changes.

### Versioned standalone counts

Add `ReadCount(long PolicyVersion, long Total)` and change public repository/facade `CountAsync(CountReadRequest, CancellationToken)` to `Task<ReadCount>`. Keep trusted session `CountAsync` returning `Task<long>`; only the repository wraps that value with its already captured policy version.

Standalone count always performs one exact entity count after filter validation, with no new metadata flags, selections, sorting demands, or pagination. Capture once and retain the bound version even if publication occurs before query completion. Do not fetch the policy again merely to label the response.

EmployeeApp returns an object such as `{"policyVersion":1,"total":2}` instead of a number. Preserve authenticated/antiforgery protections and forbidden-filter handling. Keep the numeric count as `Total` to match page metadata; do not introduce scope/schema extras that other results do not currently expose.

Alternatives: retaining a bare-count compatibility overload hides missing version metadata; changing trusted adapter return types spreads an orchestration concern into database execution.

### Authorization and failure boundaries stay shared

Metadata flags do not bypass request, selection, filter, sort, pagination, or cursor validation. Both the extra row and exact total use the same authorized predicate and captured version as the page. The lookahead row is never serialized, passed to DTO mapping, or used to reveal fields.

Retain cancellation on capture/count/page/seek and disposal on all success/failure paths. Provider errors remain explicit; no broad catches, partial-success counts, local evaluation, or unrestricted fallback are added.

Extract a small shared private helper for requested fetch size, observed has-more, and trimming if needed, reusing the cursor implementation's existing lookahead machinery. Do not introduce a general pagination strategy abstraction or a shared public request base.

## Risks / Trade-offs

- [Consumers assume Total is always a number] -> Nullable API/JSON migration examples and explicit null/zero tests.
- [Count JSON changes from scalar to object] -> Migrate core/facade/example/tests together and document extracting `Total`.
- [Flags suppress only output, not count work] -> Recording-adapter call counters and real SQL aggregate assertions for all combinations.
- [Extra row leaks output or alters cursor boundaries] -> DTO and returned-id tests, including empty selections and hidden tie-breakers.
- [Counts and has-more disagree under concurrent writes] -> Keep has-more tied to the page observation; document no snapshot and test with deliberately divergent count/page fixtures.
- [Cursor flags accidentally enter compatibility hashing] -> Cross-page flag toggling tests and unchanged rejection of genuine row-query changes.
- [Dependent specs archive out of order] -> Verify prerequisite implementation and synchronization before applying/synchronizing this delta; retain all inherited scenarios.

## Migration Plan

1. Implement and synchronize request separation, then cursor/keyset execution. Verify code/tests/specs, not planning status alone.
2. Update the request/result contracts, public count signatures, orchestration, facade, and typed callers in one coordinated sequence.
3. Add recording-adapter, serialization, and inherited provider tests; update HTTP count-object expectations and optional-metadata acceptance.
4. Update read/repository/provider docs and README examples, then run the targeted local, real-provider, HTTP, build, and formatting checks.

No dependency or persisted-data migration is required. Before release, rollback must revert request/result shapes and count consumers together; do not restore numeric count responses while callers expect objects.
