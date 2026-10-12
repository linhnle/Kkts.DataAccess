# Proposal

## Why

List operations always execute an exact count, even when callers only need records or to know whether another page exists. Make count work explicitly optional and give standalone counts the same captured-policy metadata as other protected results before release.

## What Changes

- Add independent `IncludeTotal` and `IncludeHasMore` flags to offset and cursor list requests. Defaults remain `true` and `false`, respectively; callers can request either, neither, or both.
- Skip count execution entirely when `IncludeTotal` is false.
- **BREAKING**: Make list/cursor `Total` nullable and add nullable `HasMore`. Disabled metadata is explicitly null, not a fabricated zero or false.
- Compute requested `HasMore` from one extra authorized row beyond the requested page, not from total arithmetic. Reuse cursor lookahead already required for next-token production.
- Allow metadata flags to change across cursor pages without invalidating tokens; retain all existing row-query, caller/context, dataset, and policy compatibility checks.
- **BREAKING**: Return `ReadCount` containing `PolicyVersion` and exact `Total` from standalone count operations instead of a bare integer; update the core interface, repository, SQLite facade, and example JSON response together.
- Preserve independent field authorization, one policy capture per operation, page limits, ordering, cancellation, errors, disposal, and export/detail behavior.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `authorized-reads`: Define optional exact totals and lookahead metadata for both list modes, versioned standalone counts, and response-metadata-independent cursor continuation.

## Impact

Implement and synchronize `split-read-requests-by-operation`, then `add-cursor-pagination-and-keyset-exports`, before applying this change. Both are currently planned, not implemented. This proposal deliberately supersedes their always-exact list-total and unchanged-count-result promises; it does not modify those earlier artifacts.

Affected surfaces include core contracts and list/count orchestration, SQLite delegation, cursor fingerprints, EmployeeApp JSON endpoints, HTTP acceptance, provider/read/repository tests, and directly related documentation. Trusted entity-session count/page/seek methods, protection format, dependencies, and persisted policy/entity schemas need no redesign.

Consumers must handle null list totals and read standalone count values from the versioned result. Existing list requests retain exact-count behavior by default.

### Non-goals

Approximate or cached totals, count-only permissions, database snapshots, changing page defaults, cursor replay strategy, export counts, client export limits, aggregate queries, and broader result/error restructuring are excluded.
