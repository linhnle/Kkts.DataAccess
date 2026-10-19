# Tasks

## 1. Shared release version

- [x] 1.1 Define `Version` as `0.1.0` in root `Directory.Build.props` and remove the nine source-project `Version` declarations; verify MSBuild-evaluated `Version` and `PackageVersion` are `0.1.0` for every library and that only one default release literal remains in build configuration.
- [x] 1.2 Replace the four hard-coded core package versions in EntityFrameworkCore, SQLite, DataProtection, and EmployeeApp with `$(Version)`; verify evaluated packed-core package references select both the default version and `0.1.1-localcheck` when supplied with `-p:Version`, while third-party references remain unchanged.
- [x] 1.3 Document the shared version location and supported `-p:Version` override in the root README, including the distinction from third-party dependency versions; verify the documented property names against evaluated MSBuild properties.
- [x] 1.4 Run a locked solution restore and build in normal project-reference mode; verify success without unrelated lock-file or dependency changes.

## 2. Complete package build and artifact verification

- [x] 2.1 Set a root-anchored default `PackageOutputPath` to `artifacts/packages/` in shared props and explicitly mark EmployeeApp non-packable; verify all nine libraries remain packable and all three examples plus the unit-test project evaluate as non-packable.
- [x] 2.2 Add a focused Python standard-library package verification script under `scripts/` accepting an output directory and expected version; verify it checks exactly the nine expected IDs, manifest versions, internal dependency versions/lower bounds, `net10.0` DLL assets, and README inclusion, and emits actionable failures with nonzero exit status.
- [x] 2.3 Document `dotnet pack Kkts.DataAccess.slnx --configuration Release -p:RestoreLockedMode=true`, the centralized output, individual-project behavior, verifier usage, and the caveat that failed runs can leave partial or stale artifacts; verify the complete command runs without a separately packed core and produces exactly nine validated packages in an initially empty scoped output directory.
- [x] 2.4 Verify the artifact checker rejects a missing package, unexpected package, wrong expected version, and incorrect internal dependency metadata using isolated fixtures; preserve the real package output and remove only verification-created temporary fixtures.
- [x] 2.5 Verify direct packing of one library without an output override uses the same root `artifacts/packages/` directory; check generated outputs remain ignored by Git and normal `bin/obj` paths are unchanged.

## 3. Release and consumer integration checks

- [x] 3.1 In an isolated verification workspace, change only the shared default release value to a distinct version and run the documented complete pack command; verify all nine manifests and internal dependency versions follow that one edit, then remove the scoped workspace.
- [x] 3.2 Run the complete pack command with `-p:Version=0.1.1-localcheck` into an initially empty explicit verification output directory; verify exactly nine correctly versioned packages using the artifact checker and confirm checked-in configuration retains `0.1.0`.
- [x] 3.3 Create a temporary package-only `net10.0` consumer referencing all nine generated packages, restore against the generated local source with an isolated empty package cache plus required external sources, and build it; verify resolved internal package versions match the selected release and no source-project references exist.
- [x] 3.4 Update and execute the README packed-core restore/build workflow against the distinct release using `UsePackedCore=true`, the local source, and matching `Version`; verify the newly packed core is selected, then restore the normal lock-file/project-reference baseline without reverting unrelated user edits.
- [x] 3.5 Verify normal locked solution restore/build still succeeds after consumption checks, run the existing CSharpier check for changed XML, validate this OpenSpec change strictly, and remove only task-created temporary consumers, caches, fixtures, and alternate-version outputs.
