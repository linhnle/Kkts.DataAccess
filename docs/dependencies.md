# Verified dependency surface

Target: .NET 10 (`net10.0`), SDK 10.0.401. Required dependencies: Kkts.Expressions 3.1.0 and Kkts.Dynamic 2.1.0, selected by the library author for this project. Both packages ship .NET Standard 2.0 assemblies and have passed the integration checks on .NET 10. No upstream changes are required.

Official sources: https://github.com/linhnle/Kkts.Expressions and https://github.com/linhnle/Kkts.Dynamic. Package metadata and the official READMEs bundled with these exact releases were inspected after restore. Source commits: Expressions `c94d3cc46db6987f1ace20144c210939dd8f3187`; Dynamic `72d315ec7c88bb3517d3b50fc909e4ed337f1e13`.

Expressions is used in production through the internal `ExpressionsAdapter<T>`, not just And/Or composition. Resource bindings become immutable typed `QuerySchema<T>` selectors under generated aliases, with exact ordinal public-name checks retained in DataAccess. `ExpressionQueryContext.ValidateFilterTree`, `FormatFilterTree`, and metadata-only `AnalyzeExpression` validate each condition leaf and preserve its original policy path. Formatting and escaping are delegated to the upstream codec; runtime predicates are built from structured `FilterNode`/`FilterValue` trees through `TryBuildPredicate<T>`. No arbitrary client expression endpoint is exposed.

Converted literal and trusted-context values enter a request-owned `VariableResolver` under generated keys. Values, effective grants, and bound predicates are never stored in shared schemas. Policy contexts include host constraint fields independently of client grants; client contexts apply the effective Filter allowlist after DataAccess field authorization. Subject comparisons use operation-owned typed constant selectors after eager context checks, so metadata analysis does not invoke getters or resolve trusted values. DataAccess retains policy shape/limits, strict culture-independent conversion, context validation, grant/deny composition, and operation authorization.

Compatibility adaptations are intentionally small: value-type selectors and analysis operands are lifted to nullable types so explicit null predicates remain accepted for nonnullable fields; null checks and false-on-null guards are expressed as upstream filter nodes. Ordered and string comparisons against null become an upstream contradiction. String guards remain inside NOT to preserve its truth table. Membership uses upstream structured collections with typed bound elements, including null. Analysis operands declare the same lifted type as selectors because 3.1.0 analysis otherwise rejects some nullable/nonnullable Boolean, enum, GUID, and temporal comparisons that construction accepts. Tests cover all supported scalar families and operators, precision, temporal values, escaped strings, null/NOT/membership, aliases, limits, and context isolation.

Shared EF execution delegates validated multi-key ordering to the upstream `IQueryable<T>.OrderBy(IEnumerable<OrderByInfo>)` extension after the repository appends its stable key. `OrderByInfo.Property` comes only from registered `PropertyInfo.Name`, never a raw client alias. Ascending/descending keys, repeated keys, stable ties, and pagination retain their existing behavior. The EF assembly receives the pinned Expressions dependency transitively from core; no additional package reference or version change is needed. Custom adapters retain the provider-neutral plan contract. Database execution remains provider-owned, and translation failures have no unrestricted or local authorization fallback. Upstream QueryPolicy enforces bounded expression construction; it does not grant data access.

Dynamic exposes `DynamicAssembly.DeclareClass(name, entityType, bindings)`, `Build()`, `Class.GetBuiltType()`, and `Mapper.MapFromEntityToDto(dto, entity)`. Typed `DeclareClass<T>()`, `Class.CreateDto(entity)` and `Class.GetMetadata().ResolveField(...)` are also verified. Only approved scalar bindings use `OneWayToDto`. DTO shape creation and System.Text.Json serialization are tested; database projection is outside Phase 1.

Kkts.Dynamic 2.1.0 assemblies accept declarations only before `Build()`.
Disposal invalidates their `Class` wrappers, but a retained built `Type` remains
usable with `Activator.CreateInstance` and `Mapper.MapFromEntityToDto`.
`DependencyTests.Retained_dto_type_maps_after_builder_disposal` verifies mapping
and serialization after disposal, as well as rejection of disposed wrapper access.
The DTO cache retains built types, not builders or `Class` wrappers; see
[cache ownership](reads.md#dto-shape-reuse).

SQLite provider: Microsoft.EntityFrameworkCore.Sqlite 10.0.12. Tests: Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4. Exact transitive versions are recorded in checked-in package lock files.

`Kkts.DataAccess.DataProtection` optionally references
Microsoft.AspNetCore.DataProtection.Extensions 10.0.12; core still has no ASP.NET
reference. Its protocol-specific protector provides authenticated encryption.
Hosts own persistent/shared keys, at-rest protection, application isolation and
rotation; see its [key-management documentation](../src/Kkts.DataAccess.DataProtection/README.md).
Seek ordering deliberately uses provider-translatable conditional null ranks and
lexicographic comparisons over validated mapped properties. Unlike unchanged
offset ordering, these expressions need explicit null placement and boundary
semantics; they cannot be delegated to the property-name-only ordering helper.
No upstream dependency is changed or authorization evaluated locally.

Run `dotnet restore Kkts.DataAccess.slnx --locked-mode`, `dotnet build Kkts.DataAccess.slnx --no-restore`, then run the real-provider test command from [provider setup](providers.md). For SQLite/core-only checks use `dotnet test Kkts.DataAccess.slnx --no-build --filter 'Provider!=SqlServer&Provider!=MySql'`; report the remote suites as not run.

Expression-query integration acceptance: locked solution restore and build passed with zero warnings/errors; SQLite/core passed 75 tests with one opt-in performance measurement skipped, and real SQL Server/MySQL passed all 14 tests. Formatting and strict OpenSpec validation passed. See [repository acceptance](repository-acceptance.md#expression-query-integration) for commands, null/ordering parity, production dependency inspection, and current evidence. Earlier browser/package checks remain in [Phase 1 acceptance](acceptance.md); they were not rerun for this integration.
