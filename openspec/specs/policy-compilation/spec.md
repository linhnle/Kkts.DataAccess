# Policy Compilation

## Purpose

Turn validated administrator rules into predictable access decisions using trusted context and mandatory isolation constraints.

## Requirements

### Requirement: Validated structured rules
The system SHALL accept nested all, any, and not condition nodes and typed field/operator/value leaves. Each node MUST have exactly one form; all and any MUST contain at least one child. Values SHALL be literals or references to registered trusted context keys. Rules SHALL declare resource, supported action, Allow or Deny effect, and role/group applicability. An omitted record condition SHALL mean true. Invalid, unsupported, or excessively complex definitions MUST fail validation with condition-path diagnostics and MUST NOT become executable policies.

#### Scenario: Invalid tree
- **WHEN** a draft contains an empty all node or an unknown operator
- **THEN** validation rejects it and reports the offending node path

#### Scenario: Unconditional grant
- **WHEN** an applicable Allow rule omits its record condition
- **THEN** it grants records only within mandatory constraints and outside matching Deny conditions

### Requirement: Trusted context binding
The system SHALL derive user, tenant, roles, groups, and registered context values from server-controlled sources. Client-provided replacements MUST NOT affect authorization. Missing or wrongly typed context required by any applicable rule or mandatory constraint MUST fail the operation closed.

#### Scenario: Tenant spoofing
- **WHEN** a client supplies another tenant's identifier as authorization context
- **THEN** the decision continues to use the authenticated server tenant

#### Scenario: Missing context in deny rule
- **WHEN** an applicable Deny condition requires a context value unavailable on the server
- **THEN** the operation fails without returning records or a count

### Requirement: Default-deny record and action composition
For a resource and action, the system SHALL select applicable rules using trusted subject context and evaluate records as mandatory constraints AND the OR of applicable Allow conditions AND NOT the OR of applicable Deny conditions. No applicable Allow rule SHALL deny access. Ordinary administrator rules MUST NOT weaken mandatory constraints. Unsupported or ungranted actions MUST fail before execution.

#### Scenario: Allow and deny overlap
- **WHEN** a record matches an Allow rule and a Deny rule
- **THEN** it is excluded

#### Scenario: Tenant boundary
- **WHEN** an unconditional Allow rule matches a record outside the mandatory tenant constraint
- **THEN** the record is excluded

#### Scenario: No grant
- **WHEN** no Allow rule applies to the requested action
- **THEN** the operation is denied before querying data

### Requirement: Independent context-wide field permissions
The system SHALL resolve separate field sets for selection, filtering, sorting, aggregation, and writing from resource, action, and trusted subject context. Each set SHALL equal schema-eligible fields granted by applicable field Allow entries minus those removed by applicable field Deny entries; absent grants SHALL yield an empty set. Field entries MUST NOT depend on record values in Phase 1. A rule combining a nontrivial record condition with field grants MUST be rejected rather than interpreted as record-specific permissions. Record grants alone SHALL grant no fields, and field grants alone SHALL grant no records.

#### Scenario: Independent query permission
- **WHEN** a field is selectable but lacks a filtering grant
- **THEN** returning that field is permitted but filtering by it is rejected

#### Scenario: Conditional field grant
- **WHEN** the submitted Employee rule attaches fields to a DepartmentId or Status record condition
- **THEN** validation requires separate context-wide field permissions or reports that record-dependent fields are unsupported

#### Scenario: Field deny precedence
- **WHEN** applicable field entries both allow and deny selection of Salary
- **THEN** Salary is absent from the effective selection set

### Requirement: Version-safe execution plans
The system SHALL bind each operation to one published policy version and compatible schema version. Reused compiled policies MUST NOT retain another request's identity or context. Publication or rollback SHALL affect newly started operations after its transaction commits; an in-flight operation SHALL keep its captured version. Compilation failure or schema incompatibility MUST fail closed.

#### Scenario: Cross-user reuse
- **WHEN** users from different departments execute the same cached policy
- **THEN** each predicate uses that operation's trusted department

#### Scenario: Incompatible schema
- **WHEN** a published rule references a removed schema field
- **THEN** execution fails without falling back to unfiltered data

### Requirement: Provider-bound policy validation
Hosts SHALL be able to bind policy validation/publication/preview and mandatory-constraint validation to the registered provider capability profile. Known unsupported field/operator/mapping combinations SHALL produce stable input-path diagnostics before activation; unverified combinations SHALL remain explicitly labeled and subject to runtime translation. Subject-only context checks SHALL NOT be incorrectly treated as database predicates. Provider-neutral structural validation SHALL remain available and SHALL NOT claim provider conformance. Runtime capture/binding SHALL reject known-incompatible active policy content before entity queries, including policies published through a provider-neutral administrative path.

#### Scenario: Unsupported policy predicate
- **WHEN** a provider-bound draft uses a known unsupported field/operator combination
- **THEN** validation identifies its condition path and publication leaves active pointer/content/audit unchanged

#### Scenario: Mandatory constraint unsupported
- **WHEN** registration supplies a known unsupported mandatory database predicate
- **THEN** provider-bound startup validation fails rather than weakening or removing the constraint

#### Scenario: Neutral validation remains honest
- **WHEN** an extension author validates a policy without a provider profile
- **THEN** validation checks schema/type/complexity only and does not claim successful database translation

### Requirement: Bounded validated template retention
Each compiler SHALL retain at most its configured positive template-entry capacity, defaulting to 128. Eviction SHALL be deterministic and thread-safe. Cache entries SHALL contain only privately owned validated immutable policy content/templates, never request-bound identity, context, grants, predicates, or records. Invalid builds MUST NOT evict healthy entries or be cached. Auxiliary retained lookup/history structures MUST NOT grow with historical versions. Every bind SHALL still derive fresh authorization from its operation context.

#### Scenario: Capacity exceeded
- **WHEN** successful compilation introduces more distinct versions than capacity
- **THEN** retained entries remain within capacity and the deterministic eviction rule removes older templates

#### Scenario: Concurrent distinct users
- **WHEN** different users bind the same template concurrently while other versions cause eviction
- **THEN** each bound predicate/grant set uses only its own context and the cache remains bounded

#### Scenario: Invalid build at capacity
- **WHEN** malformed content is compiled while the cache is full
- **THEN** compilation fails and healthy retained entries are not displaced by the failed build

### Requirement: Eviction-safe content and operation identity
Resident same-version different-content captures SHALL fail explicitly. After eviction, content SHALL be reparsed and validated; lifetime immutable version/content enforcement SHALL be guaranteed by the authoritative policy source, not an unbounded compiler history. In-flight operations SHALL retain the exact validated content identity needed for cursor compatibility independently of cache residency. Eviction MUST NOT cause missing-cache-key errors, change an existing operation's policy identity, or relabel its version. This change SHALL preserve the existing token identity algorithm and version compatibility rules.

#### Scenario: Resident content mismatch
- **WHEN** a retained version is supplied with different payload bytes
- **THEN** binding rejects the immutable-content mismatch

#### Scenario: Evicted version recompiled
- **WHEN** an authoritative immutable version is used again after eviction
- **THEN** it is validated again and executes with fresh request binding, without requiring a retained historical ledger

#### Scenario: Eviction during cursor operation
- **WHEN** a cursor operation binds a policy and concurrent work evicts its template before fingerprinting
- **THEN** fingerprinting uses operation-owned validated identity and succeeds without retrieving the evicted template
