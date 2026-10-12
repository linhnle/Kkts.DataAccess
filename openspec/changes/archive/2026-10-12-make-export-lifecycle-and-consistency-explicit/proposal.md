# Proposal

## Why

Exports currently retain one policy capture but read entity data in separate sessions, so revocation does not stop an in-flight export and keyset traversal is not a snapshot. Hosts need explicit, independently configurable policy and data consistency with bounded resource usage.

## What Changes

- Add host-only export configuration with independent policy modes (`CapturedPolicy`, the default; `AbortOnPolicyChange`) and entity-data modes (`BestEffort`, the default; `Snapshot`).
- Abort mode checks an authoritative monotonic activation revision before every batch query and immediately before delivery. Publication and rollback, including publication followed by rollback to the original version between checks, abort rather than rebind or mix permissions.
- Add optional positive maximum rows and duration. Row bounds successfully cap delivery; duration expiry raises an explicit error. Duration covers the whole enumeration from first advancement, including capture, queries, and consumer pauses.
- Provide export-wide snapshot execution in SQLite, SQL Server, and MySQL with verified provider-specific prerequisites; unsupported capabilities/configurations fail explicitly, never degrade to best-effort.
- Keep keyset ordering, authorized projection, public export request/batch shapes, and captured context/version unchanged. Policy checks remain outside entity snapshots.
- Extend trusted adapter and policy-source capabilities without requiring ordinary custom adapters/sources to support opt-in modes. Keep captured-policy/best-effort behavior and no limits as defaults.
- Document cooperative streaming, partial delivery on failure, the revocation check/yield race, snapshot resource costs, and consumer disposal obligations.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `authorized-reads`: Define independent export policy/data consistency and lifecycle limits while preserving authorized keyset output.
- `entity-read-repositories`: Define snapshot execution capability, provider prerequisites, export transaction ownership, and fail-closed behavior.
- `policy-administration`: Expose authoritative activation stamps atomically with policy capture, using the existing monotonic publication/rollback revision.

## Impact

The current code and main specs already include operation-specific requests, cursor/keyset traversal, optional totals, and required database projection. This proposal builds on that implemented baseline, not the earlier planning-only state, and does not rewrite its completed changes.

Affected surfaces: core export configuration/orchestration and trusted contracts, SQLite policy source and facade, shared EF provider execution, custom-adapter example, provider/recording/policy-store tests, EmployeeApp integration, and read/lifecycle/provider/repository documentation. SQLite `PolicyHeads.ActiveRevision` already increments atomically during publication/rollback, so no policy table migration or new package is planned. Opt-in snapshot hosts may need operational database changes (WAL, snapshot isolation, transactional engines) that the library must not silently perform.

### Non-goals

Immediate revocation of already delivered data, distributed policy/entity snapshots, refreshing identity/context during export, public export resume tokens, client-selected isolation/limits, automatic replay after a yielded batch, or changes to cursor token version compatibility.
