# Entity providers

Core retains Kkts.Expressions 3.1.0 and Kkts.Dynamic 2.1.0. Shared execution uses EF Core Relational 10.0.12; SQLite and SQL Server providers are 10.0.12. Oracle MySql.EntityFrameworkCore 10.0.9 targets net10.0 and depends on EF Core 10.0.9 or newer; locked restore/build verified it with 10.0.12. Policies remain in SQLite for every entity provider.

| Tested behavior | SQLite | SQL Server 2022 | MySQL 8.4 |
| --- | --- | --- | --- |
| Mandatory tenant predicate, allow/deny, hidden fields | Passed | Passed | Passed |
| List/detail/count/export and captured policy | Passed | Passed | Passed |
| Null string/decimal, decimal(18,2) roundtrip | Passed | Passed | Passed |
| DateTime and nullable decimal ordering | Passed | Passed | Passed |
| Default text equality case behavior in test servers | Case-sensitive | Case-insensitive | Case-insensitive |
| Unmapped expression translation | Explicit failure | Explicit failure | Explicit failure |

This is a verified baseline, not a promise that every registered scalar/operator translates. Collation is controlled by the host database; production collation can differ. DateTimeOffset and high-precision numeric combinations need explicit host/provider validation. Provider translation errors propagate; no fallback or local authorization evaluation is used. Core literal conversion still rejects lossy inputs.

