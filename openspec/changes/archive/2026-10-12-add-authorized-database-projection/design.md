# Design

## Context

See [proposal.md](./proposal.md) for motivation and the two delta specs for behavior. `EntityReadPlan<T>` currently contains an authorized predicate and resolved sorts; `IEntityReadSession<T>` returns entities for page/single retrieval and a `long` aggregate for count. Shared EF execution uses `AsNoTracking().Where(...)` but materializes `T`. Provider-tagged helpers delegate to that shared implementation.

`DtoMapper` uses the bounded, structural `DtoShapeCache` and Kkts.Dynamic-generated scalar DTOs with shape-local serializer metadata. Preserve this tested serialization infrastructure. Resource schema properties are direct scalar bindings, but schema validity alone does not establish that a property is mapped by EF.

The prerequisite changes introduce operation-specific inputs, optional entity seek sessions, encrypted cursor boundaries, host-batched keyset exports, nullable list metadata, and `ReadCount`. Their execution rules remain authoritative except where this change adds required projection and scalar continuation extraction.

## Goals / Non-Goals

**Goals:** keep the trusted provider seam, minimize database-to-application values, preserve exact output semantics, support projected seek without entity seek, and prove the materialization boundary with relational evidence.

**Non-goals:** constructing partial entities, exposing projection expressions, a second serializer/cache system, replacing existing entity/count methods, or providing a snapshot. Scalar boxing and DTO construction from returned values are allowed; authorization and entity-member evaluation in application memory are not.

## Decisions

### 1. Required projection with explicit compatibility

Add a host-only `ReadProjectionMode` with `Required` (default) and `AllowEntityMaterialization`. Thread a trailing optional configuration parameter through the core repository and SQLite facade, preserving existing constructor calls while intentionally changing retrieval capability requirements. Validate unsupported configuration values explicitly. Supplied EF adapters always offer projected execution; provider registration helpers do not need separate per-provider mode switches.

Choose execution by the capability relevant to the operation:

| Operation | Required mode | Allow entity materialization |
|---|---|---|
| Offset list / detail | Projected page / single capability | Prefer projection; entity methods only when capability is absent |
| Cursor / export | Projected seek capability | Prefer projected seek; otherwise existing entity seek capability |
| Standalone count | Existing count method | Existing count method |

After opening a session, check the relevant capability before queries, including optional list totals. Dispose the session on failure. Compatibility mode is explicit acceptance of hidden entity data entering memory, not permission to retry after projected translation/materialization/mapping failures. Errors propagate normally; there is no catch-and-load-entities branch.

Alternative: silently detect support and fall back would make data minimization unreliable. Removing entity contracts would needlessly eliminate the deliberate custom-adapter compatibility route.

### 2. Separate logical output, physical scalar slots, and continuation metadata

Extend the immutable read plan with ordered `EntityOutputBinding` entries containing the validated public alias and direct property binding. Derive them only from resolved effective Select grants after explicit selection validation. Count-only plans have no output bindings. Do not add policy/filter/sort dependencies to this collection.

The seek plan additionally identifies its continuation bindings from the full effective order, including the host stable key. Preserve repeated logical sort positions and all preceding seek null/direction/provider rules. This is trusted server plan data, not a new request field.

Add an immutable `ProjectedReadRow` with separate read-only snapshots of ordered output values and ordered continuation values. The row is an infrastructure result, never a public DTO. Page/detail rows contain no continuation tuple; projected seek rows contain the complete tuple. Document this trusted boundary and defensively exclude continuation properties from ordinary JSON serialization.

The EF projection layout deduplicates physical property slots when aliases or output/continuation bindings overlap, then restores logical output and continuation order from those slots. Ordinary hidden predicates/order columns remain in SQL but not the result reader. An empty output without continuation uses a constant non-entity marker: it preserves row multiplicity without retrieving a hidden key. Empty seek output needs only its continuation slots.

