# Design

## Context

See [proposal.md](proposal.md) for scope and confirmed choices.

Current source still uses `ReadRequest`; the prerequisite `split-read-requests-by-operation` is planning-complete but unimplemented. Apply and synchronize that change first. Its request/facade migration remains authoritative; this change adds cursor behavior afterward and does not rewrite its artifacts.

`AuthorizedReadRepository<T>.ExportAsync` currently increments an integer offset and opens one session per batch. `EntityReadPlan<T>` already contains the full authorized predicate and resolved sort bindings. EF materializes entities inside the trusted boundary, so the repository can capture nonselected boundary values before DTO mapping. Current adapters have count/page/single methods but no seek capability.

The provider documentation shows different text collations and explicitly limited scalar translation. `ReadTests` is inherited by SQL Server/MySQL classes, and provider CI executes real databases. `LiteralConverter` is intentionally stricter than arbitrary stored floating-point values, so it is not a general server-value cursor codec.

## Goals / Non-Goals

**Goals:**
- Separate public continuation compatibility from trusted provider seek execution.
- Preserve independent grants while using nonselected values internally.
- Bound rows and token processing and verify actual no-offset traversal.

**Non-Goals:**
- No general-purpose expression input, historic-policy replay, new snapshot transaction, or export resume protocol.
- No custom cryptographic construction or silent ordering/type coercion.
- No promise that every field accepted for offset sorting supports relational seek comparison.

## Decisions

### Additive cursor contract

Add `CursorListReadRequest(Selection = null, Filter = null, Sort = null, Limit = 50, ContinuationToken = null)` using the same field types and strict JSON attributes as the prerequisite records. Add `ListCursorAsync` to the read repository interface, core, and SQLite facade.

Return `CursorReadPage(PolicyVersion, Total, Records, NextContinuationToken)` with a nullable token. Keep `ListReadRequest`, `ReadPage`, count/detail contracts, `ExportReadRequest`, and `ExportBatch` unchanged. Exact total counts the authorized client query without the seek boundary; it is not a remaining-row count.

Fetch at most `Limit + 1` rows for cursor lookahead, return at most `Limit`, and form the next boundary from the last returned row, never the lookahead row. A short or empty result has no next token. Validate checked lookahead arithmetic and token limits explicitly. The internal extra row does not increase the client page limit or enter DTO output.

Alternatives: adding both offset and cursor to one request permits contradictory modes; replacing offset lists was explicitly rejected. Optional counts are outside scope.

### Optional trusted seek-session capability

Add `IEntitySeekReadSession<T> : IEntityReadSession<T>` with `SeekAsync(EntitySeekPlan<T> plan, int limit, CancellationToken cancellation)`. The immutable seek plan wraps the existing read plan and a nullable, ordered boundary tuple whose runtime scalar types are checked against the resolved properties. Only core constructs plans; adapters execute the complete authorized predicate and seek ordering in their database.

The repository checks this capability on an opened session and disposes it on failure. Count, page, and single contracts remain unchanged. Existing adapters continue to support those operations but must implement seek to support exports/cursor pages. There is no default offset implementation.

Core extracts and snapshots boundary values from materialized trusted entities using the resolved property bindings, before yielding mapped results. A missing/malformed boundary is an explicit error, not a restart. Empty selection still supports traversal without granting key selection.

Alternatives: changing the existing session interface would unnecessarily break adapters used only for nonseek operations; putting provider comparison behavior in core would mishandle text/Guid/mapping semantics.

### Provider-aware lexicographic seek

The complete effective order follows current alias resolution, granted client sorts, repeated-key behavior, directions, and stable-key completion. Build a strict lexicographic condition:

```text
after(row, boundary) =
  after(k1, b1)
  OR (equal(k1, b1) AND after(k2, b2))
  OR ...
```

Compose it with the already authorized client query before ordering and bounded materialization. Prefix equality and strict comparison must match the database ordering, including provider collation. Do not route these internal comparisons through client Filter permissions or registered public operators.

