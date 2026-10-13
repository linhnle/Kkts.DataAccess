# Entity Read Repositories

## Purpose

Allow hosts to select and extend entity database providers while retaining the same protected read contracts and independent policy storage.

## ADDED Requirements

### Requirement: Provider-neutral authorized repository
The system SHALL expose a generic read-only repository supporting list, detail, count and bounded export through the existing authorized-read contracts. Consumers MUST NOT receive raw entities, executable query handles or a method that bypasses authorization. Repository provider selection MUST NOT change field permissions, mandatory constraints or policy capture semantics.

#### Scenario: Equivalent protected operations
- **WHEN** a host uses any supplied provider for the same resource, trusted context and supported request
- **THEN** the operation enforces the existing authorized-read contract and returns only permitted DTO fields

#### Scenario: Rejected request before entity access
- **WHEN** a request references forbidden fields or an unsupported action
- **THEN** the repository rejects it before opening or querying the entity database

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
Repository operations SHALL propagate cancellation and dispose owned entity sessions on success, failure and export termination. An export SHALL have bounded batches and SHALL NOT promise a cross-batch data snapshot. Existing SQLite facade construction and read methods SHALL remain source-compatible and route through the shared enforcement path.

#### Scenario: Export stopped early
- **WHEN** a consumer cancels or stops enumerating an export
- **THEN** owned database resources are released without executing further batches

#### Scenario: Existing SQLite caller
- **WHEN** an existing host uses the current SQLite facade constructor and read methods
- **THEN** it builds and retains its prior protected read behavior
