# Spec Delta

## ADDED Requirements

### Requirement: Provider-bound policy validation
Hosts SHALL be able to bind policy validation/publication/preview and mandatory-constraint validation to the registered provider capability profile. Known unsupported field/operator/mapping combinations SHALL produce stable input-path diagnostics before activation; unverified combinations SHALL remain explicitly labeled and subject to runtime translation. Subject-only context checks SHALL NOT be incorrectly treated as database predicates. Provider-neutral structural validation SHALL remain available and SHALL NOT claim provider conformance. Runtime capture/binding SHALL reject known-incompatible active policy content before entity queries, including policies published through a provider-neutral administrative path.

#### Scenario: Unsupported policy predicate
- **WHEN** a provider-bound draft uses a known unsupported field/operator combination
- **THEN** validation identifies its condition path and publication leaves active pointer/content/audit unchanged

#### Scenario: Mandatory constraint unsupported
- **WHEN** registration supplies a known unsupported mandatory database predicate
- **THEN** provider-bound startup validation fails rather than weakening or removing the constraint

#### Scenario: Neutral validation remains honest
- **WHEN** an extension author validates a policy without a provider profile
- **THEN** validation checks schema/type/complexity only and does not claim successful database translation
