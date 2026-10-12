# Spec Delta

## ADDED Requirements

### Requirement: Model-aware capability validation
Supplied EF registrations SHALL validate resource bindings against the actual provider/model and expose immutable operation-specific capability results for scalar output, predicates/operators, offset ordering, seek ordering, and snapshot shapes. Known unsupported advertised combinations SHALL fail explicitly at registration or provider-bound validation; unverified combinations SHALL be identified as unverified rather than falsely supported. Profiles SHALL distinguish offset and seek semantics and MUST NOT grant permissions, broaden schema eligibility, or imply that every composed query translates. Core-only registrations SHALL remain usable without EF/provider profiles.

#### Scenario: Known unsupported advertised seek
- **WHEN** a provider-bound resource advertises seek with a converter or scalar known unsupported by the installed implementation
- **THEN** registration reports an operation/field-specific unsupported diagnostic before serving that operation

#### Scenario: Output differs from seek
- **WHEN** a mapped scalar supports projected output but not seek comparison
- **THEN** its profile distinguishes those operations rather than rejecting valid output or advertising unsupported traversal

#### Scenario: Unverified composed query
- **WHEN** a profile accepts individual bindings but a composed predicate still fails provider translation
- **THEN** execution propagates the failure without unrestricted, local, or weaker-operation fallback

### Requirement: Declared provider comparison scope
Capability documentation SHALL specify verified provider/version/model scope and database-controlled collation, precision, conversion, and null ordering separately for offset and seek. A profile MUST NOT invent a collation or silently coerce ordering/types. Runtime preflight SHALL recheck relevant model/plan restrictions and snapshot operational prerequisites that static registration cannot establish.

#### Scenario: Null order distinction
- **WHEN** a host compares offset ordering with seek traversal
- **THEN** documentation identifies provider-native offset behavior versus explicit seek null-first ascending/null-last descending semantics

#### Scenario: Runtime prerequisites
- **WHEN** startup model checks pass but snapshot prerequisites are absent at execution
- **THEN** snapshot execution fails explicitly rather than treating the profile as sufficient permission or evidence
