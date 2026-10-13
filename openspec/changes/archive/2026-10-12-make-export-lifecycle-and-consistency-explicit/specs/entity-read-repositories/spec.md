# Spec Delta

## ADDED Requirements

### Requirement: Export-wide entity snapshot capability
Snapshot exports SHALL require an explicit trusted adapter capability that owns one entity session, connection, and consistent read transaction across every batch. All entity row membership, selected values, and sort tuples SHALL be read from the same provider snapshot established before first batch delivery. Snapshot capability MUST NOT broaden permissions, bypass required projection, or freeze authoritative policy checks. Supplied SQLite, SQL Server, and MySQL adapters SHALL implement snapshot export for documented verified prerequisites. Unsupported adapters or missing database prerequisites SHALL fail explicitly before first batch delivery without silently using best-effort, a per-statement snapshot, or a weaker isolation level. Snapshot loss or a transaction failure after delivery SHALL abort without reopening/replaying from another snapshot. Operational settings MUST NOT be silently enabled by the library.

#### Scenario: Entity mutations after first batch
- **WHEN** another connection commits insertions, deletions, selected-value changes, and sort-key changes after a snapshot export's first batch
- **THEN** remaining batches match the original snapshot's authorized ordered sequence and values

#### Scenario: Adapter lacks snapshot capability
- **WHEN** a host requires snapshot through a seek adapter lacking the snapshot capability
- **THEN** export fails explicitly before delivering rows and does not use ordinary per-batch sessions instead

#### Scenario: Provider prerequisites missing
- **WHEN** a configured provider cannot establish its documented export-wide snapshot
- **THEN** export fails explicitly before first delivery without changing database settings or downgrading consistency

#### Scenario: Snapshot connection fails after delivery
- **WHEN** the connection or transaction fails after one batch is delivered
- **THEN** export aborts, releases resources, and does not replay or continue from a newly acquired snapshot

#### Scenario: Snapshot keeps projection boundary
- **WHEN** snapshot export selects a narrow DTO with hidden authorization and continuation fields
- **THEN** it enforces the same database predicate and authorized projection/sidecar rules as best-effort export

## MODIFIED Requirements

### Requirement: Independent entity and policy databases
The system SHALL allow entity reads from SQLite, SQL Server or MySQL while policy drafts, publication, audit history and rollback remain in SQLite. Each operation SHALL capture its policy from the configured policy store before entity execution; exports SHALL retain that capture for every delivered batch and optionally abort on a later policy activation according to host configuration. Entity snapshot transactions SHALL NOT encompass authoritative policy revision reads or imply an atomic snapshot across the two stores. Entity provider selection MUST NOT migrate or initialize policy tables in the entity database.

#### Scenario: Separate databases
- **WHEN** a host selects SQL Server or MySQL for entities and SQLite for policies
- **THEN** entity queries use the selected entity database and policy lifecycle operations continue using SQLite

#### Scenario: Publication during export
- **WHEN** the SQLite policy store publishes a new version during an export from another provider
- **THEN** captured-policy exports retain the original policy, abort-mode exports stop when their check observes activation change, and newly started operations capture the new version

### Requirement: Execution ownership and SQLite compatibility
Repository operations SHALL propagate cancellation and dispose owned entity sessions on success, failure, and export termination. An export SHALL have bounded batches; best-effort mode SHALL NOT promise a cross-batch data snapshot, and requested snapshot mode SHALL require one export-wide owned transaction. Policy-change abort, duration expiry, capped completion, consumer cancellation, and early disposal SHALL release owned connections/transactions without further batches. Disposal SHALL remain cooperative with async enumeration rather than promising cleanup of an abandoned undisposed enumerator. Existing SQLite facade construction SHALL remain source-compatible and route through the shared enforcement path, exposing the same host export configuration as core. SQLite read method signatures SHALL migrate together with the core operation-specific request contracts, intentionally replacing the earlier read-method source-compatibility guarantee.

#### Scenario: Export stopped early
- **WHEN** a consumer cancels or stops enumerating an export and disposes its enumerator
- **THEN** owned database resources, including any snapshot transaction, are released without executing further batches

#### Scenario: Existing SQLite caller
- **WHEN** an existing host replaces the shared request with operation-specific requests and moves the detail key into its request
- **THEN** its calls build against the redesigned facade and retain protected read behavior

#### Scenario: Existing SQLite registration
- **WHEN** an existing host constructs the SQLite facade with its current schema, compiler, store, database factory, context factory, scope, and limits
- **THEN** construction remains supported and the facade delegates to the shared repository with captured-policy, best-effort export defaults

#### Scenario: Limited snapshot cleanup
- **WHEN** a snapshot export completes at its row cap or fails a policy/deadline check
- **THEN** its export-wide transaction/session are released without further entity queries
