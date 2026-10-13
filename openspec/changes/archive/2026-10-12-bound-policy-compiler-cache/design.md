# Design

## Context

See [proposal.md](./proposal.md). PolicyCompiler uses a ConcurrentDictionary keyed by scope/resource/schema/version/compiler, stores raw Payload plus privately owned AST, and binds identity after lookup. `CursorPolicyIdentity` indexes that dictionary later, so naive eviction would race cursor fingerprinting. DtoShapeCache already supplies a simple locked bounded FIFO pattern; its cache remains separate.

## Goals / Non-Goals

**Goals:** a measurable entry bound, deterministic concurrency behavior, safe per-operation identity, and explicit post-eviction integrity guarantees.

**Non-goals:** exact managed-heap byte accounting, a durable historical hash ledger, caching bound authorization, or a new cache package.

## Decisions

### Bounded FIFO template store

Add immutable `PolicyCompilerCacheOptions(MaxEntries = 128)` with positive validation, accepted by the compiler. Replace the dictionary with a lock-protected dictionary and insertion queue. Serialize lookup/validation/insertion under that lock for simplest race-free same-key content checks; release it before request binding. A valid miss builds before eviction, then evicts oldest insertion only as needed. Hits do not reorder FIFO. Failed builds neither insert nor evict.

Use no permanent per-key tasks/locks/tombstones. All compiler-owned retained entry/queue state is bounded by capacity. In-flight callers can retain validated template references after eviction; this is operation-owned memory, not additional cache history. Existing policy transport/complexity limits bound each accepted payload, but entry limits are not an exact retained-bytes guarantee.

Alternative: concurrent unbounded GetOrAdd and a side eviction queue can overretain or race same-version payload checks; LRU is unnecessary complexity without measured need.

### Content verification and operation identity

A hit compares raw payload bytes/string equality before reuse, retaining today's strict mismatch behavior. A miss after eviction parses/revalidates the authoritative capture. Lifetime immutable-content guarantees belong to the source (SQLite already has immutable-version triggers); the compiler explicitly cannot detect a changed historical payload it no longer remembers.

Carry the exact validated payload identity on internal operation-owned bound metadata and pass it to CursorTokens.Binding. Remove the late cache indexing helper. Retain the current canonical fingerprint input, including raw policy payload representation, so eviction alone does not invalidate outstanding compatible tokens or bump their format.

Do not expose AST or cache retrieval as a public permission bypass. Preserve fresh mandatory/subject/record/field binding for every caller. Mutating the original policy/request after capture does not alter privately retained content.

### Verification

Use capacity one/two to test exact retained counts, FIFO order, invalid builds at capacity, resident payload mismatch, authoritative recompilation after eviction, cross-user isolation, and parallel mixed-version binding. Force eviction between Bind and cursor fingerprint with a deterministic gate and verify unchanged identity/results. Test queue cleanup and failed-build recovery rather than timing-based race guesses.

## Risks / Trade-offs

- [Lock serializes cold validation] -> Bounded/simple correctness first; measure before introducing per-key concurrency.
- [Post-eviction mismatch detection weakens compiler-local history] -> Explicit authoritative-source contract and tests/docs, no misleading lifetime claims.
- [In-flight memory remains live] -> Document entry retention versus operation references; never call it a total-process memory cap.

## Migration Plan

No package/data migration. Add options, bounded store, and operation identity as one coherent change; run compiler/cursor/export tests. Document default capacity and content guarantees. Structured errors and telemetry can layer onto the same insertion/hit/miss events later; neither is required to implement this cache.
