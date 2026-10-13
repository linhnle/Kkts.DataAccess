# Design

## Context

See [proposal.md](./proposal.md) for motivation and scope. The implemented repository snapshots the export request, captures policy/context once, builds authorized projection/seek plans, and opens an entity session per keyset batch. Its per-batch `await using` currently spans the yield. Export requests expose no lifecycle fields; batch output remains policy version plus records.

The SQLite policy store already updates `PolicyHeads.ActiveRevision` in the publication/rollback transaction alongside active pointer and audit. `CapturedPolicy` does not expose that revision, and `IActivePolicySource` offers only content capture. Shared EF sessions implement both projected and entity seek but own no cross-batch transaction. `AuthorizationException` is sealed, so lifecycle-specific exceptions must not inherit it.

Current specs unconditionally prohibit export snapshots and describe complete captured-policy traversal. The delta replaces these clauses with mode-specific behavior while preserving every inherited scenario name. Existing cursor version-compatibility semantics are unchanged.

## Goals / Non-Goals

**Goals:** orthogonal policy/data modes, no unchecked batches in abort mode, deterministic lifecycle bounds, verified relational snapshot semantics, and reliable ownership without replay.

**Non-goals:** freezing the policy store in an entity snapshot, re-binding permissions/identity, cursor protocol changes, distributed transactions, forcing abandoned enumerator cleanup, or making streamed delivery atomic.

## Decisions

### 1. One immutable host configuration, no client switches

Add `ExportReadConfiguration` with `ExportPolicyConsistency` (`CapturedPolicy`, default; `AbortOnPolicyChange`), `ExportDataConsistency` (`BestEffort`, default; `Snapshot`), nullable positive `long MaxRows`, nullable positive `TimeSpan MaxDuration`, and `TimeProvider` defaulting to system. Add a trailing optional configuration argument to repository and SQLite facade. Keep `ReadLimits.ExportBatchSize` as the sole batch-size setting.

Validate enum values, positive bounds, and the supported deadline timer range at configuration construction. Snapshot the immutable configuration for enumeration; allocate timer/session state per enumerator, not per repository. Defaults retain existing behavior and constructor calls. No flags enter public export JSON or change list/count/detail/cursor execution.

Keep public `ExportBatch` unchanged: a host row cap is deliberate successful prefix delivery, not evidence that all matches were exhausted. Document that no total/truncation metadata or count query is added. The host owns how capped results are labeled in its application.

Alternative: per-request switches allow untrusted callers to bypass host revocation/resource policy; a single consistency enum incorrectly couples authorization and entity data.

### 2. Optional authoritative activation capability

Add `IActivationAwarePolicySource : IActivePolicySource` with an atomic stamped capture and an authoritative activation read. Use immutable records `PolicyActivationStamp(Version, ActiveRevision)` and `CapturedPolicyActivation(Policy, Stamp)`; scope/resource remain the method arguments and captured policy identity. Validate that stamp version matches captured content and revision is valid. Keep `CapturedPolicy`, compiler binding/cache, and cursor identity unchanged.

For captured mode, call the existing capture path once. For abort mode, require this capability before entity access and call its atomic capture once, obtaining context once as before. Compile/bind the returned immutable policy; do not capture content and revision in two independent calls.

SQLite stamped capture reads head and immutable payload in the same existing deferred read transaction. Activation checks open a fresh policy connection and read the scope/resource active pointer/revision without entity transaction enlistment, cached pointers, or compiler work. Refactor legacy capture to share the atomic read helper. Missing policy and query failures propagate; never treat them as an unchanged revision.

Existing `ActiveRevision` increments already distinguish publish-and-rollback, reactivation of the same version, and failed publication. Preserve them and add explicit exhaustion protection so a signed revision cannot wrap or change representation. Source implementations must keep revision identity durable/monotonic within the configured dataset; database restore/replacement that resets revision history is an operational identity change, not supported transparent continuity.

Compare both version and revision with the original stamp. Changed revision aborts even if version is equal. Same revision with changed version is a source-contract failure, not permission to continue. Unrelated resources and draft revisions are irrelevant.

Alternative: immutable version comparison misses intermediate activation/rollback; putting a default revision of zero on all captures creates success-shaped unsupported behavior. An optional source capability preserves custom captured-policy sources.

### 3. Check activation before queries and immediately before batch delivery

Abort exports verify the original stamp before every protected batch query and immediately before `yield return`, after validation/mapping. Never replace predicate, output bindings, context, or boundary with the new policy. Publication observed during query suppresses that pending batch. Checks also happen before an empty terminal query; no successful result is inferred from failed verification.

Check caller cancellation/deadline before and after asynchronous work and mapping. If the cap is reached, resume only to complete/clean up; no terminal count/seek query or activation read is needed because no further batch will be delivered. Deadline/caller cancellation still takes precedence if they occurred before that resumed completion.

Introduce `ExportPolicyChangedException : Exception` with stable identifying semantics and no policy payload or boundary data. Unsupported capabilities use explicit `NotSupportedException`; malformed source stamps are explicit infrastructure errors. Do not unseal the existing authorization error or add broad catches.

