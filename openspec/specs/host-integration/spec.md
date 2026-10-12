# host-integration Specification

## Purpose

Integrate authorized reads into dependency-injection hosts with validated resource registrations, asynchronous trusted identity capture, and owned EF context factories.

## Requirements

### Requirement: Validated fluent host registration
An optional integration SHALL expose fluent per-resource registration of schema, compiler, policy source, adapter, trusted context, and read/cursor/export options. Contradictory or incomplete registrations and invalid options SHALL fail explicitly during startup validation. Request-scoped services MUST NOT be captured by singleton compilers/factories. Startup validation SHALL check configuration/model/provider identity without requiring a request identity or active published policy, silently migrating databases, or issuing sample authorized reads. Core direct construction SHALL remain supported.

#### Scenario: Invalid options at startup
- **WHEN** a registered export batch exceeds the maximum permitted page size
- **THEN** startup validation fails with an actionable configuration diagnostic before serving reads

#### Scenario: No request during startup
- **WHEN** a valid host starts without an authenticated request or active policy
- **THEN** configuration/model checks succeed without inventing identity or automatically publishing policy

#### Scenario: Lifetime isolation
- **WHEN** two request scopes execute the same resource with different context providers
- **THEN** neither singleton templates nor factory registrations retain the other request's identity

### Requirement: Asynchronous trusted context capture
Repositories SHALL support a cancellable asynchronous server-controlled context provider without blocking on asynchronous work. Each operation SHALL capture one trusted context snapshot; an export SHALL retain that snapshot throughout enumeration. Context acquisition failures, cancellation, missing required values, and wrong types SHALL fail closed before entity access. Clients MUST NOT replace the provider, scope, roles, groups, or registered authorization values. Existing synchronous host factories SHALL remain usable through an explicit adapter.

#### Scenario: Awaited context
- **WHEN** trusted context acquisition performs asynchronous server work
- **THEN** the operation awaits it once with cancellation and uses that snapshot for binding

#### Scenario: Export context stable
- **WHEN** the host's current identity source changes after the first export batch
- **THEN** later batches retain the initial snapshot and configured policy-change checks remain independent

#### Scenario: Context acquisition canceled
- **WHEN** context acquisition is canceled or fails
- **THEN** no entity session opens and no default identity or result is substituted

### Requirement: Owned asynchronous EF context factories
EF integration SHALL accept IDbContextFactory-based hosts and asynchronously create a distinct owned context for each applicable session. Disposal SHALL occur exactly once on success, failure, and early export termination, preserving export-wide snapshot ownership. Startup model checks SHALL dispose temporary contexts and perform no application data query. The integration MUST NOT resolve a request DbContext and dispose it as though independently owned.

#### Scenario: Factory creation and failure
- **WHEN** factory context creation succeeds but provider validation fails
- **THEN** the owned context is disposed and the error propagates without fallback

#### Scenario: Snapshot ownership
- **WHEN** snapshot export uses a factory-created context
- **THEN** all batches use that context/transaction until enumeration cleanup rather than creating a new context per batch
