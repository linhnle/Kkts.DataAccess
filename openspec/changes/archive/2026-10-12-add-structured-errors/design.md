# Design

## Context

See [proposal.md](./proposal.md). `Diagnostic` currently has Path/Message; validator paths use dotted/index notation and lose `all`/`any` segments. `DemandValid` flattens messages into AuthorizationException. The expression bridge catches dependency diagnostics, and PolicyJson contains a message-dependent special case. EmployeeApp maps broad errors into `{error: message}`; export middleware already distinguishes pre-start versus streaming failure.

## Goals / Non-Goals

**Goals:** machine-readable contract across schema, policy, read, cursor/export, and HTTP paths; safe bounded diagnostics without weakening denial.

**Non-goals:** a generic database exception taxonomy, returning policy explanation to data callers, or classifying exceptions by their text.

## Decisions

### Core contract and migration

Introduce `DataAccessException` carrying a stable `DataAccessErrorCode` and read-only `Diagnostic(Code, Path, Message)` collection. Use named typed subclasses for invalid request, denial, missing policy, schema mismatch, conflict, and unsupported operation. Migrate existing cursor/export exceptions onto appropriate typed bases while preserving their recognizable names. Intentional pre-release exception/path changes are documented, not hidden behind dual inconsistent formats.

Use a finite code catalog including `invalid_request`, `access_denied`, `policy_missing`, `schema_incompatible`, `revision_conflict`, `provider_unsupported`, `invalid_continuation`, `export_policy_changed`, and `export_duration_exceeded`, with finite validation subcodes. Messages can change independently. Known host argument/options errors remain programmer/configuration errors; do not turn every ArgumentException into a client 400.

Produce diagnostics at their source, not by parsing flattened messages. Preserve dependency diagnostic codes when stable and map unknown dependency cases to a documented general invalid-condition code. Keep the exception inner cause for internal debugging, never automatic serialization. Remove PolicyJson's message-based conditional-field special handling in favor of structural inspection of that unsupported input shape.

### Paths and privacy

A shared JSON Pointer builder tracks actual `all`, `any`, `not`, condition, operand, selection, sort, and key locations; empty string means root and `~`/`/` segments escape correctly. `DemandCondition` accepts an input root so a client filter is not mislabeled as a synthetic policy rule. Administrative validation retains policy paths only for authorized administrators.

Default messages do not interpolate literals, context values, policy conditions, SQL, or token state. Requests get safe codes and locations, not available-field lists or provider internals. Failed protected detail still returns ordinary null/not-found. Aggregate multiple bounded validator diagnostics up to existing complexity limits; no unbounded error accumulation.

### Optional HTTP integration

Add `Kkts.DataAccess.AspNetCore` with ASP.NET framework reference and core reference. An exception handler/Problem Details mapper consumes typed codes; core has no HTTP status field/dependency. Map invalid input/cursor to 400, denial/policy-change to 403, schema/revision conflict to 409, unsupported request operation to 422, missing policy to 503, duration expiry to 504, and unexpected failure to generic 500. Deployment configuration failures still stop startup, not become 422 responses.

Responses contain safe title/status, stable `code`, safe input diagnostics, and an opaque trace identifier; never `Exception.Message` by default. Normalize malformed transport JSON without reflecting raw JSON fragments. Caller cancellation follows host cancellation handling. After response start abort the connection; do not write Problem Details into NDJSON. Replace overlapping example handlers and migrate designer/client consumption.

Alternative: expanding AuthorizationException messages leaves consumers parsing prose and still confuses missing policy, invalid input, and forbidden access.

## Risks / Trade-offs

- [Breaking exception/path consumers] -> Migrate tests, example designer, docs, and published catalog together.
- [Misclassifying provider failures] -> Wrap only explicit known capability checks; residual database/translation exceptions remain unexpected infrastructure failures.
- [Diagnostic disclosure] -> Sentinel tests over public responses/diagnostics and generic unexpected failures; internal causes are not serialized.

## Migration Plan

Implement core codes/path helpers first, then all library failure surfaces and validators, then optional HTTP package and EmployeeApp. Add golden code/path/status tests plus already-started stream tests. No data migration or implementation of later proposals is needed. Future code additions must use the catalog rather than invent unbounded codes or message parsing.
