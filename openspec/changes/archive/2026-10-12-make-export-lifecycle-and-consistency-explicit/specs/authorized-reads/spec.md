# Spec Delta

## ADDED Requirements

### Requirement: Independent host export consistency
Hosts SHALL configure export policy consistency independently from entity-data consistency. Policy consistency SHALL default to captured policy; the opt-in alternative SHALL abort on any policy activation change. Data consistency SHALL default to best-effort; the opt-in alternative SHALL require an adapter-supported export-wide snapshot. All four combinations SHALL preserve the initially captured authorization and trusted context for every emitted batch, without rebinding to later permissions. Snapshot data MUST NOT freeze policy-change checks. Clients MUST NOT select these modes or lifecycle limits through export requests.

#### Scenario: Default export behavior
- **WHEN** the host provides no export lifecycle configuration
- **THEN** export uses captured policy and best-effort entity reads without a row or duration cap

#### Scenario: Snapshot with abort policy
- **WHEN** a host selects snapshot data and abort-on-policy-change and publication commits while export is running
- **THEN** the active-policy check sees the authoritative change outside the entity snapshot and aborts rather than finishing with stale permissions

#### Scenario: Snapshot with captured policy
- **WHEN** a host selects snapshot data with captured-policy consistency
- **THEN** authorized output retains both the original policy and the entity snapshot despite later publication and entity mutations

#### Scenario: Host configuration cannot be overridden
- **WHEN** export JSON contains policy mode, data mode, maximum rows, or maximum duration
- **THEN** strict deserialization rejects it before entity execution

### Requirement: Abort export on policy activation change
Abort-on-policy-change exports SHALL atomically capture immutable policy content and its authoritative activation revision, and check that revision before each batch query and immediately before yielding each batch. Any committed activation change, including rollback to the original version between checks, SHALL cause an explicit policy-change error, suppress the pending batch, and stop further queries. Draft edits SHALL NOT count as activation changes. A policy-source capability failure, missing active policy, or failed revision read SHALL fail explicitly without delivering unchecked batches. The system MUST NOT substitute immutable version equality for activation revision equality or switch authorization mid-export. Already delivered records cannot be withdrawn, and a commit after the final pre-yield check can race delivery; this bounded guarantee SHALL be documented rather than claiming instantaneous revocation.

#### Scenario: Publication between batches
- **WHEN** publication commits after a batch is delivered and before the next query
- **THEN** the next advancement reports policy change without executing that batch query

#### Scenario: Publication during query
- **WHEN** publication commits during a batch query and is visible to its pre-yield check
- **THEN** the completed pending batch is not delivered and the export reports policy change

#### Scenario: Publish then rollback between checks
- **WHEN** another version is activated and the original immutable version is reactivated before the next check
- **THEN** the advanced activation revision still aborts the export

#### Scenario: Draft edit does not abort
- **WHEN** only an unpublished draft changes during export
- **THEN** activation checks still match and export continues with its captured policy

#### Scenario: Revision verification unavailable
- **WHEN** the configured source lacks authoritative revision capture/checking or a check fails
- **THEN** abort mode fails explicitly without downgrading to captured-policy consistency or returning the unchecked batch

### Requirement: Export row cap and elapsed deadline
Hosts SHALL be able to specify an optional positive maximum delivered row count and optional positive maximum elapsed duration. Invalid bounds SHALL fail as configuration errors. A row cap SHALL produce successful capped completion, fetch at most the remaining row allowance within each batch, and execute no count or extra query merely to determine whether more rows exist beyond the cap. Duration SHALL start on first enumeration advancement and include policy capture, entity work, DTO mapping, and consumer pauses. Expiry SHALL request cancellation of in-flight work and raise an explicit duration-limit error rather than successful completion, with no later batch delivery. Caller cancellation SHALL remain distinguishable from duration expiry. Deadline enforcement and resource release SHALL remain cooperative with adapters and async enumeration; consumers SHALL dispose the enumerator even when stopping early.

#### Scenario: Cap cuts a batch short
- **WHEN** batch size is four, maximum rows is six, and at least ten rows match
- **THEN** the export delivers batches of four and two with query bounds four and two, then completes without another entity query or count

#### Scenario: Cap exceeds available rows
- **WHEN** fewer authorized matching rows exist than the configured cap
- **THEN** export completes normally after those rows without inventing missing output

#### Scenario: Pause reaches deadline
- **WHEN** the consumer pauses between batches beyond the maximum duration
- **THEN** the next advancement reports duration expiry without executing another batch query

#### Scenario: Capture or query reaches deadline
- **WHEN** policy capture or a batch query exceeds the configured duration
- **THEN** its token is canceled and the operation reports a duration-limit error without delivering a pending batch

#### Scenario: Consumer cancellation
- **WHEN** the caller cancels export before deadline expiry
- **THEN** ordinary cancellation propagates and owned resources are released rather than reporting capped success

## MODIFIED Requirements

### Requirement: Consistent list detail count and export behavior
Each offset list, cursor list, detail, count, and export operation SHALL capture one Read policy version and enforce the same authorization composition while validating only the inputs supported by that operation. Lists SHALL validate selection, filter, sort, pagination, and supported metadata options; count SHALL validate only its filter; detail SHALL validate its key, selection, and filter; export SHALL validate selection, filter, and sort. Explicit field references SHALL require the corresponding independent field grants. Detail SHALL combine the requested key with the authorized predicate without requiring a client Filter grant for the implicit host-key lookup, and SHALL return the same not-found result for an inaccessible or nonexistent record. Standalone count SHALL return an exact total of authorized rows after client filters together with the captured policy version, not a bare integer. Lists SHALL return an exact total only when requested, counting before pagination and excluding a cursor boundary; otherwise total SHALL be explicit null. Exports SHALL honor authorized filters and selection and retain one policy version throughout execution, completing over all matching rows unless a host row cap, policy-change abort, duration limit, cancellation, or failure terminates enumeration. Detail and export batch result shapes SHALL remain unchanged; list metadata and standalone count result shapes SHALL migrate consistently across core, facade, and example HTTP surfaces. Metadata does not provide a database snapshot: has-more describes the page observation and an exact total can differ under concurrent data changes.

