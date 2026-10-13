using System.Linq.Expressions;
using System.Text.Json.Serialization;

namespace Kkts.DataAccess.Testing;

[JsonConverter(typeof(JsonStringEnumConverter<ConformanceStatus>))]
public enum ConformanceStatus
{
    Passed,
    Failed,
    Unsupported,
    NotRun,
}

[JsonConverter(typeof(JsonStringEnumConverter<ConformanceProvider>))]
public enum ConformanceProvider
{
    InMemory,
    Sqlite,
    SqlServer,
    MySql,
    Custom,
}

public static class ConformanceScenario
{
    public const string Authorization = "baseline.authorization";
    public const string Grants = "baseline.grants";
    public const string Detail = "baseline.detail";
    public const string Metadata = "baseline.count-matrix";
    public const string Output = "baseline.bounded-output";
    public const string Cancellation = "baseline.cancellation";
    public const string Disposal = "baseline.disposal";
    public const string TranslationFailure = "baseline.translation-failure";
    public const string Projection = "capability.projection";
    public const string Seek = "capability.seek";
    public const string Composite = "capability.composite-keys";
    public const string Cursor = "capability.cursor-compatibility";
    public const string Export = "capability.export";
    public const string Snapshot = "capability.snapshot";
    public const string DatabaseEvidence = "evidence.database-enforcement";

    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly(
            new[]
            {
                Authorization,
                Grants,
                Detail,
                Metadata,
                Output,
                Cancellation,
                Disposal,
                TranslationFailure,
                Projection,
                Seek,
                Composite,
                Cursor,
                Export,
                Snapshot,
                DatabaseEvidence,
            }
        );

    public static IReadOnlyList<string> Required { get; } =
        Array.AsReadOnly(
            new[]
            {
                Authorization,
                Grants,
                Detail,
                Metadata,
                Output,
                Cancellation,
                Disposal,
                TranslationFailure,
                DatabaseEvidence,
            }
        );
}

public sealed record ConformanceCapabilities(
    bool Projection = true,
    bool Seek = true,
    bool CompositeKeys = true,
    bool Snapshot = false
);

public sealed record ConformanceRecord(
    int Id,
    int TenantId,
    int GroupId,
    int LocalId,
    string Name,
    int? Rank,
    bool Denied = false
)
{
    public static IReadOnlyList<ConformanceRecord> Seed { get; } =
        Array.AsReadOnly(
            new[]
            {
                new ConformanceRecord(1, 1, 2, 1, "A", null),
                new ConformanceRecord(2, 1, 1, 2, "A", null),
                new ConformanceRecord(3, 1, 1, 1, "A", 0),
                new ConformanceRecord(4, 1, 2, 2, "B", 0),
                new ConformanceRecord(5, 2, 3, 1, "hidden", 0),
                new ConformanceRecord(6, 1, 4, 1, "denied", 0, true),
                new ConformanceRecord(7, 1, 20, 1, "not allowed", 0),
            }
        );
}

public sealed record ConformanceBindings<T>(
    Expression<Func<T, int>> Id,
    Expression<Func<T, int>> TenantId,
    Expression<Func<T, int>> GroupId,
    Expression<Func<T, int>> LocalId,
    Expression<Func<T, string>> Name,
    Expression<Func<T, int?>> Rank,
    Expression<Func<T, bool>> Denied
)
    where T : class;

public sealed record ConformanceObservation(
    int OpenedSessions,
    int DisposedSessions,
    int CountQueries,
    int RowQueries,
    bool DatabaseEvidenceAvailable = false,
    bool AuthorizationInQuery = false,
    bool RestrictedReaderColumns = false,
    int MaterializedEntities = 0
);

public interface IAdapterConformanceFixture<T> : IAsyncDisposable
    where T : class
{
    ConformanceBindings<T> Bindings { get; }
    ConformanceCapabilities Capabilities { get; }
    ConformanceProvider Provider { get; }
    Version? ProviderVersion { get; }
    IContinuationTokenProtector ContinuationProtector { get; }
    ValueTask SetupAsync(
        IReadOnlyList<ConformanceRecord> records,
        OwnedFixtureResources ownership,
        CancellationToken cancellation
    );
    IEntityReadAdapter<T> CreateAdapter(ResourceSchema<T> schema);
    ValueTask RenameAsync(int id, string name, CancellationToken cancellation);
    ValueTask InjectTranslationFailureAsync(CancellationToken cancellation);
    ConformanceObservation Observe();
}

public sealed class ConformanceAssertionException(string message) : Exception(message);

public static class ConformanceAssert
{
    public static void That(bool condition, string message)
    {
        if (!condition)
            throw new ConformanceAssertionException(message);
    }

    public static async Task ThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        throw new ConformanceAssertionException(message);
    }
}

public sealed record ConformanceResult(string ScenarioId, ConformanceStatus Status, string Summary)
{
    public string? Fixture { get; init; }
    public ConformanceProvider? Provider { get; init; }
    public string? ProviderVersion { get; init; }
}

public sealed class ConformanceReport
{
    public IReadOnlyList<ConformanceResult> Results { get; }
    public bool Passed =>
        ConformanceScenario.Required.All(id =>
            Results.Any(result =>
                result.ScenarioId == id && result.Status == ConformanceStatus.Passed
            )
        )
        && ConformanceScenario.All.All(id =>
            Results.Any(result =>
                result.ScenarioId == id
                && result.Status is ConformanceStatus.Passed or ConformanceStatus.Unsupported
            )
        )
        && Results.All(result =>
            result.Status is ConformanceStatus.Passed or ConformanceStatus.Unsupported
        )
        && Results
            .Where(result => result.Status == ConformanceStatus.Unsupported)
            .All(result =>
                Results.Any(rejection =>
                    rejection.ScenarioId == result.ScenarioId + ".unsupported-rejection"
                    && rejection.Status == ConformanceStatus.Passed
                )
            );

    public ConformanceReport(IEnumerable<ConformanceResult> results)
    {
        var snapshot = results.ToArray();
        if (
            snapshot.Select(result => result.ScenarioId).Distinct(StringComparer.Ordinal).Count()
            != snapshot.Length
        )
            throw new ArgumentException("Duplicate scenario results.", nameof(results));
        Results = Array.AsReadOnly(
            snapshot
                .Concat(
                    ConformanceScenario
                        .All.Where(id => snapshot.All(result => result.ScenarioId != id))
                        .Select(id => new ConformanceResult(
                            id,
                            ConformanceStatus.NotRun,
                            "Scenario not selected or no result supplied."
                        ))
                )
                .ToArray()
        );
    }
}
