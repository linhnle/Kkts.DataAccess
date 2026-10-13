# Read diagnostics and metrics

Core uses only BCL `System.Diagnostics` and `System.Diagnostics.Metrics`.
No exporter, logging package, DI registration, or listener is required.
The activity source and meter are both `Kkts.DataAccess`, scope version `1.0.0`;
use `ReadDiagnostics.Name` and `ReadDiagnostics.Version` when configuring hosts.
Instrumentation does not promise zero overhead.

## Opt in

The following BCL-only setup also compiles in the listener tests. Keep listeners
alive for the period being observed and dispose them on host shutdown:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Kkts.DataAccess;

using var activities = new ActivityListener
{
    ShouldListenTo = source => source.Name == ReadDiagnostics.Name,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
        ActivitySamplingResult.AllData,
    ActivityStopped = activity => Console.WriteLine(activity.OperationName),
};
ActivitySource.AddActivityListener(activities);

using var metrics = new MeterListener();
metrics.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == ReadDiagnostics.Name)
        listener.EnableMeasurementEvents(instrument);
};
metrics.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
    Console.WriteLine($"{instrument.Name}: {value}"));
metrics.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
    Console.WriteLine($"{instrument.Name}: {value}"));
metrics.Start();
```

Hosts may instead subscribe with their preferred telemetry SDK. Listener
callbacks must be thread-safe and must not throw. Core does not swallow listener
exceptions; exporter configuration, additional enrichment, and callback safety
are the host's responsibility.

## Activities and instruments

Root activities are `kda.read.list`, `kda.read.count`, `kda.read.detail`,
`kda.read.cursor`, and `kda.read.export`. Child stages use explicit root parenting:
`kda.policy.capture`, `kda.policy.bind`, `kda.entity.execute`, and `kda.dto.map`.
Capture includes asynchronous trusted context and ordinary or stamped policy
capture. Binding measures compiler binding; mapping measures DTO work including
shape lookup. Direct `DtoMapper` calls use operation `standalone`.

Entity execution is observed at repository dispatch, including custom adapters:
one stage for each actual count/page/detail/seek call. An optional list total
adds a count stage only when enabled. Session creation, preflight, snapshot setup,
and activation checks are not additional entity-query observations. Adapter
internals and provider-generated SQL are outside this instrumentation's scope.

| Instrument | Kind | Unit | Tags |
| --- | --- | --- | --- |
| `kda.operation.duration` | double histogram | `s` | operation, provider, outcome, code |
| `kda.stage.duration` | double histogram | `s` | operation, provider, stage, outcome, code |
| `kda.operations` | long counter | `{operation}` | operation, provider, outcome, code |
| `kda.exports` | long counter | `{export}` | operation, provider, outcome, code |
| `kda.rows.returned` | long counter | `{row}` | operation, provider, outcome, code |
| `kda.cache.lookups` | long counter | `{lookup}` | cache, outcome |
| `kda.cache.builds` | long counter | `{build}` | cache, outcome, code |
| `kda.cache.evictions` | long counter | `{eviction}` | cache, outcome |

Durations use monotonic elapsed time. Operation duration includes owned cleanup;
export duration includes consumer pauses. Operation/export counters describe
terminal results, not starts. Rows count already-known returned DTO counts,
including delivered export batches even if a later batch fails. They do not
include lookahead rows or a standalone count's database total, and do not cause
another query or output enumeration.

Both template and DTO caches record hit/miss per lookup, success/failed per
attempted build, and evicted per actual eviction. A resident immutable-content
conflict is a hit, not a new build. Failed builds preserve resident entries.
Callbacks run after cache locks are released. There is no observable callback,
retained compiler registry, per-request registry, or aggregate cache-size gauge.

## Finite tag vocabulary and privacy

Only these tag keys and classifications are emitted:

- `operation`: `list`, `count`, `detail`, `cursor`, `export`, `standalone`.
- `provider`: `sqlite`, `sqlserver`, `mysql`, `custom`. Exact lowercase names and
  the installed EF provider names `Microsoft.EntityFrameworkCore.Sqlite`,
  `Microsoft.EntityFrameworkCore.SqlServer`, and `MySql.EntityFrameworkCore`
  normalize to the first three; all other provider strings normalize to `custom`.
- `stage`: `capture`, `bind`, `execute`, `map`.
- `cache`: `template`, `dto`.
- `outcome`: `success`, `failed`, `canceled`, `exhausted`, `row_cap`,
  `policy_changed`, `duration_exceeded`, `disposed_early`, `hit`, `miss`, `evicted`
  (only the applicable subset occurs on each instrument).
- `code`: `none`, `canceled`, `infrastructure`, `unknown`, or a finite catalog
  classification: `invalid_request`, `access_denied`, `policy_missing`,
  `schema_incompatible`, `revision_conflict`, `provider_unsupported`,
  `invalid_continuation`, `export_policy_changed`, `export_duration_exceeded`,
  `invalid_condition`, `invalid_literal`, `unknown_field`, `invalid_operator`,
  `invalid_context_reference`, `invalid_subject`, `invalid_entry`,
  `complexity_exceeded`, `conditional_fields_unsupported`, `unknown-property`,
  `operator-not-applicable`, `incompatible-operand`.

Codes use the structured exception's top-level catalog code, not diagnostics,
inner messages, or arbitrary custom strings. Non-catalog exceptions normalize
to `infrastructure`; unrecognized catalog classifications normalize to `unknown`.
Failure activity status is `Error` with no description. No exception events
or free-form status descriptions are emitted.

Library tags/events never contain row/literal/context values, identities,
schema/resource names, field paths, SQL, connection strings, policy payloads,
tokens, versions, hashes, or exception messages. Library child activities do not
copy baggage into tags or events. Standard activity context and baggage can still
be inherited, and arbitrary host listeners/exporters may add sensitive data;
this privacy boundary covers library-emitted telemetry only, not host enrichment.

## Export lifecycle

No operation starts until the first `MoveNextAsync`. An unadvanced enumerable or
enumerator emits no operation. An export root is detached from ambient context;
stages parent explicitly to it. Consumer activities created between batches keep
the consumer's parent, not the suspended export root.

Every started, properly completed or disposed enumeration records one terminal
result after owned cleanup:

| Outcome | Meaning |
| --- | --- |
| `exhausted` | Traversal naturally completes after no rows or a short batch |
| `row_cap` | Natural resumed completion reaches the configured row allowance |
| `policy_changed` | Authoritative activation verification aborts traversal |
| `duration_exceeded` | The configured deadline aborts traversal |
| `canceled` | An operation-canceled exception terminates traversal |
| `failed` | Another execution/mapping/cleanup exception terminates traversal |
| `disposed_early` | The consumer disposes while suspended before natural completion |

A short batch or cap-reaching batch is not premature success: cancellation or
deadline expiry on resumed advancement still wins. Disposing at that pause is
early disposal, even if the last batch may have exhausted the allowance.
Cleanup failure replaces any candidate success with failure. Abandoned,
undisposed enumerators are not reported as successfully complete; consumers
must dispose to release snapshot ownership and finish the activity. No finalizer
or extra query attempts to invent an outcome.

## Verification

`ReadDiagnosticsTests` and diagnostics tests in `RepositoryTests` and
`ExportLifecycleTests` check listener/no-listener result and query parity,
asynchronous context capture, projected/entity/custom adapter paths, optional
totals, explicit parenting across yield, all export outcomes and cleanup,
bounded concurrent cache accounting outside locks, sentinel secrecy, finite
series, units, and BCL listener opt-in. Existing SQLite/read/lifecycle/cache
regressions remain part of acceptance.
