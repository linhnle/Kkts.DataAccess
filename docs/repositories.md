# Authorized read repositories

`IAuthorizedReadRepository<T>` is the client-facing read-only contract. `AuthorizedReadRepository<T>` in core owns policy capture, validation, predicate composition and permitted DTO mapping. Core references neither EF Core nor a database driver. Policy storage remains `SqlitePolicyStore`, which implements `IActivePolicySource`.

Trusted host code supplies an `IEntityReadAdapter<T>`. Its async sessions receive immutable `EntityReadPlan<T>` values built by the repository; plans include the complete authorized predicate and resolved sort properties, including the stable key. Execute that predicate in the provider before counting or paging. Never replace it, return a query handle to clients, or evaluate authorization locally after fetching unrestricted records. Materialized entities belong inside this trusted adapter/facade boundary; only DTOs leave the facade.

An adapter implements `OpenAsync`; its session implements `CountAsync`, `PageAsync`, `SingleAsync` and `DisposeAsync`. All methods accept cancellation. The repository disposes a session for every operation and export batch, including failures and stopped enumeration. Export captures one policy version but does not promise a data snapshot. See the standalone adapter example in `examples/CustomReadAdapter` for a core-only implementation surface.

EF hosts use `EfEntityReadAdapter<T>` with an owned DbContext factory and expected provider identity, or provider helpers `SqliteEntityReads.Create<T>`, `SqlServerEntityReads.Create<T>` and `MySqlEntityReads.Create<T>`. A mismatched context provider fails before querying and is disposed. Provider translation failures propagate without fallback. Hosts own mappings, connection configuration, database schema, collation and migrations.

Existing `Kkts.DataAccess.Sqlite.AuthorizedReads<T>` construction and methods remain available and delegate to the same repository with the SQLite executor. Existing SQLite configuration stays valid. Entity and policy databases can be separate: pass the SQLite policy store to the core repository and an adapter for the independently configured entity DbContext. No policy table migration is needed.

See [provider support and acceptance](providers.md) for dependency versions, verified behavior and real database setup.
