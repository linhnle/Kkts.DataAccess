# Spec Delta

## ADDED Requirements

### Requirement: Trusted seek execution capability
The repository SHALL execute cursor pages and exports through a trusted seek-capable entity session. Seek requests SHALL carry the complete authorized query, resolved ordering, and typed boundary values derived by the repository, never client executable expressions. Existing count, offset-page, and single-record adapter/session contracts SHALL remain available. An adapter without seek capability SHALL remain usable for offset lists, counts, and detail but SHALL fail explicitly for cursor lists and exports. The core repository and SQLite facade SHALL expose matching cursor operations. No unsupported seek execution SHALL fall back to growing offsets, unrestricted fetching, or local authorization evaluation.

#### Scenario: Custom seek adapter
- **WHEN** a host supplies a seek-capable custom adapter
- **THEN** the adapter can execute authorized cursor pages and exports without changing client contracts or policy storage

#### Scenario: Adapter lacks seek capability
- **WHEN** a host invokes cursor pagination or export through an adapter that implements only the existing session contract
- **THEN** the operation fails explicitly, disposes the owned session, and does not invoke offset paging as a substitute

#### Scenario: Existing nonseek operations
- **WHEN** a caller uses offset list, count, or detail through an existing conforming adapter
- **THEN** the addition of seek capability does not change those operations' adapter requirements

#### Scenario: Bounded seek batches
- **WHEN** export requests successive batches
- **THEN** each batch is bounded by host configuration and uses an advancing typed boundary rather than an increasing offset

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
