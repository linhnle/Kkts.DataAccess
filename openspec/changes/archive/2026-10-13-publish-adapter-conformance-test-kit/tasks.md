# Tasks

## 1. Test-only contracts and package

- [x] 1.1 Confirm structured errors, composite keys, and provider capabilities are implemented; add Kkts.DataAccess.Testing with core-only dependencies, fixture/assertion/result contracts, and stable scenario IDs, verifying no runner/EF/driver/friend-access dependency.
- [x] 1.2 Implement exact fixture ownership/setup/cancellation/teardown semantics; verify failed/canceled setup/scenarios dispose only explicitly owned resources.
- [x] 1.3 Document fixture requirements and test-only consumption; verify package sample invokes checks through protected repositories without exposing internal plan constructors.

## 2. Baseline behavioral checks

- [x] 2.1 Extract reusable authorization/grant/detail/count/list/order/output checks from existing fixtures; verify mandatory isolation, deny precedence, object-key lookup, hidden fields, optional count matrix, and exact bounded output.
- [x] 2.2 Add cancellation/disposal/translation-failure checks and intentionally broken deny/over-return/disposal controls; verify the kit fails each fault without weakening assertions.
- [x] 2.3 Document baseline coverage and fixture limits alongside those tests; verify in-memory results make no database-execution claim.

## 3. Optional capability and evidence modules

- [x] 3.1 Add projected seek/composite/cursor/export policy/snapshot/cap/deadline modules using declared capabilities; verify genuine optional absence runs rejection checks while claimed-capability failures are failures.
- [x] 3.2 Add reader/materialization/query evidence hooks and Passed/Failed/Unsupported/NotRun report aggregation; verify missing required evidence prevents overall pass and reports exclude secrets/connection data.
- [x] 3.3 Document evidence interpretation and non-certification warnings; verify a broken sidecar adapter and missing relational evidence produce the expected failing/not-run outcomes.

## 4. Repository and extension consumers

- [x] 4.1 Refactor existing recording/SQLite/SQL Server/MySQL suites to consume kit scenarios without deleting provider-specific tests; verify unchanged or greater coverage and explicit unavailable-provider evidence.
- [x] 4.2 Add CustomReadAdapter extension example and a non-xUnit consumer, updating extension/provider docs; verify both compile/run against the packaged API without production hosts referencing the kit.

## 5. Integration

- [x] 5.1 Run build, kit negative controls, inherited provider tests, and package packing/clean-consumer acceptance; verify dependency neutrality, fixture cleanup, report accuracy, and strict OpenSpec validation.
