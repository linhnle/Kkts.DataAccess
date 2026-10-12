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

EmployeeApp uses the optional [dependency injection integration](docs/dependency-injection.md):
scoped/keyed repositories and asynchronous trusted-context capture with pooled,
independently owned EF contexts. Startup validates structure without requiring an
active policy or provisioning the database. Direct constructors remain supported.

Default database: `employee.db`. Set `Database='Data Source=/absolute/path/example.db'` to isolate a clean walkthrough. For HTTP acceptance, start a fresh database on port 5189 and run `python3 scripts/example_acceptance.py` from the repository root. HTTP tests do not replace browser interaction checks.

Read inputs are operation-specific: list accepts selection/filter/sort/offset/limit,
count accepts only filter (`{}` for an unfiltered count), detail carries its key
plus selection/filter, and export accepts selection/filter/sort with host-controlled
batching. Unknown members, action/identity overrides, and inapplicable options
are rejected rather than ignored. The shared `ReadRequest` and old detail overload
are removed in this pre-release breaking change. Constructors and trusted adapters
remain unchanged; list metadata is nullable and standalone count now returns
`{"policyVersion":1,"total":2}` rather than a number. Numeric consumers use
`ReadCount.Total`. See the [migration examples](docs/reads.md#breaking-request-migration).

Offset and cursor lists accept independent `includeTotal` (default true) and
`includeHasMore` (default false). Disabled metadata is explicit null. Disabling
total avoids the count query; requested has-more observes an extra authorized row,
not total arithmetic. For example,
`{"selection":["Name"],"limit":20,"includeTotal":false,"includeHasMore":true}`.
Count/detail/export reject these list-only flags. Separate count/page statements
do not promise a snapshot.

Forward cursor lists coexist with offsets at `POST /employees/cursor`. Supply
selection/filter/sort/limit and pass the opaque `nextContinuationToken` on the next
matching request; each page reports an exact authorized total only when requested.
Cursor lookahead remains mandatory for next-token generation, and either metadata
flag may change on resume without altering row-query compatibility. Tokens are
encrypted and bound to fresh authorization, the authenticated subject/context,
query, schema, policy version and dataset. Expiry/mismatch requires restart, not
historic-policy replay. Never log tokens or trusted boundary/context values.

The example registers the optional Data Protection integration once. `CursorKeys`
selects a host-managed persistent key directory (Development default:
`examples/EmployeeApp/.cursor-keys/`); `CursorApplication` selects application
isolation, and `CursorDataset` supplies an opaque dataset label (default includes
the synthetic provider identity). Use persistent/shared keys and at-rest protection
for deployed hosts, retain decryptable keys through token lifetime, and change
the dataset label when routing, mappings, collation or seek semantics change.
Defaults are a fifteen-minute non-sliding lifetime and 16-KiB encoded token limit.
See [cursor setup](docs/reads.md#forward-cursors) and the
[protection package](src/Kkts.DataAccess.DataProtection/README.md).

Retrieval requires database scalar projection by default; EmployeeApp explicitly
uses `ReadProjectionMode.Required`. Custom adapters implement
`IEntityProjectedReadSession<T>` for offset/detail and
`IEntityProjectedSeekReadSession<T>` for cursor/export. Projected seek need not
implement full-entity seek. Hosts can deliberately opt into
`AllowEntityMaterialization` for missing capabilities; capable adapters still
project and projection failures never retry entity loading. Standalone count
needs no projected capability. See [adapter migration](docs/repositories.md).

Export uses private keyset boundaries and projected seek. Exports
retain one captured policy/context, need no public-token configuration, expose no
resume token, and cannot fall back to offsets. Default keyset traversal is not a
snapshot or a guarantee against changes to sort values.

Hosts can opt into independent export consistency and positive row/duration
bounds through `ExportReadConfiguration`. EmployeeApp reads trusted startup
settings `Export:PolicyConsistency`, `Export:DataConsistency`, `Export:MaxRows`
and `Export:MaxDuration`; client JSON cannot select them. Defaults remain captured
policy/best-effort with no caps. For example, after provisioning SQLite WAL:

```sh
ASPNETCORE_ENVIRONMENT=Development \
Database='Data Source=/absolute/path/example.db;Cache=Private' \
Export__PolicyConsistency=AbortOnPolicyChange Export__DataConsistency=Snapshot \
Export__MaxRows=10000 Export__MaxDuration=00:05:00 \
dotnet run --project examples/EmployeeApp --urls http://127.0.0.1:5189
```

Use the [provider prerequisites](docs/providers.md#export-wide-snapshots);
the library does not enable database settings. Capped exports are successful
prefixes without truncation metadata. Policy changes/deadlines return explicit
403/504 Problem Details before response start; once NDJSON output starts, they abort
the stream without appending an error row. Already delivered prefixes are not
retractable. Consumers must dispose enumerators and treat interrupted downloads
as incomplete. See [export lifecycle](docs/reads.md#export-lifecycle).

[Policy contract](docs/policies.md), [Employee policy](docs/employee-policy.json), [Lifecycle](docs/lifecycle.md), [Reads](docs/reads.md), [Structured errors and optional HTTP integration](docs/errors.md), [Dependencies](docs/dependencies.md), and [Acceptance results](docs/acceptance.md), [Repositories](docs/repositories.md), and [Entity providers](docs/providers.md).

This release supports reads and mapped scalar database projection. Writes,
record-dependent fields, masking, relationships and aggregation remain deferred.
Ordinary hidden predicate/order columns stay out of reader values. Cursor/export
may retrieve minimal trusted continuation sidecars, never DTO properties, with
public token boundaries encrypted; empty nonseek output uses a constant marker.
Explicit compatibility may materialize hidden entity data. Provider failures
abort without unrestricted/local/entity-loading retries. Stable ordering and
projection alone do not provide a data snapshot across batches.

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
