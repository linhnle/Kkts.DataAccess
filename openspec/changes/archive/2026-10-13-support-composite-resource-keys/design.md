# Design

## Context

See [proposal.md](./proposal.md). ResourceSchema has one StableKey; repository ordering appends its alias, detail builds one implicit equality, and cursor fingerprints record its name. Seek/projection already support ordered scalar tuples, so composite identity can reuse those execution paths. Snapshot shape checks must continue accepting ordinary tables with composite mapped keys.

## Goals / Non-Goals

**Goals:** exact object identity, full-tuple uniqueness, consistent hidden key handling across all reads.

**Non-goals:** nullable/generated expression keys, relationships, automatic uniqueness DDL, or a scalar-key compatibility mode.

## Decisions

### Schema tuple and metadata

Replace singular constructor/accessor with ordered key aliases and read-only `KeyFields`. Require nonempty distinct aliases and physical properties, direct supported scalar bindings, nonnullable components, and existing non-boolean orderability constraints. Snapshot inputs to prevent caller mutation. Metadata gains ordered key aliases, still behind administrative permission.

The host guarantees joint uniqueness (prefer a database primary/unique constraint); registration does not prove live-data uniqueness or create constraints. Single-field schemas pass a one-element tuple. Existing metadata consumers, tests, and examples migrate together; the old singular API is removed intentionally.

### Exact detail object

Keep DetailReadRequest.Key as JsonElement but require Object. Enumerate properties before dictionary construction to detect duplicates, use exact ordinal aliases, and require exactly the tuple members. Convert each through existing strict LiteralConverter, rejecting null, extra/missing/wrong-case members and lossy values at `/key/<alias>` or `/key` paths.

Build equality per typed component and AND them with the authorized/client predicates. Host lookup does not use client Filter permissions. Preserve SingleOrDefault duplicate detection and inaccessible/nonexistent equivalence. Empty selection still yields `{}` for a present row.

Alternative: mixed scalar/object formats burden every transport/client and make identity validation inconsistent.

### Ordering and continuation

Resolve explicit sorts as today, preserving directions and repeated logical keys. Append each missing physical key property ascending in schema tuple order, choosing its declared alias; aliases over the same property count as already ordered. Example: explicit LocalId descending for tuple TenantId/LocalId becomes LocalId descending then TenantId ascending. Existing generic lexicographic predicates and projected physical-slot deduplication then work unchanged.

Cursor identity replaces singular StableKey with ordered alias/property/type key bindings. Intentionally invalidate tokens created under the old key identity format; do not reinterpret them. Preserve confidentiality, active-version strategy, lookahead boundary, and metadata-independent continuation. Document restart and schema/dataset bump when key definition changes; no historic-token fallback.

### Surface migration and tests

Migrate core/facade tests and examples to object keys, including GET routes constructing one-member objects. Add a real composite-key table with duplicate individual components, hidden keys, alias overlap, mixed sorts, and nullable non-key sort fields. Compare list/cursor/export concatenation with provider order; retain caps, deadlines, aborts, snapshots, projection, and no-count behavior.

No automatic policy/entity migration. Hosts explicitly update schema version and publish compatible policies where required. A source-compatible singular constructor shim was considered and rejected to keep one identity model before release.

## Risks / Trade-offs

- [Broad pre-release migration] -> Update schema metadata, requests, examples, HTTP scripts, token tests, and docs in the same change.
- [Host violates uniqueness] -> Explicit host constraint requirement and duplicate-detail/traversal negative tests; no invented unique keys.
- [Provider compare limitations] -> Reuse current supported seek types and explicit failures; capability profiles extend this later.

## Migration Plan

Apply structured errors, migrate schema/detail/order/token code as one coherent identity change, then provider and example fixtures. Run all existing single-key behavior plus composite conformance. Earlier proposals need no edits; downstream capability/test-kit plans consume KeyFields.
