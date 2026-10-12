# Design

## Context

See [proposal.md](./proposal.md). OperatorCatalog is CLR/schema-wide. EF helpers currently accept a factory/provider identity without schema. Projection checks actual mapped properties; EfSeekQuery.SortKeys checks converter/type support; snapshot checks model/runtime settings. These existing restrictions must be shared, not recreated as a divergent catalog.

## Goals / Non-Goals

**Goals:** reject known incompatible configured operations early, attach useful policy paths, and identify unverified support honestly.

**Non-goals:** static proof of all SQL translation, changing collations/types, broadening permissions, or forcing profiles on neutral core consumers.

## Decisions

### Immutable model-aware profiles

Add transport-neutral `IReadProviderCapabilities`/profile results in core with finite Supported/Unsupported/Unverified status per registered field/property, mapping, operator, and operation. Snapshot the relevant EF model identity and provider implementation/version scope; no identity/context or connection secrets enter profiles.

Expose declared resource operations (offset/detail/count/cursor/export, snapshot opt-in). Validation checks only configured operations, avoiding an unsupported seek type incorrectly forbidding valid output/count. Supplied registrations default to their normal read operations, with hosts explicitly disabling unsupported ones. Calls to disabled operations fail explicitly.

EF/provider helper registrations gain a schema-aware overload and migrate away from helper registration without schema as an intentional pre-release breaking change. Direct low-level adapters remain a trusted runtime-only path, not a claim of startup validation. Startup schema/profile validation creates/disposes a model context without opening entity data queries.

Extract shared mapping/seek/snapshot-shape validators used by registration and execution. Keep runtime operational prerequisite checks (WAL/isolation/engine/table liveness) where actual connections are available. Do not silently label arbitrary unknown providers/mappings Supported.

### Policy, mandatory, and runtime plan validation

PolicyValidator accepts an optional profile, walks database-condition leaves with stable paths, and checks known field/operator incompatibilities. Subject context trees stay on CLR/context validation. PolicyCompiler validates mandatory constraints against a configured profile and rechecks captured definitions before entity access even when publication used neutral validation.

Thread the optional validator/profile through SQLite publication and preview using existing atomic validation transactions; no active pointer/audit change occurs on unsupported diagnostics. A policy shared across multiple registered providers is validated for each host-declared target profile, not against whichever provider happened to register last.

Runtime preflight checks actual protected plan, sort bindings, output, and continuation before aggregates/retrieval. Profiles cannot relax grants or authorize unregistered fields. Unverified mappings remain labeled and may attempt normal execution; known unsupported ones fail. Residual composed-query translation errors propagate, preserving no unrestricted/local/offset fallback.

Alternative: trimming schema operators automatically changes public schema semantics and still cannot prove SQL translation.

### Semantics and integration

Document offset provider-native null ordering separately from explicit seek null-first asc/null-last desc. Report collation as host/database controlled unless actual evidence exists; no CLR compare substitution. Support claims identify model conversions/precision and verified provider versions.

Apply structured errors and DI first for the full registration hook. Profiles accept schema key tuples generically; composite support can land before this change without a separate profile redesign. Compiler cache keys must include immutable validation-profile identity if one compiler can serve different profiles; preferably one compiler registration per profile set to preserve cache correctness.

Composite-key support is not yet implemented. This change verifies ordered tuple metadata and current single-key resources without advertising end-to-end composite reads; composite resource execution is verified when that dependent support is available.

## Risks / Trade-offs

- [False supported claims] -> Three-state results and provider tests; startup is not translation certification.
- [Registration breaking change] -> Explicit schema/operations migration in helpers, examples, and docs.
- [Profile/model drift] -> Validate runtime model/plan restrictions and do not cache mutable model/config objects.

## Migration Plan

Apply structured errors and DI, then shared validators/profiles, provider registration and publication hooks, runtime preflight, and docs/tests. No automatic provider settings or database migrations. Verify SQLite/SQL Server/MySQL positive and known-negative mappings, neutral validation, and residual translation failure.
