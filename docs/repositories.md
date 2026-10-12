# Authorized read repositories

`IAuthorizedReadRepository<T>` is the client-facing read-only contract. `AuthorizedReadRepository<T>` in core owns policy capture, validation, predicate composition and permitted DTO mapping. Core references neither EF Core nor a database driver. Policy storage remains `SqlitePolicyStore`, which implements `IActivePolicySource`.

Trusted host code supplies an `IEntityReadAdapter<T>`. Its async sessions receive immutable `EntityReadPlan<T>` values built by the repository; plans include the complete authorized predicate, resolved sort properties including the stable key, and ordered authorized `OutputBindings`. Filter/sort-only dependencies are not output bindings. Execute the complete predicate in the provider before counting or paging. Never replace it, return a query handle to clients, or evaluate authorization locally after fetching unrestricted records. Only permitted JSON DTOs leave the facade.

An adapter implements `OpenAsync`; its session implements `CountAsync`, `PageAsync`, `SingleAsync` and `DisposeAsync`. All methods accept cancellation. The repository owns sessions, including failures and stopped enumeration. Default exports capture one policy version but do not promise a data snapshot. See the standalone adapter example in `examples/CustomReadAdapter` for a core-only implementation surface.

Cursor lists and exports require projected seek by default.
`SeekProjectedAsync(EntitySeekPlan<T>, limit, cancellation)` executes the complete
protected read plan and strict lexicographic boundary in the provider, with a
bounded result and no offset substitute. The boundary is an immutable snapshot
of typed scalar values in full effective sort order, including hidden stable
keys. Internal comparisons do not grant client Filter/Select permissions.
Implement provider-compatible equality, direction, precision, collation and
null-first ascending/null-last descending ordering. Do not evaluate authorization
after unrestricted fetching. `CustomAdapter<T>.ForSeek` shows registration without
EF or ASP.NET dependencies. `CustomAdapter<T>.ForProjectedSeek` accepts projected
seek sessions without requiring any full-entity seek method.

**Breaking projection migration:** core and SQLite facade append the host-only
`ReadProjectionMode` configuration, defaulting to `Required`. Offset/detail require
`IEntityProjectedReadSession<T>`; cursor/export require
`IEntityProjectedSeekReadSession<T>`. The latter inherits projected page/single
and base entity/count methods, **not** `IEntitySeekReadSession<T>`.
Standalone count needs only the original aggregate method. Missing required
capability disposes the session and fails before queries
(including a cursor count); unsupported translation propagates, never falling
back to offsets. Export captures the request, policy and context once and advances
private boundaries before yielding each host-bounded batch. It exposes no token
and needs no cursor protector.

Implement `PageProjectedAsync`, `SingleProjectedAsync` and, for cursor/export,
`SeekProjectedAsync` using only the repository's validated property bindings.
Return `ProjectedReadRow` scalar snapshots in exact logical output order;
page/detail have no sidecars. Seek returns the complete ordered
`ContinuationBindings` tuple separately, preserving repeated logical sort keys.
Physical retrieval may deduplicate overlapping aliases/output/continuation slots.
Hidden predicate/ordinary sort dependencies must stay out of the reader unless
they are required continuation values. No client supplies executable projection
expressions.

The repository checks the whole bounded page/batch for exact cardinality and
scalar CLR/null compatibility before mapping output. Nullable values retain
ordinary CLR boxing; coercion, missing-value padding and partial failed batches
are forbidden. Current registered scalars are immutable value types or strings;
collections are not supported scalar values. Infrastructure row construction
also defensively copies collection containers and array values. Continuation
properties are excluded from JSON and must never be exposed as response objects.
Only selected values enter DTO mapping; no partial entity construction is used.

Legacy custom adapters can explicitly opt into
`ReadProjectionMode.AllowEntityMaterialization` through the trailing constructor
argument. Offset/detail then use entity methods only if projected page/single is
absent; cursor/export use entity seek only if projected seek is absent and entity
seek is present. Capable adapters still project in this mode. It accepts hidden
entity data entering trusted memory, not a retry policy: translation,
materialization, malformed-row and mapping errors always propagate without entity
fallback. Unknown enum values fail construction. Clients cannot choose the mode.

```csharp
var repository = new AuthorizedReadRepository<Employee>(
    schema, compiler, policyStore, legacyAdapter, trustedContext, scope,
    projectionMode: ReadProjectionMode.AllowEntityMaterialization);
```

