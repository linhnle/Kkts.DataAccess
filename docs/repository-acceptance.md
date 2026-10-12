# Repository provider acceptance

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
