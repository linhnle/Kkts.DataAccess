# Authorized reads

AuthorizedReads<T> accepts a schema, compiler, policy store, EF Core database factory, server-derived context factory and administrative scope. ListAsync, CountAsync, DetailAsync and ExportAsync own policy capture/validation/execution/mapping. ReadRequest contains selection, structured filter, sort, offset/limit and action, with no executable expression or authorization identity. Results expose materialized JSON DTOs and counts/version ids.

Omitted selection uses effective Select fields; [] yields empty DTOs. Explicit unknown/forbidden fields fail. Filtering/sorting require independent grants. Every nested client leaf is checked; context references and navigation/property paths absent from the schema are forbidden. Writes/aggregation and non-Read actions are unsupported even with permission metadata.

Validation precedes opening the entity database. Mandatory/Allow/Deny predicates precede client filters. Counts run before pagination. Detail adds the host stable-key predicate without granting client privileges; excluded/nonexistent records both produce null (404 in the example). Ordering appends the host-declared unique stable key; hosts must enforce uniqueness. Invalid/missing keys prevent registration.

ReadLimits defaults: max page 200, max offset 1,000,000, export batch 100. Export validates requests but covers all matching rows from offset zero and retains one policy/version across bounded batches. Cancellation flows through capture and queries.

The shared repository supports SQLite, SQL Server and MySQL entity adapters with SQLite policy storage; see [provider support](providers.md). The SQLite provider baseline is EF Core 10.0.12. Translation/database errors propagate with no unrestricted or local-authorization fallback. Provider restrictions (including some decimal/DateTimeOffset ordering) fail explicitly. Hidden columns can be materialized inside the trusted facade; Phase 1 does not promise column projection. Kkts.Dynamic maps only permitted scalar public bindings in OneWayToDto mode, then serializes before returning results.

Counts/pages are separate database statements; export batches hold no transaction-wide entity snapshot. Concurrent entity changes may shift offsets or totals. Stable ordering and captured authorization do not guarantee data snapshot consistency.

HTTP calls: GET /employees and /employees/{id}; POST /employees/list, /employees/count, /employees/export. POST requires an authenticated cookie and a current antiforgery token in RequestVerificationToken; obtain a fresh token from /login after sign-in. Example JSON: {"selection":["Name","Email"],"sort":[{"field":"Name"}],"limit":20}. Export returns newline-delimited DTO JSON. ReadTests and the HTTP acceptance script exercise these operations. Hosts must route every supported data endpoint through the facade.
