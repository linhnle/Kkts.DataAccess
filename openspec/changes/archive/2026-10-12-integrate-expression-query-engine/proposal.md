# Proposal

## Why

Kkts.DataAccess currently uses Kkts.Expressions mainly for And/Or composition while independently implementing predicate construction, semantic checks, and ordering. This duplicates capabilities of the pinned dependency and falls short of the integration described in docs/dependencies.md and the original design.

## What Changes

- Introduce an internal adapter from registered resource fields and validated structured conditions to Kkts.Expressions typed schemas, filter trees, and query contexts.
- Use upstream semantic validation and predicate construction in production, retaining DataAccess checks for authorization, trusted context, strict conversion, and resource limits.
- Replace the EF adapter's manual OrderBy/ThenBy construction with the upstream structured ordering API after authorization and stable-key completion.
- Preserve public policy JSON, ReadRequest, repository/provider contracts, case-sensitive public aliases, supported operators, and existing null and conversion behavior.
- Add production-path and relational parity coverage; update dependency documentation to describe the actual integration and any narrowly required compatibility adaptations.

## Capabilities

### New Capabilities

None. This change integrates existing dependency capabilities without exposing new query syntax or operations.

### Modified Capabilities

None. The resource-schemas, policy-compilation, authorized-reads, and entity-read-repositories requirements remain unchanged. This is an implementation refactor; `.openspec.yaml` declares `skip_specs: true`.

## Impact

Core ResourceSchema, PolicyValidator, PolicyCompiler, and AuthorizedReadRepository internals; shared EF ordering; compiler/read/repository/dependency tests; dependency and policy documentation. Kkts.Expressions remains pinned to 3.1.0, Kkts.Dynamic to 2.1.0. No upstream modifications, public API breaks, policy migrations, or new arbitrary-expression endpoints are planned.
