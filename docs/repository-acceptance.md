# Repository provider acceptance

## Export lifecycle and snapshots

Verification on 2026-10-12, SDK 10.0.401:

- Final solution build passed with **zero warnings/errors**. The combined focused
  lifecycle/store/repository/mapper/read/cursor/projection/seek/compiler/translation
  selection passed **288 tests, 0 failed, 0 skipped**, including all shared real-provider
  cases. Executed server versions were SQLite **3.53.3**, SQL Server
  **16.0.4255.1** (2022), and MySQL **8.4.11**. CSharpier checked all 22 changed C#
  files; strict delta validation and inherited-scenario retention checks passed.
  All three affected main specs were reconciled; strict validation passed for
  all five main specifications. Late timer delivery is also explicitly tested
  against monotonic elapsed checks.
- Synthetic EmployeeApp HTTP acceptance passed twice on fresh isolated databases:
  unchanged defaults and `AbortOnPolicyChange`/`Snapshot` with positive row/duration
  bounds. Inherited two-tenant lifecycle, list/count/detail/cursor/export and narrow
  authorized field behavior remain compatible. Client lifecycle overrides were
  rejected and continuation sidecars were absent. Both servers were stopped and
  their database/key artifacts cleaned up. Browser acceptance was not rerun.
- Contract/lifecycle/store/repository/read-contract selection passed **127 tests**.
  It covers independent enumeration state, strict host-only settings, atomic
  activation stamps, publish/rollback/reactivation, revision exhaustion, exact
  row caps without probes/counts, both policy checkpoints, actual DTO mapping,
  monotonic timers, caller-cancellation precedence, late returns, consumer pauses
  and cooperative disposal. Existing captured-only custom sources remain valid.
- Real-provider read/snapshot matrix passed **91 tests, 0 failed, 0 skipped**
  against file-backed SQLite and configured isolated SQL Server/MySQL servers.
  Fixture-owned databases are unique and disposed after each case. Tests provision
  their own snapshot prerequisites; library code never changes those settings.
- Concurrent writers insert/delete and change selected/sort/membership values
  after first yield. All providers return the original authorized snapshot
  sequence/values, one session/transaction, minimal reader columns, zero projected
  entity materializations, and no replay after connection/native rollback.
- Negative evidence includes SQLite missing WAL/private cache/dirty reads, model
  views/query filters/retry strategies, SQL Server OFF/RCSI-only and
  IN_TRANSITION_TO_OFF, and MySQL MyISAM. MySQL retains Repeatable Read semantics
  with a Read Committed session default; the session variable is deliberately
  not substituted for the active transaction's behavior.
- The Oracle MySQL driver retains native transaction references after rollback
  and can continue SELECTs. The approved provider hook uses a unique owned
  savepoint checked before/after reads; native rollback now fails closed.
  Shared EF remains driver-neutral, with provider-owned factory hooks.
- Same-file SQLite policy publication/rollback commits while an entity snapshot
  is held and remains visible to fresh activation reads. Source failures, row cap,
  elapsed deadline, cancellation and early disposal release owned resources.
- Focused real HTTP middleware/SQLite prerequisite follow-up passed **13 tests**,
  including actual SQLite views disguised as table mappings and the unchanged
  facade/default construction. HTTP failures return safe 409/408 JSON before
  start; after start a delivered NDJSON prefix is followed by abnormal termination,
  not an appended error row or successful completion. Prefixes cannot be withdrawn.

These results verify ordinary keyed single-table mapped resources, not arbitrary
views, query hints, external sources or distributed policy/entity snapshots.
Default best-effort behavior and cursor version identity remain unchanged.

## Authorized database projection

Local verification on 2026-10-12, SDK 10.0.401, after implementing and
synchronizing request separation, cursor/keyset traversal and optional metadata
in that order:

- Solution build passed with **zero warnings/errors**, including the core-only
  custom projected-seek registration and EmployeeApp on explicit required mode.