Document the remaining race: a policy commit after the final authoritative read can precede delivery. Neither revision checks nor snapshots retract already delivered rows. Consumer processing/writing after a yield is outside the repository's check; batch size limits the unchecked delivery window but does not eliminate it.

Alternative: checking only before query can release a whole batch after revocation committed during its query. Mid-export recapture silently mixes grants and invalidates output/seek consistency.

### 4. Bounded rows and a monotonic elapsed deadline

Track delivered rows with checked arithmetic. Each query limit is `min(ExportBatchSize, remaining allowance)`; never fetch a lookahead row for export. After a capped batch, stop without probing whether more data exists. Counts are never used to enforce the cap. Validate adapter over-return before mapping or delivery.

Start elapsed timing on the first `MoveNextAsync`, before policy capture. Use `TimeProvider.GetTimestamp/GetElapsedTime` rather than wall-clock UTC. A provider-backed deadline timer cancels a dedicated deadline token, linked with caller cancellation for capture, policy checks, session open, snapshot establishment, and entity queries.

Introduce `ExportDurationExceededException : TimeoutException`. Narrowly translate cancellation attributable to deadline expiry; caller cancellation wins when both are observed, while unrelated infrastructure failures keep their original errors. Explicit elapsed checks before/after work protect against late timers and noncooperative adapters that return after expiry. No unchecked records may be yielded after expiry.

Elapsed time includes consumer pauses. While suspended at yield, the timer can signal cancellation but cannot forcibly resume/dispose an async iterator or stop the consumer writing a previously yielded batch. On next advancement, reject expired execution before more work; on `DisposeAsync`, release owned resources. Dispose linked cancellation/timer state on all iterator exits. Do not promise preemption of synchronous mapping or an adapter ignoring cancellation.

Alternative: accumulating only query durations permits indefinitely held snapshots during slow consumers. Wall-clock deadlines behave incorrectly under clock adjustments.

### 5. Export-wide snapshot capability on owned sessions

Add `IEntitySnapshotReadSession<T> : IEntityReadSession<T>` with `BeginSnapshotAsync(EntityReadPlan<T>, CancellationToken)`. It is an optional trusted lifecycle capability; the session owns its transaction until disposal. It must establish supported isolation on an unopened-for-entity-reads session, reject repeated initialization, and maintain the same connection/transaction through later seek calls. Core independently requires projected seek or explicitly allowed entity seek, so snapshot support never relaxes projection.

Best-effort retains fresh sessions per query. Refactor bounded batch acquisition so validation, boundary extraction, and DTO mapping complete within the session scope, dispose it before yielding, then perform the final activation/deadline check. This releases ordinary entity connections while consumers pause.

Snapshot opens one session after request/policy validation, verifies snapshot and relevant seek/projection capabilities, and establishes the transaction before the first protected query. Keep it through all bounded batches. A snapshot is anchored by provider transaction/read semantics no later than the first protected read; it is not guaranteed to correspond to the earlier policy capture timestamp.

Disposal ends the read-only owned transaction (rollback/dispose is sufficient; no writes or successful commit marker are needed), then context/connection. Cleanup must not use an already canceled operation token. Missing prerequisites, setup failures, source failures, mapping errors, cap completion, expiry, cancellation, and early consumer disposal all follow the same ownership boundary.

Do not join ambient or host-owned transactions. Reject contexts with conflicting transactions or automatic retry configurations that cannot preserve the same snapshot. No reconnection, whole-export replay, or retry under a new transaction after delivery. Normal failures propagate; the library never changes a requested snapshot into best-effort.

Alternative: independent repeatable-read transactions per batch are not an export snapshot. Adapter-global sessions would leak state across enumerations.

### 6. Provider-specific snapshot strategies

Keep shared EF execution provider-neutral. Provider helpers supply an optional
trusted `EfSnapshotFactory` using driver-specific transaction APIs; shared EF
owns model/connection/retry preflight, EF enlistment, transaction lifetime and
snapshot-loss checks. The SQLite facade supplies the same factory. Direct
`EfEntityReadAdapter` registrations remain valid for ordinary/default reads but
must supply a snapshot factory to opt in. This approved integration avoids new
package dependencies and reflection while preserving SQLite's explicitly deferred
transaction. Initially verify ordinary mapped single-table resources without host
query hints or external/view/nontransactional sources; reject unsupported
model/query shapes explicitly rather than advertise unproven coverage.

Real MySQL driver testing showed that native rollback can leave a usable
connection/transaction reference and permit later SELECTs. The approved hook
therefore returns `EfSnapshotTransaction` with optional pre/post-read verification.
MySQL establishes a uniquely named export-owned savepoint and releases/recreates
it at both checkpoints. A missing savepoint fails explicitly before delivery:
it proves the original transaction was lost and cannot be repaired or replayed.
These savepoint commands neither lock entity rows nor establish a different
consistent-read snapshot, change schema, or rely on extra server instrumentation.

