# Authorized reads

`AuthorizedReads<T>` accepts a schema, compiler, policy store, EF Core database factory, server-derived context factory and administrative scope. `ListAsync`, `CountAsync`, `DetailAsync` and `ExportAsync` own policy capture/validation/execution/mapping. Each operation captures once and binds internally to Read; clients cannot supply an action, executable expression, or authorization identity. Results contain materialized JSON DTOs and captured-policy metadata.

| Operation | Request | Supported inputs |
| --- | --- | --- |
| List | `ListReadRequest` | Selection, Filter, Sort, Offset (default 0), Limit (default 50), IncludeTotal (default true), IncludeHasMore (default false) |
| Cursor list | `CursorListReadRequest` | Selection, Filter, Sort, Limit (default 50), ContinuationToken, IncludeTotal (default true), IncludeHasMore (default false) |
| Count | `CountReadRequest` | Filter only |
| Detail | `DetailReadRequest` | Required JSON scalar Key, Selection, Filter |
| Export | `ExportReadRequest` | Selection, Filter, Sort |

Each request uses type-local strict JSON deserialization: unknown members, action/identity overrides, and operation-inapplicable inputs are rejected. Count rejects selection/sort/pagination; detail rejects sort/pagination; export rejects client pagination/batch overrides. Count/detail/export also reject list metadata flags. Those flags accept only JSON booleans, not null, numbers, or strings. The example returns HTTP 400 for malformed payloads and HTTP 403 for forbidden query fields.

Omitted selection uses effective Select fields; [] yields empty DTOs. Explicit unknown/forbidden fields fail. Filtering/sorting require independent grants. Every nested client leaf is checked; context references and navigation/property paths absent from the schema are forbidden. Writes/aggregation and non-Read actions are unsupported even with permission metadata.

Validation precedes opening the entity database and checks only applicable inputs. Count needs no Select or Sort grants; only list validates client pagination. Mandatory/Allow/Deny predicates precede client filters. Lists return an exact total before pagination only when requested; standalone count returns `Task<ReadCount>` with exact `Total` and the operation's captured `PolicyVersion`. Detail adds the host stable-key predicate without requiring a client Filter grant, but explicit client key filters still require that grant. Missing, null, or incompatible detail keys fail before entity access; excluded/nonexistent records both produce null (404 in the example). Ordering appends the host-declared unique stable key; hosts must enforce uniqueness. Invalid/missing stable-key registrations fail.

## Optional list metadata and versioned counts

`ReadPage` and `CursorReadPage` expose nullable `Total` and `HasMore`.
Unrequested metadata is explicitly JSON null, even with ignore-null/default
serializer options. An exact total of zero and a requested false has-more are
values, not missing metadata. Defaults retain exact-total behavior.

| IncludeTotal | IncludeHasMore | Count calls | Offset fetch bound | Total | HasMore |
| --- | --- | --- | --- | --- | --- |
| false | false | 0 | Limit | null | null |
| true | false | 1 | Limit | exact | null |
| false | true | 0 | Limit + 1 | null | observed boolean |
| true | true | 1 | Limit + 1 | exact | observed boolean |

Has-more is observed from the one extra authorized row, never inferred from
total arithmetic or a separate existence query. Lookahead is trimmed before DTO
mapping; returned rows never exceed the host page limit. An unrepresentable
lookahead bound fails before entity access. Disabled totals execute no count,
including when has-more is requested. Enabled count failures propagate.

```csharp
var page = await reads.ListAsync(new ListReadRequest(
    Selection: ["Name"], Limit: 20, IncludeTotal: false, IncludeHasMore: true));
var count = await reads.CountAsync(new CountReadRequest(Filter: filter));
long exactTotal = count.Total; // Not the former bare long response.
long capturedVersion = count.PolicyVersion;
```

The count endpoint now returns `{"policyVersion":1,"total":2}`, not `2`.
Totals and page observations are independent database statements, not a snapshot;
they can disagree under concurrent changes without either being fabricated.

`AuthorizedReadRepository<T>` resolves each `SortField` by its exact public alias
and checks its effective Sort grant before opening an entity session. It supplies
registered property bindings and directions through the unchanged
`EntityReadPlan<T>`/`EntitySort` contract. The shared EF adapter applies
Kkts.Expressions 3.1.0 structured `OrderByInfo` ordering to the authorized query
before pagination, preserving multi-key order and directions. Repeated sort keys
remain valid; the stable key is appended only when not explicitly present and
does not acquire output, Filter, or Sort privileges. Existing constructor call patterns remain supported, but custom retrieval
adapters must supply projected capabilities or explicitly allow entity
materialization; clients still send `SortField`, not
raw properties, ordering strings, or executable expressions.

