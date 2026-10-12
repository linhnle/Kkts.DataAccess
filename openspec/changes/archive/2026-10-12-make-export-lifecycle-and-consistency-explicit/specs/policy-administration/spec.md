# Spec Delta

## ADDED Requirements

### Requirement: Authoritative policy activation stamps
The supplied policy source SHALL expose a trusted capability to atomically capture immutable active policy content with its scope/resource activation revision and to read the current active version/revision authoritatively. The revision SHALL advance monotonically on every successful publication or rollback, including activation of the same immutable version, but not on draft edits or failed transactions. Version, revision, and content in a capture MUST NOT describe different activations. Reads for export-change checks SHALL use fresh authoritative state, not entity snapshot state or stale caches. Missing active policy SHALL fail closed. Revision identity SHALL remain distinct from immutable policy version identity and SHALL NOT change public cursor compatibility rules.

#### Scenario: Atomic capture races publication
- **WHEN** publication races a stamped capture
- **THEN** the capture contains either the previous committed content/version/revision or the new committed content/version/revision, never a mixed tuple

#### Scenario: Rollback advances revision
- **WHEN** a new version is published and the original version is restored
- **THEN** the active immutable version may equal the original but its activation revision has advanced

#### Scenario: Failed publication leaves revision
- **WHEN** a publication transaction fails before commit
- **THEN** policy content, active pointer, activation revision, and audit remain unchanged together

#### Scenario: Fresh revision check
- **WHEN** an export holds an entity snapshot while a policy activation commits
- **THEN** a subsequent authoritative activation read observes the changed revision independently of that snapshot

#### Scenario: Scope and resource isolation
- **WHEN** an unrelated scope/resource activates a policy or the current scope/resource only edits a draft
- **THEN** the export's scope/resource activation stamp remains unchanged
