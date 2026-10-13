# Proposal

## Why

PolicyCompiler retains every validated policy template indefinitely, and cursor identity directly indexes that cache. Bound retained state while keeping identity binding and cursor behavior correct even when templates are evicted during an operation.

## What Changes

- Add a host-configurable positive entry capacity, default 128, with deterministic bounded eviction and no unbounded history/single-flight dictionaries.
- Retain only privately owned validated policy templates, never request contexts, bound predicates, or row data.
- Reject same-version changed content while a template is resident; after eviction reparse/revalidate authoritative content.
- Define lifetime immutable-content enforcement as the authoritative policy source's responsibility, not an unbounded compiler ledger.
- Carry policy content identity in operation-owned bound state so cursor fingerprinting never assumes cache residency.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `policy-compilation`: Bounded template retention, request isolation, and eviction-safe policy identity.

## Impact

PolicyCompiler, BoundPolicy operation metadata, ContinuationTokens, compiler/cursor/concurrency tests, and cache documentation. Reuse the bounded FIFO pattern from DtoShapeCache without changing its separate lifecycle. No new dependency or policy table migration. This proposal is independently implementable; structured errors can be adopted later without changing its behavioral contract.
