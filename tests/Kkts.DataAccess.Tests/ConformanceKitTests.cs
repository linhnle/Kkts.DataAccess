using System.Text.Json;
using AdapterConformance;
using Kkts.DataAccess.Testing;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class ConformanceKitTests
{
    [Fact]
    public void Package_has_no_runner_provider_or_friend_access_dependency()
    {
        var assembly = typeof(AdapterConformanceSuite<>).Assembly;
        Assert.All(
            assembly.GetReferencedAssemblies(),
            reference =>
                Assert.True(
                    reference.Name == "Kkts.DataAccess"
                        || reference.Name!.StartsWith("System.", StringComparison.Ordinal),
                    reference.Name
                )
        );
        Assert.Empty(
            assembly.GetCustomAttributes(
                typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute),
                false
            )
        );
        Assert.Empty(
            typeof(EntityReadPlan<>).GetConstructors(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            )
        );
        Assert.DoesNotContain(
            typeof(CustomReadAdapter.CustomAdapter<>).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "Kkts.DataAccess.Testing"
        );
    }

    [Fact]
    public async Task Recording_checks_pass_without_claiming_database_enforcement()
    {
        var fixtures = new List<RecordingFixture>();
        var report = await new AdapterConformanceSuite<ConformanceEntity>().RunAsync(() =>
        {
            var fixture = new RecordingFixture();
            fixtures.Add(fixture);
            return fixture;
        });
        Assert.DoesNotContain(report.Results, result => result.Status == ConformanceStatus.Failed);
        Assert.Equal(
            ConformanceStatus.NotRun,
            Assert
                .Single(
                    report.Results,
                    result => result.ScenarioId == ConformanceScenario.DatabaseEvidence
                )
                .Status
        );
        Assert.False(report.Passed);
        Assert.All(fixtures, fixture => Assert.True(fixture.TornDown));
    }

    [Theory]
    [InlineData(RecordingFault.IgnoreDeny, ConformanceScenario.Authorization)]
    [InlineData(RecordingFault.OverReturn, ConformanceScenario.Output)]
    [InlineData(RecordingFault.LeakDisposal, ConformanceScenario.Disposal)]
    [InlineData(RecordingFault.BrokenSidecars, ConformanceScenario.Seek)]
    [InlineData(RecordingFault.DropSeek, ConformanceScenario.Seek)]
    public async Task Negative_controls_fail_the_matching_check(
        RecordingFault fault,
        string scenario
    )
    {
        var fixture = new RecordingFixture(fault);
        var report = await new AdapterConformanceSuite<ConformanceEntity>().RunAsync(
            () => fixture,
            [scenario]
        );
        Assert.Equal(
            ConformanceStatus.Failed,
            Assert.Single(report.Results, result => result.ScenarioId == scenario).Status
        );
        Assert.False(report.Passed);
        Assert.True(fixture.TornDown);
    }

    [Fact]
    public async Task Absent_optional_capabilities_run_explicit_rejections()
    {
        var report = await new AdapterConformanceSuite<ConformanceEntity>().RunAsync(() =>
            new RecordingFixture(capabilities: new(false, false, false, false))
        );
        Assert.DoesNotContain(report.Results, result => result.Status == ConformanceStatus.Failed);
        foreach (
            var unsupported in report.Results.Where(result =>
                result.Status == ConformanceStatus.Unsupported
            )
        )
            Assert.Contains(
                report.Results,
                result =>
                    result.ScenarioId == unsupported.ScenarioId + ".unsupported-rejection"
                    && result.Status == ConformanceStatus.Passed
            );
        Assert.False(report.Passed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Setup_failure_and_cancellation_dispose_only_registered_resources(
        bool canceled
    )
    {
        using var cancellation = new CancellationTokenSource();
        var owned = new Resource();
        var unowned = new Resource();
        var fixture = new FailedSetupFixture(owned, cancellation, canceled);
        if (canceled)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new AdapterConformanceSuite<ConformanceEntity>().RunAsync(
                    () => fixture,
                    [ConformanceScenario.Authorization],
                    cancellation.Token
                )
            );
        else
        {
            var report = await new AdapterConformanceSuite<ConformanceEntity>().RunAsync(
                () => fixture,
                [ConformanceScenario.Authorization]
            );
            Assert.Equal(
                ConformanceStatus.Failed,
                Assert
                    .Single(
                        report.Results,
                        result => result.ScenarioId == ConformanceScenario.Authorization
                    )
                    .Status
            );
            Assert.DoesNotContain("secret-sentinel", JsonSerializer.Serialize(report));
            Assert.Contains("setup", report.Results[0].Summary);
        }
        Assert.Equal(1, owned.Disposals);
        Assert.Equal(0, unowned.Disposals);
        Assert.True(fixture.Inner.TornDown);
    }

    [Fact]
    public async Task Owned_cleanup_is_reverse_order_idempotent_and_attempts_every_resource()
    {
        var order = new List<int>();
        var first = new Resource(() => order.Add(1));
        var second = new Resource(() =>
        {
            order.Add(2);
            throw new InvalidOperationException("secret-sentinel");
        });
        var ownership = new OwnedFixtureResources();
        ownership.Own(first);
        ownership.Own(second);
        Assert.Throws<ArgumentException>(() => ownership.Own(first));
        await Assert.ThrowsAsync<AggregateException>(() => ownership.DisposeAsync().AsTask());
        await ownership.DisposeAsync();
        Assert.Equal([2, 1], order);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
    }

    [Fact]
    public void Missing_required_results_and_unsupported_mandatory_checks_cannot_pass()
    {
        Assert.False(new ConformanceReport([]).Passed);
        var passed = ConformanceScenario
            .All.Select(id => new ConformanceResult(id, ConformanceStatus.Passed, "tested"))
            .ToArray();
        Assert.True(new ConformanceReport(passed).Passed);
        Assert.False(
            new ConformanceReport(
                passed.Where(result => result.ScenarioId != ConformanceScenario.Detail)
            ).Passed
        );
        Assert.False(
            new ConformanceReport(
                passed.Select(result =>
                    result.ScenarioId == ConformanceScenario.Authorization
                        ? result with
                        {
                            Status = ConformanceStatus.Unsupported,
                        }
                        : result
                )
            ).Passed
        );
        Assert.False(
            new ConformanceReport(
                passed.Select(result =>
                    result.ScenarioId == ConformanceScenario.DatabaseEvidence
                        ? result with
                        {
                            Status = ConformanceStatus.NotRun,
                        }
                        : result
                )
            ).Passed
        );
        Assert.Throws<ArgumentException>(() => new ConformanceReport([passed[0], passed[0]]));
    }

    sealed class Resource(Action? dispose = null) : IAsyncDisposable
    {
        public int Disposals;

        public ValueTask DisposeAsync()
        {
            Disposals++;
            dispose?.Invoke();
            return ValueTask.CompletedTask;
        }
    }

    sealed class FailedSetupFixture(
        Resource owned,
        CancellationTokenSource cancellation,
        bool cancel
    ) : IAdapterConformanceFixture<ConformanceEntity>
    {
        public readonly RecordingFixture Inner = new();
        public ConformanceBindings<ConformanceEntity> Bindings => Inner.Bindings;
        public ConformanceCapabilities Capabilities => Inner.Capabilities;
        public ConformanceProvider Provider => Inner.Provider;
        public Version ProviderVersion => Inner.ProviderVersion;
        public IContinuationTokenProtector ContinuationProtector => Inner.ContinuationProtector;

        public ValueTask SetupAsync(
            IReadOnlyList<ConformanceRecord> records,
            OwnedFixtureResources ownership,
            CancellationToken token
        )
        {
            ownership.Own(owned);
            if (cancel)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            throw new InvalidOperationException("secret-sentinel");
        }

        public IEntityReadAdapter<ConformanceEntity> CreateAdapter(
            ResourceSchema<ConformanceEntity> schema
        ) => Inner.CreateAdapter(schema);

        public ValueTask RenameAsync(int id, string name, CancellationToken token) =>
            Inner.RenameAsync(id, name, token);

        public ValueTask InjectTranslationFailureAsync(CancellationToken token) =>
            Inner.InjectTranslationFailureAsync(token);

        public ConformanceObservation Observe() => Inner.Observe();

        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
