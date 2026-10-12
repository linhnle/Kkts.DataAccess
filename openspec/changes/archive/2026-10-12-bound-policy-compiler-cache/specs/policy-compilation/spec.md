# Spec Delta

## ADDED Requirements

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
