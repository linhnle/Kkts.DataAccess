# Tasks

## 1. Provider-neutral contracts and authorization

- [x] 1.1 Introduce the generic authorized-read repository, immutable adapter execution-plan/session contracts and active-policy capture interface in core; verify core has no EF/provider dependency and public consumer results contain no raw entity or query handle.
- [x] 1.2 Extract the shared authorized orchestrator without changing request/result contracts; verify existing composition, forbidden-field pre-execution, DTO shape, detail/count and captured-export tests pass against a recording adapter.
- [x] 1.3 Implement IActivePolicySource on SqlitePolicyStore without changing lifecycle/schema behavior; verify policy-store regression tests and separate-store capture/cancellation checks pass.
- [x] 1.4 Document the repository/adapter trust boundary, plan validation, session ownership and additional-provider extension example; verify a minimal external adapter project builds against core alone.

## 2. Shared EF Core executor and SQLite compatibility

- [x] 2.1 Add the shared EF Core adapter project with no-tracking authorized queries, deterministic ordering, pre-page counts and bounded export sessions; verify real SQLite conformance and disposal on failure/cancellation/early enumeration termination.
- [x] 2.2 Add explicit provider registration and mismatch validation, and adapt the existing SQLite AuthorizedReads constructor/methods as a delegating compatibility facade; verify an unchanged legacy caller builds and all SQLite read/translation tests pass.
- [x] 2.3 Split SQLite policy and entity fixtures and document migration/default configuration; verify published-policy changes during entity export retain the original version while new reads observe the change.

## 3. SQL Server entity adapter

- [x] 3.1 Add and pin the Microsoft SQL Server EF provider aligned with the shared EF line; verify locked restore/build and provider registration rejection for inconsistent configuration.
- [x] 3.2 Implement SQL Server registration and capability checks using the shared executor; verify the common real SQL Server 2022 suite for tenant/deny isolation, DTO fields, filters, detail/count, stable ties and export version capture.
- [x] 3.3 Add SQL Server boundary tests for nulls, string collation, decimal/date ordering, translation failures and cancellation; document tested server/package versions and unsupported combinations, and verify the documented test connection setup runs.

## 4. MySQL entity adapter

- [x] 4.1 Verify Oracle MySql.EntityFrameworkCore package metadata and supported EF Core 10 compatibility, pin compatible exact dependencies and add the adapter project; verify locked restore/build smoke checks and report any dependency blocker without silently downgrading or omitting MySQL.
- [x] 4.2 Implement MySQL registration and capability checks through the shared executor; verify the common real MySQL 8.4 suite for tenant/deny isolation, DTO fields, filters, detail/count, stable ties and captured exports.
- [x] 4.3 Add MySQL null/collation/precision/date/decimal boundary and translation-failure tests; document tested versions and restrictions, and verify cancellation/disposal and the documented connection setup.

## 5. Example and required provider acceptance

- [x] 5.1 Update EmployeeApp to select EntityProvider/EntityDatabase independently from SQLite PolicyDatabase while preserving the legacy Database/default behavior; verify startup configuration failures and the two-tenant role/read matrix on each provider with policy storage on SQLite.
- [x] 5.2 Document provider startup, isolated fixture initialization, host-owned entity migrations and credentials handling; verify list/detail/count/export and SQLite publish/rollback walkthroughs with each entity backend, including the existing designer/HTTP behavior.
- [x] 5.3 Add isolated SQL Server/MySQL test provisioning and required CI provider jobs; verify all three real-provider suites execute and missing infrastructure is reported as not run rather than a passing acceptance result.
- [x] 5.4 Pack core/shared EF/provider packages and consume each from an isolated sample; verify dependencies remain independent, legacy SQLite consumption works and Kkts.Expressions 3.1.0/Kkts.Dynamic 2.1.0 remain unchanged.
- [x] 5.5 Run the complete existing regression and new provider matrix, locked restore/build/test and OpenSpec validation; record results for every new scenario and existing authorized-read guarantee before marking the change complete.
