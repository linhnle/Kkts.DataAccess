# Tasks

## 1. Core and relational package documentation

- [x] 1.1 Create the core package README using the design's structure and metadata; update its project to pack the local README once. Verify statements/examples against current public APIs and pack the project to inspect its README, description, tags, ID/version, and dependencies.
- [x] 1.2 Create the EntityFrameworkCore README and update its package metadata/local README entry. Explain its relationship to concrete providers and translation limitations; verify API signatures and inspect its packed README and nuspec without changing conditional core references.
- [x] 1.3 Create the Sqlite README and update its metadata/local README entry. Explain policy persistence separately from entity reads; verify documented setup against current APIs and inspect its packed README and nuspec.
- [x] 1.4 Create the SqlServer and MySql READMEs and update each project's metadata/local README entry. Explain entity-provider versus policy-store responsibilities and the actual EF provider dependencies; verify API signatures and inspect both packages' READMEs and nuspecs.

## 2. Optional integration documentation

- [x] 2.1 Create the DependencyInjection README and update its package metadata/local README entry. Verify current registration, trusted-context capture, and startup-validation examples against source; inspect the package and confirm it gains no EF dependency.
- [x] 2.2 Create the AspNetCore README and update its package metadata/local README entry. Verify Problem Details integration examples and framework prerequisites; inspect the package and confirm the existing ASP.NET Core framework reference is preserved.
- [x] 2.3 Update the DataProtection README and metadata, retaining its local pack entry. Verify the current token API, key persistence/sharing, at-rest protection, application isolation, subject/dataset binding, expiry, and fresh-authorization guidance; inspect the packaged document and nuspec.
- [x] 2.4 Update the Testing README and metadata, retaining its local pack entry. Verify test-only usage, runner neutrality, fixture ownership/cleanup, missing-evidence handling, and non-certification guidance against current contracts; inspect the packaged document and confirm no runner/driver/EF dependency is introduced.

## 3. Documentation navigation and consistency

- [x] 3.1 Add the nine-package selection table to the root README while retaining the solution overview; verify all nine links resolve to local package READMEs and their role summaries agree with the design.
- [x] 3.2 Review all package READMEs for the common structure, transitive versus optional dependency guidance, accurate .NET/provider requirements, necessary namespaces and identified application scaffolding. Verify repository links use the confirmed default branch and absolute GitHub URLs; ensure original detailed docs remain intact and no unsupported claims or speculative badges were added.

## 4. Cross-package publication-readiness validation

- [x] 4.1 Pack all nine library projects in Release configuration using the existing toolchain and locked restore as needed. Inspect every archive and nuspec for exactly one root README matching its local source, intended descriptions/tags, unchanged IDs/versions/frameworks/dependencies, own assembly only, and no example/internal test content. Report explicit failures rather than treating pack success alone as sufficient.
- [x] 4.2 Restore and build temporary external consumers covering all nine locally packed packages with no project references, an isolated package cache, and normal third-party sources. Keep Testing confined to a test/tool consumer; verify local inter-package resolution and the ASP.NET Core framework requirement.
- [x] 4.3 Compile the new quick-start C# examples in appropriate temporary consumer harnesses where practical, supplying documented application scaffolding. Record which examples compiled and explicitly list any unchecked examples with reasons; do not claim runtime database verification from compilation.
- [x] 4.4 Review the final diff to confirm only intended documentation/metadata changed and no public APIs, runtime code, dependency declarations, lock files, versions, licenses, or framework targets changed. Remove only temporary validation files created for this work and summarize all package/consumer/example checks and remaining blockers without publishing or committing.

## Validation results

- .NET SDK 10.0.401: all nine library projects packed in Release using `dotnet pack <project> --configuration Release -p:RestoreLockedMode=true --output <temporary-local-feed>`.
- All nine archives and embedded nuspecs inspected: one root README matching its source, intended descriptions/tags, preserved IDs/versions/frameworks/dependencies/license metadata, own assembly only, no examples or internal test content.
- All nine exact README C# examples compiled in external consumers against locally packed dependencies. Consumers used an isolated package cache and no project references; Testing was confined to its own test-helper consumer. No examples remain unchecked.
- Repository file/directory links and all nine root package-table links checked against the local tree and confirmed `main` default branch.
- `git diff --check` and strict OpenSpec validation passed. Runtime code, dependencies, lock files, framework targets, versions, and existing detailed docs were unchanged. No real-database runtime tests were run for this documentation/metadata-only change.
- Restore/pack/consumer builds reported existing NU1902/NU1903 advisories: SQL Server transitively uses System.Security.Cryptography.Pkcs 9.0.11; Data Protection transitively uses System.Security.Cryptography.Pkcs and System.Security.Cryptography.Xml 10.0.12. Builds succeeded, but dependency remediation remains a separate pre-publication concern; dependencies were not changed by this work.
- Temporary packages, consumers, isolated cache, and verification scripts were removed. Nothing was published or committed.