ReadLimits defaults: max page 200, max offset 1,000,000, export batch 100. Export validates selection/filter/sort, traverses matching rows from the beginning using private keyset boundaries subject to host lifecycle settings, and retains one policy/version across host-bounded batches. Count/detail/export have no synthetic default list limit, so a smaller host page maximum does not block them (the configured export batch must still fit that maximum). Cancellation flows through capture and queries.

## Breaking request migration

The shared `ReadRequest` and old method overloads are removed before release. Constructors and trusted adapter/session methods are unchanged. List metadata is now nullable and standalone count returns `ReadCount`; detail/export results are unchanged. Replace shared requests at each call site:

```csharp
await reads.ListAsync(new ListReadRequest(Selection: ["Name"], Limit: 20));
await reads.CountAsync(new CountReadRequest(Filter: filter)); // No selection/sort/limit.
await reads.DetailAsync(new DetailReadRequest(
    JsonSerializer.SerializeToElement(id), Selection: ["Name"], Filter: filter));
await foreach (var batch in reads.ExportAsync(
    new ExportReadRequest(Selection: ["Name"], Filter: filter, Sort: [new("Name")])))
{
    // Consume permitted JSON records.
}
```

Detail's key moves from the separate method argument into `DetailReadRequest.Key`.
Remove old `action` members from every payload and list-only options from count,
detail, and export; there are no compatibility aliases that ignore them.

The shared repository supports SQLite, SQL Server and MySQL entity adapters with SQLite policy storage; see [provider support](providers.md). The SQLite provider baseline is EF Core 10.0.12. Translation/database errors propagate with no unrestricted or local-authorization fallback. Provider restrictions (including some decimal/DateTimeOffset ordering) fail explicitly. Retrieval requires scalar database projection by default: only selected mapped values enter output buffers, plus minimal cursor/export continuation values or a constant marker for empty nonseek output. Kkts.Dynamic-generated DTO shapes map those checked scalars without constructing entities or evaluating entity getters.

Core and SQLite facade append matching host-only `ReadProjectionMode.Required`
defaults. Existing construction syntax remains usable with supplied EF adapters.
Custom adapters must implement projected page/single and projected seek as
applicable, or explicitly choose `AllowEntityMaterialization`. Compatibility uses
entity retrieval only when the relevant projected capability is absent; a
projected failure never retries entity loading. Standalone count remains an
aggregate and needs no projected capability. See [adapter migration](repositories.md).

Counts/pages are separate database statements; default best-effort exports hold no transaction-wide entity snapshot. Concurrent entity changes may shift offset pages or totals. Keyset traversal avoids position shifts before fixed boundaries, but changes to sort values or authorized membership can still cause gaps/repeats. Stable ordering and captured authorization alone do not guarantee data snapshot consistency.

## Export lifecycle

