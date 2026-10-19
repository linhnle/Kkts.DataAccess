# Proposal

## Why

The solution has nine independently consumable library projects, but most packages currently reuse the solution README or a shared provider document and lack package-specific discovery metadata. Dedicated NuGet-facing documentation will help consumers choose the right integration, install it, and understand its prerequisites without implying unsupported features.

## What Changes

- Create or update a package-local README for each of the nine libraries, with a consistent structure, accurate installation and quick-start guidance, limitations, and related documentation.
- Set package-specific descriptions and semicolon-separated tags, and pack each local README at the package root exactly once.
- Preserve the existing Data Protection key-management guidance and testing kit's test-only, runner-neutral, non-certification warnings.
- Add a compact package-selection table to the root README while retaining it as the solution overview.
- Validate all nine generated packages and compile-check documentation examples where practical, reporting any unchecked examples explicitly.

## Capabilities

### New Capabilities

None. This is a documentation and package-discovery metadata change; `skip_specs: true` applies.

### Modified Capabilities

None. Existing authorization, host-integration, provider, and adapter-conformance requirements remain unchanged.

## Impact

- Affects `src/<PackageName>/README.md`, the nine library project files, and the root README.
- Existing package IDs, versions, framework targets, dependencies, licenses, public APIs, and runtime behavior remain unchanged.
- Seven library projects currently pack a shared README; DataProtection and Testing already pack local READMEs and will retain their safety guidance.
- Examples and the internal test suite are not publication targets. No metapackage, DLL bundling, publication automation, package upload, or commit is included.
- Package validation requires the existing .NET 10 toolchain and local package-consumption checks; no real database deployment is needed for this change.
