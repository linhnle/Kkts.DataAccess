# Phase 1 acceptance

Verified on 2026-10-11 with .NET SDK 10.0.401, Kkts.Expressions 3.1.0, Kkts.Dynamic 2.1.0 and the real SQLite provider.

| Requirement area | Passing evidence |
| --- | --- |
| Resource schemas and metadata | SchemaTests: direct scalar bindings, duplicate names, administrator scope, operation eligibility, exact invariant conversion and nullable values. DependencyTests: upstream typed aliases, nested filters and DTO serialization. |
| Policy compilation | CompilerTests: serialized node forms, path diagnostics and limits, trusted context failures, mandatory/allow/deny composition, independent field precedence, unsupported actions and concurrent context binding. ExamplePolicyTests verifies the documented policy and both tenants' role matrix. |
| Policy administration | PolicyStoreTests: migrations, persistence, scope separation, optimistic conflicts, immutable versions, atomic publication failure, compatible rollback and audits. HTTP walkthrough checks administrator protection, antiforgery, preview, publication and rollback. |
| Authorized reads | ReadTests: pre-query permission checks, tenant isolation, stable ordering, detail/not-found equivalence, pre-pagination counts, permitted DTO output and captured-version export/cancellation. TranslationTests verifies relational translation failure aborts without fallback. |
| Employee endpoints | `scripts/example_acceptance.py` passed against a fresh database: manager 2, HR 3, employee 1, no-grant denied in each tenant; hidden Salary excluded; list/detail/count/export, preview, revision conflicts, publish and rollback. |
| Browser designer | Chrome acceptance: integer and string operator choices differ; nested NOT condition saved and survived reload; independent Email selection toggled and persisted; malformed collection literal displayed a diagnostic; stale draft save and publication displayed revision conflicts. Publish and rollback updated version/revision and audit history. Synthetic manager preview showed rule identifiers, effective fields, mandatory tenant constraint and per-fixture allow/deny results without changing the active version. Restored the original grants and rolled back to version 1. |
| Package independence | Packed `Kkts.DataAccess.0.1.0.nupkg`; restored and built EmployeeApp using `UsePackedCore=true` and the local package source. Neither upstream library was changed. Restored normal project references afterward. |

Final automated run: **27 passed, 0 failed, 0 skipped**. Solution build completed with zero warnings and errors. OpenSpec validation passes.

Reproduce automated checks with the commands in [README](../README.md). The HTTP script expects a fresh database and a Development server on port 5189. Browser checks are manual acceptance evidence; the script does not exercise DOM controls.

Phase 1 covers reads. Internal materialization can include hidden columns; output contains approved scalar DTO fields only. Export captures the policy version, but concurrent data changes do not receive snapshot isolation across batches. Production identity integration remains the host's responsibility; synthetic login is Development-only.
