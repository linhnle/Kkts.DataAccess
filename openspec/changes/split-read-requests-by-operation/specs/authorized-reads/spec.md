# Spec Delta

## ADDED Requirements

### Requirement: Operation-specific read inputs
The system SHALL expose distinct list, count, detail, and export request contracts. List SHALL accept selection, a structured filter, sorting, offset, and limit, with defaults of offset zero and limit fifty. Count SHALL accept only a structured filter. Detail SHALL require a key and accept selection and an optional structured filter. Export SHALL accept selection, a structured filter, and sorting, with batching controlled exclusively by host configuration. Read requests MUST NOT contain an action or authorization identity override. Every operation SHALL bind internally to the Read policy action.

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
- **THEN** the operation starts at offset zero, covers all matching rows, and uses the configured export batch size without client pagination

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

## MODIFIED Requirements

### Requirement: Consistent list detail count and export behavior
Each list, detail, count, and export operation SHALL capture one Read policy version and enforce the same authorization composition while validating only the inputs supported by that operation. List SHALL validate selection, filter, sort, and pagination; count SHALL validate only its filter; detail SHALL validate its key, selection, and filter; export SHALL validate selection, filter, and sort. Explicit field references SHALL require the corresponding independent field grants. Detail SHALL combine the requested key with the authorized predicate without requiring a client Filter grant for the implicit host-key lookup, and SHALL return the same not-found result for an inaccessible or nonexistent record. Count SHALL return the exact count of authorized rows after client filters. List SHALL always return an exact total after client filters and before pagination. Exports SHALL honor authorized filters and selection over all matching rows and retain one policy version throughout execution. Existing count, page, detail, and batch result shapes SHALL remain unchanged.

#### Scenario: Inaccessible detail
- **WHEN** a user requests an existing record outside their authorized set
- **THEN** the result is indistinguishable from a nonexistent record

#### Scenario: Count independent of page
- **WHEN** a filter matches twenty authorized rows and the list page size is five
- **THEN** the list returns at most five rows with total twenty, and a separate count with the same filter returns twenty

#### Scenario: Export retains version
- **WHEN** an export is running while a new policy version is published
- **THEN** every batch of that export uses its original captured version

#### Scenario: Count rejects forbidden filter
- **WHEN** a count filter references a field without a Filter grant
- **THEN** the operation fails before entity access and returns no count

#### Scenario: Detail does not grant client key filtering
- **WHEN** a caller uses the implicit key lookup without a Filter grant on the key
- **THEN** lookup remains permitted within the authorized predicate, but an explicit client filter on that same key still requires a Filter grant

#### Scenario: Irrelevant configuration does not block non-list reads
- **WHEN** the host sets a maximum page size below the list default and a caller requests count, detail, or export with supported inputs
- **THEN** those operations are not rejected for an unused default list limit

#### Scenario: Selection semantics remain unchanged
- **WHEN** a caller omits selection or supplies an empty selection for list, detail, or export
- **THEN** omitted selection uses effective Select grants and empty selection returns empty DTOs for authorized records
