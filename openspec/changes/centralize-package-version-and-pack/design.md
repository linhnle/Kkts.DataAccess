# Design

## Context

See [proposal.md](proposal.md) for motivation and [package release requirements](specs/package-release-build/spec.md) for the observable contract.

- Root `Directory.Build.props` already applies `net10.0`, nullable settings, a repository-local package cache, and lock-file generation to the whole solution.
- `Kkts.DataAccess.slnx` includes nine distributable libraries, three examples, and one xUnit test project. Every library declares `Version` as `0.1.0`.
- EF Core, SQLite, and Data Protection conditionally replace their core project reference with a literal-version package reference when `UsePackedCore=true`; EmployeeApp also adds a literal core package reference in that mode.
- AdapterConformance, CustomReadAdapter, and the unit-test project explicitly declare `IsPackable=false`. EmployeeApp uses the Web SDK but does not explicitly declare its packaging intent.
- The README currently packs only core before its packed-core check. `artifacts/`, `.nupkg`, and `.snupkg` are already ignored. The SDK is pinned to `10.0.401`; provider CI currently restores in locked mode, builds, and tests, without a packaging job.
- Existing capability specs cover runtime behavior, not package-release tooling; this change adds a separate build contract without changing those specs.

## Goals / Non-Goals

**Goals:**

- Use normal SDK/MSBuild property evaluation and solution packaging, with no separate package inventory to maintain in a build orchestrator.
- Keep initial artifacts compatible with current IDs, framework, dependency graph, README metadata, and version.
- Retain individual project packing and the existing optional packed-core workflow.

**Non-Goals:**

- Central Package Management for third-party dependencies, provider upgrades, automatic version calculation, or Git-tag-driven releases.
- NuGet publication, signing, symbols/source packages, release CI, or a new build tool.
- Automatically cleaning historical artifacts, changing runtime APIs, or running database-dependent tests as part of packing.

## Decisions

### 1. Use shared `Version` as the release source of truth

Add the initial `Version` value `0.1.0` to root `Directory.Build.props`, remove all nine library-local declarations, and replace all four literal packed-core reference versions with `$(Version)`. Let the SDK derive `PackageVersion` and assembly metadata normally. A command-line `-p:Version=...` is the supported temporary override and applies to both outputs and explicit internal package references.

This avoids an unnecessary separate version file or custom release property. Do not set `AssemblyVersion` to an arbitrary NuGet version, which could include a prerelease suffix. A `PackageVersion`-only override is not the documented release override because it would not also update explicit packed-core references. External `PackageReference` versions remain untouched.

Alternatives: Central Package Management would address dependency versions rather than the libraries' own release version. A generated/versioning-tool approach adds machinery without addressing an additional requirement.

### 2. Use the existing solution as the build entry point

Document this complete build command:

```sh
dotnet pack Kkts.DataAccess.slnx --configuration Release -p:RestoreLockedMode=true
```

Default SDK behavior performs restore and build; do not require `--no-build` or a separate bootstrap pack. Leave `UsePackedCore` disabled during the complete package build, so internal dependencies come from project references and do not require already-published versions of the release being built.

Explicitly mark EmployeeApp non-packable and retain the other non-packable examples/tests. All nine source libraries, including the distributable Testing kit, remain packable. The solution may build non-packable projects but must not produce packages for them.

A wrapper script, custom aggregate project, or per-project command loop would duplicate SDK behavior or solution membership; no such entry point is necessary.

### 3. Centralize only package output

Define a default `PackageOutputPath` in root `Directory.Build.props` using `$(MSBuildThisFileDirectory)artifacts/packages`, conditional on no existing value. The root-anchored absolute path avoids per-project interpretation of a relative path and works for both solution and individual project packing.

Keep ordinary `bin/obj` locations and existing ignore rules unchanged. Standard explicit output overrides remain available; no automatic deletion occurs. Release verification uses an initially empty, explicitly scoped directory so old packages cannot make an incomplete run look complete. A failed run can leave partial artifacts and must still return failure.

### 4. Validate packages as products, not merely builds

Add a focused, repeatable package verification script under `scripts/`, using the repository's existing Python standard-library scripting precedent rather than a new dependency. Have it inspect NuGet ZIP/XML contents and compare against the nine expected package identities and the supplied expected version. It must fail explicitly on missing/extra packages in the verification directory, manifest/version/internal dependency mismatches, missing `net10.0` assets, or missing README content.

Run the package command for the default version and a distinct prerelease override. Validate internal dependency declarations semantically: SDK-generated dependency versions may be lower-bound ranges rather than exact bracketed pins. Verify every dependency with a repository package ID has the selected version as its version/lower bound.

Use a temporary package-only .NET consumer referencing all nine packages, with an isolated empty NuGet cache, to restore against the new local output plus required external sources and build. Separately validate the existing EmployeeApp `UsePackedCore=true` restore/build with the same version and local source. Restore normal project-reference mode afterward; never leave mode-specific lock-file changes behind or overwrite unrelated user edits.

A source-text assertion alone would miss MSBuild import order, pack filtering, stale-cache consumption, and generated dependency metadata.

## Risks / Trade-offs

- [A project-local version overrides shared props] -> Remove all nine declarations; inspect emitted manifests for default and override builds.
- [Packed-core restore still requests the old release] -> Replace all four literals and verify the existing mode at a distinct version.
- [Lock files differ between project-reference and packed-core modes] -> Use `--force-evaluate` only for the explicit consumption check, preserve pre-check state, and restore the normal locked solution baseline afterward. Update checked-in locks only if normal configuration legitimately requires it.
- [Old package caches or output files mask a missing build] -> Use an initially empty scoped output directory and isolated consumer cache; compare exact package membership.
- [Solution packing builds examples and tests] -> Accept the existing solution graph for simplicity; `IsPackable=false` controls artifacts, and packing does not execute provider tests.
- [New internal versions are unavailable on public feeds] -> Build the complete package set with project references first, then use the local package source for consumption checks.
- [Prerelease version normalization differs from input spelling] -> Use a stable normalized verification value such as `0.1.1-localcheck`.

## Migration Plan

1. Introduce shared version/output properties, remove duplicate release literals, align packed-core references, and declare EmployeeApp non-packable.
2. Add focused artifact verification and update the README with release editing, the complete command, output behavior, failure/stale-output caveats, and consumption checks.
3. Validate formatting, normal locked restore/build, exact nine-package output at both default and temporary versions, and both consumption paths.
4. Retain `0.1.0` in checked-in configuration after verification. No deployment or package publication occurs.

Rollback consists of reverting only this change's configuration, project, validation-script, and documentation edits. No runtime data migration or external release action is involved.
