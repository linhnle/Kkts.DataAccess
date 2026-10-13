# Tasks

## 1. Tuple schema migration

- [x] 1.1 Confirm structured errors are implemented; replace singular stable-key registration/access with ordered immutable KeyFields, and verify empty/duplicate property/alias/nullable/nonorderable keys fail at exact component paths.
- [x] 1.2 Add ordered key metadata and migrate single-key registrations to one-element tuples; verify administrative metadata gating, caller-mutation isolation, and jointly unique composite fixtures.
- [x] 1.3 Document schema/dataset identity and host uniqueness obligations; verify no automatic DDL/policy migration or new field permission is introduced.

## 2. Exact detail lookup

- [x] 2.1 Require exact object detail keys and validate duplicate/missing/extra/case/null/type components before session opening; verify one-member single-key objects and composite objects produce precise diagnostics.
- [x] 2.2 Compose typed component equality with authorization/client filters; verify implicit lookups need no Filter grant, explicit filters still do, duplicate detection remains, and absent/inaccessible detail is equivalent.
- [x] 2.3 Migrate facade/example GET construction, HTTP scripts, and detail docs; verify scalar keys now fail intentionally and existing results/empty-selection JSON remain unchanged.

## 3. Full tuple ordering and cursors

- [x] 3.1 Append missing physical key properties in declared order ascending while preserving explicit directions/repeated sorts; verify partial key sorts, alias overlaps, hidden keys, and deterministic ordering.
- [x] 3.2 Bind ordered key identity into cursor fingerprints; verify old/mismatched tokens fail explicitly, encrypted sidecars remain hidden, and lookahead/metadata/policy-version semantics persist.
- [x] 3.3 Update ordering/token migration docs alongside tests; verify example requests and composite traversal expectations reflect the exact completion rule.

## 4. Provider integration

- [x] 4.1 Add composite-key real-provider fixtures and shared list/cursor/export/snapshot tests; verify SQLite/SQL Server/MySQL sequences with duplicate individual components, mixed directions/null sorts, caps/deadlines/abort, reader columns, and zero projected entities.
- [x] 4.2 Update provider acceptance evidence and run build plus existing single-key suites; explicitly report unavailable provider runs and verify strict OpenSpec validation preserves inherited scenarios.