For the supplied SQLite/SQL Server/MySQL baseline, use explicit null-first ascending and null-last descending semantics for seek queries. Match that ordering with null-aware predicate branches; do not rely on CLR nullable comparison behavior. Keep offset ordering unchanged. Provider-specific comparison builders live in EF execution and use translatable expressions, not compiled delegates.

Verify string prefix equality and comparison under actual collation, including case-equivalent strings and trailing-space behavior. Numeric/date/enum comparisons require matching mapped representations. Guid, DateTimeOffset, high-precision numeric, floating special values, or custom value-converter combinations are supported only when a matching seek translation is verified; otherwise fail explicitly and document the narrower capability. At minimum, ship the existing example's string-plus-integer order, integer stable-key traversal, mixed numeric/date directions, and supported nullable test-field orders across real-provider conformance. Do not advertise all registered scalar types as seek-supported.

Provider metadata checks reject known-incompatible mappings before executing a seek query; residual translation failures propagate. Do not convert types, override collation, fetch unrestricted data, or silently fall back.

### Query and authorization binding

Cursor-enabled hosts supply optional repository/facade cursor configuration containing a protector, a stable server-derived subject-id function, and a nonempty dataset identity. Dataset identity is an opaque host label, not a connection string; hosts change it when routing, collation, mappings, or seek semantics change. Existing constructor arguments remain valid without this optional configuration.

Core canonicalizes and hashes:
- Scope, resource, schema version and public/property/type bindings.
- Effective selection, structural client filter, complete effective order, direction/null semantics, and page limit.
- Server-derived subject id; ordinal-sorted roles/groups and registered context keys with typed values, including missing/null markers.
- Mandatory-constraint/compiler identity and immutable captured policy content/version.
- Host dataset identity and cursor protocol version.

Expose only internal compiler helpers for its context registry and immutable binding identity; do not add client-controlled identity input. Fingerprint missing unused context keys without making them newly required. Binding an applicable policy still fails closed for required missing context as before.

Normalize JSON property order/whitespace and resolved selection aliases, but preserve filter node/array order; semantically equivalent but structurally different filters can require restart. Snapshot validated request arrays/filter trees so the fingerprint and executed query cannot diverge through caller mutation.

On every cursor call, derive fresh trusted context, capture the active policy, bind authorization, validate selection/filter/sort/limit, and compare token bindings before opening an entity session. Compare the token policy version with the current captured version; do not load the token's historic policy. A concurrent publication after capture retains the existing operation-start semantics.

Version equality is checked at resume, not against an audit of intervening publications: rolling back to the identical immutable version can make an otherwise unexpired compatible token usable again. Adding active-pointer epochs would require a separate policy-source change and is not claimed here.

### Confidential token envelope and protection integration

Core defines a small `IContinuationTokenProtector` contract for protecting/unprotecting bytes. Add `Kkts.DataAccess.DataProtection` with `DataProtectionContinuationTokenProtector` backed by an isolated `IDataProtector` purpose for cursor protocol version 1. Reference matching .NET 10 Data Protection packages there, not in core. Follow the existing optional-package and packed-core reference patterns.

The encrypted envelope contains its format version, issuance/expiry, compatibility fingerprints, policy identity, and the last returned sort tuple. Hashes alone do not conceal boundary values; authenticated encryption is required. Do not issue tokens containing plaintext keys or merely base64-encoded/signed payloads.

Encode server scalar values losslessly with type tags: invariant integers, decimal representation, floating-point bit patterns, string/null, Guid, enum underlying value, DateTime ticks/kind, and DateTimeOffset ticks/offset. Decode only expected registered scalar types and matching tuple cardinality. Never resolve arbitrary CLR types from token input. Reuse existing conversion utilities only where their lossless semantics fit; do not weaken client literal conversion to accommodate cursor values.

Proposed host defaults are a 15-minute traversal lifetime and a 16-KiB maximum encoded token, with positive validated overrides and injectable `TimeProvider`. Preserve initial issuance/expiry across continuation pages instead of extending lifetime indefinitely. Bound both encoded input and unprotected payload before JSON/tuple processing. Token generation that exceeds limits fails explicitly without truncation.

