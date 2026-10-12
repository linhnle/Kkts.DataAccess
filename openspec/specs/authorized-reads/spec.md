# Authorized Reads

## Purpose

Apply one authorization contract to record retrieval, query counts, and exported data without exposing forbidden fields through query operations.

## Requirements

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
The system SHALL return a DTO containing only the requested permitted public fields. Omitted selection SHALL use the effective selectable set. Entity instances, unrequested fields, navigation properties, and internal authorization data MUST NOT be serialized. Empty effective selection SHALL return empty DTOs for authorized rows rather than adding identifier fields implicitly.

#### Scenario: Salary excluded
- **WHEN** the selectable set contains Id, Name, and Email but not Salary
- **THEN** default output contains only Id, Name, and Email

#### Scenario: Explicit forbidden selection
- **WHEN** a client explicitly selects Salary without permission
- **THEN** the request is rejected rather than returning a partial selection

### Requirement: Consistent list detail count and export behavior
Lists, detail retrieval, counts, and exports SHALL use the same Read policy version and field/query validation rules for each operation. Detail retrieval SHALL combine the requested key with the authorized predicate and return the same not-found result for an inaccessible or nonexistent record. Counts SHALL count authorized rows after client filters and before pagination. Exports SHALL honor authorized filters and selection over all matching rows and SHALL retain one policy version throughout export execution.

#### Scenario: Inaccessible detail
- **WHEN** a user requests an existing record outside their authorized set
- **THEN** the result is indistinguishable from a nonexistent record

#### Scenario: Count independent of page
- **WHEN** a filter matches twenty authorized rows and the page size is five
- **THEN** the list returns at most five rows and its total count is twenty

#### Scenario: Export retains version
- **WHEN** an export is running while a new policy version is published
- **THEN** every batch of that export uses its original captured version

### Requirement: Bounded deterministic pagination
The system SHALL enforce configured page and export batch limits and deterministic ordering before applying pagination. A host-declared unique tie-breaker SHALL complete ordering when necessary; it MUST NOT be output or made available to client filtering or sorting without the corresponding grants. A resource without a usable deterministic ordering SHALL reject paginated reads.

#### Scenario: Equal sort values
- **WHEN** several authorized records share a client sort value
- **THEN** the host-declared tie-breaker produces deterministic page ordering
