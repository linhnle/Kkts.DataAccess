# Spec Delta

## ADDED Requirements

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

## MODIFIED Requirements

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
