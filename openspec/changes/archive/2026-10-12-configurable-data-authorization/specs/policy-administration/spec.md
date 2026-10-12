# Policy Administration

## Purpose

Let authorized administrators design, validate, preview, publish, and roll back persistent policies with an auditable version history.

## ADDED Requirements

### Requirement: Protected draft editing and designer
The system SHALL restrict policy administration and schema metadata to host-authorized administrators within their administrative scope. The basic designer SHALL support selecting resources, actions, roles/groups, nested typed conditions, and independent field permissions. Draft edits SHALL be persisted without affecting active data access and SHALL reject stale concurrent edits through revision checks.

#### Scenario: Non-administrator
- **WHEN** a non-administrator attempts to read or modify policy configuration
- **THEN** access is denied

#### Scenario: Draft isolation
- **WHEN** an administrator saves a changed draft
- **THEN** data operations continue using the currently published version

#### Scenario: Conflicting edit
- **WHEN** two administrators save edits using the same draft revision
- **THEN** the later save reports a conflict without overwriting the first

### Requirement: Validation and preview
The system SHALL validate schema compatibility, supported actions, operators, types, context references, field grants, and configured complexity limits before publication. Administrators SHALL be able to preview a draft using a clearly labeled sample subject context and bounded sample records within their administrative scope. Preview SHALL show applicable rule identifiers, effective fields, mandatory constraints, and allow/deny outcomes. Preview MUST NOT activate a policy or impersonate a subject in production data operations.

#### Scenario: Invalid publication
- **WHEN** a draft references an unknown field
- **THEN** validation returns a diagnostic and publication is refused

#### Scenario: Sample context
- **WHEN** an administrator previews a manager's sample context
- **THEN** preview shows the matching department rules and field permissions while leaving the active version unchanged

### Requirement: Atomic immutable publication
The system SHALL store immutable validated versions in a database and atomically switch the active version for a resource within its administrative scope. Publication SHALL revalidate against the current schema and check draft and active-version revisions. The active pointer, version content, and audit entry MUST commit together or remain unchanged together. Absence of an active valid version SHALL deny data access.

#### Scenario: Persistence failure
- **WHEN** publication fails before its transaction commits
- **THEN** the previous active version remains effective and no successful publication audit is recorded

#### Scenario: Concurrent publication
- **WHEN** a publication uses a stale active-version revision
- **THEN** it reports a conflict and preserves the winning publication

### Requirement: Audit history and rollback
The system SHALL retain actor, time, scope, revision/version identifiers, operation, and change summary for draft changes, publication, and rollback. Rollback SHALL select a prior immutable version, revalidate it against the current schema, and atomically change the active pointer with an audit entry without deleting history. Audit records MUST NOT persist request secrets or sample identity values unnecessarily.

#### Scenario: Successful rollback
- **WHEN** an administrator rolls back to a compatible prior version
- **THEN** newly started operations use that version and history identifies the rollback actor and target

#### Scenario: Incompatible rollback
- **WHEN** the target version references a field removed from the current schema
- **THEN** rollback fails and the active pointer remains unchanged
