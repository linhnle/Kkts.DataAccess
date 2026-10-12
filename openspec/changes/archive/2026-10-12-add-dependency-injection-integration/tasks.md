# Tasks

## 1. Async trusted context

- [x] 1.1 Confirm structured errors are implemented; add the core async context provider and distinct constructor overload with synchronous wrapper compatibility, and verify both call patterns compile without ambiguity.
- [x] 1.2 Route ordinary/stamped-export captures through awaited context once per operation; verify cancellation/failure opens no session, two-user isolation, lazy export start, captured context retention, and no sync-over-async.
- [x] 1.3 Document the async seam and scope-bound export enumeration; verify server-only identity examples and direct-construction tests.

## 2. EF factory ownership

- [x] 2.1 Add IDbContextFactory<TContext> async creation/registration overloads and shared owned factory handling; verify exact disposal on mismatch/setup failure, normal sessions, and best-effort/snapshot exports.
- [x] 2.2 Add pooled-factory and temporary-startup-context tests, updating EF registration docs; verify no request DbContext is disposed as owned and no startup application data query is executed.

## 3. Optional DI package

- [x] 3.1 Add Kkts.DataAccess.DependencyInjection, fluent/keyed resource registrations, and explicit lifetimes; verify duplicate/ambiguous registration rejection and separate scoped identities with shared identity-free compilers.
- [x] 3.2 Add options plus async startup validation/hosted adapter hooks; verify contradictory settings fail before serving reads while no identity/active policy is required and databases are not provisioned.
- [x] 3.3 Migrate EmployeeApp to a documented optional registration example; verify existing direct facade construction and HTTP acceptance retain protected behavior.
- [x] 3.4 Document lifetimes, keyed resources, factory ownership, and startup/runtime boundaries; verify package samples build and core has no DI/ASP.NET package dependency.

## 4. Integration

- [x] 4.1 Run targeted async-context/factory/DI/export tests, solution build, and package consumption/packing; verify no authorization/cursor/export lifecycle regression and strict OpenSpec validation passes.
