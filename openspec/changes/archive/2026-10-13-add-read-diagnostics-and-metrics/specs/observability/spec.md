# Spec Delta

## Purpose

Expose bounded, privacy-preserving read-stage and cache/export telemetry without changing authorization, execution ownership, or lazy enumeration.

## ADDED Requirements

### Requirement: Read and cache instrumentation
The library SHALL expose opt-in activities and metrics for policy capture, binding, entity execution, mapping, template/DTO cache hits/misses/builds/evictions, and operation outcomes. Instruments SHALL use documented names, units, and bounded label vocabularies. Instrumentation MUST NOT run extra database queries, retain request objects, enumerate output again, or change policy binding. No listener SHALL be required for normal use and core SHALL require no exporter/logging dependency.

#### Scenario: Stage observations
- **WHEN** a listener observes an authorized list
- **THEN** it can distinguish capture, binding, entity execution, mapping, and final outcome without extra count/data queries

#### Scenario: Cache churn
- **WHEN** a successful build evicts a compiler template
- **THEN** cache telemetry reports the build/eviction without including the policy key/content or user identity

#### Scenario: No listener
- **WHEN** no activity or metric listener is attached
- **THEN** results, query counts, cancellation, and disposal remain unchanged

### Requirement: Privacy and bounded cardinality
Default tags/events/status descriptions SHALL include only documented bounded operation/provider/stage/outcome/code classifications. They MUST NOT include row/literal/context values, user/tenant/group identifiers, schema/resource names, arbitrary field paths, SQL, connection strings, policy payloads, token contents, version identifiers, or free-form exception messages. Unknown custom provider/code classifications SHALL normalize into bounded categories. Library-created child activities SHALL NOT copy sensitive baggage into tags or events.

#### Scenario: Sentinel privacy
- **WHEN** execution uses sentinel secrets in literals, context, tokens, payloads, and provider errors
- **THEN** no library-emitted telemetry contains those sentinels

#### Scenario: Custom cardinality
- **WHEN** many custom resource/provider/error strings are used
- **THEN** metric tag sets remain within the documented finite vocabulary rather than creating per-string series

### Requirement: Export lifecycle outcomes
Started export enumerations SHALL emit exactly one terminal outcome on exhaustion, row cap, policy abort, duration expiry, caller cancellation, failure, or early disposal. Unadvanced enumerables SHALL emit no started operation; abandoned undisposed enumerators SHALL NOT be reported as successfully complete. Long-lived export activity scopes MUST NOT leak ambient activity across yield into unrelated consumer work.

#### Scenario: Early disposal
- **WHEN** a consumer reads one batch and disposes before exhaustion
- **THEN** one early-disposal outcome is emitted and no success outcome is invented

#### Scenario: Aborted export
- **WHEN** an export detects changed activation
- **THEN** one policy-abort outcome is emitted while ownership/cancellation semantics remain unchanged
