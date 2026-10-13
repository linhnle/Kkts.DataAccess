# Resource Schemas

## Purpose

Provide a shared, typed public contract for configuring policies and validating authorized data operations.

## Requirements

### Requirement: Typed resource registration
The system SHALL register a versioned resource schema containing its entity binding, unique public field names, field types and nullability, property bindings, supported actions, supported operators, and separate eligibility for selection, filtering, sorting, aggregation, and writing. Invalid or ambiguous bindings MUST prevent registration. Schema eligibility SHALL limit, never grant, user permissions.

#### Scenario: Duplicate public name
- **WHEN** a schema registers two fields with the same public name
- **THEN** registration fails with a field-specific diagnostic

#### Scenario: Ineligible field
- **WHEN** a policy grants sorting on a field whose schema prohibits sorting
- **THEN** validation rejects that grant

### Requirement: Schema-controlled field resolution
The system SHALL resolve policy and client query fields exclusively through registered public names and SHALL reject unknown fields, unregistered property paths, incompatible operators, and values incompatible with the declared field type. Type conversion SHALL use documented, culture-independent rules and SHALL reject ambiguous or lossy conversions.

#### Scenario: Unregistered nested property
- **WHEN** a request filters on an entity property path absent from the public schema
- **THEN** the request fails before data execution

#### Scenario: Invalid numeric comparison
- **WHEN** a condition compares a numeric field to a nonnumeric literal
- **THEN** validation identifies the condition and expected type

### Requirement: Rule designer metadata
The system SHALL expose authorized administrators to schema metadata sufficient to select resources, actions, fields, typed values, and valid operators without exposing arbitrary expression execution.

#### Scenario: Typed controls
- **WHEN** an administrator selects a field in the rule designer
- **THEN** the designer offers only that field's supported operators and compatible value controls

### Requirement: Ordered resource key tuples
A resource SHALL declare an immutable nonempty ordered tuple of exact public aliases bound to distinct direct nonnullable orderable scalar properties. The host SHALL guarantee tuple uniqueness; individual components need not be unique. Empty, duplicate alias/property, unknown, nullable, or nonorderable key components SHALL prevent registration with component diagnostics. Metadata SHALL expose ordered key aliases only to authorized schema consumers. Changing key identity/order SHALL require a compatible schema/dataset version migration, never silently reuse incompatible cursor state. Key membership MUST NOT grant Select, Filter, or Sort permission.

#### Scenario: Composite identity registration
- **WHEN** a host registers TenantId and LocalId as a jointly unique ordered key
- **THEN** the resource accepts rows sharing either component individually and retains their declared tuple order

#### Scenario: Invalid tuple
- **WHEN** the tuple repeats an alias/property or contains a nullable component
- **THEN** registration fails at the offending key component

#### Scenario: Hidden key components
- **WHEN** a key component has no Select or client query grant
- **THEN** key registration does not expose it in DTOs or permit explicit client filtering/sorting
