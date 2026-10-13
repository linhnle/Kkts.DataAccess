# Structured failures

Library failures expose `DataAccessException.Code`, `Category`, and an owned,
read-only `Diagnostics` collection (at most 256 entries). Messages are explanatory
text, not a compatibility contract. Never classify errors by parsing messages.
Infrastructure exceptions and caller cancellation propagate unchanged. Host
argument/options errors remain configuration errors rather than invalid requests.

| Code | Category | Exception | HTTP status |
| --- | --- | --- | --- |
| `invalid_request` | InvalidRequest | InvalidRequestException | 400 |
| `access_denied` | AccessDenied | AuthorizationException | 403 |
| `policy_missing` | PolicyMissing | PolicyMissingException | 503 |
| `schema_incompatible` | SchemaIncompatible | SchemaIncompatibleException | 409 |
| `revision_conflict` | RevisionConflict | RevisionConflictException / PolicyConflictException | 409 |
| `provider_unsupported` | ProviderUnsupported | ProviderUnsupportedException | 422 |
| `invalid_continuation` | InvalidContinuation | InvalidContinuationTokenException | 400 |
| `export_policy_changed` | ExportPolicyChanged | ExportPolicyChangedException | 403 |
| `export_duration_exceeded` | ExportDurationExceeded | ExportDurationExceededException | 504 |

Validation diagnostic subcodes are `invalid_condition`, `invalid_literal`,
`unknown_field`, `invalid_operator`, `invalid_context_reference`, `invalid_subject`,
`invalid_entry`, `complexity_exceeded`, and `conditional_fields_unsupported`.
Diagnostics can also use a catalog code such as `schema_incompatible`.
The pinned Expressions dependency codes `unknown-property`,
`operator-not-applicable`, and `incompatible-operand` are preserved when reported
by the expression bridge. Unknown dependency codes map to `invalid_condition`;
dependency messages and alias names are not forwarded.
Codes serialize as the snake-case names above, including when using
`PolicyJson.Options`. Categories serialize as enum names.

## Paths and migration

Paths are RFC 6901 JSON Pointers: `""` identifies the input root. For example,
`/rules/0/condition/all/1/not/literal` identifies a policy literal, and
`/filter/all/1/operator` identifies a client filter operator.
`JsonPointer.Append` escapes `~` as `~0` and `/` as `~1` in each segment.
Selection, sort, and key errors use `/selection/0`, `/sort/0/field`, and `/key`.
`PolicyValidator.DemandCondition(condition, inputRoot)` accepts the actual input
root, rather than exposing synthetic policy paths to request callers.

This pre-release change intentionally replaces `Diagnostic(Path, Message)` with
`Diagnostic(Code, Path, Message)` and dotted/index paths with JSON Pointers.
Invalid input no longer throws `AuthorizationException`; update catches to
`InvalidRequestException` or inspect `DataAccessException.Code`. Missing policy,
schema conflict, and provider limitations are distinct typed failures.
Export duration exceptions now derive from `DataAccessException`, not
`TimeoutException`; cursor and export exception names are preserved.

Diagnostics never include literals, trusted context values, SQL, tokens, or
record values. Administrative validation paths must only be returned after
administrator authorization. Protected detail remains null for both absent and
inaccessible records. Inner exceptions are for internal investigation, not
automatic public serialization.

## Optional ASP.NET Core integration

Reference `Kkts.DataAccess.AspNetCore` only in HTTP hosts:

```csharp
using Kkts.DataAccess.AspNetCore;

builder.Services.AddDataAccessProblemDetails();
var app = builder.Build();
app.UseDataAccessProblemDetails(); // Before endpoints and streaming work.
```

Registration enables strict minimal-API bad-request exceptions, allowing
malformed JSON and unknown transport members to use the same safe 400 shape.
Core itself has no ASP.NET or HTTP dependency. The middleware logs exceptions
internally and emits `application/problem+json` with `type`, `title`, `status`,
`code`, and an opaque `traceId`. Invalid requests also have a bounded
`diagnostics` array with `code`, `path`, and a generic safe `message`.

```json
{
  "type": "about:blank",
  "title": "Invalid request",
  "status": 400,
  "code": "invalid_request",
  "traceId": "5ecfba97e1aa4554828ff61d96a45b32f",
  "diagnostics": [
    {"code": "invalid_operator", "path": "/filter/all/1/operator", "message": "The input is invalid."}
  ]
}
```

Unexpected exceptions use 500/`internal_error`, without exception messages or
diagnostics. Configuration errors thrown during startup still stop startup;
an unexpected programmer error during a request is not mislabeled as client
input. Caller cancellation is propagated without inventing a response body.
Any failure after response start aborts the connection, never appending Problem
Details to NDJSON. A delivered prefix is not retractable; consumers must treat
abnormal stream termination as incomplete. No full-export buffering is added.

The EmployeeApp designer consumes codes and JSON Pointer paths instead of an
`error` prose field. Existing clients must migrate from `{error: message}` and
the former export 409/408 mapping to the table above.
