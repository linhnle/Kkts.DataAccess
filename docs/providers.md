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

The shared EF adapter uses Kkts.Expressions 3.1.0 structured `OrderByInfo`
ordering over the already authorized query. Public `SortField` aliases and Sort
grants are resolved by the repository before entity access; only registered
property bindings reach the ordering API. Multi-key directions and stable-key
completion retain their existing contract, including deterministic ties and
bounded exports. Delegating expression construction does not expand provider
type support, alter database collation/null placement, or permit local fallback.
Custom adapters still consume the provider-neutral `EntityReadPlan<T>` without
requiring IQueryable or Expressions.

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

Provider extension and compatibility details: [repositories](repositories.md). No policy migration or distributed transaction is introduced. Counts/pages/batches can observe concurrent entity changes.

Acceptance evidence: [repository acceptance](repository-acceptance.md).
