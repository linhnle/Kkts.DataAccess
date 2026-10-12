# Proposal

## Why

Schema operator catalogs are provider-neutral, registration checks only provider identity, and seek mapping restrictions are discovered during execution. Reject known incompatible combinations early without pretending static checks prove relational translation.

## What Changes

- Add immutable provider/model-aware capability profiles distinguishing supported, unsupported, and unverified combinations for output, predicates, offset ordering, seek, and snapshots.
- **BREAKING**: migrate supplied provider helper registration to require schema and declared operation scope for model-aware validation.
- Require checks in supplied EF registrations and provider-bound validation of policies/mandatory constraints; keep provider-neutral core validation available.
- Reuse current seek/projection/snapshot checks as the source of capability rules rather than duplicating them.
- Preserve schema eligibility and grants; publish collation/null/precision scope and runtime limitations.
- Validate supported runtime plans and propagate residual translation errors, never fall back to local/unrestricted execution.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `entity-read-repositories`: Provider/model capability checks and declared semantics.
- `policy-compilation`: Provider-bound diagnostic validation in addition to structural validation.

## Impact

Core profile/validation contracts, shared EF/model rules and provider helpers, publication/preview host wiring, DI startup hook, tests, and docs. Depends on `add-structured-errors` and `add-dependency-injection-integration` for the complete registration hook. Composite-key support is consumed when available without rewriting that proposal.
