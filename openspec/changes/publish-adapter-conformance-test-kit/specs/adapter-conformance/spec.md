# Spec Delta

## Purpose

Give adapter extension authors reusable runner-neutral behavioral scenarios with explicit capability coverage and honest database-evidence limitations.

## ADDED Requirements

### Requirement: Portable test-only scenarios
A separate test-only package SHALL expose runner-neutral async scenario/assertion APIs and disposable fixture contracts without requiring xUnit, EF, a database driver, or production host registration. Checks SHALL exercise public protected repository behavior rather than bypass it through an unrestricted query API. Mandatory scenarios SHALL cover authorization/grants, detail/count/list behavior, output restrictions, ordering, cancellation, disposal, and explicit failures. Capability-specific scenarios SHALL cover projected seek, composite keys, and export policy/snapshot behavior as applicable.

#### Scenario: Non-xUnit consumer
- **WHEN** an extension author invokes scenarios from another test runner
- **THEN** the same assertions run without an xUnit dependency or hidden repository internals

#### Scenario: Fixture ownership
- **WHEN** a scenario fails or is canceled
- **THEN** its explicitly owned fixture resources are disposed without deleting unrelated databases/files

### Requirement: Honest conformance reports
Reports SHALL distinguish passed, failed, unsupported optional capability, and not-run/missing evidence. Required scenario failures or missing required evidence SHALL prevent an overall passing result. A claimed capability MUST NOT be marked unsupported to hide a failed check. Real-provider translation/projection/materialization guarantees SHALL require corresponding fixture evidence; in-memory output tests SHALL NOT count as database enforcement proof. Passing fixtures SHALL be documented as evidence for tested cases, not certification that an adapter is safe for arbitrary policies/data.

#### Scenario: Missing provider evidence
- **WHEN** an in-memory fixture passes JSON checks but provides no database materialization evidence
- **THEN** the report marks that database guarantee not run rather than passed

#### Scenario: Claimed seek fails
- **WHEN** an adapter claims seek support but drops rows across equal-sort boundaries
- **THEN** its seek scenario fails rather than reporting unsupported

#### Scenario: Broken-adapter control
- **WHEN** a deliberately broken adapter ignores deny predicates or fails disposal
- **THEN** the appropriate scenario fails, demonstrating the suite can detect that fault

#### Scenario: Genuine optional absence
- **WHEN** an adapter explicitly lacks snapshot capability
- **THEN** the unsupported-mode rejection check passes while snapshot consistency scenarios are reported as unsupported, not successful snapshots