#### Scenario: Inaccessible detail
- **WHEN** a user requests an existing record outside their authorized set
- **THEN** the result is indistinguishable from a nonexistent record

#### Scenario: Count independent of page
- **WHEN** a filter matches twenty authorized rows and the list page size is five with exact total requested
- **THEN** the list returns at most five rows with total twenty, and a standalone count returns exact total twenty with its captured policy version

#### Scenario: Export retains version
- **WHEN** an export is running while a new policy version is published
- **THEN** every delivered batch uses its original captured version; captured-policy mode continues and abort mode stops when its activation check detects change

#### Scenario: Count rejects forbidden filter
- **WHEN** a count filter references a field without a Filter grant
- **THEN** the operation fails before entity access and returns no count result

#### Scenario: Detail does not grant client key filtering
- **WHEN** a caller uses the implicit key lookup without a Filter grant on the key
- **THEN** lookup remains permitted within the authorized predicate, but an explicit client filter on that same key still requires a Filter grant

#### Scenario: Irrelevant configuration does not block non-list reads
- **WHEN** the host sets a maximum page size below the list default and a caller requests count, detail, or export with supported inputs
- **THEN** those operations are not rejected for an unused default list limit

#### Scenario: Selection semantics remain unchanged
- **WHEN** a caller omits selection or supplies an empty selection for list, detail, or export
- **THEN** omitted selection uses effective Select grants and empty selection returns empty DTOs for authorized records

#### Scenario: Count retains the operation capture
- **WHEN** publication occurs after a count operation captures its policy and before its query completes
- **THEN** the exact count and returned policy version both correspond to that operation's captured authorization

### Requirement: Operation-specific read inputs
The system SHALL expose distinct list, count, detail, and export request contracts. List SHALL accept selection, a structured filter, sorting, offset, and limit, with defaults of offset zero and limit fifty. Count SHALL accept only a structured filter. Detail SHALL require a key and accept selection and an optional structured filter. Export SHALL accept selection, a structured filter, and sorting, with batching, consistency, and lifecycle limits controlled exclusively by host configuration. Read requests MUST NOT contain an action or authorization identity override. Every operation SHALL bind internally to the Read policy action.

#### Scenario: List pagination remains explicit
- **WHEN** a caller lists records without supplying pagination
- **THEN** the operation uses offset zero and limit fifty, subject to configured list limits

#### Scenario: Count has only query criteria
- **WHEN** a caller requests a count with an authorized filter
- **THEN** the operation counts matching authorized records without requiring selection, sort, or pagination inputs

#### Scenario: Detail key belongs to its request
- **WHEN** a caller requests detail with a key, selection, and an authorized filter
- **THEN** the system retrieves at most the authorized record matching both the key and filter

#### Scenario: Missing or incompatible detail key
- **WHEN** a caller omits the required detail key or supplies a value incompatible with the registered stable key
- **THEN** the operation fails explicitly before opening the entity database

#### Scenario: Export uses host limits
- **WHEN** a caller exports matching authorized rows
- **THEN** the operation starts at the initial keyset boundary and traverses the authorized sequence subject to host batch/row/duration/consistency settings, without client pagination

### Requirement: Internal keyset exports
Export SHALL traverse matching authorized rows by seeking strictly after the last returned sort tuple, rather than increasing offsets. It SHALL retain one captured policy and trusted context for the entire enumeration, host-controlled batch and optional row/duration bounds, permitted selection, cancellation, and session disposal. Policy consistency SHALL independently choose captured-policy continuation or explicit abort on activation change; entity-data consistency SHALL independently choose best-effort or an explicitly supported export-wide snapshot. Public export requests and batch results SHALL remain unchanged and SHALL NOT expose resume tokens. Unsupported seek capability, snapshot capability/configuration, or ordering SHALL fail explicitly without offset, unrestricted, local-authorization, or best-effort fallback. Best-effort keyset traversal SHALL NOT promise snapshot consistency or immunity to changes to sort values.

#### Scenario: Changes before an export boundary
- **WHEN** rows are inserted or deleted before the last emitted boundary during a best-effort export while remaining rows keep their sort tuples
- **THEN** subsequent batches do not skip or repeat those remaining rows because earlier row positions shifted

#### Scenario: Deletion of the boundary row
- **WHEN** the last row of a completed batch is deleted before the next batch
- **THEN** export continues using its captured boundary values without requiring that row to still exist

#### Scenario: Publication during export
- **WHEN** a new policy is published during an already-running export in captured-policy mode
- **THEN** subsequent batches retain the original captured policy and do not use the cross-request cursor rejection rule

#### Scenario: No public export resume
- **WHEN** a caller supplies a continuation token to the export request
- **THEN** strict request deserialization rejects it and export batches expose only their existing policy version and records

#### Scenario: Export stops early
- **WHEN** a consumer cancels or stops enumerating export and disposes the enumerator
- **THEN** the current session and any export-wide snapshot transaction are disposed and no further batch is executed

#### Scenario: Sort value changes during traversal
- **WHEN** a row moves across the seek boundary because its sort value changes during best-effort traversal
- **THEN** traversal remains authorized but does not claim a snapshot or exactly-once delivery guarantee for that changing row
