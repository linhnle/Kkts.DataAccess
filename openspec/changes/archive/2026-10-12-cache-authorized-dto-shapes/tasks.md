# Tasks

## 1. Verify the retained-type mapping lifecycle

- [x] 1.1 Add a Kkts.Dynamic 2.1.0 integration test in `DependencyTests` that retains a built type, disposes its assembly, constructs/maps fresh DTOs through the public Mapper API, and serializes only selected fields; verify it passes and disposed Class access still throws.
- [x] 1.2 Add a test-only uncached mapper baseline reproducing the current `DtoMapper.Map`, including empty selection and null-entity behavior; verify ordered aliases, supported scalar/null values, and invalid-binding cases establish an exact comparison oracle.
- [x] 1.3 Update `docs/dependencies.md` with the tested retained-type lifecycle and immutable-after-build limitation; verify statements match the exact-release integration test and keep both upstream versions unchanged.

## 2. Implement bounded reusable shape entries

- [x] 2.1 Implement immutable structural keys from entity type, schema resource/version, and ordered resolved property/public-name bindings; verify focused tests reuse equal independently constructed keys and distinguish field order, entity types, aliases, schema versions, and same-label schemas with different properties.
- [x] 2.2 Implement one non-generic process-wide internal cache with a 128-entry FIFO bound and a lock covering lookup/build/publication; verify isolated small-capacity tests count one build per resident key, enforce capacity across entity types, and rebuild evicted keys.
- [x] 2.3 Build entries from a fresh disposable assembly and retain only built Type/default-configured shape-local serialization options; verify failed builds dispose the builder, propagate without caching or evicting healthy entries, and permit a subsequent retry.
- [x] 2.4 Wire `DtoMapper.Map` to resolve fields before lookup, construct fresh DTOs through the retained Type/public Mapper API, reject null entities explicitly, and serialize outside the cache lock; verify output/exception parity against the uncached baseline, unknown fields on hits, empty selections without builds, and invalid nonempty selections with zero rows.
- [x] 2.5 Add barrier-coordinated first-use, parallel mapping, and eviction-during-active-use tests; verify a resident first-use key emits once, per-row DTOs are independent, and an evicted entry remains usable by a caller holding it.
- [x] 2.6 Update `docs/reads.md` with the 128-entry internal FIFO capacity, exact binding/order identity, continued per-request authorization, and eviction/collection caveat; verify no new public API, DI option, runtime dependency, or provider-specific mapping path is introduced.

## 3. Verify authorization integration and measurable improvement

- [x] 3.1 Extend `ReadTests`/`RepositoryTests` to warm a broader permitted shape before exercising narrower grants, explicit forbidden selections, policy publication, detail, and multi-batch export; verify no forbidden/unrequested fields leak, validation still precedes entity database access, and export retains its captured policy version.
- [x] 3.2 Add a reproducible bounded Release-mode mapper measurement using existing .NET test tooling and the uncached baseline, without timing assertions in normal CI; verify identical outputs and zero resident-hit builds, and report cold misses separately from warm one-row/100-row loops over at least five repetitions.
- [x] 3.3 Run the measurement and record SDK/runtime, command, warm-up/iteration counts, elapsed/allocation medians, and concurrent-miss behavior in `docs/reads.md`; verify warm cached mapping has lower median elapsed time and allocations for both batch sizes, or revise the implementation before claiming success.

## 4. Final integration checks

- [x] 4.1 Run the focused dependency/mapper/cache/repository/read tests with the existing xUnit/VSTest runner and exclude remote-provider traits; verify all deterministic lifecycle, capacity, concurrency, output, and authorization assertions pass.
- [x] 4.2 Run the repository's CSharpier check and a solution build, then run the core/SQLite regression filter `Provider!=SqlServer&Provider!=MySql` using the built outputs; verify formatting/build/tests pass and explicitly report remote-provider suites as not run unless their configured databases are available.
- [x] 4.3 Validate the completed OpenSpec change and check the final changed-file list; verify `skip_specs: true` remains intentional, main capability specs/public contracts and dependency versions are unchanged, and modifications are limited to the planned mapper/cache, tests, and related documentation.
