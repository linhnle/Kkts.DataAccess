# Tasks

## 1. Prerequisite gate and trusted contracts

- [x] 1.1 Confirm request separation, cursor/keyset exports, and optional totals/versioned counts are implemented and synchronized in that order; verify their public contracts and main-spec requirements exist, and block this implementation if they remain planning-only.
- [x] 1.2 Extend trusted immutable read/seek plans with ordered authorized output and continuation bindings, plus projected row snapshots; add contract tests proving caller collection mutation cannot change bindings and that filter/sort-only fields are not output bindings.
- [x] 1.3 Add projected page/single and projected seek session capabilities without inheriting full-entity seek; verify recording custom adapters compile with projected seek alone and existing count/entity methods remain available.
- [x] 1.4 Add strict projected-row validation and defensive nonserialization of sidecars; verify exact cardinality, nullable boxing, wrong scalar types, missing boundary values, and mutable boundary snapshots with focused contract tests.
- [x] 1.5 Document trusted projected contracts and capability requirements in docs/repositories.md; verify the sample adapter contract uses only validated host bindings and no client executable projection expressions.

## 2. Value-based DTO mapping

- [x] 2.1 Add checked value-based mapping to DtoShape/DtoMapper using existing generated DTO types, ordered setters, and shape-local serializer options; verify golden JSON matches entity mapping for ordered aliases and supported scalar/null/enum/date/escaping cases.
- [x] 2.2 Preserve empty-output handling and prohibit entity reconstruction/getter evaluation in projected mapping; verify empty rows produce `{}`, empty selection bypasses shape builds, and entity constructor/getter counters remain zero.
- [x] 2.3 Keep setter metadata inside the existing bounded structural shape cache; verify cross-mode reuse, alias/property/schema distinctions, identity-independent sharing, capacity/eviction, concurrency, failed builds, and retained-type dependency tests.
- [x] 2.4 Update directly related mapping/cache documentation to describe scalar mapping without a second cache; verify documented shape identity and JSON guarantees match both tested paths.

## 3. Shared EF page/detail projection

- [x] 3.1 Implement actual-model validation and mapped scalar selectors with deduplicated physical slots in the shared EF adapter; verify unselected wide payloads are absent from result-reader columns and unmapped selected getters fail without entity construction.
- [x] 3.2 Implement projected offset and single retrieval with protected predicates, provider ordering, bounded pagination, and empty-output markers; verify authorized row cardinality, empty detail existence/not-found parity, alias restoration, and duplicate detection in SQLite relational tests.
- [x] 3.3 Preserve aggregate-only count and explicit projection-error propagation; verify count produces no projected rows/entities and translation/materialization errors do not invoke entity retrieval in either host mode.
- [x] 3.4 Update docs/providers.md for mapped scalar projection, top-level client-evaluation rejection, and the distinction between SQL predicate columns and reader columns; verify claims are backed by focused SQLite materialization/reader tests.

## 4. Core/facade modes and nonseek orchestration

- [x] 4.1 Add default-required host projection configuration to core and SQLite facade while preserving construction call patterns; verify invalid modes fail explicitly, defaults match, and client JSON rejects mode/projection-expression members.
- [x] 4.2 Route offset lists and detail through the relevant projected capability, with explicit absence-only compatibility paths and session disposal; verify missing capabilities fail before optional counts, capable adapters still project in compatibility mode, and projected errors never retry raw entity methods.
- [x] 4.3 Preserve exact/no-count and has-more list behavior plus standalone ReadCount metadata; verify all four metadata combinations, zero counts when total is disabled, lookahead trimming, explicit null metadata serialization, and capture consistency.
- [x] 4.4 Update docs/reads.md and docs/repositories.md with required defaults and explicit custom-adapter migration; verify documented offset/detail results and standalone count behavior against focused repository/facade tests.

## 5. Projected cursor and export traversal

- [x] 5.1 Implement shared EF projected seek with existing provider-aware comparisons, full authorized predicate, and minimal deduplicated continuation slots; verify hidden stable keys, empty output, repeated sorts, overlapping aliases, mixed directions/nulls, and supported collation traversal.
- [x] 5.2 Integrate projected cursor orchestration without requiring entity seek; verify fresh authorization/token compatibility, optional metadata query counts, lookahead trimming, last-returned-row boundaries, encrypted hidden sidecars, and no sidecar DTO properties.
- [x] 5.3 Integrate projected export with captured bindings/context/policy, pre-yield boundary snapshots, and bounded per-batch sessions; verify complete authorized traversal, policy publication during export, cancellation/early termination, malformed rows, and failure disposal without offsets or entity-loading retries.
- [x] 5.4 Cover cursor/export capability and host-mode combinations with recording adapters; verify required mode rejects missing projected seek before entity queries, explicit compatibility uses entity seek only when projected seek is absent, and absence of both seek capabilities fails explicitly.
- [x] 5.5 Update cursor/export and provider documentation with trusted sidecar exceptions, unchanged encrypted-token policy/version strategy, and no-snapshot guarantees; verify examples never disclose or log boundary values and never imply resume-token possession grants permission.

## 6. Provider and application acceptance

- [x] 6.1 Extend shared relational/provider fixtures with result-reader column assertions, entity-materialization evidence, wide hidden payloads, supported value converters, and unmapped bindings; run SQLite, SQL Server, and MySQL projection cases and verify equivalent authorized output/traversal with zero projected entity instances.
- [x] 6.2 Keep provider-tagged registration helpers on shared projected execution and document verified scalar/seek support separately; verify provider identity remains unchanged and unsupported mapping/comparison failures propagate rather than coercing or falling back.
- [x] 6.3 Keep EmployeeApp on required projection and update README's deferred/full-entity claims; verify existing synthetic HTTP acceptance covers the migrated request/result shapes and exported fields without exposing sidecars.

## 7. Cross-cutting validation

- [x] 7.1 Run the existing build and focused contract, mapper/cache, repository, shared read, and real-provider suites together; verify no authorization/grant/token/metadata/disposal regression and report unavailable provider execution explicitly rather than treating it as passing evidence.
- [x] 7.2 Run strict OpenSpec validation against the synchronized prerequisites; verify the final delta preserves inherited scenarios and that the seek requirement is present before syncing or archiving this change.

Evidence: [projection acceptance](../../../../docs/repository-acceptance.md#authorized-database-projection).
Task 6.1 completed on 2026-10-12 after loading the configured isolated SQL
Server/MySQL test connections without displaying credentials. All 42 inherited
SQLite/SQL Server/MySQL read cases passed, including reader/materialization,
converter/unmapped, projected seek traversal and optional metadata assertions.
The shared materialization interceptor has a positive raw-entity control and
test-scoped counters; projected paths materialize zero entity instances.
The completed change was synchronized to both main specs and archived on 2026-10-12.
