# Tasks

## 1. Bounded compiler templates

- [x] 1.1 Add positive compiler cache options with default capacity 128; verify zero/negative rejection and existing constructor behavior.
- [x] 1.2 Replace unbounded template retention with lock-protected dictionary/FIFO state and build-before-evict behavior; verify capacities one/two, exact retained count/order, no queue growth, and failed builds preserve healthy entries.
- [x] 1.3 Preserve privately owned ASTs and fresh identity binding; verify parallel same/mixed-version cross-user binding and resident same-version payload mismatch detection.
- [x] 1.4 Document entry bounds, in-flight references, and authoritative-source lifetime immutability; verify post-eviction recompilation tests demonstrate the stated guarantee without an unbounded history ledger.

## 2. Operation-owned cursor identity

- [x] 2.1 Carry validated raw policy identity in internal bound operation state and remove late cache indexing; verify deterministic eviction between binding/fingerprinting produces identical cursor identity without cache-key failures.
- [x] 2.2 Add cursor/export regression tests and update cache/cursor docs; verify warm/cold/evicted paths preserve token compatibility, version capture, metadata flags, and authorization results.

## 3. Integration

- [x] 3.1 Run focused compiler/cursor/cache/export tests and build; verify bounded retention under concurrent load and strict OpenSpec validation without introducing new packages.