Validate rows against the captured plan before mapping the affected bounded page/batch: exact logical cardinalities, scalar CLR types, nullable boxing, and required continuation presence/types. Do not use client literal parsing or lossy coercion on stored values. Snapshot boundary values before retaining/yielding them, including supported mutable scalar representations. Invalid infrastructure rows fail explicitly; do not pad missing values or emit partial DTOs from the failed page/batch. Previously yielded valid export batches are not rolled back.

Alternative: an alias/value dictionary obscures type/order contracts and repeated slots. A partial `T` invokes entity construction/getters and undermines the materialization boundary.

### 3. Add projected capabilities without requiring entity seek

Add `IEntityProjectedReadSession<T> : IEntityReadSession<T>` with `PageProjectedAsync` and `SingleProjectedAsync`, mirroring the applicable existing method inputs but returning projected rows. Add `IEntityProjectedSeekReadSession<T> : IEntityProjectedReadSession<T>` with `SeekProjectedAsync` accepting the trusted seek plan and bounded limit. Do not inherit the entity-seek interface: a custom projected adapter must not implement an unnecessary full-entity seek route.

Retain existing methods for aggregates and explicit compatibility. Session ownership is unchanged. The EF session implements both projected and entity capabilities; custom adapters may advertise only the relevant supported projected capabilities. Contracts remain provider-neutral and carry no EF types into core.

Alternative: adding required methods to the base session would break count-only adapters and obscure explicit compatibility.

### 4. Use a mapped scalar EF selector, not entity-shaped projection

Build the query from the full authorized predicate and any detail key/seek boundary. Apply provider ordering and bounded pagination, then a selector consisting exclusively of validated mapped scalar member accesses, with scalar boxing into a bounded heterogeneous row buffer where necessary. EF's scalar reader/shaper may assemble the buffer locally; it must never materialize `T` to evaluate a selected getter.

Before building the selector, resolve every output/continuation property against the actual EF entity model. Require a mapped scalar member matching the schema property and the expected CLR value representation. Reject unmapped, navigation, or otherwise unsupported bindings explicitly. Preserve EF value-converter behavior for supported mappings and the narrower verified seek comparison capabilities from the cursor design.

This guard matters because EF permits client evaluation in top-level projections: merely adding `Select` does not prove that an unmapped member cannot cause entity loading. Do not call compiled entity selectors, `AsEnumerable`, or getter delegates over entities. Database/translation failures abort in both host modes.

Offset/detail use output-only layouts, including the empty-selection marker. Seek appends only the deduplicated continuation fields. Count remains `LongCountAsync` over the protected base query; it does not select rows or include the cursor boundary.

Alternative: selecting an entity plus a DTO or calling existing entity mapping inside `Select` still materializes hidden members. A separate dynamic EF DTO generator would duplicate shape lifecycle/serialization concerns.

### 5. Map projected values with existing DTO shapes

Add value-based mapping to `DtoShape` and `DtoMapper`: build/cache the existing DTO shape from the same structural selection key, retain validated writable DTO scalar properties in alias order, instantiate the DTO, and assign the checked output scalar values directly. Continue using the shape's retained type and serializer options.

Never call entity-to-DTO mapping on projected rows or reconstruct `T`. Retain the existing entity mapping path for explicit compatibility. Empty selection still bypasses dynamic shape generation and produces `{}` per authorized row.

The cache key remains entity type/resource/schema/ordered alias-property identity, independent of user, policy version, row data, and host materialization mode. Shape-local setter metadata is bounded by the existing cache, not a new global cache. Preserve FIFO capacity, concurrency, failed-build behavior, and assembly lifetime semantics.

Alternative: serializing dictionaries directly risks diverging scalar typing, property order, and serializer behavior from the tested DTO contract.

### 6. Integrate all retrieval orchestration without changing metadata or tokens

