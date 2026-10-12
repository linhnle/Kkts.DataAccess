# Tasks

## 1. Establish the shared Expressions adapter

- [x] 1.1 Add focused 3.1.0 compatibility tests for typed schema construction from registered PropertyInfo bindings, structured filter validation/building, typed request-owned resolver values, metadata-only semantic analysis, and OrderByInfo IQueryable ordering; verify the tests compile and pass without upgrading dependencies.
- [x] 1.2 Implement internal schema/operator mapping from ResourceSchema with immutable upstream schemas and exact public-name resolution; verify alias casing, unusual public names, colliding aliases, unsupported operators, and raw entity/navigation-name rejection in adapter tests.
- [x] 1.3 Configure separate policy/client query contexts and bounded upstream limits accounting for translation expansion; verify an existing valid policy at DataAccess limits remains accepted while over-limit input fails, and policy fields remain usable independently of client Filter grants.
- [x] 1.4 Document adapter ownership, pinned APIs, and authorization boundaries in docs/dependencies.md; verify documented APIs are exercised by the compatibility tests and no request values or effective grants are cached globally.

## 2. Integrate semantic validation

- [x] 2.1 Implement metadata-only semantic checks using shared field/operator mappings and declared context types; use structured analysis if supported, otherwise a canonical internal analysis emitter with escaping and condition-path source mapping; verify nested diagnostic paths, unknown fields, incompatible values, and string escaping with no getter or resolver invocation.
- [x] 2.2 Wire PolicyValidator and mandatory/client condition validation to upstream semantic checks while retaining policy shape, authorization, strict conversion, and size-limit diagnostics; verify existing CompilerTests and forbidden-request tests, plus publication/rollback rejection through the existing validation boundary.
- [x] 2.3 Update docs/policies.md and docs/dependencies.md to distinguish upstream expression semantics from DataAccess authorization and conversion checks; verify examples retain the existing policy JSON and Diagnostic contract.

## 3. Replace local predicate construction

- [x] 3.1 Lower validated all/any/not and leaf conditions to FilterNode/FilterValue and build through ExpressionQueryContext with request-owned preconverted values; verify parity for every supported scalar/operator family, including nullable values, enum, numeric precision, temporal kind/offset, strings, and in collections.
- [x] 3.2 Route record, mandatory, subject-context, client-filter, and detail-key predicates through the adapter; eagerly bind all required context before composition and retain mandatory/allow/deny precedence; verify tenant isolation, overlapping denies, missing context in short-circuited branches, and cross-user cached-policy reuse in compiler/repository tests.
- [x] 3.3 Remove the general manual PolicyCompiler.Node operator construction, retaining only documented adaptations demonstrated necessary by parity tests; verify null eq/ne, ordered-null behavior, null predicates, membership with null, and nested NOT against expected in-memory and SQLite results.
- [x] 3.4 Update docs/dependencies.md and docs/policies.md with actual production construction APIs and any compatibility adaptations; verify every documented adaptation has a regression case and no general parallel leaf compiler remains.

## 4. Delegate ordering to Expressions

- [x] 4.1 Replace EfEntityReadAdapter manual Queryable call construction with the pinned structured OrderByInfo ordering extension over the authorized query, adding an explicit pinned package reference only if necessary; verify ascending/descending multi-key ordering and ties on SQLite.
- [x] 4.2 Preserve EntityReadPlan/EntitySort and stable-key completion in AuthorizedReadRepository; verify public alias sorting, duplicate keys, forbidden Sort requests before session opening, stable-key omission from output, deterministic export batches, and existing custom-adapter/source-compatibility tests.
- [x] 4.3 Update docs/reads.md, docs/providers.md, and docs/dependencies.md for delegated ordering and unchanged provider limits; verify examples still use SortField and existing repository constructors without introducing raw expression inputs.

## 5. Verify the complete integration

- [x] 5.1 Run locked restore, solution build, and the SQLite/core suite using the README commands; verify public list/detail/count/export outcomes, cancellation/disposal, context isolation, and provider failures with no local or unrestricted fallback.
- [x] 5.2 Run the existing SQL Server and MySQL provider conformance suites when their documented infrastructure is available; record actual results or explicitly record suites not run, including null/precision/ordering cases affected by new expression shapes.
- [x] 5.3 Run the repository formatting checks and inspect the final production dependency use; verify predicate construction, semantic analysis, and EF ordering call upstream APIs, public contracts and policy examples remain compatible, and docs/repository-acceptance.md records the actual validation evidence.
