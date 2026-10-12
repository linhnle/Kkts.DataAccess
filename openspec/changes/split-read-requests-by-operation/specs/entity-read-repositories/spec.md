# Spec Delta

## MODIFIED Requirements

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

### Requirement: Execution ownership and SQLite compatibility
Repository operations SHALL propagate cancellation and dispose owned entity sessions on success, failure, and export termination. An export SHALL have bounded batches and SHALL NOT promise a cross-batch data snapshot. Existing SQLite facade construction SHALL remain source-compatible and route through the shared enforcement path. SQLite read method signatures SHALL migrate together with the core operation-specific request contracts, intentionally replacing the earlier read-method source-compatibility guarantee.

#### Scenario: Export stopped early
- **WHEN** a consumer cancels or stops enumerating an export
- **THEN** owned database resources are released without executing further batches

#### Scenario: Existing SQLite registration
- **WHEN** an existing host constructs the SQLite facade with its current schema, compiler, store, database factory, context factory, scope, and limits
- **THEN** construction remains supported and the facade delegates to the shared repository

#### Scenario: Existing SQLite caller
- **WHEN** an existing host replaces the shared request with operation-specific requests and moves the detail key into its request
- **THEN** its calls build against the redesigned facade and retain protected read behavior
