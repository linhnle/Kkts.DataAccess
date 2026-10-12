# Spec Delta

## ADDED Requirements

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

## MODIFIED Requirements

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
