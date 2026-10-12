# Design

## Context

See [proposal.md](./proposal.md). Core owns capture/bind/map orchestration, shared EF owns entity execution, and both compiler/DTO caches have bounded lifecycle hooks after the cache prerequisite. Export is lazy and can yield across a consumer pause or terminate in several distinct ways.

## Goals / Non-Goals

**Goals:** stable low-cardinality instrumentation with complete lifecycle and privacy tests.

**Non-goals:** logging payloads/SQL, exporters/dashboards, resource/user dimensions by default, or absolute allocation/latency promises.

## Decisions

### BCL instruments and bounded vocabulary

Use ActivitySource and Meter named `Kkts.DataAccess` with versioned documented instrumentation scope. Stage activities are `kda.policy.capture`, `kda.policy.bind`, `kda.entity.execute`, and `kda.dto.map`; root read activities use bounded operation names. Metrics include operation/stage duration histograms in seconds, operation/export counters, cache lookup/build/eviction counters, and returned-row counters only from already-known bounded result counts.

Tags are only operation, stage, provider family (sqlite/sqlserver/mysql/custom), outcome, cache kind, and catalog error classification. Do not tag resource/schema/field/scope/version identities. Normalize external/custom codes to `unknown`/`infrastructure`; the catalog is finite. No `Exception.ToString`, exception events/messages, payload hashes, tokens, context, SQL, or connection information are added.

Listeners opt in; no OpenTelemetry dependency is introduced. Use standard enabled/listener checks and monotonic timing; build tags only for enabled instruments. The contract does not guarantee zero overhead or suppress listener exceptions through broad catches. Standard listener callback safety is the host's responsibility; test valid listeners for behavior parity.

### Instrumentation boundaries

Core instruments capture/binding/mapping once, including stamped capture and async context when DI is present. Repository dispatch owns query-stage timing so custom adapters receive coverage; EF adds only nonduplicative snapshot/setup detail, never a second count of the same query.

Cache hooks report finite hit/miss/build/failure/eviction outcomes outside locks where possible; no callbacks run under cache locks. Do not use observable callbacks retaining each compiler/request. No global unbounded registry is added merely to report aggregate cache size.

### Export context and terminal state

Start an export operation on first MoveNext, not enumerable creation. Hold explicit parent ActivityContext for traversal but restore Activity.Current before yields; create batch/stage activities with explicit parent so unrelated consumer work is not accidentally parented by a live export scope.

A small iterator-local terminal state records exactly one reason: exhausted, row_cap, policy_changed, duration_exceeded, canceled, failed, or disposed_early. A short batch or reached row allowance records a candidate success reason, committed only when enumeration naturally completes; cancellation/deadline on resumed advancement can still replace that candidate. Disposal while suspended reports early disposal, not premature success. Abandonment is not invented completion.

Capture final events/metrics in ownership cleanup without extra queries or re-enumeration. Exception types/codes determine terminal classification; do not parse messages.

### Verification and documentation

ActivityListener/MeterListener tests enumerate every operation and termination path, verify counts/tag vocabularies/units, nested parenting, and behavior with no listener. Put unique sentinel strings in requests, policy/context, provider exceptions, and protected token contents; search all tags/events/status descriptions for disclosure. High-cardinality resource/custom provider fixtures must still produce a finite set of series.

## Risks / Trade-offs

- [Ambient activity leaks across async yield] -> Explicit parent context and restoration tests with unrelated consumer activities.
- [Telemetry becomes new retained state] -> No request closures/registry or cache callbacks under locks.
- [Host exporter adds its own sensitive enrichment] -> Document library-only privacy boundary; do not claim control over arbitrary host enrichment.

## Migration Plan

Apply structured errors and bounded compiler cache first. Add instruments and core/cache/export hooks together with listener tests; cover optional EF/async paths without requiring DI. Update instrumentation docs and example opt-in snippet, preserving all results/query counts.
