# Tasks

## 1. Prerequisites, metadata contracts, and versioned counts

- [x] 1.1 Verify request separation and then cursor/keyset execution are implemented and synchronized in that order; confirm their code, focused tests, and main cursor requirement exist, otherwise stop this change and report the prerequisite blocker rather than treating planning status as completion.
- [x] 1.2 Add independent defaulted metadata flags to both list requests, nullable `Total` and `HasMore` page fields, and `ReadCount`; verify serialization tests cover all flag combinations, explicit null versus exact zero under default/web/ignore-null settings, strict boolean input types, and rejection of list-only flags by count/detail/export.
- [x] 1.3 Change public/interface/core/SQLite count signatures to return `ReadCount` and migrate typed consumers plus the example count response together, leaving trusted session count types unchanged; verify solution build and repository tests cover exact value, one capture, the bound version during publication, forbidden-filter rejection, cancellation, and disposal.
- [x] 1.4 Document nullable page metadata and the count-object API/JSON migration in `docs/reads.md` and `docs/repositories.md`; verify examples distinguish unrequested null from exact zero and extract standalone counts from `Total`.

## 2. Optional offset-list execution

- [x] 2.1 Implement the offset execution matrix from the design, skipping session count entirely when disabled and using `Limit + 1` only for requested has-more; verify recording-adapter assertions cover exact count-call numbers and page-fetch bounds for all four flag combinations.
- [x] 2.2 Trim lookahead before DTO mapping and guard lookahead arithmetic before entity access; verify zero/short/full/final pages, offsets beyond matches, maximum host page size, unrepresentable lookahead, empty selections, hidden fields, and forbidden rows beyond the page produce the required bounded output and metadata.
- [x] 2.3 Add disabled-count tests using an adapter that fails on count and enabled-count tests that propagate its failure; verify has-more follows the page observation even when a deliberately divergent count would imply the opposite, with no success-shaped fallback or extra existence query.
- [x] 2.4 Extend the inherited relational read suite with SQL interception for count enabled/disabled, bounded lookahead, and complete authorization predicates; verify local SQLite execution observes no COUNT/aggregate query when disabled and no unfiltered lookahead.
- [x] 2.5 Update offset pagination examples and provider/read documentation with the execution matrix and non-snapshot metadata semantics; verify default exact-total examples still work and no documentation infers has-more from count arithmetic.

## 3. Cursor metadata and continuation consistency

- [x] 3.1 Honor optional totals in cursor orchestration and expose has-more from its existing token lookahead without another row/query; verify all four flag combinations have the expected count-call numbers, exactly one `Limit + 1` seek, stable last-returned-row boundaries, and unchanged next-token behavior.
- [x] 3.2 Keep metadata flags out of cursor identity and snapshot them separately; verify resumed tokens remain valid while either flag changes, but filter/selection/sort/limit/context/dataset/policy mismatches still fail before entity access.
- [x] 3.3 Add cursor tests for null metadata with a usable next token, requested has-more agreement with token presence, exact totals excluding the boundary, empty/final pages, hidden-key/empty selection, and provider failures; verify no lookahead row enters DTO output and existing policy/protection/cancellation regressions pass.
- [x] 3.4 Update cursor documentation with independent metadata options, unchanged mandatory lookahead, and cross-page flag toggling; verify examples preserve every existing row-query and policy compatibility constraint and make no snapshot claim.

## 4. HTTP acceptance and integrated verification

- [x] 4.1 Update `scripts/example_acceptance.py` and nearby HTTP coverage for the versioned count object, default exact totals, all metadata combinations on offset/cursor endpoints, explicit null fields, and metadata changes across continuation; verify authentication, antiforgery, strict invalid-member rejection, and forbidden-filter behavior remain unchanged.
- [x] 4.2 Update README and relevant acceptance examples alongside the transport changes, recording only executed results; verify a fresh synthetic HTTP walkthrough exercises count metadata and null/boolean shapes without exposing identity or token payloads in output.
- [x] 4.3 Build the solution and run one focused local contract/repository/read/cursor/token/compiler/translation test selection excluding unconfigured remote traits; verify no new dependency/session method is introduced and detail/export/provider-failure behavior remains intact.
- [x] 4.4 Run inherited SQL Server/MySQL metadata and authorization tests on configured isolated databases, or explicitly report unavailable remote checks as not run; verify real-provider aggregate-query counts and bounded authorized lookahead rather than using SQLite results as remote evidence.
- [x] 4.5 Run fresh-database HTTP acceptance, relevant existing formatting checks, and strict OpenSpec validation against the synchronized prerequisite specs; verify all modified scenario blocks retain prior scenarios and the earlier cursor-header archive ordering notice is resolved before synchronization.

Validation evidence: [optional totals acceptance](../../../../docs/repository-acceptance.md#optional-totals-and-versioned-counts).
SQL Server/MySQL verification was rerun on 2026-10-12 with isolated connections
loaded safely from the repository-root `.env`. All 28 inherited remote-provider
read tests passed, with zero failures or skips; task 4.4 is now verified through
real-provider execution, including aggregate-query counts and bounded authorized
lookahead.
