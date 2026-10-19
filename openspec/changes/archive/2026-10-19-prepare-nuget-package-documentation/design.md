# Design

## Context

See [proposal.md](proposal.md) for motivation and scope.

All nine libraries currently target `net10.0` through `Directory.Build.props` and declare version `0.1.0`. Core references Kkts.Expressions and Kkts.Dynamic; EF integration depends on core, and the three entity providers depend on EF integration. The DI, ASP.NET Core, Data Protection, and Testing integrations are separate optional dependencies on core.

Seven projects pack either the root README or `docs/providers.md`; only DataProtection and Testing already have local READMEs. Several projects have descriptions but none declare package tags. Conditional `UsePackedCore` references exist in some projects and must remain intact.

The repository origin is `linhnle/Kkts.DataAccess`, with local origin HEAD resolving to `main`. Existing docs distinguish SQLite policy storage from entity providers, document test-only conformance limitations, and retain host-owned security responsibilities. Existing host-integration and adapter-conformance specs require no changes.

## Goals / Non-Goals

**Goals:**
- Make each NuGet README independently understandable without duplicating the entire solution manual.
- Use a consistent information hierarchy while retaining package-specific caveats.
- Make packaged README content, discovery metadata, and dependency preservation directly verifiable.

**Non-Goals:**
- No new runtime functionality, API corrections, dependencies, framework support, release versions, licensing changes, or build infrastructure.
- No new provider support, native database row-level-security claims, or adapter certification.
- No automated publication, metapackage, package upload, or commit.

## Decisions

### 1. Package-local documents with shared structure

Create or update `src/<PackageName>/README.md` for all nine packages. Include purpose, when to use or omit the package, requirements/dependencies, `dotnet add package <PackageName>`, a minimal API-verified quick start, limitations/host responsibilities, and related packages/documentation. Omit only genuinely inapplicable sections.

Use necessary namespaces and explicitly identify application-defined models, schemas, contexts, and prerequisites. Do not present fragments requiring undeclared application infrastructure as standalone runnable programs. Do not invent convenience APIs to shorten examples. Reuse current public examples and verify signatures against source during implementation; documentation is not authority when source disagrees.

Keep the root README as the solution overview, adding a compact table linking to all nine local READMEs. Retain detailed docs in place. This is preferable to continuing to embed the solution walkthrough or the same provider document into unrelated packages.

### 2. Explicit package-specific guidance

| Package suffix | Required distinctions |
| --- | --- |
| Core (`Kkts.DataAccess`) | Schema-controlled record/field authorization and provider-neutral contracts; no bundled database adapter; library enforcement is not native database row-level security. |
| EntityFrameworkCore | Shared relational read adapter, projection and traversal; relationship to provider packages, without claiming universal translation support. |
| Sqlite | SQLite policy persistence and SQLite entity reads are separate roles, even when used together. |
| SqlServer | SQL Server entity integration does not supply SQL Server policy persistence; describe the current separate policy-store setup. |
| MySql | MySQL entity integration uses the existing Oracle EF provider, not an assumed alternative; describe the separate policy-store setup. |
| DependencyInjection | Current scoped registration, trusted context capture, and startup validation; the package itself introduces no EF dependency. |
| AspNetCore | Problem Details translation and shared-framework requirement; installation does not create endpoints or automatically enforce authorization. |
| DataProtection | Preserve authenticated encryption, persistent/shared keys, application isolation, subject/dataset binding, expiry, and fresh authorization; hosts own key protection and rotation. |
| Testing | Prominent test-only and runner-neutral guidance, isolated fixture ownership and cleanup, missing-evidence limitations, and no certification/database-enforcement guarantee from passing output checks. |

State that NuGet installs required dependencies transitively. Identify optional packages separately and include them in example prerequisites when actually used.

### 3. Package metadata remains explicit in each project

Use the following proposed descriptions and semicolon-separated tags, correcting wording only if verified implementation contradicts it:

