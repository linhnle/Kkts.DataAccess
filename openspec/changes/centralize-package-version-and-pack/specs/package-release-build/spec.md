# Spec Delta

## Purpose

Provide a consistent release version for repository-produced NuGet packages and a reliable single-command build that collects the complete package set in one location.

## ADDED Requirements

### Requirement: One release version controls every library package

The repository SHALL define its default NuGet release version in one shared location, initially `0.1.0`. All nine distributable libraries SHALL inherit that version without project-local release version literals. References between repository packages, including the existing packed-core consumption mode, SHALL resolve to the selected release version. Third-party dependency versions SHALL remain unchanged.

#### Scenario: Update the default release version once

- **WHEN** a maintainer changes only the shared release version and runs the package build
- **THEN** all nine package manifests declare the new version
- **AND** every dependency on another repository-produced package uses that release version as its dependency version or lower bound
- **AND** no third-party dependency upgrade is required

#### Scenario: Use a temporary release override

- **WHEN** a maintainer runs the documented package command with `-p:Version=0.1.1-localcheck`
- **THEN** all nine packages and their internal dependency version declarations use `0.1.1-localcheck`
- **AND** the checked-in shared default remains unchanged

#### Scenario: Consume packed core after a version change

- **WHEN** core is packaged at the selected release version and the existing packed-core mode is restored against that local package source
- **THEN** the example and participating integrations request the selected core version rather than a hard-coded earlier version
- **AND** normal project-reference builds remain supported when packed-core mode is disabled

### Requirement: One command builds the complete distributable package set

The repository SHALL document one command, runnable from the repository root, that restores dependencies, builds in Release configuration, and packs the complete distributable library set. A successful run SHALL produce one ordinary `.nupkg` for each of `Kkts.DataAccess`, `Kkts.DataAccess.EntityFrameworkCore`, `Kkts.DataAccess.Sqlite`, `Kkts.DataAccess.SqlServer`, `Kkts.DataAccess.MySql`, `Kkts.DataAccess.DependencyInjection`, `Kkts.DataAccess.AspNetCore`, `Kkts.DataAccess.DataProtection`, and `Kkts.DataAccess.Testing`. Examples and the unit-test project SHALL NOT produce packages.

#### Scenario: Build all packages from a clean checkout

- **WHEN** the documented command runs with the pinned SDK and required dependency sources available, without prebuilt binaries or a prepacked local core
- **THEN** restore, Release build, and packaging complete in one invocation
- **AND** the output contains all nine expected package identities at the selected version
- **AND** no example or unit-test package is produced

#### Scenario: Report an unsuccessful build

- **WHEN** restore, compilation, or packaging fails
- **THEN** the command exits unsuccessfully and reports the failing operation
- **AND** existing or partially generated packages are not evidence of a successful complete release

### Requirement: Package output is centralized and consumable

The default package output SHALL be `artifacts/packages/` relative to the repository root, independent of the packed project's directory. Generated packages SHALL remain excluded from source control. Package IDs, target frameworks, per-package README inclusion, and external dependency declarations SHALL be preserved, and the resulting package set SHALL support isolated NuGet consumer restore and build.

#### Scenario: Inspect a clean package output directory

- **WHEN** all packages are built with the default output configuration into an initially empty package output directory
- **THEN** exactly nine ordinary `.nupkg` files appear together in the root `artifacts/packages/` directory
- **AND** each contains its expected ID, selected version, `net10.0` library assets, README, and internal dependency declarations

#### Scenario: Pack an individual library

- **WHEN** a maintainer packs one library directly without overriding the output directory
- **THEN** its package is written to the same repository-root `artifacts/packages/` directory

#### Scenario: Restore without cached repository packages

- **WHEN** a separate consumer references all nine packages at the selected version and restores with an empty isolated NuGet cache and the generated directory as a local source
- **THEN** restore and build succeed using the newly generated repository packages
- **AND** dependencies between those packages resolve at the selected release version
