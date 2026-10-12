# Proposal

## Why

Hosts lack a consistent view of capture, binding, queries, mapping, cache churn, and export termination. Instrument these stages without leaking data or creating high-cardinality identity labels.

## What Changes

- Add BCL activities and metrics for read stages, template/DTO cache outcomes, and export lifecycle.
- Use bounded operation/provider/stage/outcome/error-code labels, excluding values, SQL, policy payloads, tokens, identities, and free-form exception messages.
- Distinguish successful exhaustion, row cap, abort, expiry, cancellation, failure, and early disposal.
- Preserve lazy export/cancellation semantics and avoid record counting/enumeration solely for telemetry.
- Document listener opt-in, cardinality guarantees, and privacy/coverage tests.

## Capabilities

### New Capabilities

- `observability`: Privacy-preserving activity/metric contracts and terminal outcomes.

### Modified Capabilities

None.

## Impact

Core repository/compiler/DTO caches and shared EF stages, listener tests, and documentation; no mandatory exporter/logging/ASP.NET package. Depends on `add-structured-errors` and `bound-policy-compiler-cache` for stable classifications and cache outcomes. DI can document consumption later but is not required.
