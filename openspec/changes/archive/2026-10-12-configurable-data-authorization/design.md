# Design

## Context

See `proposal.md` for motivation and the four delta specifications for behavior. Repository inspection found an MIT license, a .NET-oriented ignore file, OpenSpec configuration without project constraints, and no solution, source, tests, or existing specs. CodeGraph exists but reports no indexed code. The Kkts package sources and public APIs are not present; this design does not assume particular library methods or database translation support.

## Goals / Non-Goals

**Goals:** Make the enforcement boundary explicit, compile only schema-controlled operations, keep request context separate from cached plans, and deliver Phase 1 as a usable library plus database-backed example and basic administrator UI.

**Non-Goals:** Phase 1 does not persist entity writes, support record-dependent field sets, perform masking or relationship traversal, execute aggregation, or promise database column projection. Raw entities may be materialized inside the trusted service before mapping; only permitted DTOs cross the output boundary. Phase 2 and Phase 3 require separate changes and provider acceptance criteria.

## Decisions

### 1. Independent core and adapters

Create `Kkts.DataAccess` for schema registration, rule contracts, compilation, trusted-context binding, and read enforcement. Keep database policy storage and query-provider support in adapter projects; provide an ASP.NET Core example with a basic server-rendered administrator UI and an Employee resource. Use one relational adapter with SQLite for the executable example and translation tests. Supported framework and dependency versions will be pinned after explicit Kkts compatibility checks; do not change existing Kkts packages in Phase 1.

Directly adding authorization to either existing library would couple generic expression/mapping behavior to identity and policy persistence. A standalone integration layer preserves independence. SQLite establishes one concrete provider baseline rather than claiming universal `IQueryable` compatibility.

### 2. Versioned schemas and bounded rule AST

Schemas map public names to typed property bindings and per-operation eligibility. Policies use discriminated all/any/not/leaf nodes with registered operators and literal/context values. Define explicit null handling: equality and inequality support typed nulls; ordered comparisons against null evaluate false; null predicates use explicit supported operators. Operator adapters must preserve these semantics on the selected provider. Reject arbitrary expression strings, methods, and navigation paths. Enforce configurable limits for tree depth, nodes, literal size, and collection size at validation and binding.

Use Kkts.Expressions behind an adapter to construct typed predicates from validated nodes. First verify which construction APIs actually exist; fill any integration gaps inside this package with typed expression-tree composition. This preserves the intended dependency without assuming its parser safely accepts arbitrary administrator strings. A string-only rule language was rejected because it weakens typed UI validation and expands the execution surface.

### 3. Separate record rules and context-wide field entries

A version contains record rules and field permission entries. Both select subjects by trusted roles/groups and optional registered subject-context conditions. Record conditions govern rows only. Field entries contain operation-specific Allow/Deny field sets and cannot reference entity values. Their effective sets are `(union allows minus union denies) intersect schema eligibility`.

Normalize the original Employee example into a record Allow rule for DepartmentId equal to `user.departmentId` AND Status equal to Active, plus a separate context-wide field entry granting selection of Id, Name, and Email to the same subject scope. Query operations need separate grants. The original combined JSON is explanatory input, not a promised persistence wire format: validation rejects ambiguous conditional field grants and gives a diagnostic directing the author to separate entries.

Unioning fields from all record rules would let one rule disclose a field on rows authorized by another. Evaluating field entries only from trusted subject context gives Phase 1 a constant output shape and avoids that leakage. Per-record conditional fields remain a Phase 3 feature.

### 4. Immutable plans and trusted binding

Compile schema/rule metadata into immutable templates keyed by administrative scope, resource, schema version, policy version, and compiler/operator configuration version. Never cache a request-bound user predicate. At operation start, read the authoritative active version pointer and capture its immutable content; cache content/compilation by version, not an indefinitely stale active pointer. Bind a server-produced context snapshot for that request and validate all applicable context references before execution.

The row predicate is `Mandatory AND (Allow1 OR ... OR AllowN) AND NOT (Deny1 OR ... OR DenyN)`, with an empty allow set meaning denied and an empty deny set meaning false. Mandatory constraints are supplied by the host independently of editable policies; the multi-tenant example requires a trusted tenant and tenant constraint for every resource. Missing context, invalid versions, and compilation failures produce explicit closed failures. Dropping a broken deny rule is never a recovery strategy.

