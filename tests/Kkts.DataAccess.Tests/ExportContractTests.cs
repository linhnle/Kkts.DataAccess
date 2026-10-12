using System.Text.Json;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class ExportContractTests
{
    [Fact]
    public void Configuration_is_immutable_and_defaults_are_backward_compatible()
    {
        var config = new ExportReadConfiguration();
        Assert.Equal(ExportPolicyConsistency.CapturedPolicy, config.PolicyConsistency);
        Assert.Equal(ExportDataConsistency.BestEffort, config.DataConsistency);
        Assert.Null(config.MaxRows);
        Assert.Null(config.MaxDuration);
        Assert.Same(TimeProvider.System, config.TimeProvider);
        Assert.All(typeof(ExportReadConfiguration).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.Equal(long.MaxValue, new ExportReadConfiguration(maxRows: long.MaxValue).MaxRows);
        Assert.Equal(
            TimeSpan.FromMilliseconds(uint.MaxValue - 1),
            new ExportReadConfiguration(
                maxDuration: TimeSpan.FromMilliseconds(uint.MaxValue - 1)
            ).MaxDuration
        );
    }

    [Fact]
    public void Invalid_modes_bounds_and_timer_ranges_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExportReadConfiguration(policyConsistency: (ExportPolicyConsistency)99)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExportReadConfiguration(dataConsistency: (ExportDataConsistency)99)
        );
        foreach (var rows in new long[] { 0, -1, long.MinValue })
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ExportReadConfiguration(maxRows: rows)
            );
        foreach (
            var duration in new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromTicks(-1),
                TimeSpan.MaxValue,
                TimeSpan.FromMilliseconds(uint.MaxValue),
            }
        )
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ExportReadConfiguration(maxDuration: duration)
            );
    }

    [Fact]
    public void Stamps_require_positive_revision_and_matching_immutable_version()
    {
        var policy = CompilerTests.Capture(
            CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
        );
        var stamp = new PolicyActivationStamp(policy.Version, 1);
        Assert.Equal(stamp, new CapturedPolicyActivation(policy, stamp).Stamp);
        Assert.Throws<ArgumentException>(() =>
            new CapturedPolicyActivation(policy, new(policy.Version + 1, 1))
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => new PolicyActivationStamp(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PolicyActivationStamp(0, 1));
        Assert.Throws<ArgumentNullException>(() => new CapturedPolicyActivation(policy, null!));
        Assert.Throws<ArgumentNullException>(() => new CapturedPolicyActivation(null!, stamp));
    }

    [Fact]
    public void Snapshot_capability_does_not_require_entity_or_projected_seek()
    {
        var interfaces = typeof(IEntitySnapshotReadSession<CompilerTests.Employee>).GetInterfaces();
        Assert.Contains(typeof(IEntityReadSession<CompilerTests.Employee>), interfaces);
        Assert.DoesNotContain(typeof(IEntitySeekReadSession<CompilerTests.Employee>), interfaces);
        Assert.DoesNotContain(
            typeof(IEntityProjectedSeekReadSession<CompilerTests.Employee>),
            interfaces
        );
    }

    [Fact]
    public void Lifecycle_errors_have_stable_safe_messages_and_distinct_types()
    {
        Assert.True(typeof(AuthorizationException).IsSealed);
        Assert.IsAssignableFrom<DataAccessException>(new ExportDurationExceededException());
        Assert.Equal(
            DataAccessErrorCode.ExportPolicyChanged,
            new ExportPolicyChangedException().Code
        );
        Assert.Equal(
            DataAccessErrorCode.ExportDurationExceeded,
            new ExportDurationExceededException().Code
        );
        Assert.DoesNotContain(
            typeof(ExportPolicyChangedException).GetProperties(),
            p => p.DeclaringType == typeof(ExportPolicyChangedException)
        );
    }

    [Theory]
    [InlineData("policyConsistency", "\"AbortOnPolicyChange\"")]
    [InlineData("dataConsistency", "\"Snapshot\"")]
    [InlineData("maxRows", "6")]
    [InlineData("maxDuration", "\"00:00:01\"")]
    [InlineData("timeProvider", "{}")]
    public void Clients_cannot_select_lifecycle_settings(string name, string value)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ExportReadRequest>(
                $"{{\"{name}\":{value}}}",
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )
        );
        var batch = JsonSerializer.SerializeToElement(
            new ExportBatch(1, []),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        Assert.Equal(
            new[] { "policyVersion", "records" },
            batch.EnumerateObject().Select(p => p.Name).ToArray()
        );
    }
}
