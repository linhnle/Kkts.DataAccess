# Proposal

## Why

`DtoMapper.Map` builds a fresh dynamic DTO type for every nonempty selection on every list, detail, or export batch, even when the same shape was already used. Reusing built shapes can avoid repeated emission, mapping initialization, and serialization metadata construction without changing the authorization contract; the size of the improvement must be measured.

## What Changes

- Introduce a bounded, thread-safe internal cache of built DTO types and shape-local serialization options, not a single mutable static `DynamicAssembly`.
- Identify entries by entity type, schema resource/version, and the ordered resolved public-to-entity bindings. Never cache entities, caller context, grants, or policy decisions.
- Build each resident shape once, dispose its builder, and map through the retained built type using the existing public Kkts.Dynamic mapper API.
- Preserve public APIs, field order, null/value serialization, empty selections, explicit failure behavior, and authorization validation on every request.
- Add focused identity, concurrency, capacity, lifecycle, and authorization regression tests, plus a reproducible Release-mode comparison against the current implementation.
- Document cache ownership, capacity, and measured results without promising immediate assembly collection.

## Capabilities

### New Capabilities

None. This is an internal performance optimization.

### Modified Capabilities

None. The existing `authorized-reads` requirements remain unchanged, including permitted output shapes and validation before execution. This change explicitly sets `skip_specs: true`; no delta specs will be created.

## Impact

- Implementation: `src/Kkts.DataAccess/DtoMapper.cs` and, if needed for clarity/testing, one internal cache helper.
- Tests: focused mapper tests and existing `DependencyTests`, `ReadTests`, and `RepositoryTests`.
- Documentation: `docs/reads.md` and `docs/dependencies.md`.
- All entity providers and the SQLite compatibility facade benefit through their existing shared repository mapping path.
- Keep .NET 10, Kkts.Dynamic 2.1.0, and Kkts.Expressions 3.1.0 pinned. No new runtime dependency, dependency changes, upstream edits, repository/provider redesign, database projection, or public cache configuration is proposed.
