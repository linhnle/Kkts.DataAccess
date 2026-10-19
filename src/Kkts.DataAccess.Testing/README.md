# Kkts.DataAccess.Testing

Test-only, runner-neutral conformance checks for protected read adapters.
Reference this package only from tests or test tools, never production hosts.
The package depends only on core (including core's existing transitive packages);
it has no runner, EF, database driver, DI, ASP.NET, or friend-access dependency.

## When to use

Use this package when implementing a custom protected read adapter or maintaining
provider regression fixtures. Application hosts do not need it. Passing checks
does not certify an adapter or prove database enforcement.

## Requirements and installation

Targets .NET 10. NuGet installs core and its dependencies transitively.
Choose your own test runner and any driver/provider packages your fixture needs;
they are not supplied by this kit.

```sh
dotnet add package Kkts.DataAccess.Testing
```

## Fixture setup and responsibilities

Fixtures bind their own entity's direct scalar properties with
`ConformanceBindings<T>`. No production entity type or internal plan constructor
is required. Implement `IAdapterConformanceFixture<T>` and seed the supplied
records into a fresh isolated dataset. The suite constructs schemas, policies,
trusted identities and **protected repositories**; the adapter receives plans
only through those public operations.

Fixture constructors must acquire no resources. Setup registers each exact
owned asynchronous resource with `OwnedFixtureResources.Own` immediately after
acquisition. The fixture itself is registered before setup. Teardown is
idempotent, reverse-order, attempts all owned resources, and surfaces failures.
Never register a shared database, host context, unrelated file, or directory.
The kit neither provisions nor deletes databases/files. A failed acquisition
must clean up resources not yet returned to the ownership registry.

Observe actual session/query counters, not expected values. Rename mutates the
same isolated dataset through a separate connection. Translation fault injection
must make the next row query fail explicitly, without a successful fallback.
Continuation protection must be authenticated encryption with an isolated test
key. Declared capabilities must reflect the fixture, not desired results.

Passing output checks is not proof of database enforcement. Real-provider
evidence must come from fixture-owned query, reader-column and materialization
interception. Reports never contain SQL, connection strings, rows, policy
payloads, token values, or exception messages. Passing fixtures is evidence for
executed cases, **not certification** that an adapter is safe for arbitrary
policies, models, data or collations.

## Quick start

This runner-neutral helper takes an application-defined fixture factory for
your entity type. Implement `IAdapterConformanceFixture<TEntity>` with real
isolated setup, adapter bindings, observations, mutations, and cleanup before
calling it. It is not a complete fixture implementation.

```csharp
using Kkts.DataAccess.Testing;

public static class AdapterChecks
{
    public static async Task VerifyAsync<TEntity>(
        Func<IAdapterConformanceFixture<TEntity>> fixtureFactory,
        CancellationToken cancellation)
        where TEntity : class
    {
        var report = await new AdapterConformanceSuite<TEntity>().RunAsync(
            fixtureFactory, cancellation: cancellation);
        if (!report.Passed)
            throw new InvalidOperationException("Inspect the structured conformance results.");
    }
}
```

Adapt failure reporting to your runner's assertion API as needed.

## Behavior and limitations

An optional scenario-ID selection runs focused checks. Unselected scenarios
remain explicitly `NotRun`, so a partial selection cannot claim an overall
pass. IDs are stable strings in `ConformanceScenario`. Results include the
fixture type, provider kind and numeric provider version, not connection data.
Unexpected setup/scenario/teardown errors are reported as `Failed` with only
their type; caller cancellation propagates after owned cleanup.

| Module | Coverage |
| --- | --- |
| Baseline | Mandatory tenant isolation, allow-minus-deny/default deny, independent grants/field deny, exact object detail validation before session opening, absent/inaccessible equivalence, empty selection, count matrix, ordering and exact bounded output |
| Lifecycle | Pre-cancellation and streaming cancellation, normal and early session disposal, explicit translation failure with no successful fallback/retry |
| Projection/seek/composite | Required projection, equal-prefix and nullable/mixed/repeated sorting, hidden full-tuple completion, detail and cursor/export sequence equality |
| Cursor | Metadata-independent continuation and rejection across identity, query order/limit, tampering and policy versions |
| Export/snapshot | All four policy/data combinations, independent mutations, activation abort, exact caps/no count or terminal probe, monotonic deadlines and early cleanup |
| Database evidence | Actual protected query predicates, exact requested/continuation reader columns and zero entity materialization for declared projection |

Each absent optional capability runs an explicit protected-operation rejection.
Its rejection result is `Passed`; its behavior/consistency result is
`Unsupported`. Claimed-capability exceptions are `Failed`, never unsupported.
Every baseline and database-evidence result is required for an overall pass.
Missing real-provider evidence is `NotRun`, including recording/in-memory output
fixtures. An unsupported optional projection does not claim projected-column or
zero-materialization guarantees.

The seed is deliberately bounded: four authorized rows sharing client sort
values, duplicate individual composite components, nullable ranks, one other
tenant, one denied row and one row outside the allow. This is not exhaustive
collation/type, arbitrary-policy, concurrency or query-plan certification.
Fixture callbacks and evidence are trusted test infrastructure: never fabricate
observations to make checks pass. Use query/reader/materialization interception
and retain provider-specific translation and lifecycle tests.

The repository's `examples/AdapterConformance` console uses the public kit with
`CustomReadAdapter`. It has no xUnit dependency. Its output checks pass while
database evidence remains `NotRun` and overall `Passed` is false. A zero process
exit means the sample produced its expected honest report, not certification.

## Related packages and documentation

- [Core adapter contracts](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/repositories.md).
- [Runner-neutral sample fixture](https://github.com/linhnle/Kkts.DataAccess/tree/main/examples/AdapterConformance).
- [Provider evidence](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/providers.md)
  and [recorded acceptance results](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/repository-acceptance.md).
