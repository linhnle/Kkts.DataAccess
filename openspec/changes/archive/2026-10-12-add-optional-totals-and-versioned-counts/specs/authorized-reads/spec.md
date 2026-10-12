# Spec Delta

The cursor requirement below replaces the requirement introduced by the prerequisite `add-cursor-pagination-and-keyset-exports`. Apply and synchronize the prerequisite changes in order before synchronizing this delta.

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Consistent list detail count and export behavior
Each offset list, cursor list, detail, count, and export operation SHALL capture one Read policy version and enforce the same authorization composition while validating only the inputs supported by that operation. Lists SHALL validate selection, filter, sort, pagination, and supported metadata options; count SHALL validate only its filter; detail SHALL validate its key, selection, and filter; export SHALL validate selection, filter, and sort. Explicit field references SHALL require the corresponding independent field grants. Detail SHALL combine the requested key with the authorized predicate without requiring a client Filter grant for the implicit host-key lookup, and SHALL return the same not-found result for an inaccessible or nonexistent record. Standalone count SHALL return an exact total of authorized rows after client filters together with the captured policy version, not a bare integer. Lists SHALL return an exact total only when requested, counting before pagination and excluding a cursor boundary; otherwise total SHALL be explicit null. Exports SHALL honor authorized filters and selection over all matching rows and retain one policy version throughout execution. Detail and export batch result shapes SHALL remain unchanged; list metadata and standalone count result shapes SHALL migrate consistently across core, facade, and example HTTP surfaces. Metadata does not provide a database snapshot: has-more describes the page observation and an exact total can differ under concurrent data changes.

#### Scenario: Inaccessible detail
- **WHEN** a user requests an existing record outside their authorized set
- **THEN** the result is indistinguishable from a nonexistent record

#### Scenario: Count independent of page
- **WHEN** a filter matches twenty authorized rows and the list page size is five with exact total requested
- **THEN** the list returns at most five rows with total twenty, and a standalone count returns exact total twenty with its captured policy version

#### Scenario: Export retains version
- **WHEN** an export is running while a new policy version is published
- **THEN** every batch of that export uses its original captured version

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