### 5. Closed read facade

Expose materialized read results and bounded export output through one facade accepting schema-controlled request objects. Internally retain `IQueryable` and entity objects; do not return a composable unfiltered query or let a client supply an executable predicate. Validate selection/filter/sort before execution, bind the authorized predicate, combine it with client conditions, then apply ordering and paging or count. Detail adds its key predicate and returns uniform not-found for excluded rows. Counts do not inspect selected DTO fields and do not count after pagination. Export is a Read operation over all filtered authorized rows with bounded batches and a captured version.

Require a host-declared unique stable ordering key, with a deterministic default and appended tie-breaker as necessary. Its internal use grants no client field permission. Enforce configurable pagination, export, and cancellation limits. Deterministic ordering does not promise snapshot consistency under concurrent entity changes; document the provider's isolation behavior.

Use Kkts.Dynamic through a mapping adapter to build only the approved public DTO shape after authorized materialization. Test serialization as well as mapping to catch entity/navigation leakage. Database-side `Select` is deferred: Phase 1 fetches trusted entity data internally and makes no claim that only permitted columns are retrieved. Translation failures abort; no fallback loads unrestricted rows for local authorization.

### 6. Transactional policy administration

The persistence adapter stores draft payload/revision, immutable published payload/schema version, active pointer/revision per administrative scope and resource, and append-only audit entries. Scope comes from host-authorized administration context, never solely from a request field. Transactions cover each successful mutation and its audit event. Publication validates the complete resource bundle, compares draft and active revisions, inserts the immutable version, and updates the pointer atomically. A rollback validates an existing version and switches the pointer with a new audit event; it never edits old content.

The designer uses schema metadata for typed controls, edits record rules and field entries separately, and displays diagnostics. Preview evaluates the same compiler against labeled synthetic subject context and bounded synthetic fixtures; it cannot accept unrestricted production query access or impersonate a production user. Explain matched rule identifiers, mandatory constraints, record outcomes, and effective field sets without claiming Phase 3's richer explanations.

Unversioned mutable rules were rejected because concurrent edits and multi-batch exports could otherwise observe inconsistent policy content. Preview using client context in production was rejected because it could bypass the trusted identity boundary.

## Risks / Trade-offs

- [Kkts APIs or target frameworks differ from expectations] → Pin compatible versions and verify predicate construction, mapping, and serialization with small integration checks before implementing dependent components.
- [Provider null or expression translation differences] → Execute authorization scenarios on the real SQLite relational provider and reject unsupported translation; add other providers only after equivalent evidence.
- [Stale active-version caches delay revocation] → Read the authoritative pointer at operation start; reuse immutable version content only. In-flight exports deliberately retain their captured version.
- [Entity materialization retrieves hidden columns internally] → Keep raw entities within the trusted boundary; document this Phase 1 limit and defer projection optimization to Phase 2.
- [Host bypasses the facade or supplies weak mandatory constraints] → Keep example endpoints on the facade, require tenant configuration in the example, and document host responsibilities and the raw query boundary.
- [Resource exhaustion through rules or exports] → Apply validation/binding limits, bounded batches, cancellation, and scoped preview fixtures.
- [Concurrent entity changes alter paginated/export results] → Guarantee authorization and stable ordering, document isolation limits, and avoid claiming transaction-wide data snapshots.

## Migration Plan

There is no existing application or policy data to migrate. Bootstrap the solution and policy database migrations, register the Employee schema and trusted context, then seed reviewed example drafts and publish through the same validation path. Start with no active policies to demonstrate default deny. Connect every example read endpoint and export to the facade before demonstrating access. Validate cross-tenant and hidden-field behavior before marking Phase 1 complete.

For policy rollback, select a compatible immutable version through administration. For a code rollback, restore the previous application build and its compatible schemas/policies; keep policy history and avoid destructive database downgrades. An incompatible version must fail closed rather than silently adopting an older schema.

## Open Questions

- Which compatible .NET target and Kkts package versions should be pinned? Resolve through the dependency compatibility task without changing the public authorization contract.
- What page, batch, and AST limit defaults fit the example? Choose conservative configurable defaults and document them; the requirement to enforce limits is fixed.
