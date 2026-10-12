# Authorized reads

AuthorizedReads<T> accepts a schema, compiler, policy store, EF Core database factory, server-derived context factory and administrative scope. ListAsync, CountAsync, DetailAsync and ExportAsync own policy capture/validation/execution/mapping. ReadRequest contains selection, structured filter, sort, offset/limit and action, with no executable expression or authorization identity. Results expose materialized JSON DTOs and counts/version ids.

Omitted selection uses effective Select fields; [] yields empty DTOs. Explicit unknown/forbidden fields fail. Filtering/sorting require independent grants. Every nested client leaf is checked; context references and navigation/property paths absent from the schema are forbidden. Writes/aggregation and non-Read actions are unsupported even with permission metadata.

Validation precedes opening the entity database. Mandatory/Allow/Deny predicates precede client filters. Counts run before pagination. Detail adds the host stable-key predicate without granting client privileges; excluded/nonexistent records both produce null (404 in the example). Ordering appends the host-declared unique stable key; hosts must enforce uniqueness. Invalid/missing keys prevent registration.

ReadLimits defaults: max page 200, max offset 1,000,000, export batch 100. Export validates requests but covers all matching rows from offset zero and retains one policy/version across bounded batches. Cancellation flows through capture and queries.

The shared repository supports SQLite, SQL Server and MySQL entity adapters with SQLite policy storage; see [provider support](providers.md). The SQLite provider baseline is EF Core 10.0.12. Translation/database errors propagate with no unrestricted or local-authorization fallback. Provider restrictions (including some decimal/DateTimeOffset ordering) fail explicitly. Hidden columns can be materialized inside the trusted facade; Phase 1 does not promise column projection. Kkts.Dynamic maps only permitted scalar public bindings in OneWayToDto mode, then serializes before returning results.

Counts/pages are separate database statements; export batches hold no transaction-wide entity snapshot. Concurrent entity changes may shift offsets or totals. Stable ordering and captured authorization do not guarantee data snapshot consistency.

## DTO shape reuse

List, detail, and export mapping share a process-wide internal cache containing
at most 128 built DTO shapes across all entity types. Keys include entity type,
schema resource/version, and the exact ordered public-to-property bindings.
Equivalent shapes reuse a built type; different selection orders or property
bindings do not collide. Hits do not change FIFO insertion order. After eviction,
a shape can be built again.

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

HTTP calls: GET /employees and /employees/{id}; POST /employees/list, /employees/count, /employees/export. POST requires an authenticated cookie and a current antiforgery token in RequestVerificationToken; obtain a fresh token from /login after sign-in. Example JSON: {"selection":["Name","Email"],"sort":[{"field":"Name"}],"limit":20}. Export returns newline-delimited DTO JSON. ReadTests and the HTTP acceptance script exercise these operations. Hosts must route every supported data endpoint through the facade.
