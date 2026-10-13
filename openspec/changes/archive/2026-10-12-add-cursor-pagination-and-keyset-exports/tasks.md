# Tasks

## 1. Prerequisite and trusted seek execution

- [x] 1.1 Verify `split-read-requests-by-operation` is implemented and synchronized, not merely planning-complete; confirm the four request contracts and matching repository/facade signatures exist and their focused regression tests pass, otherwise stop this change and report the prerequisite blocker.
- [x] 1.2 Add the immutable typed-boundary seek plan and optional seek-session interface without changing existing count/page/single methods; verify core tests cover property-type/cardinality/null validation and immutable boundary snapshots, and the core-only custom adapter project still builds.
- [x] 1.3 Implement EF/provider lexicographic seek execution with complete authorized predicates, directions, explicit null placement, matching provider equality/collation, and bounded `Take` without `Skip`; verify focused SQLite seek tests compare concatenated batches to the ordered authorized fixture sequence.
- [x] 1.4 Add inherited real-provider conformance cases for integer tie-breakers, string-plus-key orders, repeated and mixed-direction keys, nullable numeric/date/string values, case-equivalent and trailing-space text, and supported precision boundaries; verify complete sequence equality and document explicit rejection for unsupported mappings/comparisons.
- [x] 1.5 Update `docs/providers.md`, `docs/repositories.md`, and the custom adapter example with seek capability and export migration requirements; verify documentation distinguishes offset-sort support from seek support and the example builds without EF/ASP.NET dependencies.

## 2. Continuation binding and protection

- [x] 2.1 Add core token-protector/configuration/error contracts, lossless scalar encoding, and canonical query/context fingerprints with internal compiler binding helpers; verify tests cover decimal and floating bit round trips, null/missing registered context, arbitrary-type rejection, schema/property changes, request snapshots, and unchanged strict client literal behavior.
- [x] 2.2 Add `Kkts.DataAccess.DataProtection` using the matching .NET 10 Data Protection dependency and existing packed-core project-reference conventions; register solution/test/example references and update dependency locks, then verify restore/build and real protect/unprotect tests succeed while core retains no ASP.NET dependency.
- [x] 2.3 Implement purpose-isolated confidential envelopes, finite non-sliding expiry, encoded/decrypted size bounds, and narrow invalid-token error handling; verify tests cover tampering, wrong purpose, malformed/unsupported formats, wrong tuple types, expiry boundaries, exact size thresholds, generation overflow, and preservation of unrelated failures.
- [x] 2.4 Test Data Protection continuity with isolated persistent temporary keys across providers/instances/restarts and compatible rotation, plus rejection with different application configuration or lost keys; verify no readable protected payload reveals boundary values and clean up only test-owned key files.
- [x] 2.5 Document host-managed keys, application isolation, dataset/subject binding, the proposed 15-minute/16-KiB defaults, restart/rotation rules, and no token logging in the protection package README and directly related dependency docs; verify setup examples use no fixed production keys or connection strings as dataset identifiers.

## 3. Cursor lists and keyset exports

- [x] 3.1 Add strict cursor request/result contracts and `ListCursorAsync` on core/interface/SQLite facade using optional cursor configuration; verify contract/API tests show unchanged offset/count/detail/export shapes, zero/over-limit rejection, and no offset/action/identity inputs on the cursor request.
- [x] 3.2 Implement fresh policy/context capture, applicable grant validation, pre-session token compatibility checks, base-query exact total, bounded lookahead, and last-returned-row next-token creation; verify first/full/short/empty/final pages, unchanged total across resumes, hidden/empty selection, and no skipped lookahead row.
- [x] 3.3 Add recording-adapter rejection tests for policy mismatch, subject/context/query/selection/sort/limit/resource/schema/dataset mismatch, invalid tokens, and missing cursor configuration; verify zero entity sessions for those failures, no historic-policy fetch, no silent restart, and unchanged standalone compiler/authorization behavior.
- [x] 3.4 Replace export offset advancement with private seek boundaries while retaining captured policy/context, request snapshots, host batch bounds, and per-batch disposal; verify no offset paging calls, unchanged batch shape, full-final-batch termination, hidden-key traversal, cancellation/provider failures, and publication during enumeration.
- [x] 3.5 Add deterministic cursor/export mutation cases for insertion/deletion before the boundary, deletion of the boundary row, and changes after it while remaining sort tuples are fixed; verify no position-shift gaps/duplicates, unchanged authorization, and explicit non-snapshot documentation for mutable sort values.
- [x] 3.6 Add SQL interception assertions that later cursor/export batches include provider-side authorization and lexicographic boundaries with bounded row requests and no OFFSET-based traversal; verify long traversal does not accumulate integer offsets and unsupported capability/translation cases dispose sessions without fallback.
- [x] 3.7 Update `docs/reads.md` and facade/API documentation with forward-only token usage, exact-total semantics, restart-on-version-mismatch, possible reuse after rollback to the identical immutable version, and nonresumable keyset exports; verify examples use the prerequisite request types and preserve offset behavior.

## 4. Example and transport acceptance

- [x] 4.1 Configure EmployeeApp's Data Protection integration once with host-managed persistent keys and server-derived dataset/subject binding; add authenticated/antiforgery-protected `POST /employees/cursor` and map invalid continuation errors to generic HTTP 400, verifying example build and unchanged existing endpoint contracts.
- [x] 4.2 Extend HTTP acceptance for bounded multi-page traversal, final-token absence, exact totals, hidden fields, token tampering, cross-caller/query replay, policy publication mismatch, and existing export/lifecycle behavior; verify unsupported request members return 400 and authorization/antiforgery protections still apply.
- [x] 4.3 Update README and relevant acceptance documentation with cursor/protection setup and the custom-adapter export behavior change; verify a fresh synthetic walkthrough succeeds, and report only acceptance actually executed without logging tokens or identity payloads.

## 5. Integrated verification

- [x] 5.1 Build the solution and run the smallest combined contract/token/protection/seek/repository/read/compiler/translation selectors locally, excluding unconfigured remote-provider traits; verify all targeted checks pass and offset-list/count/detail and compiler-action regressions remain covered.
- [x] 5.2 Execute real SQLite/SQL Server/MySQL seek conformance and inherited protected-read acceptance using isolated databases; verify complete ordered sequences and SQL shape, or explicitly mark unavailable remote-provider checks as not run rather than treating local results as proof.
- [x] 5.3 Run fresh-database EmployeeApp HTTP acceptance and a compatible-key restart/resume check; verify the cursor remains usable only with matching current authorization/configuration, then stop owned processes and remove only created database/key artifacts.
- [x] 5.4 Pack and consume core plus the optional protection integration through the existing packed-core pattern, run relevant existing formatting checks and strict OpenSpec validation, and verify no database/policy migration, new core ASP.NET dependency, or production seek offset fallback was introduced.

Evidence: [cursor/keyset acceptance](../../../../docs/repository-acceptance.md#forward-cursor-and-keyset-export-acceptance).
SQLite conformance passed. Inherited SQL Server/MySQL cases compile but execution
was not run because isolated test connections were unconfigured; task 5.2 uses
its explicit unavailable-provider branch, not passing remote-provider evidence.