- Offset list: select capability, execute a count only if requested, fetch limit or checked limit-plus-one, trim lookahead before DTO mapping, and compute exposed has-more from the authorized extra row.
- Detail: execute the authorized implicit key lookup through projected single retrieval. A null row means not-found; a present row with zero output values means `{}`. Preserve duplicate detection.
- Cursor: authorize and validate the token with the current capture before session access. Use projected seek with its existing lookahead; trim before mapping and use the last returned row's sidecar for the next encrypted token. Metadata flags stay excluded from token identity.
- Export: retain the initial policy/context/output bindings, use bounded per-batch projected seek, snapshot the last row's boundary before yielding, and dispose on completion/cancellation/early termination. No public export resume token is added.
- Count: preserve aggregate-only execution, exact `ReadCount` metadata, and zero count queries when list total is disabled.

The compatibility branches perform the same orchestration using existing entity mapping and entity boundary extraction. Binding and authorization do not depend on the chosen materialization mode. Projection changes the data path, not cursor protocol identity; the earlier binding of schema/property/dataset identity still protects incompatible mappings.

### 7. Verify database-to-application boundaries, not only DTO fields

Extend recording-adapter tests to cover every capability/mode combination, malformed rows, count-call matrices, lookahead boundaries, capture and disposal, and explicit failure without retry.

Extend mapper golden tests across aliases, order, supported scalar/null/enum/date values, empty selection, and cache reuse across both mapping paths.

Run relational acceptance on SQLite plus the existing SQL Server/MySQL shared-provider suites. Inspect final SELECT/result-reader columns: forbidden names may legitimately occur in WHERE/ORDER BY, so rejecting their appearance anywhere in SQL is incorrect. Use an entity-materialization interceptor or constructor/getter counters to establish zero entity instances in projected retrieval. A wide hidden payload fixture must be absent from reader columns; report this measurable reduction, not unverified timing/allocation guarantees.

Include hidden authorization/filter/order fields, empty output/detail existence, hidden stable-key sidecars, alias/physical-slot overlap, lookahead trimming, mixed-direction/null/collation traversal, supported value converters, unmapped getter rejection, and cancellation/failure disposal. Verify that both host modes propagate projection errors without legacy page/single/seek calls.

## Risks / Trade-offs

- [Top-level client evaluation can load entities] -> Mapped scalar selector constraints plus result-column and materialization tests.
- [Hidden continuation values enter trusted memory] -> Minimal dedicated sidecars, no DTO mapping/logging, encrypted tokens, and explicit documentation of this exception.
- [Provider scalar mappings differ] -> Actual model validation and real-provider conformance; advertise only verified projection/seek combinations.
- [Extra shape setters retain types] -> Keep setter metadata within the existing bounded shape and test eviction/concurrency.
- [Default projection breaks custom retrieval adapters] -> Explicit host compatibility option with documented loss of minimization; no implicit fallback.
- [Rows change between statements/batches] -> Retain existing no-snapshot warnings; projection does not fix mutable sort values or count/page races.

## Migration Plan

1. Implement and synchronize `split-read-requests-by-operation`, then `add-cursor-pagination-and-keyset-exports`, then `add-optional-totals-and-versioned-counts`.
2. Add projected contracts and mapping, integrate shared EF execution and repository/facade mode selection, then run targeted and shared-provider acceptance.
3. Keep EmployeeApp on required projection. Update README and read/repository/provider docs, replacing deferred-projection/full-entity claims with exact guarantees, sidecar exceptions, and custom adapter migration.
4. Existing hosts using supplied EF adapters retain public shapes without opt-in. Hosts using custom adapters implement projection or deliberately configure entity materialization; standalone counts need no new capability.
5. No data/policy migration is required. Emergency compatibility is a deliberate host configuration change for a missing projected capability, not automatic rollback after an execution error. Fix projected failures or explicitly replace the adapter; never turn a failure into silent full-entity loading.
