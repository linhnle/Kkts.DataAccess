# Authorized Reads

## Purpose

Apply one authorization contract to record retrieval, query counts, and exported data without exposing forbidden fields through query operations.

## Requirements

### Requirement: Authorized database projection
By default, offset lists, detail, cursor lists, and exports SHALL retrieve scalar projections rather than materializing complete entities. The database result SHALL contain only the permitted requested output values, except for minimal trusted continuation values required by cursor/export traversal or a non-entity cardinality marker for empty output. Authorization predicates, client filters, sorting, and pagination SHALL execute in the provider before projected rows are returned. Columns referenced only by authorization, filtering, or ordinary ordering MUST NOT be materialized merely because the query uses them. Clients MUST NOT supply executable projection expressions or choose the host's materialization mode. Projection MUST NOT change policy composition, grants, public results, optional metadata, cancellation, or disposal.

#### Scenario: Wide entity selects a narrow DTO
- **WHEN** a list requests permitted Id and Name from an entity with an unselected large secret column
- **THEN** the provider returns only those selected output values and does not materialize the secret column or complete entity

#### Scenario: Authorization uses a hidden column
- **WHEN** the authorized predicate references a nonselected tenant or policy-condition field
- **THEN** the provider enforces it without returning that field as an output value or complete entity member

#### Scenario: Filtering and ordinary ordering stay internal
- **WHEN** an offset list filters or sorts on a granted field absent from selection
- **THEN** the field participates in the database query but does not enter projected output values merely for filtering or ordering

#### Scenario: Projected detail preserves protected lookup
- **WHEN** a detail request addresses an inaccessible or nonexistent row
- **THEN** both cases retain the same not-found result without loading a complete entity

#### Scenario: Projection cannot be controlled by client code
- **WHEN** a read payload contains an executable projection or host materialization-mode member
- **THEN** strict deserialization rejects it and no arbitrary expression or mode override reaches execution

#### Scenario: Count remains aggregate-only
- **WHEN** a caller executes standalone count or enables a list total
- **THEN** counting remains a provider aggregate with captured-policy metadata, without requiring entity materialization or projected output rows

### Requirement: Trusted continuation projection sidecars
Cursor and export projection SHALL return only the minimal complete effective sort tuple needed for continuation as trusted metadata separate from selected output. Values already selected SHALL be reusable without duplicating physical retrieval. Sidecars MUST NOT grant Select or Filter permissions, enter DTO mapping, or be serialized into public results; boundary values in public cursor tokens SHALL remain encrypted. Sidecars SHALL retain typed/null/precision semantics compatible with provider ordering. The last returned row, not a lookahead row, SHALL provide the next public cursor boundary.

#### Scenario: Hidden tie-breaker permits empty output
- **WHEN** a cursor or export has empty selection and uses a hidden stable key
- **THEN** authorized rows still produce empty DTOs while trusted sidecars enable correct traversal without disclosing the key

#### Scenario: Nonselected permitted sort value
- **WHEN** a cursor sorts on a granted but nonselected field
- **THEN** its value is available only as trusted continuation metadata and encrypted token state, not as a DTO property

#### Scenario: Lookahead stays separate
- **WHEN** a cursor page fetches one extra authorized row
- **THEN** the extra row is not mapped into output and its sidecar is not used as the next boundary

#### Scenario: Export capture remains stable
- **WHEN** publication occurs during a projected export
- **THEN** output bindings, sidecars, and authorization retain the export's original captured policy/context

### Requirement: Authorization precedes query execution
The system SHALL apply the captured Read policy and mandatory constraints before client filters, sorting, pagination, materialization, or counts. Clients MUST NOT replace the authorized predicate or receive an unfiltered query handle. Unsupported query constructs or provider translation failures SHALL fail explicitly without an unfiltered fallback.

#### Scenario: Filter cannot broaden access
- **WHEN** a client filter matches records in several tenants
- **THEN** only records matching both the filter and authorized predicate participate in the operation

#### Scenario: Provider cannot translate
- **WHEN** a provider cannot execute the authorized predicate
- **THEN** the request fails without retrying an unrestricted query

### Requirement: Query field authorization
The system SHALL validate all explicit and implicit client field references against schema eligibility and effective permissions for the requested operation, including predicate operands and sort keys. Invalid selection, filtering, sorting, or aggregation requests SHALL fail before execution; forbidden fields MUST NOT be silently removed from explicit requests. Phase 1 SHALL reject aggregation, relationship traversal, and write operations as unsupported even when permission metadata exists.

