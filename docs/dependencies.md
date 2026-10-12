# Verified dependency surface

Target: .NET 10 (`net10.0`), SDK 10.0.401. Required dependencies: Kkts.Expressions 3.1.0 and Kkts.Dynamic 2.1.0, selected by the library author for this project. Both packages ship .NET Standard 2.0 assemblies and have passed the integration checks on .NET 10. No upstream changes are required.

Official sources: https://github.com/linhnle/Kkts.Expressions and https://github.com/linhnle/Kkts.Dynamic. Package metadata and the official READMEs bundled with these exact releases were inspected after restore. Source commits: Expressions `c94d3cc46db6987f1ace20144c210939dd8f3187`; Dynamic `72d315ec7c88bb3517d3b50fc909e4ed337f1e13`.

Expressions exposes typed `QuerySchema<T>.Field(...).Build()`, `ExpressionQueryContext`, `FilterNode`, `FilterValue`, and `FilterTreeJson`, alongside `Filter.BuildPredicate<T>()` and typed `ExpressionExtensions.And<T>()` / `Or<T>()`. Tests verify public aliases, nested AND/NOT, and rejection of unregistered entity names. Use the typed schema and nested tree APIs behind authorization validation rather than duplicating the upstream query schema/filter implementation. The compiler can compose typed expression trees through this API. Do not expose the upstream arbitrary string parser, navigation resolution or variable resolver directly to clients. Conversion and null handling belong to the integration layer.

Dynamic exposes `DynamicAssembly.DeclareClass(name, entityType, bindings)`, `Build()`, `Class.GetBuiltType()`, and `Mapper.MapFromEntityToDto(dto, entity)`. Typed `DeclareClass<T>()`, `Class.CreateDto(entity)` and `Class.GetMetadata().ResolveField(...)` are also verified. Only approved scalar bindings use `OneWayToDto`. DTO shape creation and System.Text.Json serialization are tested; database projection is outside Phase 1.

SQLite provider: Microsoft.EntityFrameworkCore.Sqlite 10.0.12. Tests: Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4. Exact transitive versions are recorded in checked-in package lock files.

Run `dotnet restore Kkts.DataAccess.slnx --locked-mode`, `dotnet build Kkts.DataAccess.slnx --no-restore`, then run the real-provider test command from [provider setup](providers.md). For SQLite/core-only checks use `dotnet test Kkts.DataAccess.slnx --no-build --filter 'Provider!=SqlServer&Provider!=MySql'`; report the remote suites as not run.

Acceptance: solution restore and build succeeded with zero warnings/errors; all 27 tests passed, including predicate construction, typed public fields, nested filters, DTO mapping, metadata and serialization. See [Phase 1 acceptance](acceptance.md) for the complete matrix and browser/package evidence.