The table records the earlier provider baseline. The operation-specific API
migration uses the same trusted adapter/session contracts on all providers:
list/count/detail/export take their distinct requests, with detail's key inside
its request and no client export pagination. Unknown JSON members are rejected;
see [read migration](reads.md#breaking-request-migration). Updated validation
evidence and any unavailable provider runs are recorded in
[repository acceptance](repository-acceptance.md). Host batching and non-snapshot
count/page/export semantics remain unchanged.

Optional pagination metadata uses the same shared session methods on every
provider. Disabling total suppresses the aggregate entirely; offset has-more
fetches at most Limit + 1 authorized rows, while cursor always retains its existing
lookahead for token generation. Has-more follows that page observation, not count
arithmetic. Standalone counts now expose the captured version and exact total as
`ReadCount`. The inherited relational metadata suite records aggregate-query
counts and bounded protected row queries; its current provider execution evidence
is reported in [acceptance](repository-acceptance.md), separately from the baseline
table above. No snapshot or new translation support is implied.

The shared EF adapter uses Kkts.Expressions 3.1.0 structured `OrderByInfo`
ordering over the already authorized query. Public `SortField` aliases and Sort
grants are resolved by the repository before entity access; only registered
property bindings reach the ordering API. Multi-key directions and stable-key
completion retain their existing contract, including deterministic ties and
bounded exports. Delegating expression construction does not expand provider
type support, alter database collation/null placement, or permit local fallback.
Custom adapters still consume the provider-neutral `EntityReadPlan<T>` without
requiring IQueryable or Expressions.

## Seek execution

The shared EF session implements `IEntitySeekReadSession<T>` for cursor pages and
keyset exports. Seek explicitly orders nulls first ascending/last descending,
applies provider-side lexicographic comparisons to the complete protected query,
and uses bounded `Take` without `Skip`. Offset sorting is unchanged.

Local SQLite EF 10.0.12 conformance verifies integer tie-breakers, string-plus-key
orders (including case variants/trailing spaces), repeated/mixed directions,
nullable integers/dates/strings/decimals, and decimal(18,2) precision boundaries.
SQLite's installed provider uses `EF_DECIMAL` collation for exact decimal ordering;
no double conversion is introduced. The inherited SQL Server/MySQL conformance
cases also passed against configured isolated databases on 2026-10-12, including
projected traversal. This verifies the fixture's mapped combinations and
configured collations, not arbitrary production mappings. Current execution
evidence is recorded in [acceptance](repository-acceptance.md#real-provider-follow-up);
earlier offset-provider results are not substituted for seek evidence.

Seek requires actual mapped scalar properties and rejects custom value converters
and unmapped members. The implemented comparison surface is string, byte/sbyte,
short/int/long, decimal, DateTime and numeric enums with default mappings (and
nullable forms); enum combinations need host/provider verification before use.
Guid, DateTimeOffset, floating-point/special values, other unsigned numeric types
and custom converters are not advertised as seek-supported and fail explicitly.
Mapped combinations can still encounter provider translation restrictions; those
errors propagate without coercion, collation overrides, local evaluation or
offset/unrestricted fallback. Wider offset-sort support does not imply seek
support. Hidden boundary values remain trusted metadata, not public fields.

## Scalar database projection

The shared EF session also implements projected page/single and projected seek.
Provider-tagged helpers keep the same identities and delegate to this shared
execution. The core/facade default is required projection; compatibility mode
still selects the projected path for supplied adapters.

Selectors consist only of scalar properties resolved against the actual EF
model, with matching CLR property bindings. Supported value converters retain
their mapped CLR values for output; seek comparisons retain the narrower
converter-free support described above. An unmapped selected getter fails before
retrieval rather than triggering EF top-level client evaluation over entities.
Translation/materialization failures propagate in both host modes.

Authorization/filter/order columns may legitimately appear in SQL WHERE/ORDER
BY without appearing in result-reader columns. Projection minimizes reader
values, not SQL text references. Aliases and output/sidecar overlaps deduplicate
physical slots, then restore logical order. Empty offset/detail output uses a
constant scalar marker, not a hidden identifier; empty seek output retrieves only
the minimal continuation tuple. Count is aggregate-only and never constructs
projected rows or entities.

SQLite, SQL Server and MySQL relational evidence uses result-reader column metadata and an entity
materialization interceptor, including an unselected 64-KiB payload, converter
output, overlapping aliases, empty output, hidden predicate fields and projected
seek traversal. All 42 inherited read cases passed in the configured real-provider
follow-up, including zero projected entity instances with a positive raw-entity
control. This is not a timing/allocation claim or broader scalar/seek support
promise; see [acceptance](repository-acceptance.md#real-provider-follow-up) for the
executed scope, separately from the earlier provider baseline.

Trusted continuation values are not DTO fields or new grants. Cursor tokens keep
the existing encryption, host binding and active-version strategy; neither
projection nor token possession provides permission or a database snapshot.

## Example setup

Keep the legacy `Database='Data Source=employee.db'` setting for default SQLite behavior. New settings are `EntityProvider` (Sqlite, SqlServer, MySql), `EntityDatabase` (required for non-SQLite), and `PolicyDatabase` (SQLite connection string; defaults to legacy Database). Unknown providers fail at startup. Each connection belongs to the host; do not commit production credentials. The example explicitly creates/seeds synthetic entity fixtures; production hosts own mappings/migrations and should provision their schema separately.

```sh
ASPNETCORE_ENVIRONMENT=Development EntityProvider=MySql EntityDatabase='Server=127.0.0.1;Port=53306;User ID=root;Password=kda-local-fixture-only;SslMode=Required;Database=kda_example' PolicyDatabase='Data Source=/tmp/kda-example-policy.db' dotnet run --project examples/EmployeeApp --urls http://127.0.0.1:5192
KDA_EXAMPLE_URL=http://127.0.0.1:5192 python3 scripts/example_acceptance.py
```

For SQL Server use EntityProvider=SqlServer and an entity connection such as `Server=127.0.0.1,51433;User ID=sa;Password=Kda-local-Fixture-Only42;TrustServerCertificate=True;Database=kda_example`. Fixture credentials/certificate settings are for isolated local tests. Use trusted certificates and host-managed secrets in production. The HTTP walkthrough requires a fresh policy and fixture database.

## Real database tests

```sh
docker compose -f scripts/provider-databases.compose.yml up -d
export KDA_SQLSERVER_TEST='Server=127.0.0.1,51433;User ID=sa;Password=Kda-local-Fixture-Only42;TrustServerCertificate=True'
export KDA_MYSQL_TEST='Server=127.0.0.1;Port=53306;User ID=root;Password=kda-local-fixture-only;SslMode=Required'
dotnet restore Kkts.DataAccess.slnx --locked-mode
dotnet test Kkts.DataAccess.slnx --no-restore
docker compose -f scripts/provider-databases.compose.yml down
```

Wait for servers to be ready before tests. Tests create unique entity databases and delete them, with independent temporary SQLite policy files. Never point tests at a production server. SQL Server requires an x64-compatible Docker runtime or remote server; on Apple Silicon the real server can run under Docker's amd64 emulation. The test still executes real SQL Server queries, not an in-memory substitute.

Without server connections run `dotnet test --filter 'Provider!=SqlServer&Provider!=MySql'`. This excludes remote-provider acceptance and must be reported as not run. An unfiltered run without configured connections fails explicitly. The CI workflow runs all providers without that filter; repository owners can require its check in branch protection.

### Saved local connections

For repeated local runs, save the two `KDA_SQLSERVER_TEST` and `KDA_MYSQL_TEST`
assignments above in a repository-root `.env` file using the isolated fixture
credentials from `scripts/provider-databases.compose.yml`. `.env` is Git-ignored;
do not commit credentials. .NET does not load this file automatically. From the
repository root, export its assignments before running the provider tests:

```sh
docker compose -f scripts/provider-databases.compose.yml up -d
set -a
. ./.env
set +a
dotnet test tests/Kkts.DataAccess.Tests/Kkts.DataAccess.Tests.csproj --no-restore --filter 'Provider=SqlServer|Provider=MySql'
```

These connections target SQL Server at `127.0.0.1:51433` and MySQL at
`127.0.0.1:53306`. Wait for both servers to accept connections before testing;
`up -d` alone does not guarantee readiness. Run the locked restore above first
if dependencies have not yet been restored. The containers remain running until
you explicitly stop them with the Compose `down` command above.

Provider extension and compatibility details: [repositories](repositories.md). No policy migration or distributed transaction is introduced. Counts/pages/batches can observe concurrent entity changes.

Acceptance evidence: [repository acceptance](repository-acceptance.md).