#### Scenario: Hidden-field inference
- **WHEN** a count request filters on Salary without filtering permission
- **THEN** the system rejects the request and returns no count

#### Scenario: Unauthorized sorting
- **WHEN** a request sorts by a readable field without sorting permission
- **THEN** it fails before querying

#### Scenario: Unsupported aggregation
- **WHEN** a client asks for a sum over an otherwise permitted field in Phase 1
- **THEN** the operation is rejected as unsupported

### Requirement: Permitted output shape
The system SHALL return a DTO containing only the requested permitted public fields. Omitted selection SHALL use the effective selectable set. Entity instances, unrequested fields, navigation properties, and internal authorization or continuation data MUST NOT be serialized. Empty effective selection SHALL return empty DTOs for authorized rows rather than adding identifier fields implicitly. Projected and explicitly allowed entity-materialization paths SHALL preserve the same aliases, selection order, scalar/null representations, and JSON shape. Projected mapping SHALL consume selected scalar values without constructing partial entities. Empty projected output SHALL preserve authorized row cardinality and detail existence without selecting a hidden identifier merely to produce a DTO.

#### Scenario: Salary excluded
- **WHEN** the selectable set contains Id, Name, and Email but not Salary
- **THEN** default output contains only Id, Name, and Email

#### Scenario: Explicit forbidden selection
- **WHEN** a client explicitly selects Salary without permission
- **THEN** the request is rejected rather than returning a partial selection

#### Scenario: Scalar and alias parity
- **WHEN** equivalent authorized records are mapped from projected scalars and the explicitly permitted entity path
- **THEN** JSON preserves the same ordered public aliases, scalar types, nullable values, and serialization behavior

#### Scenario: Empty projected detail
- **WHEN** an authorized detail request has empty selection
- **THEN** an existing authorized row produces an empty DTO and an absent or inaccessible row still produces not-found

#### Scenario: No entity reconstruction
- **WHEN** a projected row is converted to its permitted JSON DTO
- **THEN** mapping does not instantiate the entity or invoke its entity-property getters

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

### Requirement: Exact object-shaped detail keys
All detail requests SHALL provide an object containing exactly one value for each registered key alias, including single-component resources. Scalar keys, missing/extra/duplicate/case-mismatched members, nulls, and incompatible values SHALL fail before entity access with component diagnostics. Detail lookup SHALL combine equality for every key component with authorization and any client filter. Implicit key equality SHALL NOT require client Filter grants; explicit filters still SHALL. Inaccessible and nonexistent tuple identities SHALL retain identical not-found behavior.

#### Scenario: Complete tuple detail
- **WHEN** the caller supplies `{"TenantId":1,"LocalId":7}` for that resource key
- **THEN** detail retrieves at most the authorized row matching both values

#### Scenario: Single key migration
- **WHEN** a single-key resource receives a scalar rather than `{"Id":7}`
- **THEN** input is rejected and the caller must use the one-member object contract

#### Scenario: Duplicate or incomplete components
- **WHEN** the key repeats LocalId or omits TenantId
- **THEN** validation reports the invalid component/object and opens no entity session

### Requirement: Bounded deterministic pagination
The system SHALL enforce configured page and export batch limits and deterministic ordering before applying pagination. A host-declared ordered unique key tuple SHALL complete ordering by appending each missing key property in declared order ascending while preserving explicit sort order, directions, and repeated sort semantics; it MUST NOT be output or made available to client filtering or sorting without the corresponding grants. Aliases bound to the same property SHALL NOT cause duplicate implicit completion. A resource without usable deterministic ordering SHALL reject paginated reads. Cursor pages and export batches SHALL use lexicographic seek boundaries over the complete effective ordering, preserving each direction and using null placement and equality/comparison semantics consistent with provider ordering. Internal seek comparisons SHALL NOT require or grant additional client Filter permissions. For unchanged matching rows and sort tuples, traversal SHALL produce no gaps or duplicates across equal sort values. Cursor compatibility SHALL bind the complete ordered key identity; hidden key values SHALL remain separate encrypted continuation metadata.

#### Scenario: Equal sort values
- **WHEN** several authorized records share a client sort value
- **THEN** the host-declared full key tuple produces deterministic page ordering

#### Scenario: Mixed directions and nulls
- **WHEN** supported ordering combines ascending and descending fields, nullable values, and the unique key tuple
- **THEN** resumed pages and export batches traverse the same effective ordered sequence without dropping equal-prefix rows

#### Scenario: Sort does not grant filtering
- **WHEN** a caller has Sort permission but lacks Filter permission on an ordering field
- **THEN** internal seek comparisons remain permitted, but an explicit client filter on that field is still rejected