| Provider | Verified snapshot strategy | Host prerequisites / rejection |
|---|---|---|
| SQLite | One deferred read transaction on a private-cache connection; first protected read anchors the snapshot | Require WAL and no dirty-read/shared-cache configuration for this verified baseline. Do not enable WAL automatically. File-backed tests permit a second writer to commit between batches. |
| SQL Server | One explicit `IsolationLevel.Snapshot` transaction | Require `ALLOW_SNAPSHOT_ISOLATION` fully enabled. Statement-level `READ_COMMITTED_SNAPSHOT` alone is insufficient. Do not alter database options. |
| MySQL | One explicit Repeatable Read transaction, ordinary nonlocking consistent SELECTs over InnoDB | Verify mapped queried table is InnoDB and no locking reads/unsupported sources; first consistent entity read establishes the snapshot. Do not rely on server default isolation. |

Preflight actual runtime settings/model/provider and fail before first yield when unsupported. Setup/first-read failures also remain explicit. No claim is made about transaction snapshots over arbitrary host views, external functions, mixed storage engines, or global filters reaching unverified sources; reject these shapes for snapshot mode until separately verified.

Policy reads must remain fresh even when entities and policies happen to share a SQLite file; the verified WAL/private-connection baseline allows committed publication while the entity reader remains active. Never enlist those policy reads in the snapshot.

Grounding:
- [SQLite isolation](https://www.sqlite.org/isolation.html) explains stable WAL read snapshots and dirty-read/shared-cache exceptions.
- [SQL Server snapshot isolation](https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/snapshot-isolation-sql-server?view=sql-server-ver17) distinguishes transaction snapshots from statement-level versioning and requires database opt-in.
- [MySQL InnoDB consistent reads](https://docs.oracle.com/cd/E17952_01/mysql-8.4-en/innodb-consistent-read.html) establishes one snapshot at the first consistent read under Repeatable Read, unlike Read Committed.

### 7. Tests, example behavior, and operational documentation

Recording adapters/sources cover all four mode combinations, atomic capture/check counts, revision changes during query/mapping, rollback to the original version, missing capabilities, malformed stamps/rows, row-cap exact query bounds, and no fallback. Fake `TimeProvider` tests must implement both monotonic timestamps and timers; advancing UTC alone does not exercise duration cancellation.

Real-provider tests mutate selected/sort fields and insert/delete rows with an independent writer after first yield, then compare the complete snapshot export to the initial authorized sequence/values. Also test negative prerequisites, empty output/sidecars, publication with snapshot held, policy-check failure, transaction loss, early disposal, and cancellation. Verify reader columns and zero entity materialization remain consistent with projection requirements.

EmployeeApp currently writes NDJSON rows after each batch yield. Keep host settings trusted and defaults unchanged; add documented configuration examples. Before response starts, map policy-change/duration errors explicitly. After response starts, abort the stream rather than append an error-shaped row or report success. HTTP streaming failure cannot remove the already-written prefix. Acceptance must verify strict rejection of client lifecycle overrides and absence of sidecars; do not change to full-export buffering merely to simplify errors.

Update read/repository/provider/lifecycle/acceptance documentation together with each affected implementation group. Document the four-mode matrix, cap prefix semantics, the final-check race, enumeration-start timing, consumer disposal, snapshot acquisition timing, prerequisites, lack of cross-store atomicity, and long-running snapshot resource costs.

## Risks / Trade-offs

- [Revocation can race delivery] -> Two batch checkpoints and explicit bounded/cooperative semantics, never instantaneous-revocation claims.
- [Snapshot pins WAL/version-store/undo and connections during pauses] -> Optional elapsed deadline/row cap, cancellation, consumer disposal, and operational guidance; defaults remain best-effort.
- [SQLite same-file policy writes can be blocked by transaction choice] -> Deferred WAL snapshot plus independent authoritative policy connection, verified with concurrent publication tests.
- [Custom sources lack revision support] -> Optional capability with explicit abort-mode failure; captured defaults remain available.
- [Transparent reconnect/retry changes snapshot] -> Fail without replay; reject incompatible host retry configuration.
- [Partial streamed output precedes failure] -> Typed errors and stream abort; no success-shaped terminal fallback or claim of atomic delivery.

## Migration Plan

1. Add optional source/snapshot capabilities and host configuration without changing existing request/batch signatures or defaults.
2. Use the existing SQLite activation revision with atomic stamped reads; no schema migration. Verify publication/rollback durability and exhaustion protection.
3. Add shared EF snapshot strategies and lifecycle-aware export orchestration; keep ordinary reads unaffected.
4. Hosts opting into abort mode provide an authoritative revision source. Hosts opting into snapshots explicitly provision verified provider prerequisites; the library never changes them.
5. Keep the example default behavior unchanged and demonstrate opt-in host configuration. Run focused lifecycle/provider/HTTP validation before recommending these modes.
6. Hosts can explicitly restore captured-policy/best-effort settings for future enumerations. Do not dynamically downgrade an in-progress failed snapshot or aborted export.