The trailing host-only `export:` configuration selects independent policy/data
consistency; see the [four-mode matrix](repositories.md#trusted-export-lifecycle-configuration).
Defaults are `CapturedPolicy`, `BestEffort`, and no row/duration bounds.
Export JSON remains Selection/Filter/Sort only; lifecycle overrides are rejected.

Abort mode captures policy content and its activation stamp atomically once,
then verifies the original stamp before every batch query (including a terminal
empty query) and immediately before delivery after validation/mapping. It never
rebinds permissions/context. A failed check suppresses the pending batch and
propagates; an observed activation change raises `ExportPolicyChangedException`.
Draft edits do not change an activation. Publish followed by rollback does.
There is still a race between the final authoritative read and delivery: a
later commit cannot retract delivered rows or stop consumer work on a yielded
batch. These checkpoints are bounded cooperative revocation, not instantaneous
or atomic streamed delivery.

`MaxRows` caps delivered rows successfully. With batch size four and cap six,
queries request four then two, and no terminal probe/count runs when the cap is
reached. Empty-selection records still count. No count/truncation metadata is
added to `ExportBatch`; the host must label capped prefixes appropriately.

`MaxDuration` starts on the first `MoveNextAsync`, not enumerable construction,
and includes capture, checks, opening, queries, mapping and consumer pauses.
It uses monotonic timestamps and a `TimeProvider`-backed timer, not UTC.
Positive durations up to 4,294,967,294 milliseconds are supported; timers round
fractional milliseconds upward, while elapsed checks retain the exact bound.
Expiry cancels linked work and raises `ExportDurationExceededException`, never
successful completion, including when resuming after a capped/final short batch.
Caller cancellation takes precedence when both are observed. Unrelated failures
retain their original errors; late noncooperative returns cannot be delivered.

Best-effort sessions dispose before yielding. Snapshot sessions retain their
connection/transaction until completion, failure or enumerator disposal, so
pauses pin database snapshot resources. Timer/token state is per enumerator and
disposed with it. While suspended at yield, expiry can request cancellation but
cannot resume/dispose the iterator or retract consumer writes. Always use
`await foreach` or `await using` for the enumerator, including early stops.
An abandoned undisposed enumerator has no forced-cleanup guarantee.

EmployeeApp uses these trusted startup settings (environment variables use `__`
instead of `:`): `Export:PolicyConsistency`, `Export:DataConsistency`,
`Export:MaxRows`, `Export:MaxDuration` (`TimeSpan`, e.g. `00:05:00`). Defaults remain
unchanged; the provider helpers wire snapshot support, with prerequisites still
owned by the host. Settings never come from request JSON.

Before response start, its lifecycle middleware returns the existing safe JSON
error shape with HTTP 409 for policy change or 408 for elapsed duration. After
response start it logs the typed failure and aborts NDJSON, without an error row
or successful terminal fallback. The API retains its existing error format rather
than introducing unrelated Problem Details/OpenAPI/service-layer changes.
No full-export buffering is added. A previously streamed prefix cannot be
withdrawn; applications must treat abnormal stream termination as incomplete.

## Forward cursors

`CursorListReadRequest` accepts Selection/Filter/Sort, Limit (default fifty), and
an optional ContinuationToken and independent IncludeTotal/IncludeHasMore flags;
strict JSON rejects offset, action, identity, and
unknown inputs. `ListCursorAsync` returns `CursorReadPage` with PolicyVersion,
nullable Total for the authorized query **before** the boundary, permitted Records,
nullable HasMore, and nullable NextContinuationToken. Total defaults on and
has-more defaults off, matching offset metadata.
Cursor pagination always requests bounded Limit-plus-one lookahead, trims it
before mapping, and uses the last **returned** row as the next boundary. Empty,
short, or final pages have no next token; empty selection still produces `{}`.
Cursor fetches Limit + 1 in every metadata combination because token production
requires lookahead. Requested HasMore is true exactly when a next token is
produced; no second lookahead query is issued. Disabling total skips count only.
Both metadata flags may change on resume without invalidating a token; selection,
filter, sort, limit, authorization and host binding compatibility still apply.

Configure `CursorReadConfiguration` with an established authenticated-encryption
protector, a server-derived authenticated subject function, and an opaque dataset
label. Do not use a connection string as the label. The optional
[Data Protection integration](../src/Kkts.DataAccess.DataProtection/README.md)
provides protocol-purpose isolation and persistent-key continuity.

```csharp
var page = await reads.ListCursorAsync(new CursorListReadRequest(
    Selection: ["Name"], Sort: [new("Name")], Limit: 20));
if (page.NextContinuationToken is { } next)
{
    page = await reads.ListCursorAsync(new CursorListReadRequest(
        Selection: ["Name"], Sort: [new("Name")], Limit: 20,
        ContinuationToken: next, IncludeTotal: false, IncludeHasMore: true));
}
```

Keep the effective query/selection/order/limit, current policy, authenticated
subject/context and host dataset/schema bindings compatible on resume. Every
request recaptures current authorization; tokens do not grant permission or load
historic policies. A policy-version mismatch requires restarting without a token.
Rollback to the identical immutable version can make an otherwise unexpired
compatible token usable again; intervening publications are not an epoch check.
Malformed, expired, oversized, tampered, wrong-purpose or incompatible tokens fail
generically before entity access. No silent restart is performed.

Defaults are a fifteen-minute **non-sliding** traversal lifetime and 16-KiB encoded
token limit with a bounded decrypted payload. Hidden sort/stable-key values remain
internal or encrypted and never become DTO fields or client Filter permissions.
Projected seek obtains these values as minimal trusted sidecars; ordinary hidden
WHERE/ORDER BY dependencies are not selected solely for query evaluation. Sidecars
are separately validated and excluded from public JSON. Overlapping selected and
continuation properties share physical retrieval slots. Tokens use the unchanged
encrypted envelope and active-policy/version compatibility strategy.
Never log tokens, keys or context values. Hosts own persistent/shared key storage,
application isolation, at-rest protection and rotation; retain decryptable keys
through token lifetime for restart continuity. Change dataset identity when
routing, mappings, collation or seek semantics change.

Exports use captured policy/context and private typed boundaries, not public
resume tokens. They require seek-capable adapters and supported orderings but no
protector; unsupported seek cannot fall back to offsets. Saved boundaries survive
deletion of the boundary row. Inserts after a boundary can appear; neither cursor
pages nor exports promise a snapshot or exactly-once delivery for changing rows.

## DTO shape reuse

List, detail, and export mapping share a process-wide internal cache containing
at most 128 built DTO shapes across all entity types. Keys include entity type,
schema resource/version, and the exact ordered public-to-property bindings.
Equivalent shapes reuse a built type; different selection orders or property
bindings do not collide. Hits do not change FIFO insertion order. After eviction,
a shape can be built again.

Entity mapping and checked scalar mapping use the same generated DTO shape and
shape-local ordered setter metadata; there is no second projection/serializer
cache. Cache identity is independent of host materialization mode, policy and
caller. Both paths preserve the same alias/order/scalar/null JSON behavior.

Only mapping machinery is cached, never entities, grants, caller context, or
authorization decisions. Each request still captures and validates its policy
before accessing the entity database. Every row gets a fresh DTO. Empty selections
still yield `{}` without building a shape.

A cache miss builds a fresh Kkts.Dynamic assembly and disposes its builder after
retaining the built type. Shape-local default serialization options and a distinct
reflection resolver reuse JSON metadata without sharing the process-wide default
options cache. Construction
and publication are synchronized; mapping and serialization run outside the
cache lock. Failed builds propagate and are not cached or allowed to evict a
healthy entry.

Eviction releases cache-owned type/options references, not references held by
active calls or other runtime consumers. It does not invalidate active mapping
and does not guarantee immediate assembly collection or bound total process
memory. Capacity is fixed internally; there is no new host configuration or
public API.

### Mapper measurement

The opt-in xUnit measurement reproduces the previous uncached implementation,
checks identical JSON, and asserts zero builds during resident-hit loops. It
does not impose wall-clock thresholds on normal CI. Run from the repository root:

```sh
KKTS_DTO_PERFORMANCE=1 dotnet test tests/Kkts.DataAccess.Tests/Kkts.DataAccess.Tests.csproj \
  --no-restore -c Release \
  --filter 'FullyQualifiedName~DtoMapperPerformanceTests|FullyQualifiedName~DtoShapeCacheTests|FullyQualifiedName~DtoMapperTests|FullyQualifiedName~DependencyTests' \
  --logger 'console;verbosity=detailed'
```

Measured on 2026-10-12 with SDK 10.0.401, .NET 10.0.12, macOS 27.0.1/Arm64,
using two selected fields (`id`, `displayName`), 20 warm-up calls per implementation,
and five repetitions of 100 calls per batch size. Values are medians per call;
allocated bytes use `GC.GetAllocatedBytesForCurrentThread`.

| Rows | Uncached ms | Resident ms | Uncached bytes | Resident bytes |
|------|-------------|-------------|----------------|----------------|
| 1 | 0.433140 | 0.001377 | 63,965 | 616 |
| 100 | 0.529621 | 0.092440 | 89,599 | 27,544 |

Both resident workloads built zero types during timing. Five separately measured
cold misses had medians of 0.450600 ms/63,352 bytes for one row and
0.661100 ms/92,304 bytes for 100 rows. Eight concurrent distinct misses built eight
entries; per-call median latency including lock wait was 1.266600 ms, maximum
1.841700 ms, and median thread-local allocation was 81,288 bytes.

These are mapper-only measurements, not database or end-to-end throughput claims.
Cold construction, contention, workload shape, cache churn, and machine/runtime
conditions can change the results. The cache substantially reduced warm time
and allocations in both measured batch sizes; cold misses still pay type emission
and metadata initialization costs.

HTTP calls: GET /employees and /employees/{id}; POST /employees/list, /employees/cursor, /employees/count, /employees/export. POST requires an authenticated cookie and a current antiforgery token in RequestVerificationToken; obtain a fresh token from /login after sign-in. List JSON: `{"selection":["Name","Email"],"sort":[{"field":"Name"}],"limit":20}`. Unfiltered count JSON: `{}`; filtered count: `{"filter":{"field":"Status","operator":"eq","literal":"Active"}}`. Export JSON: `{"selection":["Name","Email"],"sort":[{"field":"Name"}]}` without pagination; it returns newline-delimited DTO JSON. GET detail constructs its request from the route id. ReadTests and the HTTP acceptance script exercise these operations. Hosts must route every supported data endpoint through the facade.