| Package | Description | PackageTags |
| --- | --- | --- |
| Kkts.DataAccess | Schema-controlled record- and field-level authorization for .NET data access, with provider-neutral protected read contracts. | data-access;authorization;access-control;row-level-security;field-level-security;policy;dotnet |
| Kkts.DataAccess.EntityFrameworkCore | Shared Entity Framework Core relational adapters for Kkts.DataAccess protected reads, scalar projection, and keyset traversal. | data-access;authorization;entity-framework-core;efcore;relational;projection;keyset-pagination |
| Kkts.DataAccess.Sqlite | SQLite policy persistence and Entity Framework Core entity-read integration for Kkts.DataAccess. | data-access;authorization;sqlite;entity-framework-core;efcore;policy-store |
| Kkts.DataAccess.SqlServer | SQL Server entity-read integration for Kkts.DataAccess using Entity Framework Core. | data-access;authorization;sqlserver;entity-framework-core;efcore;protected-reads |
| Kkts.DataAccess.MySql | MySQL entity-read integration for Kkts.DataAccess using Entity Framework Core. | data-access;authorization;mysql;entity-framework-core;efcore;protected-reads |
| Kkts.DataAccess.DependencyInjection | Dependency injection registration, scoped resource management, and startup validation for Kkts.DataAccess protected reads. | data-access;authorization;dependency-injection;di;hosting;startup-validation |
| Kkts.DataAccess.AspNetCore | ASP.NET Core Problem Details translation for Kkts.DataAccess authorization and read errors. | data-access;authorization;aspnetcore;problem-details;http;error-handling |
| Kkts.DataAccess.DataProtection | ASP.NET Core Data Protection integration for authenticated encryption of Kkts.DataAccess cursor continuation tokens. | data-access;data-protection;cursor-pagination;continuation-tokens;authenticated-encryption |
| Kkts.DataAccess.Testing | Test-only, runner-neutral conformance checks for Kkts.DataAccess protected read adapters. Passing checks does not constitute adapter certification. | data-access;authorization;testing;conformance;adapters;test-utilities |

Set `Description`, `PackageTags`, and `PackageReadmeFile=README.md` in each project. Pack the local README once at `/`, replacing shared README pack entries without deleting their source documents. Follow the existing project item style and check for duplicate SDK items through packing. Do not introduce a centralized metadata abstraction for nine distinct descriptions.

Preserve IDs (including those currently inferred from project names), versions, dependency declarations, license metadata, target frameworks, framework references, and conditional packed-core behavior. The row-level-security discovery tag does not justify claims of native database enforcement.

### 4. NuGet-compatible links

Use absolute `https://github.com/linhnle/Kkts.DataAccess/blob/main/...` links for files and `.../tree/main/...` for directories in package READMEs. Reconfirm the default branch if repository configuration changes before implementation. Convert retained relative links in existing local READMEs where needed. Keep root README links repository-relative. Do not add unverified badges.

Branch links can evolve after release; version-pinned links would require release/tag policy outside this change.

### 5. Validate artifacts rather than only source settings

Use existing .NET tooling to pack the nine library projects in Release configuration to a dedicated local validation directory, using normal project references and preserving dependency versions. Restore/build only as required by packing; honor checked-in lock files. Do not install dependencies preemptively or alter lock files to bypass failures.

Inspect all nine `.nupkg` archives and embedded nuspecs:
- Exactly one root `README.md`, identical to that package's local README.
- Correct README metadata, expected description and tags.
- Unchanged package ID, version, framework and dependency graph, including the ASP.NET Core framework reference where applicable.
- Own runtime assembly only, not bundled sibling project DLLs.
- No example application or internal test assembly/content.

Use temporary external consumer projects with the local package directory as an additional source and an isolated package cache, retaining normal sources for third-party dependencies. Cover all nine packages across suitable production and test consumers, with no project references; do not reference Testing from a production consumer. Restore/build to verify inter-package resolution and framework requirements. Compile documentation examples in minimal temporary harnesses where practical, supplying explicitly documented application scaffolding. Report each unchecked example and the reason, rather than treating it as verified. Remove only temporary files created for validation.

A full real-provider database test run is unnecessary for documentation/metadata-only changes. Report package verification and consumer compilation separately from runtime testing.

## Risks / Trade-offs

- [Existing docs can lag public APIs] -> Check exact signatures in source and compile quick-start examples where practical; record unchecked examples.
- [Generic provider guidance can imply unsupported policy stores or translation] -> State entity/policy separation and current provider limitations explicitly.
- [Shortening existing READMEs can remove safety guidance] -> Preserve Data Protection and Testing caveats as an explicit review checklist.
- [Pack can succeed with incorrect README content or bundled DLLs] -> Inspect archive entries, README bytes, and nuspec metadata for all nine outputs.
- [Consumer restore can accidentally use project references or cached packages] -> Use external temporary projects, local feed and isolated cache, without project references.
- [Build or network availability may block verification] -> Surface exact blockers; do not claim package readiness from source inspection alone.

## Migration Plan

Implement local documents, update their project pack entries/metadata, and add the root package table. Pack and inspect the outputs, then validate external consumption before considering publication readiness established. No deployed application migration is required. Rollback consists only of reverting these documentation and metadata edits; no database or runtime state is affected.
