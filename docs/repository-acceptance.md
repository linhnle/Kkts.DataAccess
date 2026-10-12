# Repository provider acceptance

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
