# Proposal

## Why

All nine NuGet library projects repeat the same release version, and four packed-core references repeat it again, making a coordinated release easy to get wrong. The documented packaging workflow only packs core, so maintainers lack one documented command that produces the complete release set in one directory.

## What Changes

- Define the repository's own NuGet release version once in shared MSBuild configuration, initially preserving `0.1.0`.
- Make all nine library packages and existing `UsePackedCore` references consume that shared version.
- Support one Release-mode solution pack command that restores, builds, and writes all library packages to the repository-root `artifacts/packages/` directory.
- Explicitly exclude examples and the unit-test project from packaging while retaining `Kkts.DataAccess.Testing` as a distributable library.
- Document version updates, complete packaging, output location, and the existing packed-core consumption workflow.
- Verify actual package identities, versions, internal dependency versions, and isolated consumer restore, not just successful command exit.

## Capabilities

### New Capabilities

- `package-release-build`: Shared release versioning and one-command local NuGet packaging with a centralized output directory.

### Modified Capabilities

None. Runtime authorization, adapters, and host integration contracts are unchanged.

## Impact

- Shared `Directory.Build.props`, nine `src/` project files, and `examples/EmployeeApp/EmployeeApp.csproj`; verify existing non-packable test/example projects.
- Root `README.md` packaging instructions and focused packaging validation.
- Existing solution, .NET 10 SDK, MSBuild, and NuGet pack tooling; no new build framework, third-party dependency upgrades, publishing credentials, or automatic publishing.
- Assumption: "all packages" means the nine repository-produced NuGet packages, not centralizing third-party `PackageReference` versions or the private npm tooling package.
- Default version remains unchanged; future release edits and supported `-p:Version=...` overrides must keep package references aligned.
