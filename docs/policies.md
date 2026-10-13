# Resource and policy contract

Register a `ResourceSchema<Employee>` with a resource name, schema version, typed direct-property `Field` bindings, supported action names, per-field operators and operation eligibility, and an ordered key alias tuple, for example `["TenantId", "Id"]`. Single-key resources pass `["Id"]`; the singular constructor and `StableKey` accessor are removed. `KeyFields` is an immutable snapshot of the resolved bindings. The tuple must be nonempty and have distinct aliases and physical properties; components must be nonnullable, supported, non-boolean orderable scalars. Invalid components have `/keyFields/<index>` diagnostics. Duplicate names (including case variants), navigation/computed bindings, unsupported scalar types and incompatible operators are rejected. Public names resolve exactly; CLR paths are not a query API. Eligibility limits grants and grants nothing on its own.

The host must guarantee **joint tuple uniqueness**, preferably with a primary or unique database constraint. Individual components need not be unique. Registration does not inspect live data, create DDL, or migrate entities or policies. Changing key bindings/order requires a compatible schema version, policy publication, and dataset identity migration; restart cursor traversal rather than reusing old tokens. Key membership grants no Select, Filter, or Sort permission.

For example, the tested Employee schema registers Id for selection, Name for selection/sorting, DepartmentId and Status for filtering, and TenantId for host constraints only. `schema.Metadata(admin, scope)` demands host-authorized administration in that scope. Metadata contains ordered key aliases, public names, scalar types, nullability, registered operators and separate eligibility sets.

Policies contain `resource`, `schemaVersion`, `rules` and `fieldEntries`. A record rule has an id, resource, action, Allow/Deny effect, subject and optional condition. A subject has roles/groups (OR across both; both empty matches everyone) and an optional context-only condition. Record grants and field grants are independent. Field entries contain separate Select/Filter/Sort/Aggregate/Write arrays under `fields`. Unknown JSON members, including fields on a record rule or a record condition on a field entry, are rejected. Phase 1 executes Read only; writes and aggregation remain unavailable regardless of permission metadata.

Conditions contain exactly one of `all`, `any`, `not`, or a leaf (`field`, `operator`, plus exactly one `literal` or `context`). All/any are nonempty. `isNull`/`isNotNull` take no value. Public operators: eq, ne, in; ordered numeric/date comparisons lt/le/gt/ge; contains/startsWith/endsWith for strings. Operators can be restricted per registration. Equality/inequality use typed nulls; ordered comparisons and string predicates with null operands are false. Explicit JSON null differs from an omitted literal. Collection membership uses typed equality including null where compatible.

Literal conversion is invariant and exact: JSON booleans/strings/numbers must match their declared types; integers reject fractions and overflow. Decimal conversion rejects rounding. Floating types accept only exactly representable numeric values (for example 1.5; 0.1 is rejected). Guid strings use D format; dates use round-trip O format; enum values use exact declared names. Numeric strings, undefined enum values and coercion are rejected.

The host registers context keys/types, builds a fresh `TrustedContext` from authenticated server identity, and supplies immutable mandatory constraints independently of editable policy data. Do not deserialize trusted context from client data. Missing or wrong-type context in applicable allow/deny/subject conditions or mandatory constraints fails closed. Client read contracts must have no identity overrides.

Record composition is `mandatory AND (OR allows) AND NOT (OR denies)`. No applicable Allow fails before provider execution. Fields are `(union applicable allows minus union applicable denies) intersect eligibility`, independently for each operation. A record grant adds no fields. A field grant adds no records.

Default configurable limits: depth 12, total nodes 256, literal/context string bytes 4096, collection size 100 and total entries 100. Diagnostics include condition paths. Limits are enforced before predicate construction.

## Bounded compiler templates

Each `PolicyCompiler<T>` retains up to 128 privately owned validated templates by
default. Supply `cacheOptions: new PolicyCompilerCacheOptions(MaxEntries: 32)` to
configure a positive entry capacity; zero and negative values are rejected.
Existing constructor calls keep the default. Templates are keyed by
scope/resource/schema/policy/compiler version and provider validation identity.
Request identity, grants, predicates and records are never cached: every bind
derives fresh authorization from its trusted context.

Lookup, validation and insertion are serialized under a compiler-local lock;
request binding occurs outside it. Eviction is FIFO by successful insertion,
and hits do not reorder entries. Invalid builds neither enter the cache nor
evict healthy templates. The dictionary and insertion queue are both bounded;
there is no historical content ledger or permanent per-version lock/task state.

A resident version supplied with different raw payload content fails with
`RevisionConflictException`. An evicted version is parsed and validated again.
Lifetime version/content immutability must be enforced by the authoritative
policy source (the SQLite store uses immutable-version triggers), not by the
compiler's bounded history. Custom sources must enforce the same contract.

In-flight operations can retain validated AST/content references after eviction.
The entry capacity bounds compiler-owned retention, not total process memory
or exact bytes; policy transport/complexity limits remain separate. Eviction
does not change already-bound authorization or an operation's captured version.

The normalized Employee rule is a manager Allow with `all` containing DepartmentId eq context department and Status eq literal Active. A separate manager field entry grants Select Id/Name (add Email when registered), with Filter DepartmentId/Status and Sort Name explicitly granted separately. The host mandatory constraint is TenantId eq context tenant. `CompilerTests` verifies this composition and field separation. Kkts.Expressions supplies structured predicate construction, metadata-only semantic analysis, and typed And/Or composition through an internal adapter. DataAccess retains authorization, strict conversion, eager trusted-context binding, and explicit null behavior. See [dependency integration](dependencies.md) for the upstream APIs and compatibility adaptations.
