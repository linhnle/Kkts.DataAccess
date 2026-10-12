# Design

## Context

See [proposal.md](proposal.md) for motivation. `AuthorizedReadRepository<T>` validates selection against the captured policy before calling the public static `DtoMapper.Map`. List/detail/export share this path; counts do not map DTOs. The SQLite `AuthorizedReads<T>` facade delegates to the same repository. Default selection is sorted upstream, while explicit selection retains first-occurrence order.

`ResourceSchema<T>` is immutable, but callers can construct distinct schemas with identical resource/version labels and different property bindings. A label-only key is therefore insufficient.

The pinned Kkts.Dynamic 2.1.0 source at commit `72d315ec7c88bb3517d3b50fc909e4ed337f1e13` establishes:

- `DynamicAssembly` uses `AssemblyBuilderAccess.RunAndCollect`.
- `DeclareClass` is allowed only before `Build`; even unique names do not make one built static assembly extensible.
- Assembly disposal disposes each `Class`; subsequent `Class.CreateDto` and `GetBuiltType` calls reject use.
- A retained built `Type` can be used with `Activator.CreateInstance` and the public `Mapper.MapFromEntityToDto` API. The repository already tests this mapping pattern before disposal; add an exact-release integration test covering use after disposal.
- Mapper factories and mapping delegates use weak-key tables. This does not by itself guarantee collection because other consumers, including serializers, may retain generated types.

