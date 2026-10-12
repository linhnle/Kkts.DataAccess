# Policy lifecycle

InitializeAsync installs idempotent version-1 tables and immutable-version/append-only-audit triggers on an empty SQLite database. PolicyHeads stores draft and active revisions per scope/resource; PolicyVersions stores immutable payload/schema content; PolicyAudit stores actor/time/scope/resource/operation/revisions/version/summary. Entity tables belong to the host.

The host supplies AdministrationContext from authenticated identity. Draft read/edit, publication, rollback and history demand administration in that scope. The Employee host derives scope from the authenticated tenant claim.

SaveDraft compares the expected draft revision, then updates payload/revision and audit in one transaction. Drafts never affect active reads. Payload transport is limited to 1 MiB. Publish compares expected draft/active revisions, revalidates against the current schema/context registry/limits, inserts a version and changes the pointer with audit in one transaction. Failure rolls back all changes. Competing writers serialize; stale expectations raise PolicyConflictException. Reload and review before retrying.

Rollback requires a version in the same scope/resource, schema compatibility and current validation, checks the active revision, then atomically switches the pointer with an audit event. It preserves old content. Audit summaries are fixed descriptions and contain no policy payloads, sample identity values or request secrets.

CaptureAsync reads the authoritative pointer and immutable content in a read transaction at operation start. New operations observe committed publication/rollback; in-flight operations retain captured content. Missing active policy fails closed.

`SqlitePolicyStore` also implements `IActivationAwarePolicySource`.
`CaptureActivationAsync` reads the head and immutable content in the same deferred
read transaction and returns `CapturedPolicyActivation(Policy, Stamp)`.
`PolicyActivationStamp(Version, ActiveRevision)` identifies an activation, not
just content. Publication and rollback advance the existing signed positive
revision atomically, including reactivation of the same version. Draft edits and
failed transactions do not advance it. Exhausted revisions reject activation
before changing content, pointer or audit; there is no wrapping or schema migration.

`ReadActivationAsync` opens a fresh authoritative policy connection. Abort-mode
exports compare both version and revision before a batch query and immediately
before delivery. Publish followed by rollback still stops such an export.
Captured-mode exports continue with their original policy. A missing policy or
failed check is an error, never evidence of an unchanged activation. Immutable
versions still control public cursor compatibility; activation revisions do not
change cursor identity.

Entity snapshots do not include these policy reads, even when both databases
share a SQLite file. There is no atomic cross-store snapshot. Revisions must
remain durable and monotonic: restoring/replacing a dataset with reset revision
history is an operational identity change, not transparent export continuity.

Clean example walkthrough: sign in as tenant admin, publish seeded draft revision 1 / active revision 0, save a changed draft using the current revision, publish with current draft/active revisions, then select the prior immutable version for rollback. PolicyStoreTests exercise restart persistence, conflicts/concurrent saves, transaction failure, incompatible rollback, immutability and audits. The HTTP script checks the example lifecycle.
