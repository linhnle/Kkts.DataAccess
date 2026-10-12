# Tasks

## 1. Operation-specific contracts and coordinated API migration

- [x] 1.1 Replace `ReadRequest` in `ReadContracts.cs` with the four flat request records from the design, add type-local strict JSON handling, and move the required detail key into `DetailReadRequest`; verify contract tests cover supported members, defaults, round trips, and rejection of action/identity/unknown/operation-inapplicable members.
- [x] 1.2 Migrate `IAuthorizedReadRepository<T>`, `AuthorizedReadRepository<T>`, and the SQLite facade together; remove the shared type and legacy overloads, preserve constructors/results/adapter contracts, and migrate typed test callers and EmployeeApp handler signatures so `dotnet build Kkts.DataAccess.slnx --no-restore` succeeds.
- [x] 1.3 Refactor repository validation into private applicable-component helpers and bind capture internally to Read; verify recording-adapter tests show invalid selection/filter/sort/list pagination/detail keys open zero entity sessions, and count/detail/export have no synthetic list-pagination checks.
- [x] 1.4 Extend repository tests for valid host limits below the default list size (including a compatible export batch size), count without selection/sort grants, implicit detail-key lookup versus explicit key filtering, optional detail filters, and preserved exact totals/default-or-empty selections; verify the targeted contract and `RepositoryTests` suites pass.
- [x] 1.5 Update `docs/reads.md`, `docs/repositories.md`, and the facade summary with the four input shapes, strict JSON, unchanged results/limits, and breaking count/detail/export migration examples; verify documentation no longer promises compatible old read methods or describes ignored client export pagination.

## 2. Transport and relational behavior

- [x] 2.1 Update `scripts/example_acceptance.py` to send `{}` for unfiltered count and add independent HTTP 400 cases for count selection/sort/offset/limit, export offset/limit, and action/unknown/identity members; verify supported requests retain exact count, NDJSON export, authentication/antiforgery behavior, and forbidden-field HTTP 403 results.
- [x] 2.2 Extend the shared `ReadTests` suite with operation-specific requests, strict transport-contract coverage, absent/null/incompatible detail keys, and detail filtering; verify SQLite execution preserves mandatory/Allow/Deny composition, alias ordering, hidden fields, and inaccessible/nonexistent detail equivalence.
- [x] 2.3 Preserve and migrate cancellation, stopped enumeration, provider-failure, captured-version export, and warmed DTO-permission tests across repository and read suites; verify sessions are disposed and no local/unrestricted fallback or additional policy captures occur.
- [x] 2.4 Update README and relevant provider/repository acceptance documentation for the new HTTP payloads and API migration, without claiming new acceptance results; verify examples match EmployeeApp signatures and still describe host-controlled batching and non-snapshot reads.

## 3. Integrated verification

- [x] 3.1 Build the entire solution and run one focused test command covering contract, repository, read, compiler-action, and translation tests with `Provider!=SqlServer&Provider!=MySql`; verify existing compiler action enforcement and core/custom-adapter builds remain intact, recording commands and outcomes.
- [x] 3.2 Run the updated HTTP acceptance script against a fresh synthetic EmployeeApp database; verify all valid and strict-rejection scenarios pass, then stop the owned process and clean up only the created database files.
- [x] 3.3 Run inherited SQL Server/MySQL read acceptance against configured isolated servers, or explicitly report those provider checks as not run when unavailable; verify the same operation-specific behavior without treating local SQLite results as remote-provider evidence.
- [x] 3.4 Run the existing relevant formatting checks and strict OpenSpec validation; verify all implementation tasks are supported by passing evidence or an explicitly documented external validation blocker, and no old shared-request signatures remain in production code or example callers.

Validation evidence: [repository acceptance](../../../docs/repository-acceptance.md#operation-specific-request-migration).
SQL Server/MySQL execution was not run because isolated test connections were
not configured; this completes task 3.3's explicit unavailable-provider branch,
not its real-provider execution branch.
