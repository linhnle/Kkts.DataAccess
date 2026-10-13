# Proposal

## Why

The existing xUnit recording/provider suites are tied to internal fixtures and EF models, making them hard for extension authors to reuse. Publish runner-neutral behavioral checks with explicit evidence and limitations.

## What Changes

- Add a separate test-only `Kkts.DataAccess.Testing` package with reusable scenarios/assertions and disposable fixture contracts, without xUnit/EF/driver dependencies.
- Cover authorization/grants, projection, counts, composite keys, ordering/seek, cancellation/disposal, translation failures, and export policy/data modes.
- Separate mandatory checks from optional-capability scenarios; report passed, unsupported, not-run, and failed explicitly.
- Require real-provider evidence hooks for materialization/reader/translation claims; in-memory fixtures cannot prove database enforcement.
- Include a deliberately broken adapter control, extension-author sample, and warnings that passing fixtures does not certify a safe adapter.

## Capabilities

### New Capabilities

- `adapter-conformance`: Portable test-only scenario contracts and honest evidence reporting.

### Modified Capabilities

None. Existing provider conformance remains and will consume the kit.

## Impact

New test package, existing repository/read/provider fixtures, standalone adapter example, solution/packing checks, and docs. Depends on `add-structured-errors`, `support-composite-resource-keys`, and `validate-provider-capabilities` to target final contracts. DI/telemetry are separate concerns and are not mandatory for an adapter.