Use a narrow `InvalidContinuationTokenException` with one generic public message for malformed/expired/mismatched tokens; map it to HTTP 400 in the example. Ordinary authorization failures retain existing handling. Translate only expected protection/format failures, not unrelated configuration/provider exceptions.

Hosts own key storage, at-rest protection, application isolation, sharing across instances, and retirement. Retain decryptable keys through token lifetime if restart/rotation continuity is wanted. Key-ring loss or different purpose rejects tokens; it never becomes a valid empty cursor. No token/key/context payload logging.

Alternatives: custom AES/HMAC code duplicates established key-management logic; integrity-only tokens expose hidden fields; ephemeral per-request protectors break continuation. The user selected an optional established protection integration.

### Cursor orchestration and internal export state

Cursor lists count the base authorized query and seek the page in one owned session, without promising a snapshot between statements. After lookahead, snapshot the last returned tuple, protect the next envelope when needed, and map only permitted fields.

Exports do not require token configuration: they hold a private typed boundary, immutable request snapshot, and one captured policy/context. Each batch opens a seek session, executes the authorized seek query with `ExportBatchSize`, snapshots the last row's tuple before yielding, and disposes as today. Stop on empty/short batches; an exact full final batch can require one final empty query. No offsets advance and no external cursor is created.

If a boundary row is deleted, saved values still work. Inserts/deletes before an unchanged boundary cannot shift later row positions; inserts after it may appear. Sort-value changes or changing membership can still cause gaps/repeats. Neither total-plus-page nor export is a snapshot, and running exports keep their captured policy despite publication.

### Public surfaces and example

Extend the SQLite facade with the same optional cursor configuration and method. Configure EmployeeApp's Data Protection services once, bind dataset/subject ids from server configuration and authenticated identity, and add authenticated/antiforgery-protected `POST /employees/cursor`. Preserve existing list/count/detail endpoints and NDJSON export.

Tests use shared protection configuration for real restart/cross-instance acceptance and isolated temporary key directories; never hard-code production keys. Test-only provider sequence oracles can compare full ordered fixture results, but production never evaluates authorization locally.

## Risks / Trade-offs

- [Export incompatibility for nonseek adapters] -> Explicit migration notes and a core-only seek adapter example; no offset fallback.
- [Provider ordering mismatch] -> Compare entire fixture traversal against each real provider, including nullable/collation/precision boundaries and SQL shape.
- [Hidden fields leak through tokens] -> Established authenticated encryption, purpose isolation, and plaintext/DTO assertions.
- [Token sharing or stale context] -> Bind stable authenticated subject plus registered authorization snapshot; reapply fresh authorization on every request.
- [Long sorts produce oversized tokens] -> Bound token size before decoding and fail explicitly on generation overflow.
- [Exact counts still cost work] -> Preserve selected scope; no throughput claim that hides the count query.
- [Sort mutation defeats exactly-once traversal] -> Document non-snapshot semantics and use deterministic mutation tests for unchanged sort tuples only.
- [Overlapping changes archive out of order] -> Apply/synchronize the request-contract prerequisite first; this delta changes only the previously untouched pagination block and adds requirements.

## Migration Plan

1. Confirm the prerequisite request-contract change is implemented and synchronized; do not mark it complete based only on planning status.
2. Add seek capability, provider comparisons, conformance tests, and adapter documentation before switching exports.
3. Add token/core cursor contracts and the optional protection package with locks, package consumption tests, and key-management documentation.
4. Wire cursor/export orchestration, matching facade APIs, EmployeeApp, HTTP acceptance, and regression tests.
5. Run targeted local checks, real-provider conformance, HTTP/restart acceptance, package consumption, and formatting.

No persisted entity/policy migration is required. If reverting before release, revert the cursor/protection surfaces and export seek behavior together; discard issued cursor tokens by retiring the cursor purpose/configuration rather than altering policy history.