Source references: [DynamicAssembly](https://github.com/linhnle/Kkts.Dynamic/blob/72d315ec7c88bb3517d3b50fc909e4ed337f1e13/src/Kkts/Kkts.Dynamic/DynamicAssembly.cs), [Class](https://github.com/linhnle/Kkts.Dynamic/blob/72d315ec7c88bb3517d3b50fc909e4ed337f1e13/src/Kkts/Kkts.Dynamic/Class.cs), [Mapper](https://github.com/linhnle/Kkts.Dynamic/blob/72d315ec7c88bb3517d3b50fc909e4ed337f1e13/src/Kkts/Kkts.Dynamic/Mapper.cs).

## Goals / Non-Goals

**Goals:**

- Eliminate repeated emission for resident shapes across callers and export batches.
- Bound cache-owned references across all entity types, without retaining schemas or authorization state.
- Preserve serialization and exception behavior, including null-entity rejection on nonempty selections.
- Prove reuse and capacity deterministically; measure speed/allocation improvements separately.

**Non-Goals:**

- No public settings, DI registration changes, authorization caching, new projection path, new runtime packages, or shared mutable assembly.
- No claim that eviction immediately unloads an assembly or bounds every runtime allocation.
- No deduplication or normalization of direct mapper inputs that changes existing ordering or invalid-binding behavior.

## Decisions

### 1. Cache immutable built-type entries, not assembly or Class wrappers

Build a miss using a fresh `using`-scoped `DynamicAssembly`, the existing `AuthorizedDto` name, and `OneWayToDto` bindings. After successful `Build`, capture `GetBuiltType` and dispose the builder. Cache only the built type and a dedicated `JsonSerializerOptions` instance with default settings.

Map each entity into a fresh DTO using `Activator.CreateInstance` and `Mapper.MapFromEntityToDto`, then serialize using the cached type and options. Explicitly reject a null entity before invoking the public mapper: that method silently ignores null, whereas the current `Class.CreateDto` throws. Do not cache DTO instances or entities.

This reuses an existing tested public mapping pattern rather than introducing expression-generated factories or upstream internals. The lifecycle integration test is a prerequisite to implementation; if it fails on the exact package, stop and revise the design rather than retaining a disposed Class or silently changing the mapping path.

**Alternatives:** A static assembly cannot accept declarations after build. Retaining assemblies/Class wrappers would need coordinated disposal during eviction and active calls. Retaining only immutable runtime types avoids that lifecycle complexity.

### 2. Use collision-safe structural keys with ordered bindings

Snapshot the selection and resolve every field before lookup, including on hits. The key consists of:

- Entity `Type`.
- Schema `Resource` and `Version`, compared ordinally.
- An ordered immutable sequence of resolved bindings: canonical public field name, actual `PropertyInfo` identity, entity property name/type, and `OneWayToDto` mode.

Use structural equality, not array-reference equality, delimiter-concatenated strings, or a hash alone. Keep sequence length/order, and preserve repeated direct mapper selections rather than silently removing them. Do not retain the schema itself. Binding identity prevents cross-schema reuse when labels and aliases match but their mapped properties differ.

Policy version, tenant, caller, and grants are not key material: identical already-authorized shapes may share mapping machinery, but repository policy capture and validation still run on every operation.

**Alternatives:** Schema-object identity is safe but misses reuse across equivalent schemas and can retain schema instances. Sorting selections would merge distinct requested orders. Resource/version labels alone can alias different bindings.

### 3. Use one process-wide bounded FIFO cache

Use a non-generic internal cache shared across all `T`, with an initial fixed capacity of **128 entries**. This is a documented tuning assumption, not a public setting or a claimed optimal size.

A dictionary and FIFO queue behind one lock are sufficient. Under the lock, return an existing entry or build one new entry. Publish only after successful build; when full, remove the oldest entry before inserting the new one. A failed build leaves no resident entry, does not evict a healthy entry, disposes its builder, and propagates the original exception. A later request may retry with a new assembly.

Serialize/map outside the lock. Each caller holds a local strong reference to its entry, so eviction cannot invalidate work already in progress. No eviction disposal is needed for Type/options entries. The bound applies to cache-owned entries, not entries retained temporarily by active calls or downstream runtime caches.

**Alternatives:** `ConcurrentDictionary.GetOrAdd` with a build factory can emit duplicate types under contention. Lazy entries can avoid duplicates but add failed-entry cleanup and eviction races. LRU adds mutation on every hit; FIFO is simpler and adequate unless measured workload data shows harmful churn.

### 4. Own serialization metadata with each entry

Reuse one default-configured `JsonSerializerOptions` instance per shape, with a distinct `DefaultJsonTypeInfoResolver` so System.Text.Json cannot share an equivalent options metadata cache with process-wide default options. Options become read-only on first use and can be shared by concurrent calls. Verify their serialization matches the existing default overload for every supported scalar type.

Eviction releases our strong references to both the type and its serialization metadata together. This reduces avoidable long-lived roots but does not prove immediate collection. Keep the existing empty-selection branch unchanged and do not create a cache entry for it. Do not add a zero-row early return that bypasses the existing binding validation/build behavior.

### 5. Separate correctness acceptance from timing evidence

Use an internal, non-public test seam only as necessary to construct isolated small-capacity caches and count actual builds; avoid production telemetry or public inspection APIs. Use barriers rather than sleeps for concurrent tests.

Deterministic acceptance:

- Repeated structurally equal selections use the same built type and build once while resident.
- Concurrent first use emits one resident type; concurrent mapping uses independent DTO instances.
- Different entity types, field orders, public aliases, schema versions, or property bindings do not collide.
- Cache-owned entries never exceed capacity; FIFO eviction permits rebuilding, and an active evicted entry remains usable.
- Failed builds propagate, are not cached, and do not corrupt existing entries.
- Unknown fields still fail on lookup; empty selection produces one `{}` per row without building; invalid nonempty selections still fail with zero rows.
- Null entities still fail for nonempty selection. Supported null/scalar values, aliases, and ordered JSON properties match the current implementation.
- A broader warmed shape cannot leak into a narrower authorized request. Policy changes still affect new requests, while an in-progress export retains its captured version.

Performance acceptance:

- Keep a test-only uncached baseline reproducing the current implementation; compare identical output before timing.
- Supply a reproducible Release-mode measurement using existing test tooling, with bounded iterations, warm-up, and at least five repetitions for one-row and 100-row batches. Measure cold misses separately.
- Record medians for elapsed time and allocated bytes, with runtime/SDK, iteration counts, and command. Warm resident mapping must show lower median time and allocations than the baseline in both batch sizes before calling the optimization successful.
- Deterministically confirm no builds during the timed resident-hit loops. Do not gate normal CI on wall-clock ratios or imply database throughput gains from a mapper-only measurement.
- If results do not improve, investigate the measured key/factory/serialization overhead and revise before declaring completion.

## Risks / Trade-offs

- [Cold builds serialize under a global lock] -> Keep mapping outside the lock; measure concurrent misses and warm-hit throughput before considering more complex synchronization.
- [Many shape combinations churn a FIFO cache] -> Cap retention at 128, measure churn, and document that one build per shape is guaranteed only while resident.
- [Runtime/serializer roots delay collection] -> Use shape-local options, test safe eviction, and describe cache retention rather than promising an assembly-unload deadline.
- [Switching from Class.CreateDto to public Mapper changes error semantics] -> Explicitly preserve null rejection and compare behavior against the current baseline, including bad bindings and entity types.
- [Internal test hooks become accidental public API] -> Keep hooks internal and scoped to the existing test assembly; prefer isolated helper tests over resetting the global cache during parallel tests.

## Migration Plan

No persisted data or host configuration changes are required. Ship as a compatible internal optimization after the focused tests, Release measurements, and existing core/SQLite regression tests pass. Update reads/dependency documentation with cache capacity and verified lifecycle behavior. Rollback consists of restoring the prior mapper implementation; no policy or schema migration is needed.
