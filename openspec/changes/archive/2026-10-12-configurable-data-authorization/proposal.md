# Proposal: Configurable Data Authorization

## Why

Applications need consistent record, field, and action authorization across every data endpoint. Administrator-managed policies can replace duplicated endpoint logic while preserving tenant isolation and preventing hidden-field inference.

## What Changes

- Introduce an independent `Kkts.DataAccess` integration package using `Kkts.Expressions` for record predicates and `Kkts.Dynamic` for permitted output shapes.
- Phase 1 delivers resource schemas, typed condition trees, trusted request context, default-deny policy compilation, mandatory tenant constraints, and user-context field permissions for selection, filtering, sorting, aggregation, and writing.
- Phase 1 enforces read authorization for lists, detail retrieval, counts, and exports, including filtering, sorting, and pagination. Aggregation and writes remain unavailable until their later phases.
- Phase 1 adds database-backed drafts and immutable published versions, validation, audit history, rollback, a basic administrator rule designer, and a working example.
- Phase 2 adds field-restricted writes with existing/proposed-state validation and provider-verified database projection.
- Phase 3 adds record-dependent field permissions, masking, relationship policies, authorized aggregation, and richer explanations. These are roadmap items, not implementation requirements of this change.

## Capabilities

### New Capabilities

- `resource-schemas`: Public resource fields, typed bindings, allowed operators, and operation eligibility.
- `policy-compilation`: Validated condition trees, trusted context binding, action decisions, record composition, and independent field permission sets.
- `authorized-reads`: Consistent authorization of lists, details, counts, exports, and client query operations.
- `policy-administration`: Database-backed lifecycle, validation, publication, audit, rollback, preview, and the basic rule designer.

### Modified Capabilities

None. This repository has no existing specifications or implementation.

## Impact

This is a greenfield .NET integration package with separate persistence/provider and example UI components. Existing Kkts libraries remain independent; their current APIs, versions, and provider capabilities must be verified during the explicit integration tasks. Phase 1 requires no modification to those libraries and does not promise database projection. Exact framework/package versions and UI technology are implementation choices recorded after compatibility verification. Consumers must route supported data operations through the enforcement layer and supply trusted identity and mandatory constraints.
