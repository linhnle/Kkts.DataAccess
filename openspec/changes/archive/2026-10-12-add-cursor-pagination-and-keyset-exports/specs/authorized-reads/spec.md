# Spec Delta

## ADDED Requirements

### Requirement: Forward cursor lists coexist with offset lists
The system SHALL expose a distinct forward cursor-list operation accepting selection, structured filter, sorting, bounded limit, and an optional opaque continuation token, but no offset or client action. It SHALL retain the existing offset-list operation. Each cursor page SHALL return permitted records, its captured policy version, an exact total for the authorized client query before the continuation boundary, and a next token only when another row was observed beyond the returned page. The default cursor limit SHALL be fifty, subject to host page limits. Cursor input SHALL reject unknown JSON members. Count and detail contracts SHALL remain unchanged.

#### Scenario: First cursor page
- **WHEN** a caller requests a cursor page without a token
- **THEN** the system returns the first bounded page in effective order and a next token if an additional authorized row exists

#### Scenario: Empty or final page
- **WHEN** no authorized rows remain beyond the boundary or no additional row exists beyond the returned page
- **THEN** the system returns no next token, without inventing a boundary for an empty page

#### Scenario: Total is not the remaining count
- **WHEN** twenty authorized rows match a query and the caller resumes after the first five
- **THEN** the resumed page still reports total twenty in the absence of concurrent data changes

#### Scenario: Offset contract remains usable
- **WHEN** an existing migrated caller lists with an offset and limit
- **THEN** the offset-list API retains its existing request, exact total, result shape, and configured offset limits

#### Scenario: Cursor request rejects offset
- **WHEN** a cursor payload includes offset, action, or another unknown member
- **THEN** deserialization fails rather than combining pagination modes or changing authorization

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
Export SHALL traverse all matching authorized rows by seeking strictly after the last returned sort tuple, rather than increasing offsets. It SHALL retain one captured policy and trusted context for the entire enumeration, host-controlled batch bounds, permitted selection, cancellation, and session disposal. Public export requests and batch results SHALL remain unchanged and SHALL NOT expose resume tokens. Unsupported seek capability or ordering SHALL fail explicitly without offset, unrestricted, or local-authorization fallback. Keyset traversal SHALL NOT promise snapshot consistency or immunity to changes to sort values.

#### Scenario: Changes before an export boundary
- **WHEN** rows are inserted or deleted before the last emitted boundary while the remaining rows keep their sort tuples
- **THEN** subsequent batches do not skip or repeat those remaining rows because earlier row positions shifted

#### Scenario: Deletion of the boundary row
- **WHEN** the last row of a completed batch is deleted before the next batch
- **THEN** export continues using its captured boundary values without requiring that row to still exist

#### Scenario: Publication during export
- **WHEN** a new policy is published during an already-running export
- **THEN** subsequent batches retain the original captured policy and do not use the cross-request cursor rejection rule

#### Scenario: No public export resume
- **WHEN** a caller supplies a continuation token to the export request
- **THEN** strict request deserialization rejects it and export batches expose only their existing policy version and records

#### Scenario: Export stops early
- **WHEN** a consumer cancels or stops enumerating export
- **THEN** the current session is disposed and no further batch is executed

#### Scenario: Sort value changes during traversal
- **WHEN** a row moves across the seek boundary because its sort value changes
- **THEN** traversal remains authorized but does not claim a snapshot or exactly-once delivery guarantee for that changing row

## MODIFIED Requirements

### Requirement: Bounded deterministic pagination
The system SHALL enforce configured page and export batch limits and deterministic ordering before applying pagination. A host-declared unique tie-breaker SHALL complete ordering when necessary; it MUST NOT be output or made available to client filtering or sorting without the corresponding grants. A resource without a usable deterministic ordering SHALL reject paginated reads. Cursor pages and export batches SHALL use lexicographic seek boundaries over the complete effective ordering, preserving each direction and using null placement and equality/comparison semantics consistent with the provider's ordering. Internal seek comparisons SHALL NOT require or grant additional client Filter permissions. For unchanged matching rows and sort tuples, traversal SHALL produce no gaps or duplicates across equal sort values.

#### Scenario: Equal sort values
- **WHEN** several authorized records share a client sort value
- **THEN** the host-declared tie-breaker produces deterministic page ordering

#### Scenario: Mixed directions and nulls
- **WHEN** supported ordering combines ascending and descending fields, nullable values, and the unique tie-breaker
- **THEN** resumed pages and export batches traverse the same effective ordered sequence without dropping equal-prefix rows

#### Scenario: Sort does not grant filtering
- **WHEN** a caller has Sort permission but lacks Filter permission on an ordering field
- **THEN** internal seek comparisons remain permitted, but an explicit client filter on that field is still rejected

#### Scenario: Provider-equivalent text values
- **WHEN** the provider treats distinct text values as equal under its configured collation
- **THEN** seek equality uses that same collation and the unique tie-breaker distinguishes rows across boundaries
