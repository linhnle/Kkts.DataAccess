# Entity Read Repositories

## Purpose

Allow hosts to select and extend entity database providers while retaining the same protected read contracts and independent policy storage.

## Requirements

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

### Requirement: Provider-neutral authorized repository
The system SHALL expose a generic read-only repository supporting list, detail, count, and bounded export through distinct operation-specific request contracts. Core and SQLite facade read methods SHALL expose matching signatures and behavior, with the detail key supplied inside the detail request. The shared all-purpose read request and its method overloads SHALL be removed as an intentional pre-release breaking change. Consumers MUST NOT receive raw entities, executable query handles, or a method that bypasses authorization. Repository provider selection MUST NOT change field permissions, mandatory constraints, or policy capture semantics. Trusted entity adapter and session contracts SHALL remain unchanged.

#### Scenario: Equivalent protected operations
- **WHEN** a host uses any supplied provider for the same resource, trusted context, and supported operation-specific request
- **THEN** the operation enforces the authorized-read contract and returns only permitted DTO fields

#### Scenario: Rejected request before entity access
- **WHEN** a request references forbidden fields or has invalid operation-specific inputs
- **THEN** the repository rejects it before opening or querying the entity database

#### Scenario: Matching repository surfaces
- **WHEN** a caller switches between the core repository and SQLite facade
- **THEN** the same operation-specific requests and result types are accepted without a legacy shared-request overload

#### Scenario: Existing trusted adapters
- **WHEN** a host uses a conforming entity adapter built against the existing session contract
- **THEN** the request redesign does not require new adapter/session methods or a policy storage migration

### Requirement: Independent entity and policy databases
The system SHALL allow entity reads from SQLite, SQL Server or MySQL while policy drafts, publication, audit history and rollback remain in SQLite. Each operation SHALL capture its policy from the configured policy store before entity execution; exports SHALL retain that capture across batches. Entity provider selection MUST NOT migrate or initialize policy tables in the entity database.

#### Scenario: Separate databases
- **WHEN** a host selects SQL Server or MySQL for entities and SQLite for policies
- **THEN** entity queries use the selected entity database and policy lifecycle operations continue using SQLite

#### Scenario: Publication during export
- **WHEN** the SQLite policy store publishes a new version during an export from another provider
- **THEN** later export batches retain the original policy while newly started operations capture the new version

### Requirement: Provider registration and extensibility
The system SHALL supply usable SQLite, SQL Server and MySQL entity adapters and allow a host to register an additional adapter through the same repository boundary without changing policy compilation or client contracts. Registration SHALL explicitly identify the entity provider and connection configuration. Unknown, missing or contradictory provider configuration SHALL fail clearly without silently choosing a different provider.

#### Scenario: Additional provider
- **WHEN** a host registers a conforming additional entity adapter
- **THEN** the existing authorized facade can execute supported reads without provider-specific client requests

#### Scenario: Invalid configuration
- **WHEN** the configured provider is unknown or mismatches the configured database context
- **THEN** startup or repository construction fails with an actionable configuration error

### Requirement: Explicit provider semantics and failure
Each adapter SHALL document its supported database versions, field types, operators and ordering semantics. Unsupported translations or database failures MUST abort without unrestricted queries or local authorization evaluation. Null handling SHALL preserve the authorization contract; database collation and precision limitations SHALL be documented and verified. Provider capabilities MUST NOT broaden schema eligibility or user grants.

#### Scenario: Unsupported translation
- **WHEN** a supported adapter cannot translate a requested predicate or ordering
- **THEN** the operation fails explicitly and returns no unrestricted result

#### Scenario: Provider conformance
- **WHEN** acceptance runs against each supplied real database
- **THEN** tenant isolation, deny precedence, hidden-field restrictions, null cases, deterministic tie ordering, detail/count/export and policy version capture satisfy the common contract

### Requirement: Execution ownership and SQLite compatibility
Repository operations SHALL propagate cancellation and dispose owned entity sessions on success, failure, and export termination. An export SHALL have bounded batches and SHALL NOT promise a cross-batch data snapshot. Existing SQLite facade construction SHALL remain source-compatible and route through the shared enforcement path. SQLite read method signatures SHALL migrate together with the core operation-specific request contracts, intentionally replacing the earlier read-method source-compatibility guarantee.

#### Scenario: Export stopped early
- **WHEN** a consumer cancels or stops enumerating an export
- **THEN** owned database resources are released without executing further batches

#### Scenario: Existing SQLite caller
- **WHEN** an existing host replaces the shared request with operation-specific requests and moves the detail key into its request
- **THEN** its calls build against the redesigned facade and retain protected read behavior

#### Scenario: Existing SQLite registration
- **WHEN** an existing host constructs the SQLite facade with its current schema, compiler, store, database factory, context factory, scope, and limits
- **THEN** construction remains supported and the facade delegates to the shared repository

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

### Requirement: Provider-conformant seek ordering
Supplied seek adapters SHALL execute both boundary comparisons and ordering in the entity database with compatible type, direction, null, collation, and precision semantics. They SHALL document their verified seek-supported type/order combinations separately from offset-sort support. Unsupported comparisons or translation failures SHALL abort explicitly before returning a successful page or batch. Provider capabilities MUST NOT broaden schema eligibility or user grants. Conformance SHALL test complete traversal against the provider's ordered authorized sequence, not only individual page sizes.

#### Scenario: Relational traversal conformance
- **WHEN** conformance executes against SQLite, SQL Server, or MySQL using a supported multi-field ordering
- **THEN** concatenated cursor pages and export batches match that provider's ordered authorized sequence, including ties, directions, nulls, and collation cases

#### Scenario: Unsupported ordering combination
- **WHEN** a seek ordering uses a type or comparison the configured provider cannot execute consistently
- **THEN** the adapter fails explicitly without coercing types, changing collation, or switching to local evaluation

#### Scenario: Authorization before continuation
- **WHEN** a valid boundary and client filter would also match forbidden rows
- **THEN** the entity query excludes those rows before seek pagination and materialization

### Requirement: Host-managed continuation protection
The library SHALL provide an optional protection integration using established authenticated encryption with host-managed keys, without adding an ASP.NET dependency to core. Hosts SHALL supply an application-isolated key configuration and stable authenticated-caller and dataset bindings. Shared-key deployments SHALL be able to resume unexpired compatible tokens across instances or restarts; incompatible protection configurations SHALL reject tokens explicitly. Token contents and keys MUST NOT be logged by default or embedded as fixed secrets in source.

#### Scenario: Restart with shared key configuration
- **WHEN** a host resumes an unexpired compatible cursor after restart or on another instance with the same isolated protection configuration
- **THEN** token protection accepts it and the repository still reapplies authorization and compatibility checks

#### Scenario: Key or application isolation mismatch
- **WHEN** a token is presented to a host with incompatible key or application-purpose configuration
- **THEN** the token is rejected rather than interpreted as an unprotected boundary