#### Scenario: Provider-equivalent text values
- **WHEN** the provider treats distinct text values as equal under its configured collation
- **THEN** seek equality uses that same collation and the unique key tuple distinguishes rows across boundaries

#### Scenario: Partially explicit key sorting
- **WHEN** a client sorts LocalId descending for a key ordered TenantId then LocalId
- **THEN** that explicit direction remains and missing TenantId is appended ascending to complete unique ordering

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

### Requirement: Strict read request JSON
Deserialization of each read request SHALL reject unmapped JSON members, including action and identity overrides and members belonging only to another operation. Such malformed HTTP input in the example SHALL result in a bad-request response before entity execution, rather than being ignored or executed as another operation.

#### Scenario: Count rejects unrelated options
- **WHEN** a count payload contains selection, sort, offset, or limit
- **THEN** deserialization rejects the payload

#### Scenario: Detail rejects unrelated options
- **WHEN** a detail payload contains sort, offset, or limit
- **THEN** deserialization rejects the payload

#### Scenario: Export rejects client pagination
- **WHEN** an export payload contains offset, limit, or a client export batch override
- **THEN** deserialization rejects the payload

#### Scenario: Client action cannot change authorization
- **WHEN** a read payload contains an action member, including Read, Write, or Aggregate
- **THEN** deserialization rejects the payload and does not invoke a different policy action

#### Scenario: Unknown or identity override member
- **WHEN** a read payload contains an unrecognized member or an authorization identity override
- **THEN** deserialization rejects the payload before entity execution

### Requirement: Forward cursor lists coexist with offset lists
The system SHALL expose a distinct forward cursor-list operation accepting selection, structured filter, sorting, bounded limit, independent total/has-more metadata options, and an optional opaque continuation token, but no offset or client action. It SHALL retain the existing offset-list operation. Each cursor page SHALL return permitted records, its captured policy version, an exact base-query total only when requested, nullable has-more metadata, and a next token only when another row was observed beyond the returned page. The default cursor limit SHALL be fifty, subject to host page limits. Metadata defaults SHALL enable total and disable has-more. Cursor input SHALL reject unknown JSON members. Standalone count SHALL return its exact total with policy version; detail contracts SHALL remain unchanged.

#### Scenario: First cursor page
- **WHEN** a caller requests a cursor page without a token
- **THEN** the system returns the first bounded page in effective order and a next token if an additional authorized row exists

#### Scenario: Empty or final page
- **WHEN** no authorized rows remain beyond the boundary or no additional row exists beyond the returned page
- **THEN** the system returns no next token, without inventing a boundary for an empty page

#### Scenario: Total is not the remaining count
- **WHEN** twenty authorized rows match a query and the caller resumes after the first five with exact total requested
- **THEN** the resumed page still reports total twenty in the absence of concurrent data changes

#### Scenario: Offset contract remains usable
- **WHEN** an existing migrated caller lists with an offset and limit without specifying metadata options
- **THEN** the offset-list API retains exact-total behavior by default and configured offset limits while exposing the new nullable metadata fields

#### Scenario: Cursor request rejects offset
- **WHEN** a cursor payload includes offset, action, or another unknown member
- **THEN** deserialization fails rather than combining pagination modes or changing authorization

### Requirement: Independent optional pagination metadata
Offset and cursor list requests SHALL accept independent boolean options to include an exact total and to include a has-more indicator, defaulting respectively to true and false. When the total is disabled, the operation MUST NOT execute a count query and SHALL return total as explicit null. When has-more is disabled, its result SHALL be explicit null. When has-more is enabled, the operation SHALL determine it from an additional authorized row beyond the returned page, never from total arithmetic; it SHALL return true when that row exists and false otherwise. The operation SHALL return at most the requested limit, and the additional row MUST NOT be mapped into output. Both metadata options SHALL retain ordinary input validation, field permissions, and captured-policy enforcement. Count, detail, and export requests SHALL reject these list-only options.

#### Scenario: Default metadata
- **WHEN** a caller omits both metadata options
- **THEN** the list returns its exact total and null has-more, preserving default exact-count behavior

#### Scenario: Neither metadata item
- **WHEN** a caller disables total and does not request has-more
- **THEN** the result has explicit null total and has-more and no count query executes

#### Scenario: Has-more without total
- **WHEN** a caller disables total and requests has-more
- **THEN** no count query executes and a bounded extra authorized row determines has-more

#### Scenario: Both metadata items
- **WHEN** a caller requests both total and has-more
- **THEN** the operation returns an exact total and an independently observed boolean has-more

