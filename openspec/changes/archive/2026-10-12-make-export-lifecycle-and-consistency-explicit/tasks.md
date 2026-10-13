# Tasks

## 1. Host configuration and trusted lifecycle contracts

- [x] 1.1 Add immutable export policy/data modes, optional positive row/duration limits, and TimeProvider configuration with backward-compatible core/facade construction; verify defaults, invalid enum/bound/timer-range rejection, and independent per-enumerator state in focused contract tests.
- [x] 1.2 Add optional authoritative stamped-policy capture/check contracts without changing CapturedPolicy or cursor identity; verify valid stamps, version/revision mismatch rejection, and unchanged captured-only custom-source behavior.
- [x] 1.3 Add the optional owned-session snapshot capability independently of projected/entity seek interfaces; verify recording adapters can provide snapshot plus projected seek without implementing full-entity seek, and unsupported custom sessions remain valid for default export.
- [x] 1.4 Add distinct policy-change and duration-limit exceptions without unsealing AuthorizationException; verify errors contain stable identifying semantics but no policy payload, identity, or boundary values.
- [x] 1.5 Document the mode matrix, unchanged export request/batch shapes, and trusted adapter/source boundaries in docs/repositories.md; verify examples cannot select lifecycle modes through client JSON and custom adapter examples still build.

## 2. SQLite authoritative activation reads

- [x] 2.1 Implement atomic stamped capture and fresh active version/revision reads in SqlitePolicyStore using its existing PolicyHeads revision; verify concurrent publication yields consistent content/version/revision tuples and no schema migration is introduced.
- [x] 2.2 Preserve and guard monotonic revision advancement during publication/rollback, including exhaustion; verify publish-then-rollback, same-version reactivation, draft edits, failed transactions, unrelated scope/resource activation, and restart durability in PolicyStoreTests.
- [x] 2.3 Share the existing capture helper without changing ordinary operation or cursor behavior; verify legacy capture results and version-based cursor compatibility still pass.
- [x] 2.4 Update docs/lifecycle.md for authoritative activation stamps versus immutable versions and the no-cross-store-snapshot boundary; verify documented failure/rollback semantics match store tests.

## 3. Best-effort export policy checks and row caps

- [x] 3.1 Refactor bounded batch acquisition to validate/map/snapshot boundaries inside each session and dispose best-effort sessions before yield; verify no session remains held during consumer pauses and captured fields/context/policy/keyset behavior stay unchanged.
- [x] 3.2 Add abort-mode atomic initial capture, pre-query verification, and post-mapping pre-yield verification; verify publication between batches skips the next query, publication during query/mapping suppresses the pending batch, and publish-rollback to the original version aborts.
- [x] 3.3 Fail explicitly on missing revision capability, absent policy, malformed stamps, or activation-read failures; verify no unchecked output, recapture/rebinding, count, offset, or captured-mode fallback occurs and all opened sessions dispose.
- [x] 3.4 Enforce maximum rows with checked delivered counts and remaining-allowance query bounds; verify batch size four/cap six yields four then two, caps below/at/above a batch, empty selection counting, over-return rejection, and no terminal query/count when capped.
- [x] 3.5 Extend strict read contract tests and docs/reads.md for host-only lifecycle configuration, successful capped-prefix semantics, and the final-check race; verify client mode/limit overrides are rejected and default export result JSON remains unchanged.

## 4. Whole-enumeration elapsed deadline

- [x] 4.1 Implement per-enumerator monotonic elapsed timing and provider-backed deadline cancellation from first advancement, linking caller/deadline tokens through capture, checks, session opening, and queries; verify fake-clock tests at/beyond expiry and no time is consumed merely by creating an unadvanced enumerable.
- [x] 4.2 Translate only deadline-attributable cancellation into the duration-limit exception with caller-cancellation precedence; verify cancellation before/simultaneous with expiry, unrelated failures, noncooperative late adapter returns, and deadline expiry during capture/query/mapping.
- [x] 4.3 Check deadlines on resumed enumeration before additional work and dispose timer/token state on completion/failure/DisposeAsync; verify consumer pauses, pause after a capped batch, early disposal, and no successful completion or later yield after observed expiry.
- [x] 4.4 Document elapsed timing, timer support limits, cooperative cancellation, and undisposed-enumerator limitations in docs/reads.md and docs/repositories.md; verify the test fake advances both timestamps and timers rather than UTC alone.

