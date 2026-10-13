# Model/provider capability validation

Profiles supplement, never replace, resource schemas, grants and mandatory
authorization. They do not remove operators, broaden permissions, or grant
unregistered fields. Structured errors and optional DI startup integration are
prerequisites of the supplied schema-aware registration path.

## What the profile means

`IReadProviderCapabilities` reports **Supported**, **Unsupported** or
**Unverified** separately for output, each database predicate operator, offset
ordering, seek ordering and snapshot shape. Supported describes an individual
binding under the installed implementation's rules, not arbitrary SQL proof.
`ComposedQuery` is always Unverified. Unknown providers and converter-dependent
comparisons are not broadly certified. Unverified uses can attempt ordinary
provider execution; known unsupported uses fail explicitly.

Profiles capture provider/implementation version, static model identity,
registered CLR/store mappings, converter type, precision/scale, mapped column,
configured collation and an ordered key list. Collections/results are immutable;
profiles contain no request context, connection credentials or sample identities.
Missing model collation metadata does not certify database-default collation.
Converter type identity does not prove arbitrary converter-expression semantics,
and model identity does not prove connection routing or live database schema.
Current resource execution is single-key; ordered tuple metadata is tested without
claiming end-to-end composite reads before composite-key support is implemented.

Registration uses an owned temporary context without opening an entity connection
or querying sample data. Shared actual-model mapping, seek and snapshot-shape
rules are reused at runtime. Output can support a mapped converter even when
seek cannot; offset support never implies seek support. Native SQLite
DateTimeOffset ordered uses are rejected. Unsupported advertised output/sort/key
uses fail registration; Count-only scope does not demand irrelevant output/seek.
See [operation masks and helper migration](providers.md).

## Policy targets and activation

`PolicyValidator` and `PolicyCompiler` accept optional `capabilities` collections.
No profiles means provider-neutral structural/CLR/schema validation, **not**
translation certification. Database-condition leaves receive exact operator paths;
subject-context conditions remain CLR-only. Select and Sort grants are checked
only for uses declared by each target. Mandatory constraints are checked too.

An EF registration contributes its actual model profile automatically at DI
startup; `WithCapabilities(...)` can add multiple explicit target profiles.
Every target is retained and checked. Compiler cache and cursor validation
identities include the immutable profile set so incompatible templates cannot
be reused. Hosts validating the same policy for multiple databases must supply
every intended target, not whichever provider registered most recently.

Pass the same profiles to SQLite store `PublishAsync` and `RollbackAsync` through
the optional trailing `capabilities` parameter (after cancellation), and to draft
validation/preview. Invalid activation leaves the active pointer and audit
unchanged. EmployeeApp does this for save, preview, publication and rollback.
Neutral drafts/publication remain possible. A provider-bound compiler revalidates
captured definitions before querying; a configured EF session also checks the
captured definition for its actual profile when the compiler was neutral,
including incompatible rules that do not apply to the current subject.

## Runtime boundaries

Protected predicate, output, sort and continuation bindings are preflighted
before optional totals or retrieval. A model-profile mismatch fails closed.
Residual composed-query translation errors retain their original failure:
no local authorization evaluation, coercion, offset substitute, unrestricted
query or entity fallback is introduced.

Offset uses provider-native null placement and database comparisons. Seek uses
explicit null-first ascending/null-last descending and provider-side lexicographic
comparisons. Collation and precision remain database/mapping controlled, never
substituted with CLR comparison. EF SQLite 10.0.12 retains exact decimal ordering
via `EF_DECIMAL`; decimals are not converted to double. SQL Server/MySQL fixture
evidence concerns their actual mapped decimal(18,2), nullable and text combinations,
not all production precision/collation configurations.

Snapshot shape validation covers ordinary keyed single-table mappings and static
model restrictions. WAL/private cache/isolation, SQL Server snapshot settings,
MySQL storage engine, actual table sources and transaction liveness still require
runtime checks. The library never changes those settings.

Direct low-level EF adapters and neutral custom adapters remain supported with
honest runtime-only scope. Custom adapters may implement the optional profile and
protected-plan validator seams described in [repositories](repositories.md).
For async startup ordering and lifetimes see [DI](dependency-injection.md);
for executed evidence see [acceptance](repository-acceptance.md).
