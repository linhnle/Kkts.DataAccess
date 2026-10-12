# Tasks

## 1. Instrument contract and read stages

- [ ] 1.1 Confirm structured errors and bounded compiler cache are implemented; add documented BCL source/meter, instruments/units, and finite tag vocabulary, verifying contract names and custom-code/provider normalization with listeners.
- [ ] 1.2 Instrument capture/binding/query/mapping without duplicate query counts or extra enumeration; verify all operations, optional counts, async contexts, custom adapters, and no-listener behavior parity.
- [ ] 1.3 Document listener opt-in and library-only privacy scope; verify core introduces no exporter/logging dependency and sample listener setup compiles.

## 2. Cache and export lifecycle

- [ ] 2.1 Instrument both bounded caches without callbacks under locks or unbounded retained registries; verify hit/miss/build/failure/eviction outcomes, concurrency, and finite cardinality.
- [ ] 2.2 Implement iterator-local candidate/terminal outcomes and explicit activity parenting across yield; verify unadvanced/early-disposed/abandoned enumerators, exhaustion/cap/abort/expiry/cancellation/failure, and exactly one terminal result after cleanup.
- [ ] 2.3 Test unrelated consumer activities during pauses and deadline after candidate cap completion; verify no ambient parent leak, premature success, or additional queries.
- [ ] 2.4 Document terminal semantics and cache metrics alongside these tests; verify metric totals align with recorded executed operations rather than invented completion.

## 3. Privacy and integration

- [ ] 3.1 Add sentinel-secret and high-cardinality tests over tags/events/status descriptions; verify no values/SQL/payloads/tokens/messages/identities/version/resource names leak and arbitrary custom strings normalize.
- [ ] 3.2 Update instrumentation/acceptance docs and run listener/no-listener suites plus build; verify measured behavior/query/disposal parity and strict OpenSpec validation without claiming zero overhead.