- The combined local selection passed **149 tests, 0 failed, 0 skipped**:

  ```sh
  dotnet test tests/Kkts.DataAccess.Tests --no-build --no-restore \
    --filter '(FullyQualifiedName~ProjectionContractTests|FullyQualifiedName~DtoMapperTests|FullyQualifiedName~DtoShapeCacheTests|FullyQualifiedName~DependencyTests|FullyQualifiedName~ReadContractTests|FullyQualifiedName~RepositoryTests|FullyQualifiedName~ReadTests|FullyQualifiedName~CursorTokenTests|FullyQualifiedName~ContinuationProtectionTests|FullyQualifiedName~SeekContractTests|FullyQualifiedName~CompilerTests|FullyQualifiedName~TranslationTests)&Provider!=SqlServer&Provider!=MySql'
  ```

  Evidence covers immutable output/continuation bindings, strict scalar/null/
  cardinality checks, nonserialized sidecars, constructor/getter-free mapping,
  JSON golden parity across supported scalars and aliases, cross-mode structural
  cache reuse/concurrency/eviction/failure, operation-specific capabilities and
  explicit absence-only compatibility. Projected seek adapters need no raw seek.
  Malformed rows and projected failures dispose without entity retries.
- SQLite reader metadata excludes the unselected **65,536-character payload**
  and hidden tenant/ordinary order columns. Overlapping aliases share physical
  slots; supported output converters restore their CLR values. An entity
  materialization interceptor observes **zero entities** in projected retrieval
  and counts. Empty offset/detail output selects a constant marker, preserving
  cardinality, not-found parity and duplicate detection without a hidden key.
  Unmapped selected getters fail before retrieval. Invalid stored converter data
  causes a propagated materialization failure with one narrow reader query and
  no full-entity retry in either host mode.
- Projected seek matches complete authorized provider-ordered sequences for
  hidden keys, empty output, overlapping aliases, repeated/mixed-direction sorts,
  nullable numeric/date/string values and collation/precision cases. Reader slots
  contain only deduplicated selected/continuation properties and no wide payload.
  Lookahead boundaries use the last returned row; captured exports retain bindings
  and policy across publication and snapshot boundaries before yield. Failure,
  cancellation and early termination release owned sessions.
- Fresh isolated SQLite HTTP acceptance passed under required projection:
  inherited two-tenant authorization/lifecycle, hidden/detail/export fields,
  versioned counts, all nullable metadata combinations, cursor traversal/replay/
  protection, and rejection of client projection expressions/mode overrides.
  The server was stopped and its database/key/log artifacts removed.
  Browser acceptance was not rerun.
- CSharpier checked all 17 changed C# files and whitespace checks passed. Strict
  validation passed for the projection delta and all five prerequisite main specs;
  inherited scenarios and the main seek requirement remain present.
- At the initial local run, SQL Server/MySQL projection and seek execution were
  **not run**: isolated
  `KDA_SQLSERVER_TEST` and `KDA_MYSQL_TEST` connections were unconfigured.
  Inherited relational cases compile, but SQLite/earlier baseline evidence does
  did not satisfy real-provider projection acceptance; task 6.1 was left open
  pending the follow-up below.

### Real-provider follow-up

Verified 2026-10-12 with the user's configured isolated SQL Server/MySQL
connections loaded from the local environment file without displaying
credentials or executing it as shell code:

- All **42 inherited read tests passed, 0 failed, 0 skipped** across SQLite,
  SQL Server and MySQL:

  ```sh
  dotnet test tests/Kkts.DataAccess.Tests --no-restore \
    --filter 'FullyQualifiedName~ReadTests' --verbosity minimal
  ```

  The runner received `KDA_SQLSERVER_TEST` and `KDA_MYSQL_TEST` from that file.
  Tests created unique fixture databases and cleaned them up through fixture
  disposal; no server configuration or production schema was changed.
