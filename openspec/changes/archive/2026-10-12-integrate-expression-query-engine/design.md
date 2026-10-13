# Design

## Context

See proposal.md for motivation. Production PolicyCompiler.Node builds System.Linq.Expressions nodes directly; PolicyValidator performs local schema/type/operator checks; EfEntityReadAdapter.PageAsync builds Queryable calls manually. DependencyTests already demonstrate QuerySchema, ExpressionQueryContext, FilterNode, FilterTreeJson, and BuildPredicate against 3.1.0. The exact restored package README also documents structured OrderByInfo ordering and metadata-only AnalyzeExpression.

The existing specs require exact registered public names, strict culture-independent conversion, mandatory AND allow AND NOT deny composition, context isolation, field-operation authorization, and provider-side execution. They do not require a particular expression implementation. This change therefore skips delta specs and preserves their requirements.

## Goals / Non-Goals

**Goals:** Put reusable query mechanics behind one internal Expressions adapter; share registered bindings across construction and semantic validation; retain authorization decisions in DataAccess; exercise these paths through the public repositories.

**Non-Goals:** Arbitrary client expression strings, a new editor/autocomplete API, computed/navigation fields, new operators or actions, public contract changes, dependency upgrades, upstream edits, or changes to Kkts.Dynamic output shaping.

## Decisions

### 1. Derive upstream schemas from ResourceSchema

Keep ResourceSchema as the authorization registration contract and derive an immutable upstream typed public schema from its PropertyInfo bindings. Generate typed selectors internally; translate the local operator catalog to upstream operator identifiers. Preserve exact ordinal name checks before calling upstream APIs, whose general parser supports case-insensitive names. Detect alias collisions that upstream cannot represent and use deterministic internal aliases mapped from exact public names rather than rejecting previously valid registration.

Use distinct authorization-policy and client-query contexts. Policy conditions may reference registered fields needed by mandatory/allow/deny rules regardless of a user's client Filter grants. Client operations must first satisfy the corresponding effective grants. Never conflate schema eligibility or upstream QueryPolicy with authorization. Request-specific allowlists and values must not enter shared mutable schema state.

Keeping only the current local schema would continue duplicate query mechanics. Replacing the public registration API outright would introduce unnecessary compatibility breaks.

### 2. Lower validated conditions into structured filters

An internal Expressions adapter maps all/any/not and supported leaves to FilterNode and FilterValue and builds predicates through ExpressionQueryContext. Local validation retains transport/tree-shape limits, strict LiteralConverter conversion, context-reference compatibility, and authorization-specific diagnostics. Upstream handles field/operator/type semantics on the lowered representation.

Resolve every required trusted reference in every applicable rule and mandatory condition before boolean composition. Supply already converted typed values through a request-owned resolver when string/number encoding could change precision, enum representation, DateTime kind, or offset. Such resolver keys are generated internally; clients cannot supply them, and neither built-in variables nor object navigation are enabled. Validate subject context conditions against registry metadata and evaluate them only after trusted binding; no entity database is involved.

Retain existing And/Or composition for mandatory/allow/deny and client intersection. Delete the general manual leaf/operator builder once parity passes. Permit only narrow, documented expression adaptations for proven upstream differences, such as explicit non-null guards for string or ordered operations. These adaptations must still use upstream-built comparisons rather than preserve a second general operator switch. Null eq/ne, null predicates, membership containing null, ordered comparison with null, and nested NOT require explicit parity cases.

Directly serializing policy JSON into upstream JSON was rejected: the policy adds context and authorization semantics and has a different contract. Formatting untrusted expressions as executable strings was rejected in favor of structured predicate construction.

### 3. Use upstream semantic analysis without executing context

Use upstream structured validation for the executable filter tree and metadata-only AnalyzeExpression for condition semantics. If 3.1.0 lacks structured semantic-analysis overloads, internally emit canonical expression text from validated condition nodes, with escaped literals, generated field/variable aliases, and a source map back to condition paths. This text is an internal analysis representation, never a client execution endpoint. Declare variable types from ContextRegistry; analysis must not resolve values, invoke getters, or read the entity database. Runtime predicates still come from structured filters.

Normalize upstream diagnostics to existing Diagnostic(Path, Message), preserving original public field names and paths. Keep local diagnostics for policy shape, size limits, grants, and strict conversion. Avoid duplicate errors for the same failed leaf. Publication, rollback, mandatory-condition validation, and client filtering must traverse the shared validation boundary. Analysis success does not imply database translation success.

Local-only validation was rejected because it leaves semantic capabilities unused. A public text editor endpoint was rejected because it changes scope and accepted syntax.

### 4. Delegate EF ordering after authorization

Keep ReadRequest.Sort, EntitySort, and EntityReadPlan source-compatible. AuthorizedReadRepository continues to check exact public aliases and Sort grants and append the host stable key. EfEntityReadAdapter translates only the resulting validated direct PropertyInfo bindings to upstream OrderByInfo and applies the library's structured IQueryable ordering extension to the authorized query. Explicitly qualify the upstream extension where necessary to avoid LINQ overload ambiguity.

No raw client field or sorting string reaches the adapter. The stable key is a host ordering detail and gains no output/filter/sort permission. Preserve ordered keys and directions, then apply Skip/Take. Custom adapters retain their existing plan contract and are not forced to depend on IQueryable. The EF assembly may reference the same pinned Expressions package explicitly if needed for this direct use.

### 5. Verify mechanics and behavior independently

Adapter tests verify real upstream construction, analysis, alias mapping, and ordering with the pinned release. Compiler and public repository tests verify authorization outcomes, not merely package availability. Extend existing CompilerTests, ReadTests, RepositoryTests, and provider conformance cases rather than add implementation-mirroring tests. Compare database result identifiers to handwritten LINQ or fixed expected results for null, NOT, enum/numeric/temporal, membership, and tie-ordering cases. Instrument rejection paths to prove no entity session opens and analysis performs no runtime resolution.

## Risks / Trade-offs

- Upstream conversion or null semantics differ → use preconverted request-owned values and narrowly documented adaptations; require parity before replacing existing code.
- Upstream limits count translated/generated nodes differently → maintain original DataAccess limits and configure bounded upstream limits to accommodate internal expansion; never silently reduce accepted policy limits.
- Analyzer and runtime accept different representations → validate both and test every supported operator/type family; report a gap explicitly rather than bypassing upstream or broadening syntax.
- Context or permissions leak through caches → cache only immutable schema/template metadata; keep resolvers, grants, and bound expressions operation-owned.
- Changed expression shapes fail relational translation → run the existing SQLite and available SQL Server/MySQL suites; abort translation failures without local or unrestricted fallback.
- Canonical analysis text adds adapter complexity → isolate the emitter/source map and prefer a verified structured analysis API if available.

## Migration Plan

Implement behind existing internal boundaries and run parity coverage before removing manual query mechanics. No policy data or public API migration is required. Update docs/dependencies.md and docs/policies.md with the actual production APIs, supported adaptations, and verification results. Roll back by reverting this refactor; persisted policies and provider contracts remain compatible.

## Open Questions

- During implementation, confirm exact 3.1.0 overloads for runtime-generated typed schema bindings, structured semantic validation, metadata-only analysis, and request-owned typed variable binding. The restored README verifies these capability families, but existing DependencyTests do not cover all overload combinations. A focused adapter compatibility test resolves signatures within this design; inability to preserve an existing supported case must be surfaced before changing behavior or dependency versions.