#### Scenario: Empty and exact-full final pages
- **WHEN** a caller requests has-more and the remaining authorized sequence contains zero rows or exactly the requested number of rows
- **THEN** has-more is false, including when the returned page is full

#### Scenario: Extra forbidden row does not count
- **WHEN** only a forbidden row lies beyond the returned authorized page
- **THEN** has-more is false and no forbidden fields or record are returned

#### Scenario: Omitted total differs from an exact zero
- **WHEN** an empty authorized query is listed with total enabled and then disabled
- **THEN** the enabled response contains total zero and the disabled response contains explicit null

#### Scenario: One-row lookahead at the page limit
- **WHEN** a caller requests the maximum permitted page size and has-more
- **THEN** internal lookahead is bounded to one additional row and output still respects the host page limit

#### Scenario: Non-list inputs stay narrow
- **WHEN** count, detail, or export JSON contains a list metadata option
- **THEN** strict deserialization rejects the option rather than ignoring it

### Requirement: Metadata-independent cursor continuation
The include-total and include-has-more options SHALL NOT participate in cursor compatibility identity. A caller SHALL be able to change those options on resumed cursor requests without invalidating an otherwise compatible token. Cursor traversal SHALL retain its existing bounded lookahead and next-token rules even when has-more metadata is not requested, reusing that same observation when it is requested rather than fetching a second extra row. Requested exact cursor totals SHALL count the base authorized client query, excluding the continuation boundary.

#### Scenario: Metadata options change on resume
- **WHEN** a caller resumes a valid cursor with different metadata options but unchanged row query, limit, caller/context, dataset, schema, and policy
- **THEN** continuation remains valid and the response follows the new metadata options

#### Scenario: Cursor has-more reuses token lookahead
- **WHEN** a cursor page requests has-more
- **THEN** has-more is true exactly when the observed page produces a next token, and no second lookahead query or extra row is fetched

#### Scenario: No metadata still allows continuation
- **WHEN** a cursor page disables both metadata items and another authorized row is observed
- **THEN** it returns null total and has-more, performs no count query, and still returns a next token

#### Scenario: Incompatible row query still fails
- **WHEN** a caller changes a token-bound filter, selection, ordering, limit, identity/context, dataset, or policy version while also changing metadata options
- **THEN** the request still fails the existing compatibility checks

### Requirement: Protected continuation compatibility
Continuation tokens SHALL provide confidentiality and integrity and SHALL bind the last returned row's effective sort tuple, including the unique tie-breaker, to scope, resource, schema and bindings, query identity, host dataset identity, authenticated caller, trusted authorization context, and active policy version. Tokens SHALL contain a versioned format and a finite host-controlled expiry. Token possession MUST NOT replace policy capture, trusted-context binding, or field-grant validation. Every resumed request SHALL capture and apply current authorization and reject a token whose policy version differs from the current capture; it MUST NOT retrieve a historical policy to resume. Malformed, tampered, expired, oversized, wrong-purpose, or incompatible tokens SHALL fail explicitly before entity execution, without silently restarting.

#### Scenario: Matching authorized resume
- **WHEN** the caller resumes with a valid token and matching query, dataset, context, and policy
- **THEN** the system reapplies authorization and returns rows strictly after the protected boundary

#### Scenario: Policy publication invalidates continuation
- **WHEN** a resumed cursor contains a policy version different from the currently captured active version
- **THEN** the request fails and the caller must restart without the token

#### Scenario: Different caller or context
- **WHEN** a token is supplied by a different authenticated caller or after the caller's trusted roles, groups, or registered authorization values change
- **THEN** the request fails before accessing entity data even if the policy version is unchanged

#### Scenario: Different query or dataset
- **WHEN** a token is reused with different filter, effective selection, ordering, limit, resource bindings, scope, schema, or host dataset identity
- **THEN** the request rejects the incompatible token

#### Scenario: Untrusted or unusable token
- **WHEN** a token is malformed, tampered, expired, oversized, uses an unsupported format, or belongs to another protection purpose
- **THEN** the request fails explicitly and does not issue an entity query or restart at the beginning

#### Scenario: Hidden boundary values remain confidential
- **WHEN** ordering uses a nonselected sort value or an implicit key without a Select grant
- **THEN** neither DTO output nor readable token contents expose those values

#### Scenario: Protection unavailable
- **WHEN** a host invokes cursor pagination without configured token protection or authenticated caller/dataset binding
- **THEN** the operation fails explicitly before entity access; ordinary offset/count/detail and internal keyset exports do not require public-token protection

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
