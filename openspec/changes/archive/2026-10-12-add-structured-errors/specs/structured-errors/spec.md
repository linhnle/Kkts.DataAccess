# Spec Delta

## Purpose

Provide stable machine-readable failure semantics and safe optional HTTP responses without parsing human-readable messages or weakening authorization.

## ADDED Requirements

### Requirement: Stable typed failures
Library-originated failures SHALL expose immutable machine-readable categories, stable codes, and optional structured diagnostics. Invalid requests, access denial, missing active policy, schema incompatibility, revision conflict, unsupported provider operations, invalid continuation, export policy change, and export duration expiry SHALL remain distinguishable without message parsing. Unexpected infrastructure failures and caller cancellation MUST NOT be relabeled as access denial. Human-readable messages SHALL NOT form the compatibility contract.

#### Scenario: Invalid input differs from denial
- **WHEN** a request has an invalid literal and a separate valid request references a forbidden field
- **THEN** callers can distinguish invalid input from access denial using typed codes without reading messages

#### Scenario: Policy unavailable differs from conflict
- **WHEN** policy capture finds no active version or a publication uses a stale revision
- **THEN** the failures carry distinct missing-policy and revision-conflict codes

#### Scenario: Infrastructure failure
- **WHEN** a provider connection fails unexpectedly
- **THEN** the original failure remains available internally and is not classified as permission denial or known unsupported translation

### Requirement: Precise safe diagnostic paths
Validation SHALL return stable diagnostic codes and RFC 6901 JSON Pointer paths into the validated input, using empty string for its root and correct escaping for path segments. Nested all/any/not nodes and selection/filter/sort/key components SHALL identify their actual location. Request diagnostics MUST NOT expose policy conditions, registered secret context values, row values, tokens, SQL, or exception internals. Invalid or inaccessible detail SHALL retain the same not-found result rather than introducing an existence-disclosing error.

#### Scenario: Nested condition
- **WHEN** the second child of a request filter's all node has an invalid operator
- **THEN** its diagnostic identifies `/filter/all/1/operator` with a stable validation code

#### Scenario: Administrative validation
- **WHEN** an authorized administrator validates a malformed policy rule condition
- **THEN** returned diagnostics locate the policy input nodes without embedding literal/context values in messages

#### Scenario: Detail existence privacy
- **WHEN** detail lookup targets an inaccessible or absent record
- **THEN** both retain identical not-found behavior without diagnostic evidence of record existence

### Requirement: Optional safe Problem Details translation
An optional ASP.NET Core integration SHALL translate known library failures and malformed transport input into documented Problem Details status/code/diagnostic shapes. Core MUST NOT depend on ASP.NET. Responses SHALL exclude exception stack traces, database text, policy/context/record values, and protected token state. Unexpected failures SHALL use a generic server error without exposing internals. Already-started streams SHALL abort rather than append error-shaped records or report successful completion. Cancellation SHALL remain cancellation rather than an invented successful/error body.

#### Scenario: Known error response
- **WHEN** a known invalid-request failure occurs before response start
- **THEN** the integration emits a safe 400 Problem Details response with stable code and applicable input diagnostics

#### Scenario: Streaming failure
- **WHEN** export fails after NDJSON records have been written
- **THEN** the integration terminates the stream without replacing status or appending an error record

#### Scenario: Unexpected failure response
- **WHEN** an unclassified database exception occurs before response start
- **THEN** the public response is generic 500 Problem Details and does not serialize the exception message
