# Kkts.DataAccess

.NET 10 record/field authorization integration using Kkts.Expressions 3.1.0 and Kkts.Dynamic 2.1.0. SQLite policy persistence and EF Core execution are separate from the core package.

```sh
dotnet restore Kkts.DataAccess.slnx --locked-mode
dotnet build Kkts.DataAccess.slnx --no-restore
dotnet test Kkts.DataAccess.slnx --no-build --filter 'Provider!=SqlServer&Provider!=MySql'
ASPNETCORE_ENVIRONMENT=Development dotnet run --project examples/EmployeeApp --urls http://127.0.0.1:5189
```

Open `/login` and choose Tenant 1 admin. Fixed synthetic identity login is disabled outside Development. A clean database seeds employees and a draft, with no active policy. Publish the saved draft. Manager sees active department-10 employees, HR sees all active tenant employees, employee sees its own row, and none has no grant. Salary is never granted. Repeat publication for Tenant 2 admin.

The designer edits nested typed conditions and independent field entries. Save uses the displayed draft revision. Preview uses fixed synthetic fixtures and sample roles without querying production data or changing the active pointer. Publication checks draft and active revisions. Audit history provides immutable versions for rollback. Validation/conflict diagnostics appear below the editor.

Default database: `employee.db`. Set `Database='Data Source=/absolute/path/example.db'` to isolate a clean walkthrough. For HTTP acceptance, start a fresh database on port 5189 and run `python3 scripts/example_acceptance.py` from the repository root. HTTP tests do not replace browser interaction checks.

[Policy contract](docs/policies.md), [Employee policy](docs/employee-policy.json), [Lifecycle](docs/lifecycle.md), [Reads](docs/reads.md), [Dependencies](docs/dependencies.md), and [Acceptance results](docs/acceptance.md), [Repositories](docs/repositories.md), and [Entity providers](docs/providers.md).

Phase 1 supports reads only. Writes/projection are Phase 2; record-dependent fields, masking, relationships and aggregation are Phase 3. Hidden columns may be materialized internally. Provider failures abort without unrestricted/local fallback. Stable ordering does not provide a data snapshot across batches.

Package consumption check:

```sh
dotnet pack src/Kkts.DataAccess/Kkts.DataAccess.csproj -o artifacts/packages
dotnet restore examples/EmployeeApp/EmployeeApp.csproj -p:UsePackedCore=true -p:RestoreAdditionalProjectSources="$PWD/artifacts/packages" --force-evaluate
dotnet build examples/EmployeeApp/EmployeeApp.csproj -p:UsePackedCore=true --no-restore
dotnet restore Kkts.DataAccess.slnx --force-evaluate
```

UsePackedCore switches the adapter to the packed core dependency. Neither upstream library is modified.

## Formatting

Use the pinned local [CSharpier](https://csharpier.com/) tool for C# and XML
(including `.csproj`, `.props`, and `.slnx` files) from the repository root:

```sh
dotnet tool restore
dotnet csharpier format .
dotnet csharpier check .
```

C# formatting uses four spaces, a 100-column print width, and LF line endings.
XML uses CSharpier's default two-space indentation and strict whitespace handling.
Build outputs and dependency caches are excluded by `.gitignore`.

Use the pinned [Prettier](https://prettier.io/) development dependency for JavaScript:

```sh
npm ci
npm run format
npm run format:check
```

JavaScript formatting uses two spaces, single quotes, a 100-column print width,
and LF line endings. The npm scripts target `.js`, `.mjs`, `.cjs`, and `.jsx` files;
`.prettierignore` excludes build outputs and dependency caches. Neither formatting
command changes JSON files.

In VS Code, install the recommended **CSharpier - Code Formatter** and
**Prettier - Code formatter** extensions. Workspace settings enable formatting on
save with CSharpier for C#/XML and Prettier for JavaScript/JSX.
Run `npm run format:check` to check C#, XML, and JavaScript together.

### Pre-commit formatting check

After cloning, run `dotnet tool restore` and `npm ci`. The npm `prepare` script
installs the Husky Git hooks automatically. Each commit runs `npm run format:check`
and is blocked if either CSharpier or Prettier fails.

The hook checks the entire working tree, including unstaged files. It does not
reformat or stage files. To fix formatting, run `dotnet csharpier format .` and
`npm run format`, then stage the intended changes and commit again.
The .NET SDK, Node.js, and npm must be available to the Git process.