Core and SQLite facade expose matching `ListCursorAsync(CursorListReadRequest)`
and a trailing optional `CursorReadConfiguration` constructor argument.
Construction and existing noncursor calls remain usable without public-token
configuration. Cursor-enabled hosts supply a protector, stable authenticated
subject function and opaque dataset label; see [cursor setup](reads.md#forward-cursors).

EF hosts use `EfEntityReadAdapter<T>` with an owned DbContext factory and expected provider identity, or provider helpers `SqliteEntityReads.Create<T>`, `SqlServerEntityReads.Create<T>` and `MySqlEntityReads.Create<T>`. A mismatched context provider fails before querying and is disposed. Provider translation failures propagate without fallback. Hosts own mappings, connection configuration, database schema, collation and migrations.

**Breaking provider registration migration:** supplied helper overloads require
`schema` before the owned context factory and accept a `ReadOperations` mask.
Synchronous delegate registration validates immediately; asynchronous factories
use hosted validation, explicit `ValidateAsync`, or `CreateAsync`. Low-level
`new EfEntityReadAdapter<T>(factory, providerName)` remains runtime-only with no
startup profile claim. See [provider registration](providers.md).

Optional core seams `IReadProviderCapabilitySource` and
`IEntityReadPlanValidator<T>` let trusted custom adapters expose immutable metadata
and validate the complete protected plan before count/retrieval, including seek
output and continuation bindings. Neither is required for neutral custom adapters;
omitting them is not a claim of validated provider support. Do not derive profiles
from request identity or sample data. A schema-aware EF session rechecks its model
against compiler targets and validates captured policy rules even if publication
was neutral; failures cannot trigger unrestricted, local or offset retries.
See [capability validation](provider-capabilities.md).

For snapshot exports, use the provider helpers (or the SQLite facade). They wire
trusted provider-owned `EfSnapshotFactory` hooks into shared EF ownership without
adding driver dependencies to the shared package. Direct EF registrations need a
factory to opt in; their default exports are unaffected. A factory returns an
`EfSnapshotTransaction` and can verify transaction liveness before/after each read.
Shared EF rejects existing connections/transactions, retry strategies, views,
query filters, inheritance and shared/multiple-table mappings. Runtime source
checks further restrict snapshots to verified ordinary tables. Initialization is
single-use, and snapshot loss never opens another connection or replays output.

Existing `Kkts.DataAccess.Sqlite.AuthorizedReads<T>` construction remains supported and delegates to the same repository with the SQLite executor. Read method signatures intentionally break before release: core, `IAuthorizedReadRepository<T>`, and the facade accept `ListReadRequest`, `CountReadRequest`, `DetailReadRequest`, and `ExportReadRequest` respectively. The shared request and old overloads are removed. Detail's required key now belongs to its request; count accepts only Filter and export accepts Selection/Filter/Sort without client pagination. All requests reject unmapped JSON members and bind to Read internally. Limits and detail/export result shapes are unchanged. See [read migration examples](reads.md#breaking-request-migration).

Both list requests append independent `IncludeTotal=true` and
`IncludeHasMore=false`. Results expose nullable `Total` and `HasMore` with explicit
JSON null for disabled metadata. Count is called once only when enabled. Offset
has-more uses a single bounded extra authorized row; cursor reuses its mandatory
token lookahead. Neither uses total arithmetic. Metadata flags may change across
cursor pages without changing row-query identity. See the
[execution matrix](reads.md#optional-list-metadata-and-versioned-counts).

Public `CountAsync` returns `Task<ReadCount>`, including the captured
`PolicyVersion` and exact `Total`; migrate numeric consumers to `result.Total`.
The trusted session method remains `Task<long>` and needs no new capability.
Publication during a count does not relabel its captured version. No count,
detail, or export input accepts list metadata flags. Counts/pages have no
transaction-wide snapshot; failures/cancellation retain ordinary disposal and
never become partial-success metadata.

Existing SQLite configuration and trusted adapter/session contracts stay valid. Entity and policy databases can be separate: pass the SQLite policy store to the core repository and an adapter for the independently configured entity DbContext. No policy table migration is needed.

See [provider support and acceptance](providers.md) for dependency versions, verified behavior and real database setup.

## Trusted export lifecycle configuration

Core and the SQLite facade accept a trailing optional `ExportReadConfiguration`
(`export:`). It is immutable host configuration, not an export request member.
Selection/filter/sort and `ExportBatch(PolicyVersion, Records)` remain unchanged.

| Policy consistency | Data consistency | Behavior |
|---|---|---|
| CapturedPolicy (default) | BestEffort (default) | Original policy, fresh bounded entity reads |
| AbortOnPolicyChange | BestEffort | Original policy, authoritative activation checks |
| CapturedPolicy | Snapshot | Original policy and one entity snapshot |
| AbortOnPolicyChange | Snapshot | One entity snapshot, independent fresh policy checks |

`MaxRows` is an optional positive delivered-row cap; capped completion is a
successful prefix, not proof of exhaustion. No total/truncation field or count
query is added. `MaxDuration` is an optional positive elapsed deadline, using the
configured `TimeProvider` (system by default). Batch size remains
`ReadLimits.ExportBatchSize`. Clients cannot override these settings through JSON.

Abort mode requires trusted `IActivationAwarePolicySource`: atomic content/stamp
capture and fresh scope/resource activation reads. Positive activation revisions
are distinct from immutable versions and must advance durably on every activation,
including rollback to the original version. Custom captured-only sources remain
valid for default exports. Never derive activation checks from cached captures or
an entity snapshot.

Snapshot mode independently requires `IEntitySnapshotReadSession<T>` on an owned
session, plus projected seek (or the explicit entity compatibility mode). Snapshot
support does not imply full-entity seek support. `BeginSnapshotAsync` owns one
transaction until session disposal, rejects repeated initialization and incompatible
transactions/retry settings, and must never reopen/replay after snapshot loss.
Unsupported capabilities fail explicitly before delivery, without fallback.

Best-effort batch acquisition validates scalar cardinality/types, snapshots the
next boundary and maps DTOs while the session is owned, then disposes it before
yield. Snapshot sessions remain owned across yields. Consumers must dispose
enumerators even when stopping early; an elapsed deadline cannot force an
abandoned iterator to resume and release its transaction.

Duration starts at first advancement and includes consumer pauses. Use monotonic
`TimeProvider` timestamps and timer support (positive bounds no larger than
4,294,967,294 milliseconds); a test clock must advance both timestamps and timers,
not UTC alone. Cancellation reaches capture, checks, opening, snapshot setup and
queries. Caller cancellation wins over simultaneous expiry. Cleanup never uses
the canceled operation token, and no late unchecked batch is delivered.
