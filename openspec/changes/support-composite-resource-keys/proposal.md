# Proposal

## Why

A single stable-key property excludes common composite-key tables and cannot uniquely complete traversal for them. Generalize identity as an ordered unique tuple while preserving independent grants and provider-aware seek behavior.

## What Changes

- **BREAKING**: replace singular stable-key registration/access with an immutable ordered nonempty key tuple.
- **BREAKING**: require exact alias-keyed JSON objects for detail keys, including single-key resources; reject scalar, missing, extra, duplicate, null, or incompatible components.
- Build authorized detail equality across all components without requiring Filter grants for implicit lookup.
- Complete page/seek ordering with every missing key property and preserve explicit directions/repeated sort semantics.
- Include ordered key identity in protected cursor compatibility, retaining hidden encrypted sidecars and existing export modes.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `resource-schemas`: Ordered composite resource identity and host uniqueness obligations.
- `authorized-reads`: Object detail keys and full-tuple deterministic tie-breaking.

## Impact

Schema metadata/constructors, detail validation, ordering/token bindings, provider fixtures, facade/example/client migration, and docs. Depends on `add-structured-errors` for component diagnostics. No entity/policy table migrations are automatic; hosts bump schema/dataset identity when changing key bindings. Existing single-key fixtures migrate to one-member objects.