- All three providers verify narrow result-reader columns, absent wide hidden
  payload/tenant fields, alias deduplication, empty output/detail parity,
  supported scalar converters, unmapped selection rejection, aggregate-only
  counts, and materialization errors without entity retries in both host modes.
  Projected seek preserves complete authorized provider ordering across hidden
  stable keys, overlapping aliases, repeated/mixed-direction sorts, nulls,
  precision and the configured collations, with zero projected entity instances.
  The inherited optional-total/has-more and disposal cases also passed.
- Two fixture-only issues were corrected: exact-count SQL checks now expect
  SQL Server's `COUNT_BIG` rather than `COUNT`; the materialization observer is
  a shared EF singleton with context-specific recorders, avoiding excessive
  internal service-provider creation. A positive control confirms the observer
  counts raw entity loading before checking zero entities in projected paths.
  No production query fallback, coercion or warning suppression was added.
- Final solution build passed with zero warnings/errors. The combined focused
  contract/mapper/cache/repository/read/cursor/token/protection/seek/compiler/
  translation selection above, **without remote-provider exclusions**, passed
  **177 tests, 0 failed, 0 skipped**. CSharpier and whitespace checks passed;
  strict validation passed for the projection delta and all five main specs.
- **Task 6.1 is complete**, making the projection change **27/27 complete**.
  Both capability deltas were synchronized to main specs and the change was
  [archived on 2026-10-12](../openspec/changes/archive/2026-10-12-add-authorized-database-projection/).

