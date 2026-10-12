# Spec Delta

The modified seek requirement comes from the prerequisite cursor/keyset change. Synchronize the prerequisite changes in order before this delta.

## ADDED Requirements

### Requirement: Host-controlled projection requirement
Core and SQLite facade SHALL require projected retrieval by default and SHALL expose matching host-only configuration that explicitly permits entity materialization for adapters lacking a relevant projected capability. When projection is required and that capability is absent, retrieval SHALL fail explicitly after releasing the owned session and before executing entity queries, including a requested list count. When entity materialization is explicitly permitted, a projection-capable path SHALL still use projection; absence of the relevant capability SHALL allow the authorized legacy entity path. Projection, translation, mapping, and database failures MUST NOT trigger entity-loading retries in either mode. Standalone count SHALL remain usable without projected retrieval capability.

#### Scenario: Default rejects entity-only adapter
- **WHEN** a host uses an entity-only adapter with default configuration for list or detail
- **THEN** retrieval fails explicitly without calling entity page/single methods or a list count, and the session is disposed

#### Scenario: Explicit compatibility allowance
- **WHEN** a host explicitly allows entity materialization and its adapter lacks the operation's projected capability
- **THEN** the authorized legacy entity path remains usable with the same permitted public DTO output

#### Scenario: Capable adapter still projects in compatibility mode
- **WHEN** a host allows entity materialization but the adapter provides the relevant projected capability
- **THEN** the operation uses projection rather than loading complete entities

#### Scenario: Projection failure never retries entity loading
- **WHEN** a projected query or mapping fails in either host mode
- **THEN** the error propagates without calling the entity-materialization path

#### Scenario: Count needs no projection capability
- **WHEN** a host requests standalone count through an adapter with the existing count contract
- **THEN** default projection requirements do not block its exact authorized count and policy version

### Requirement: Trusted projected adapter contracts
Execution plans SHALL carry immutable validated output bindings independently of predicate and sort bindings. Projected adapters SHALL return typed scalar values and optional distinct continuation sidecars, not entity instances or executable client projections. They SHALL execute the supplied complete authorization and ordering in their provider and preserve alias/order/null/precision semantics. The repository SHALL reject invalid projected row cardinality, incompatible scalar types, or malformed required sidecars explicitly rather than silently filling values or returning partial DTOs. Existing entity/count/session methods SHALL remain available for explicit compatibility and aggregate execution.

#### Scenario: Conforming custom projection adapter
- **WHEN** a host registers a custom projected adapter that executes the protected plan in its database
- **THEN** the same public requests and permitted JSON results work without requiring EF in core

#### Scenario: Invalid projected output
- **WHEN** an adapter returns the wrong number or type of selected values, or an invalid required continuation tuple
- **THEN** the operation fails without partial DTO output, silent defaults, or a full-entity retry

#### Scenario: Unmapped selection cannot become local evaluation
- **WHEN** a supplied EF projection uses a schema binding absent from the entity's mapped scalar model
- **THEN** it fails explicitly instead of loading an entity and evaluating its getter in a top-level client projection

#### Scenario: Provider conformance verifies materialization
- **WHEN** projection acceptance runs on SQLite, SQL Server, or MySQL
- **THEN** output, result-column metadata, and entity-materialization evidence confirm that only selected values and permitted continuation sidecars are materialized

## MODIFIED Requirements

### Requirement: Trusted seek execution capability
The repository SHALL execute cursor pages and exports through a trusted seek-capable entity session, using projected seek when projection is required or when the relevant projected capability is available. Seek requests SHALL carry the complete authorized query, resolved ordering, validated output bindings, and typed boundary values derived by the repository, never client executable expressions. Existing count, offset-page, and single-record adapter/session contracts SHALL remain available subject to the host's projection requirement for retrieval. An adapter without either projected or explicitly allowed entity seek capability SHALL fail explicitly for cursor lists and exports. The core repository and SQLite facade SHALL expose matching cursor operations and projection configuration. No unsupported seek execution SHALL fall back to growing offsets, unrestricted fetching, or local authorization evaluation.

#### Scenario: Custom seek adapter
- **WHEN** a host supplies a seek-capable custom adapter conforming to the configured projection requirement
- **THEN** the adapter can execute authorized cursor pages and exports without changing client contracts or policy storage

#### Scenario: Adapter lacks seek capability
- **WHEN** a host invokes cursor pagination or export through an adapter without the required projected or explicitly permitted entity seek capability
- **THEN** the operation fails explicitly, disposes the owned session, and does not invoke offset paging as a substitute

#### Scenario: Existing nonseek operations
- **WHEN** a caller uses offset list or detail through an existing adapter with entity materialization explicitly allowed, or requests standalone count
- **THEN** those operations remain usable through their applicable existing contracts without a seek-capability requirement

#### Scenario: Bounded seek batches
- **WHEN** export requests successive batches
- **THEN** each batch is bounded by host configuration and uses an advancing typed boundary rather than an increasing offset

#### Scenario: Projected seek without entity seek
- **WHEN** an adapter supplies projected seek but no full-entity seek implementation
- **THEN** required-projection cursor and export retrieval can execute using projected rows and trusted sidecars
