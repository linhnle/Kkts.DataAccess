# Tasks

This checklist implements Phase 1 only. Phase 2 writes/projection and Phase 3 advanced policies require separate changes.

## 1. Package and adapter foundation

- [x] 1.1 Verify official Kkts.Expressions and Kkts.Dynamic APIs and compatible target frameworks, pin dependency versions, and record the supported integration surface; verify with predicate construction, DTO mapping, and serialization integration checks.
- [x] 1.2 Bootstrap the .NET solution with independent core, policy persistence/query-provider adapter, ASP.NET Core example, and test projects; verify restore and build succeed without modifying either upstream Kkts package.

## 2. Resource contracts

- [x] 2.1 Implement versioned schemas, typed public field bindings, action/operator registration, per-operation eligibility, and host-declared stable keys; verify duplicate names, invalid bindings, and prohibited grants fail registration/validation tests.
- [x] 2.2 Implement schema-only field resolution and culture-independent literal conversion with documented null/operator semantics; verify unknown paths, invalid types, nullable comparisons, and lossy conversion cases.
- [x] 2.3 Expose administrator-scoped schema metadata and document resource registration; verify an Employee registration example and metadata tests show only supported controls and deny non-administrators.

## 3. Policy definitions and compilation

- [x] 3.1 Implement serialized all/any/not/leaf nodes, record rules, subject applicability, and independent operation-specific field entries; verify round trips, invalid node forms, empty groups, unsupported actions, and rejection of conditional field grants.
- [x] 3.2 Implement path-specific validation diagnostics and configurable depth/node/value/collection bounds; verify malformed and over-limit definitions cannot compile and document the chosen limits.
- [x] 3.3 Implement trusted server context registration and per-request binding; verify spoofed tenant/user inputs are ignored and missing or wrong-type context in Allow, Deny, or mandatory constraints fails closed.
- [x] 3.4 Build the Kkts.Expressions adapter and mandatory/allow/deny composition; verify truth-table cases for empty grants, multiple allows, overlapping denies, nested conditions, and tenant isolation.
- [x] 3.5 Resolve independent field permission sets with field Deny precedence and schema eligibility; verify selection does not imply filtering/sorting, empty grants add no fields, and separate record grants cannot broaden fields.
- [x] 3.6 Cache immutable compilation templates by scope/resource/schema/policy/compiler versions and bind context outside the cache; verify concurrent users never share bound identity and invalid/incompatible versions fail closed.
- [x] 3.7 Document the rule format, permission semantics, trusted context contract, and normalized Employee example; verify the documented record rule plus separate field entries compiles and matches the intended access matrix.

## 4. Database-backed policy lifecycle

- [x] 4.1 Implement SQLite policy schema/migrations for drafts, immutable published versions, active pointers, optimistic revisions, and audits; verify restart persistence, resource/scope separation, and migration on an empty database.
- [x] 4.2 Implement administrator-scoped draft read/edit with revision checks and atomic audit events; verify non-administrator and cross-scope access denial, draft isolation, and conflicting saves on the relational database.
- [x] 4.3 Implement revalidated atomic publication with draft/active revision checks; verify invalid definitions, concurrent publication, and injected transaction failure preserve the prior pointer and version history.
- [x] 4.4 Implement compatible-version rollback and audit history retrieval; verify successful rollback, schema-incompatible rollback rejection, immutable old content, actor/version attribution, and secret-free audit records.
- [x] 4.5 Resolve the authoritative active pointer at operation start and retain captured versions; verify a newly started operation observes committed publication/rollback while an existing operation retains its version.
- [x] 4.6 Document lifecycle transactions, administrative scope, conflict handling, database setup, and rollback; verify the documented publish/rollback sequence against the example database.

## 5. Authorized read enforcement

- [x] 5.1 Implement closed read request/result contracts and pre-execution query validation; verify no entity/query handle escapes and forbidden or unknown selection/filter/sort references fail before provider execution.
- [x] 5.2 Implement authorized predicate application, client filters, deterministic ordering, stable tie-breakers, pagination bounds, and cancellation; verify real SQLite tests for cross-tenant exclusion, repeated sort values, invalid limits, and missing stable keys.
- [x] 5.3 Implement detail lookup and authorized pre-pagination counts; verify inaccessible/nonexistent keys give identical not-found results and counts match filtered authorized rows rather than page size.
- [x] 5.4 Implement the Kkts.Dynamic output adapter with permitted public DTO fields only; verify default/explicit/empty selections and serialized output exclude Salary, navigation properties, raw entities, and internal context.
- [x] 5.5 Implement bounded export batches using one captured Read version; verify all matching authorized rows and permitted fields are exported, hidden-field filters are rejected, cancellation works, and publication during export does not change later batches.
- [x] 5.6 Enforce provider translation errors and unsupported operation failures; verify no unrestricted fallback occurs and Phase 1 rejects writes, aggregation, and relationship traversal even with permission metadata.
- [x] 5.7 Document facade APIs, supported SQLite operations, error behavior, internal materialization, and concurrent-data isolation limits; verify documented list/detail/count/export calls run against the example.

## 6. Basic administrator designer and example

- [x] 6.1 Implement administrator-protected server-rendered resource/action/role/group controls, nested typed conditions, and separate field permission editors; verify UI interactions persist a draft, restrict operators by type, and display validation/conflict diagnostics.
- [x] 6.2 Implement bounded synthetic-context/fixture previews using the shared compiler; verify matched rule identifiers, effective fields, mandatory constraints, and allow/deny outcomes while the active version stays unchanged and no production impersonation occurs.
- [x] 6.3 Add publication, audit history, and rollback UI actions; verify administrator scope, request protection, version/revision handling, and successful/failed lifecycle flows through UI acceptance checks.
- [x] 6.4 Build the Employee example with trusted host identity/tenant context, mandatory tenant constraints, schema registration, seeded synthetic records, and all read endpoints routed through the facade; verify manager, HR, employee, and no-grant scenarios across two tenants.
- [x] 6.5 Document example startup, synthetic identity setup, draft-to-publication workflow, preview, export, and Phase 2/3 boundaries; verify a clean-database walkthrough reproduces the expected access decisions.

## 7. End-to-end acceptance

- [x] 7.1 Run the complete Phase 1 scenario matrix across lists, detail, counts, exports, publication, rollback, and UI using the real relational provider; verify every delta requirement has passing evidence and build/test commands succeed.
- [x] 7.2 Pack the integration package and consume it from the example using documented setup; verify package independence, supported dependencies, and no need to modify either Kkts library, recording the acceptance results.