No timing/allocation or database-snapshot guarantee is inferred from reader
minimization. See [provider scope](providers.md#scalar-database-projection) and
[custom-adapter migration](repositories.md).

## Optional totals and versioned counts

Verified 2026-10-12 on SDK 10.0.401 after confirming implemented and synchronized
request separation and cursor/keyset prerequisites:

- `dotnet build Kkts.DataAccess.slnx --no-restore` passed with zero warnings/errors.
  No dependency or trusted session method was added.
- The combined local contract/repository/read/cursor/token/protection/seek/
  compiler/translation selection passed **113 tests, 0 failed, 0 skipped**:

  ```sh
  dotnet test tests/Kkts.DataAccess.Tests --no-build --no-restore \
    --filter '(FullyQualifiedName~ReadContractTests|FullyQualifiedName~RepositoryTests|FullyQualifiedName~ReadTests|FullyQualifiedName~CursorTokenTests|FullyQualifiedName~ContinuationProtectionTests|FullyQualifiedName~SeekContractTests|FullyQualifiedName~CompilerTests|FullyQualifiedName~TranslationTests)&Provider!=SqlServer&Provider!=MySql'
  ```

  Evidence covers strict boolean/list-only flags, explicit null metadata under
  default/web/ignore-null/default settings, exact zero/false, all four offset and
  cursor metadata combinations, precise count/fetch bounds, lookahead trimming,
  forbidden trailing rows, hidden/empty output, overflow before session access,
  deliberately divergent count/page observations, count failures, publication
  during count, cancellation/disposal, and metadata changes on valid resume.
  Existing incompatible-query/context/policy token rejection remains covered.
- SQLite relational SQL interception verifies zero aggregates when totals are
  disabled, one when enabled, one protected bounded row query, and unchanged
  base-query totals across cursor boundaries. Both core and SQLite facade pass.
- SQL Server/MySQL verification was rerun on 2026-10-12 with
  `KDA_SQLSERVER_TEST` and `KDA_MYSQL_TEST` loaded from the repository-root `.env`
  without executing it as shell code or displaying connection strings.
  All **28 inherited remote-provider read tests passed, 0 failed, 0 skipped**:

  ```sh
  dotnet test tests/Kkts.DataAccess.Tests/Kkts.DataAccess.Tests.csproj --no-restore \
    --filter 'Provider=SqlServer|Provider=MySql' --verbosity minimal
  ```

  This includes all four metadata combinations, aggregate-query counts,
  bounded authorized lookahead, cursor metadata toggling, and existing
  authorization, ordering, projection, cancellation, and disposal checks.
  The fixtures create unique isolated databases and delete them on disposal.
- Fresh isolated SQLite EmployeeApp HTTP acceptance passed the two-tenant
  authorization/lifecycle matrix, versioned count-object responses, default
  totals, all offset/cursor metadata combinations, explicit null/boolean fields,
  metadata changes on continuation, strict invalid-member/boolean rejection,
  hidden/export fields, forbidden filters, authentication and antiforgery.
  The owned server was stopped; its database, keys and log were removed.
  Browser acceptance was not rerun.
- CSharpier checked all seven changed C# files; `git diff --check` passed.
  Strict OpenSpec validation passed for this delta and all five main specs.
  The modified cursor requirement now exists in main specs; the prerequisite
  ordering constraint is resolved. No synchronization of unfinished work was used.

See [metadata/count migration](reads.md#optional-list-metadata-and-versioned-counts).
Earlier exact-total/bare-count evidence below describes its historical API.

## Forward cursor and keyset export acceptance

Verified 2026-10-12 on SDK 10.0.401, after synchronizing the implemented
operation-specific request prerequisite into both main specs (without archiving):

- Solution restore, locked restore and build passed with zero warnings/errors.
  Core retains no ASP.NET reference; the optional protection package uses
  DataProtection.Extensions 10.0.12. The core-only custom seek registration
  example builds.
- The focused local command passed **90 tests, 0 failed, 0 skipped**:

  ```sh
  dotnet test Kkts.DataAccess.slnx --no-restore \
    --filter '(FullyQualifiedName~ReadContractTests|FullyQualifiedName~CursorTokenTests|FullyQualifiedName~ContinuationProtectionTests|FullyQualifiedName~SeekContractTests|FullyQualifiedName~RepositoryTests|FullyQualifiedName~ReadTests|FullyQualifiedName~CompilerTests|FullyQualifiedName~TranslationTests)&Provider!=SqlServer&Provider!=MySql'
  ```

  Evidence includes immutable typed boundaries; exact cardinality/null/type
  checks; lossless decimal representation/floating bits/temporal values; strict
  cursor JSON/API signatures; canonical binding and missing/null unused context;
  policy/schema/property/compiler/subject/context/query/dataset mismatch;
  exact encoded/decrypted size limits, expiry and non-sliding lifetime; generic
  token errors without swallowing unrelated configuration failures; encrypted
  protection with restart/rotation, wrong purpose/application and key loss;
  bounded lookahead and last-returned-row boundaries; request snapshots; private
  captured-policy exports; mutation, failure, cancellation and disposal.
- SQLite relational seek batches match the complete protected provider-ordered
  sequence across integer keys, string ties/case/trailing spaces, repeated and
  mixed directions, nullable numeric/date/string values, and decimal(18,2)
  precision boundaries. SQL interception confirms protected WHERE/order and
  no OFFSET. SQLite EF 10.0.12 translates decimal ordering with `EF_DECIMAL`
  collation; no lossy conversion is used. Unmapped, converter-backed and
  unsupported floating seek fields fail explicitly without querying.
- SQL Server/MySQL seek and inherited read execution were **not run**:
  `KDA_SQLSERVER_TEST` and `KDA_MYSQL_TEST` were unconfigured. The inherited
  cases compile; neither SQLite results nor earlier offset-provider acceptance
  constitute new remote seek evidence. Verify their real-provider combinations
  before deployment, especially provider-specific mappings/comparisons.
- Fresh synthetic SQLite EmployeeApp HTTP acceptance passed both tenants'
  inherited authorization/lifecycle matrix plus cursor traversal, exact totals,
  final-token absence, hidden/empty selection, tampering, query/caller/policy
  replay rejection, strict unsupported-member handling, and authentication/
  antiforgery. A separately captured cursor and authenticated cookie resumed
  after process restart with matching persistent keys, database, bindings and
  current authorization. Tokens/identity/boundary payloads were not logged.
- Core and optional protection packages were packed, restored into an isolated
  package-only console consumer, built and executed with a protection roundtrip.
  The protection project also built with `UsePackedCore=true`; normal project
  references were restored and the solution rebuilt afterward.
- CSharpier checked all 54 C#/XML files; whitespace checks passed. Strict
  validation passed for the cursor delta and all five main specs. The cursor
  delta remains unsynchronized/unarchived until the next explicit workflow.
- All owned servers were stopped. Temporary HTTP database, key ring, cookie/
  resume state, ordering probe and isolated package/consumer artifacts were
  removed. Browser acceptance was not rerun.

Cursor pages still report exact totals; exports use internal keysets and no
public resume protocol. Neither operation promises a snapshot. Entity-only
custom adapters must migrate export to seek; offset/count/detail remain usable.
See [cursor usage](reads.md#forward-cursors), [adapter migration](repositories.md),
and [verified provider scope](providers.md#seek-execution).

## Operation-specific request migration

Verified 2026-10-12 on SDK 10.0.401:

- `dotnet build Kkts.DataAccess.slnx --no-restore` passed with zero warnings
  and errors, including EmployeeApp and the core-only custom adapter.
- The focused local command passed **50 tests, 0 failed, 0 skipped**:

  ```sh
  dotnet test Kkts.DataAccess.slnx --no-restore \
    --filter '(FullyQualifiedName~ReadContractTests|FullyQualifiedName~RepositoryTests|FullyQualifiedName~ReadTests|FullyQualifiedName~CompilerTests|FullyQualifiedName~TranslationTests)&Provider!=SqlServer&Provider!=MySql'
  ```

  It covers strict request JSON and matching core/interface/facade signatures,
  invalid inputs before session access, missing/null/incompatible detail keys,
  optional detail filters, count without selection/sort grants, non-list reads
  with host limits below fifty, empty/default output, inherited authorization,
  captured export policy, cancellation, failure disposal, and translation errors.
- `scripts/example_acceptance.py` passed against a fresh, isolated SQLite
  EmployeeApp database in Development. List/count/export use their respective
  payload shapes; count selection/sort/offset/limit, export pagination/batch
  overrides, and action/unknown/identity members return HTTP 400. Both tenants'
  role matrix, detail not-found equivalence, hidden fields, forbidden-filter
  HTTP 403, authentication/antiforgery, preview, conflicts, publication, rollback,
  exact count, and NDJSON export passed. The owned server was stopped and its
  database files removed. Browser checks were not rerun.
- SQL Server/MySQL execution was **not run**: neither `KDA_SQLSERVER_TEST` nor
  `KDA_MYSQL_TEST` was configured. Their inherited test callers compile, but
  SQLite results are not new remote-provider evidence.
- CSharpier checks passed for all eight changed C# files; `git diff --check`
  passed. `openspec validate split-read-requests-by-operation --strict` passed.
  No production/example shared-request signature or legacy detail overload
  remains. No dependencies, trusted adapter/session contracts, result types,
  database schema, or compiler action semantics were changed.

The shared `ReadRequest` is intentionally removed; see the
[breaking migration](reads.md#breaking-request-migration). Earlier evidence below
describes the pre-migration API and is not a rerun of its HTTP/browser/package or
remote-provider checks.

## Expression-query integration

Verified 2026-10-12 on SDK 10.0.401 with Kkts.Expressions 3.1.0 and
Kkts.Dynamic 2.1.0 unchanged:

- `dotnet restore Kkts.DataAccess.slnx --locked-mode` passed without dependency
  or lock-file changes.
- `dotnet build Kkts.DataAccess.slnx --no-restore` passed with zero warnings and
  zero errors, including the existing core-only custom-adapter example and
  employee application.
- The focused expression/compiler/read/repository run passed all 45 tests.
- The README SQLite/core command,
  `dotnet test Kkts.DataAccess.slnx --no-build --filter 'Provider!=SqlServer&Provider!=MySql'`,
  passed 75 tests, failed none, and skipped the one opt-in DTO performance
  measurement. No performance measurement was rerun for this integration.
- Both already-running local fixture services from
  `scripts/provider-databases.compose.yml` were used with their isolated test
  connections. The remote-only command,
  `dotnet test Kkts.DataAccess.slnx --no-build --filter 'Provider=SqlServer|Provider=MySql'`,
  passed all 14 tests (seven each for real SQL Server and MySQL), with no failures
  or skips. Each test created and removed its own entity database.
- The expanded shared provider regression checks null eq/ne, explicit null
  predicates on nullable and nonnullable fields, ordered comparisons against
  null, mixed null membership and its negation, nested NOT, and string guards
  against fixed identifiers and in-memory truth tables. All three providers
  preserve mandatory tenant isolation and overlapping deny exclusion.
- Ordering checks cover exact public aliases, mixed ascending/descending keys,
  repeated keys, stable ties, and single-row export batches without exposing the
  hidden stable key. Existing checks still reject forbidden Sort before session
  opening and propagate translation failures without a local/unrestricted
  fallback; cancellation, disposal, and captured-policy/context isolation pass.
- Production dependency inspection confirms `ExpressionsAdapter<T>` uses
  `ValidateFilterTree`, `FormatFilterTree`, `AnalyzeExpression`, and structured
  `TryBuildPredicate<T>`, while the EF adapter uses structured `OrderByInfo`
  ordering. There is no parallel manual leaf/operator compiler or manual
  Queryable ordering construction. Public policy JSON, diagnostics, read plans,
  sort requests, and repository constructors remain compatible.
- `npm run format:check` passed CSharpier checks for 46 C#/XML files and the
  Prettier JavaScript check. Strict OpenSpec validation passed with the declared
  implementation-only spec skip.

Total for the two complete suite selections: **89 passed, 0 failed, 1 skipped**.
The HTTP/browser/package-consumption walkthroughs below are earlier baseline
evidence, not reruns for this expression integration.

## Original repository-provider baseline

Verified 2026-10-12 on .NET 10.0.401. Kkts.Expressions 3.1.0 and Kkts.Dynamic 2.1.0 remain unchanged.

- Locked solution restore passed; build completed with zero warnings/errors.
- 43 tests passed, 0 failed, 0 skipped, with real SQLite, SQL Server 2022 (16.0.4255.1) and MySQL 8.4.11 entity databases and independently persisted SQLite policies.
- Core recording-adapter checks cover field rejection before entity access, DTO shapes, detail/count and captured export; these supplement rather than substitute for real provider tests.
- Real provider tests cover tenant isolation, deny precedence, nulls, decimal precision, DateTime/decimal ordering, database-default case behavior, unmapped-expression translation failure, stable ties, counts/details, export version capture, cancellation and early session disposal.
- Three fresh-database HTTP walkthroughs passed: SQLite, SQL Server and MySQL entities with SQLite policies. Both tenants' manager/HR/employee/no-grant matrix, hidden fields, inaccessible detail, administration protection, antiforgery, preview, publication/rollback and revision conflicts passed.
- Unknown provider and absent non-SQLite entity connection failed clearly at startup. Mismatched provider registrations fail before queries; runtime context identity is rechecked and owned contexts are disposed.
- Core-only external adapter example builds. Five library packages were packed and restored into a fresh isolated consumer cache; all provider registrations and unchanged SQLite constructor loaded successfully without project references.
- OpenSpec strict validation passed. All original policy lifecycle tests continue to pass; no policy schema migration was made.

The CI workflow provisions both remote database services and runs the unfiltered suite. It was added and locally mirrored with real Docker services; a hosted GitHub run is not claimed. Repository owners must configure branch protection to require its status. Unconfigured remote-provider tests fail explicitly, and filtered local runs must state those suites were not run.

Test server containers expose localhost-only fixture ports. Exact dependency graph is recorded in lock files. See [providers](providers.md) for setup and observed collation/type limits and [repositories](repositories.md) for adapter contracts. Existing designer controls were retained; HTTP acceptance verified their backing routes for each backend, while the earlier browser acceptance remains documented in [Phase 1 results](acceptance.md).
