# Spec Delta

## ADDED Requirements

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