## 5. Shared EF snapshot lifecycle and SQLite implementation

- [x] 5.1 Implement owned EF snapshot transaction state with single initialization, conflict/retry-configuration checks, and disposal independent of canceled operation tokens; verify setup failures, repeated initialization, transaction ownership, unsupported mapped/query shapes, and no reconnection/replay.
- [x] 5.2 Add SQLite deferred read snapshots with explicit WAL/private-cache/no-dirty-read prerequisites and no automatic setting changes; verify file-backed tests allow an independent writer to commit while the snapshot remains stable, and negative settings fail before delivery.
- [x] 5.3 Route snapshot exports through one session/transaction while preserving projected seek or explicit entity-compatibility requirements; verify all four consistency combinations, missing capabilities before delivery, one session across batches, and no best-effort fallback.
- [x] 5.4 Verify same-file SQLite policy publication remains authoritative outside an entity snapshot; add tests for publication during query, rollback between batches, source-check failure, cap/deadline/cancellation/early disposal, and assert connections/transactions are released.
- [x] 5.5 Update docs/providers.md and docs/repositories.md for SQLite snapshot prerequisites, snapshot acquisition timing, and transaction resource costs; verify statements are supported by concurrent-writer and cleanup tests.

## 6. SQL Server and MySQL snapshot conformance

- [x] 6.1 Implement explicit SQL Server Snapshot isolation with runtime ALLOW_SNAPSHOT_ISOLATION verification and no database alterations; verify enabled settings preserve one snapshot and disabled/transitional/RCSI-only configurations fail explicitly.
- [x] 6.2 Implement explicit MySQL Repeatable Read consistent reads with verified InnoDB/supported mapped-source prerequisites; verify server defaults are not relied upon, nontransactional/unsupported sources fail, and no locking read weakens consistent snapshot semantics.
- [x] 6.3 Extend shared real-provider tests with independent insert/delete/selected-value/sort-key mutations after first yield; verify all three snapshot providers return the initial authorized ordered sequence/values with minimal reader columns and zero projected entity materializations.
- [x] 6.4 Verify provider transaction loss, cancellation, duration expiry, early disposal, cap completion, and policy changes abort without retry/replay or changed isolation; run focused SQL Server/MySQL suites and explicitly report unavailable provider execution.
- [x] 6.5 Update docs/providers.md and docs/repository-acceptance.md with verified versions/model scope, prerequisites, negative capability cases, and snapshot-versus-best-effort evidence; verify every supported claim is backed by executed provider tests.

## 7. Host streaming integration

- [x] 7.1 Wire matching trusted export configuration through EmployeeApp without changing defaults, and document opt-in examples in README/docs/reads.md; verify existing list/count/detail/cursor/export acceptance remains compatible.
- [x] 7.2 Handle policy-change/duration errors explicitly before response start and abort already-started NDJSON responses without error rows or success-shaped completion; verify HTTP tests distinguish pre-start failure from partial-stream termination without buffering the whole export.
- [x] 7.3 Extend synthetic HTTP acceptance for rejected lifecycle overrides and unchanged authorized field output; verify hidden sidecars never appear and document that delivered prefixes are not retractable.

## 8. Cross-cutting verification

- [x] 8.1 Run the existing build and focused lifecycle/store/repository/mapper/read tests, then the complete shared real-provider export matrix; verify no cursor identity, grant, count metadata, projection, or disposal regressions and report missing provider evidence explicitly.
- [x] 8.2 Run strict OpenSpec validation and reconcile implementation with all mode/limit/error scenarios before syncing or archiving; verify every inherited scenario remains present and planning artifacts/main specs describe the delivered behavior.
