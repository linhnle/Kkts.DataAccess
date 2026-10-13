using System.Runtime.ExceptionServices;
using System.Text.Json;
using static Kkts.DataAccess.Testing.ConformanceAssert;

namespace Kkts.DataAccess.Testing;

public sealed class AdapterConformanceSuite<T>
    where T : class
{
    public async Task<ConformanceReport> RunAsync(
        Func<IAdapterConformanceFixture<T>> fixtureFactory,
        IEnumerable<string>? scenarios = null,
        CancellationToken cancellation = default
    )
    {
        ArgumentNullException.ThrowIfNull(fixtureFactory);
        var ids = (scenarios ?? ConformanceScenario.All).ToArray();
        if (
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
            || ids.Any(id => !ConformanceScenario.All.Contains(id))
        )
            throw new ArgumentException("Unknown or duplicate scenario IDs.", nameof(scenarios));
        var results = new List<ConformanceResult>();
        foreach (var id in ids)
        {
            cancellation.ThrowIfCancellationRequested();
            var ownership = new OwnedFixtureResources();
            IAdapterConformanceFixture<T>? fixture = null;
            string? fixtureName = null;
            ConformanceProvider? provider = null;
            string? providerVersion = null;
            IReadOnlyList<ConformanceResult>? checks = null;
            Exception? failure = null;
            var phase = "setup";
            try
            {
                fixture = ownership.Own(
                    fixtureFactory()
                        ?? throw new InvalidOperationException("The fixture factory returned null.")
                );
                fixtureName = fixture.GetType().Name;
                provider = fixture.Provider;
                await fixture.SetupAsync(ConformanceRecord.Seed, ownership, cancellation);
                cancellation.ThrowIfCancellationRequested();
                phase = "scenario";
                checks = await ExecuteAsync(id, fixture, cancellation);
                providerVersion = fixture.ProviderVersion?.ToString();
            }
            catch (Exception error)
            {
                failure = error;
            }
            try
            {
                await ownership.DisposeAsync();
            }
            catch (Exception error)
            {
                failure = failure == null ? error : new AggregateException(failure, error);
                phase = "teardown";
            }
            if (failure != null && cancellation.IsCancellationRequested)
                ExceptionDispatchInfo.Capture(failure).Throw();
            if (failure != null)
                checks =
                [
                    new(
                        id,
                        ConformanceStatus.Failed,
                        failure is ConformanceAssertionException assertion && phase == "scenario"
                            ? assertion.Message
                            : $"Fixture {phase} failed ({failure.GetType().Name}); inspect the fixture locally."
                    ),
                ];
            foreach (
                var check in checks ?? throw new InvalidOperationException("No scenario result.")
            )
                results.Add(
                    check with
                    {
                        Fixture = fixtureName,
                        Provider = provider,
                        ProviderVersion = providerVersion,
                    }
                );
        }
        return new(results);
    }

    static async Task<IReadOnlyList<ConformanceResult>> ExecuteAsync(
        string id,
        IAdapterConformanceFixture<T> fixture,
        CancellationToken token
    )
    {
        var capability = id switch
        {
            ConformanceScenario.Projection => fixture.Capabilities.Projection,
            ConformanceScenario.Seek or ConformanceScenario.Cursor or ConformanceScenario.Export =>
                fixture.Capabilities.Seek,
            ConformanceScenario.Composite => fixture.Capabilities.CompositeKeys,
            ConformanceScenario.Snapshot => fixture.Capabilities.Snapshot,
            _ => true,
        };
        if (!capability)
        {
            await ThrowsAsync<ProviderUnsupportedException>(
                async () =>
                {
                    var unsupported = new ScenarioHarness<T>(
                        fixture,
                        composite: id == ConformanceScenario.Composite,
                        projection: id == ConformanceScenario.Projection
                            ? ReadProjectionMode.Required
                            : null,
                        export: id == ConformanceScenario.Snapshot
                            ? new(dataConsistency: ExportDataConsistency.Snapshot)
                            : null
                    );
                    if (id == ConformanceScenario.Projection || id == ConformanceScenario.Composite)
                        await unsupported.Repository.ListAsync(new(Limit: 1), token);
                    else if (id == ConformanceScenario.Snapshot || id == ConformanceScenario.Export)
                    {
                        await using var iterator = unsupported
                            .Repository.ExportAsync(new(), token)
                            .GetAsyncEnumerator(token);
                        await iterator.MoveNextAsync();
                    }
                    else
                        await unsupported.Repository.ListCursorAsync(new(Limit: 1), token);
                },
                "An absent capability must reject the protected operation."
            );
            Clean(fixture);
            return
            [
                new(
                    id,
                    ConformanceStatus.Unsupported,
                    "Optional capability absent; consistency/behavior not tested."
                ),
                new(
                    id + ".unsupported-rejection",
                    ConformanceStatus.Passed,
                    "Unsupported operation rejected explicitly."
                ),
            ];
        }
        var harness = new ScenarioHarness<T>(
            fixture,
            composite: id == ConformanceScenario.Composite
        );
        var reads = harness.Repository;
        switch (id)
        {
            case ConformanceScenario.Authorization:
                That(
                    (await reads.CountAsync(new(), token)).Total == 4,
                    "Mandatory AND allow-minus-deny count is incorrect."
                );
                CheckRows((await reads.ListAsync(new(Limit: 2), token)).Records, [1, 2]);
                harness.Tenant = 2;
                That(
                    (await reads.CountAsync(new(), token)).Total == 1,
                    "Mandatory identity isolation failed."
                );
                harness.Tenant = 1;
                That(
                    (await reads.CountAsync(new(), token)).Total == 4,
                    "Compiler context leaked between identities."
                );
                harness.Role = "no-grants";
                await ThrowsAsync<AuthorizationException>(
                    () => reads.CountAsync(new(), token),
                    "Missing allow did not fail closed."
                );
                break;
            case ConformanceScenario.Grants:
                await ThrowsAsync<AuthorizationException>(
                    () => reads.ListAsync(new(Selection: ["TenantId"], Limit: 1), token),
                    "Hidden selection was permitted."
                );
                await ThrowsAsync<AuthorizationException>(
                    () =>
                        reads.CountAsync(
                            new(
                                new Condition
                                {
                                    Field = "Id",
                                    Operator = "eq",
                                    Literal = JsonSerializer.SerializeToElement(1),
                                }
                            ),
                            token
                        ),
                    "Implicit key lookup granted explicit filtering."
                );
                await ThrowsAsync<AuthorizationException>(
                    () => reads.ListAsync(new(Sort: [new("Id")], Limit: 1), token),
                    "Key membership granted client sorting."
                );
                That(
                    (
                        await reads.CountAsync(
                            new(
                                new Condition
                                {
                                    Field = "Rank",
                                    Operator = "eq",
                                    Literal = JsonSerializer.SerializeToElement(0),
                                }
                            ),
                            token
                        )
                    ).Total == 2,
                    "Independent filter-only grant failed."
                );
                harness.Source.DenyName();
                var restricted = await reads.ListAsync(new(Limit: 1), token);
                That(restricted.PolicyVersion == 2, "Changed policy version was not captured.");
                That(
                    restricted
                        .Records[0]
                        .EnumerateObject()
                        .Select(property => property.Name)
                        .SequenceEqual(["Id"]),
                    "Field deny did not override the field allow."
                );
                await ThrowsAsync<AuthorizationException>(
                    () => reads.ListAsync(new(Selection: ["Name"], Limit: 1), token),
                    "Denied output field remained selectable."
                );
                break;
            case ConformanceScenario.Detail:
                CheckRows(
                    [
                        (await reads.DetailAsync(new(ScenarioHarness<T>.Key(1)), token)).Record
                            ?? throw new ConformanceAssertionException(
                                "Authorized detail is absent."
                            ),
                    ],
                    [1]
                );
                That(
                    (
                        await reads.DetailAsync(
                            new(ScenarioHarness<T>.Key(1), Selection: []),
                            token
                        )
                    ).Record!.Value.GetRawText() == "{}",
                    "Empty detail selection changed output."
                );
                foreach (var hidden in new[] { 5, 6, 7 })
                    That(
                        (await reads.DetailAsync(new(ScenarioHarness<T>.Key(hidden)), token)).Record
                            == null,
                        "Inaccessible detail was disclosed."
                    );
                That(
                    (
                        await reads.DetailAsync(
                            new(JsonSerializer.SerializeToElement(new { Id = 99 })),
                            token
                        )
                    ).Record == null,
                    "Absent detail behavior differs."
                );
                foreach (
                    var json in new[]
                    {
                        "1",
                        "{}",
                        """{"Id":null}""",
                        """{"Id":"1"}""",
                        """{"id":1}""",
                        """{"Id":1,"Id":2}""",
                        """{"Id":1,"extra":2}""",
                    }
                )
                {
                    var before = fixture.Observe().OpenedSessions;
                    await ThrowsAsync<InvalidRequestException>(
                        () => reads.DetailAsync(new(JsonDocument.Parse(json).RootElement), token),
                        "Malformed object key was accepted."
                    );
                    That(
                        fixture.Observe().OpenedSessions == before,
                        "Invalid detail opened an entity session."
                    );
                }
                break;
            case ConformanceScenario.Metadata:
                foreach (var total in new[] { false, true })
                foreach (var more in new[] { false, true })
                {
                    var before = fixture.Observe();
                    var page = await reads.ListAsync(
                        new(Limit: 2, IncludeTotal: total, IncludeHasMore: more),
                        token
                    );
                    CheckRows(page.Records, [1, 2]);
                    That(page.Total == (total ? 4L : null), "Optional total value is incorrect.");
                    That(
                        page.HasMore == (more ? true : null),
                        "Optional lookahead value is incorrect."
                    );
                    That(
                        fixture.Observe().CountQueries - before.CountQueries == (total ? 1 : 0),
                        "The optional-count matrix executed the wrong number of aggregates."
                    );
                }
                var last = await reads.ListAsync(
                    new(Offset: 3, Limit: 2, IncludeTotal: false, IncludeHasMore: true),
                    token
                );
                CheckRows(last.Records, [4]);
                That(last.HasMore == false, "Terminal has-more is incorrect.");
                break;
            case ConformanceScenario.Output:
                CheckRows(
                    (await reads.ListAsync(new(Sort: [new("Name")], Limit: 2), token)).Records,
                    [1, 2]
                );
                CheckRows(
                    (
                        await reads.ListAsync(new(Sort: [new("Name")], Offset: 2, Limit: 2), token)
                    ).Records,
                    [3, 4]
                );
                var empty = await reads.ListAsync(new(Selection: [], Limit: 2), token);
                That(
                    empty.Records.Count == 2 && empty.Records.All(row => row.GetRawText() == "{}"),
                    "Empty selection lost cardinality or leaked fields."
                );
                await ThrowsAsync<InvalidRequestException>(
                    () => reads.ListAsync(new(Limit: 3), token),
                    "The host page bound was not enforced."
                );
                break;
            case ConformanceScenario.Cancellation:
                using (var canceled = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    canceled.Cancel();
                    var before = fixture.Observe().OpenedSessions;
                    await ThrowsAsync<OperationCanceledException>(
                        () => reads.CountAsync(new(), canceled.Token),
                        "Caller cancellation was ignored."
                    );
                    That(
                        fixture.Observe().OpenedSessions == before,
                        "Pre-cancellation opened an entity session."
                    );
                }
                if (fixture.Capabilities.Seek)
                {
                    using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
                    await using var iterator = reads
                        .ExportAsync(new(), canceled.Token)
                        .GetAsyncEnumerator(canceled.Token);
                    That(await iterator.MoveNextAsync(), "Export did not begin.");
                    canceled.Cancel();
                    await ThrowsAsync<OperationCanceledException>(
                        () => iterator.MoveNextAsync().AsTask(),
                        "Streaming cancellation was ignored."
                    );
                }
                break;
            case ConformanceScenario.Disposal:
                await reads.CountAsync(new(), token);
                await reads.ListAsync(new(Limit: 1), token);
                await reads.DetailAsync(new(ScenarioHarness<T>.Key(1)), token);
                if (fixture.Capabilities.Seek)
                {
                    await using var iterator = reads
                        .ExportAsync(new(), token)
                        .GetAsyncEnumerator(token);
                    That(await iterator.MoveNextAsync(), "Early export did not begin.");
                }
                break;
            case ConformanceScenario.TranslationFailure:
                await fixture.InjectTranslationFailureAsync(token);
                var beforeFailure = fixture.Observe();
                await ThrowsAsync<InvalidOperationException>(
                    () => reads.ListAsync(new(Limit: 1, IncludeTotal: false), token),
                    "Translation failure was hidden by successful fallback."
                );
                That(
                    fixture.Observe().RowQueries - beforeFailure.RowQueries == 1,
                    "Failed translation retried retrieval or did not attempt the fault."
                );
                break;
            case ConformanceScenario.Projection:
                CheckRows((await reads.ListAsync(new(Limit: 2), token)).Records, [1, 2]);
                CheckRows(
                    [
                        (await reads.DetailAsync(new(ScenarioHarness<T>.Key(1)), token))
                            .Record!
                            .Value,
                    ],
                    [1]
                );
                if (fixture.Capabilities.Seek)
                    CheckRows((await reads.ListCursorAsync(new(Limit: 1), token)).Records, [1]);
                That(
                    fixture.Observe().MaterializedEntities == 0,
                    "Projected reads materialized entities."
                );
                break;
            case ConformanceScenario.Seek:
                await Traverse(reads, [new("Name")], [1, 2, 3, 4], token);
                await Traverse(reads, [new("Rank"), new("LocalAlias", true)], [2, 1, 4, 3], token);
                break;
            case ConformanceScenario.Composite:
                CheckRows(
                    (await reads.ListAsync(new(Sort: [new("Name")], Limit: 2), token)).Records,
                    [3, 2]
                );
                if (fixture.Capabilities.Seek)
                {
                    await Traverse(reads, [new("Name")], [3, 2, 1, 4], token);
                    await Traverse(
                        reads,
                        [new("Rank"), new("LocalAlias", true), new("LocalAlias", true)],
                        [2, 1, 4, 3],
                        token
                    );
                }
                CheckRows(
                    [
                        (await reads.DetailAsync(new(ScenarioHarness<T>.Key(2, true)), token))
                            .Record!
                            .Value,
                    ],
                    [2]
                );
                break;
            case ConformanceScenario.Cursor:
                var first = await reads.ListCursorAsync(new(Limit: 1), token);
                That(first.NextContinuationToken != null, "A cursor boundary is absent.");
                var next = await reads.ListCursorAsync(
                    new(
                        Limit: 1,
                        ContinuationToken: first.NextContinuationToken,
                        IncludeTotal: false,
                        IncludeHasMore: true
                    ),
                    token
                );
                CheckRows(next.Records, [2]);
                That(
                    next.Total == null && next.HasMore == true,
                    "Metadata-only changes invalidated cursor semantics."
                );
                harness.Tenant = 2;
                await ThrowsAsync<InvalidContinuationTokenException>(
                    () =>
                        reads.ListCursorAsync(
                            new(Limit: 1, ContinuationToken: first.NextContinuationToken),
                            token
                        ),
                    "A cursor replayed across identity contexts."
                );
                harness.Tenant = 1;
                await ThrowsAsync<InvalidContinuationTokenException>(
                    () =>
                        reads.ListCursorAsync(
                            new(Limit: 2, ContinuationToken: first.NextContinuationToken),
                            token
                        ),
                    "A cursor replayed across query bindings."
                );
                await ThrowsAsync<InvalidContinuationTokenException>(
                    () =>
                        reads.ListCursorAsync(
                            new(
                                Limit: 1,
                                Sort: [new("Name")],
                                ContinuationToken: first.NextContinuationToken
                            ),
                            token
                        ),
                    "A cursor replayed across order bindings."
                );
                await ThrowsAsync<InvalidContinuationTokenException>(
                    () =>
                        reads.ListCursorAsync(
                            new(Limit: 1, ContinuationToken: first.NextContinuationToken + "x"),
                            token
                        ),
                    "A modified protected cursor was accepted."
                );
                harness.Source.DenyName();
                await ThrowsAsync<InvalidContinuationTokenException>(
                    () =>
                        reads.ListCursorAsync(
                            new(Limit: 1, ContinuationToken: first.NextContinuationToken),
                            token
                        ),
                    "A cursor replayed across policy versions."
                );
                break;
            case ConformanceScenario.Export:
                await ExportModes(fixture, snapshot: false, token);
                break;
            case ConformanceScenario.Snapshot:
                await ExportModes(fixture, snapshot: true, token);
                break;
            case ConformanceScenario.DatabaseEvidence:
                await reads.ListAsync(new(Limit: 2, IncludeTotal: false), token);
                if (fixture.Capabilities.Seek)
                    await reads.ListCursorAsync(new(Limit: 1, IncludeTotal: false), token);
                var evidence = fixture.Observe();
                if (
                    !evidence.DatabaseEvidenceAvailable
                    || fixture.Provider == ConformanceProvider.InMemory
                    || fixture.ProviderVersion == null
                )
                    return
                    [
                        new(
                            id,
                            ConformanceStatus.NotRun,
                            "Missing real-provider query/reader/materialization evidence; output is not database proof."
                        ),
                    ];
                That(
                    evidence.AuthorizationInQuery,
                    "Query evidence does not establish mandatory/allow/deny database predicates."
                );
                if (fixture.Capabilities.Projection)
                {
                    That(
                        evidence.RestrictedReaderColumns,
                        "Reader evidence includes unrequested hidden fields."
                    );
                    That(
                        evidence.MaterializedEntities == 0,
                        "Database projection materialized entities."
                    );
                }
                break;
            default:
                throw new ArgumentException("Unknown scenario.", nameof(id));
        }
        if (
            id
                is ConformanceScenario.Projection
                    or ConformanceScenario.Seek
                    or ConformanceScenario.Composite
            && fixture.Capabilities.Projection
            && fixture.Observe().DatabaseEvidenceAvailable
        )
        {
            That(
                fixture.Observe().RestrictedReaderColumns,
                "Reader evidence includes fields outside the exact output/continuation bindings."
            );
            That(
                fixture.Observe().MaterializedEntities == 0,
                "Projected traversal materialized entities."
            );
        }
        Clean(fixture);
        return [new(id, ConformanceStatus.Passed, "Checks passed for this isolated fixture only.")];
    }

    static void Clean(IAdapterConformanceFixture<T> fixture) =>
        That(
            fixture.Observe().OpenedSessions == fixture.Observe().DisposedSessions,
            "An owned entity session was not disposed."
        );

    static void CheckRows(IReadOnlyList<JsonElement> rows, int[] ids)
    {
        That(rows.Count == ids.Length, "Protected output has incorrect bounded cardinality.");
        for (var index = 0; index < ids.Length; index++)
        {
            That(
                rows[index].ValueKind == JsonValueKind.Object,
                "Protected output is not an object."
            );
            That(
                rows[index]
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .SequenceEqual(["Id", "Name"]),
                "Protected output leaked or omitted fields."
            );
            That(
                rows[index].GetProperty("Id").GetInt32() == ids[index],
                "Protected output order or authorization is incorrect."
            );
            var expected = ConformanceRecord.Seed.Single(record => record.Id == ids[index]);
            That(
                rows[index].GetProperty("Name").GetString() == expected.Name,
                "Protected output values are incorrect."
            );
        }
    }

    static async Task Traverse(
        IAuthorizedReadRepository<T> reads,
        SortField[] sort,
        int[] expected,
        CancellationToken token
    )
    {
        var ids = new List<int>();
        string? continuation = null;
        do
        {
            That(
                ids.Count <= expected.Length,
                "Seek traversal repeated rows or failed to terminate."
            );
            var page = await reads.ListCursorAsync(
                new(
                    Sort: sort,
                    Limit: 1,
                    ContinuationToken: continuation,
                    IncludeTotal: false,
                    IncludeHasMore: true
                ),
                token
            );
            That(page.Records.Count <= 1, "Seek returned lookahead as output.");
            foreach (var row in page.Records)
            {
                var id = row.GetProperty("Id").GetInt32();
                CheckRows([row], [id]);
                ids.Add(id);
            }
            continuation = page.NextContinuationToken;
            That(page.HasMore == (continuation != null), "Cursor lookahead metadata is incorrect.");
        } while (continuation != null);
        That(
            ids.SequenceEqual(expected),
            "Seek traversal dropped, repeated, or misordered equal-prefix rows."
        );
        var exported = new List<int>();
        await foreach (var batch in reads.ExportAsync(new(Sort: sort), token))
        {
            That(exported.Count <= expected.Length, "Export failed to terminate.");
            That(batch.Records.Count <= 1, "Export exceeded the host batch size.");
            foreach (var row in batch.Records)
                exported.Add(row.GetProperty("Id").GetInt32());
        }
        That(exported.SequenceEqual(expected), "Export and cursor traversal differ.");
    }

    static async Task ExportModes(
        IAdapterConformanceFixture<T> fixture,
        bool snapshot,
        CancellationToken token
    )
    {
        foreach (var abort in new[] { false, true })
        {
            var clock = new ScenarioClock();
            var harness = new ScenarioHarness<T>(
                fixture,
                export: new(
                    abort
                        ? ExportPolicyConsistency.AbortOnPolicyChange
                        : ExportPolicyConsistency.CapturedPolicy,
                    snapshot ? ExportDataConsistency.Snapshot : ExportDataConsistency.BestEffort,
                    maxRows: 3,
                    maxDuration: TimeSpan.FromSeconds(5),
                    timeProvider: clock
                )
            );
            var before = fixture.Observe();
            await using (
                var iterator = harness
                    .Repository.ExportAsync(new(), token)
                    .GetAsyncEnumerator(token)
            )
            {
                That(await iterator.MoveNextAsync(), "Export did not begin.");
                CheckRows(iterator.Current.Records, [1]);
                await fixture.RenameAsync(2, "mutated", token);
                That(await iterator.MoveNextAsync(), "Export lost the next row.");
                var second = iterator.Current.Records.Single();
                That(
                    second.GetProperty("Id").GetInt32() == 2
                        && second.GetProperty("Name").GetString() == (snapshot ? "A" : "mutated"),
                    "Export data consistency mode is incorrect."
                );
                That(await iterator.MoveNextAsync(), "Capped export ended early.");
                CheckRows(iterator.Current.Records, [3]);
                That(!await iterator.MoveNextAsync(), "Capped export delivered extra rows.");
            }
            var after = fixture.Observe();
            That(
                after.CountQueries == before.CountQueries
                    && after.RowQueries - before.RowQueries == 3,
                "Capped export issued an aggregate, excess query, or terminal probe."
            );
            await fixture.RenameAsync(2, "A", token);
            await using (
                var iterator = harness
                    .Repository.ExportAsync(new(), token)
                    .GetAsyncEnumerator(token)
            )
            {
                That(await iterator.MoveNextAsync(), "Deadline export did not begin.");
                clock.Advance(TimeSpan.FromSeconds(5));
                await ThrowsAsync<ExportDurationExceededException>(
                    () => iterator.MoveNextAsync().AsTask(),
                    "Expired export completed successfully."
                );
            }
            await using (
                var iterator = harness
                    .Repository.ExportAsync(new(), token)
                    .GetAsyncEnumerator(token)
            )
            {
                That(await iterator.MoveNextAsync(), "Activation export did not begin.");
                harness.Source.Activate();
                if (abort)
                    await ThrowsAsync<ExportPolicyChangedException>(
                        () => iterator.MoveNextAsync().AsTask(),
                        "Policy activation change did not abort."
                    );
                else
                    That(
                        await iterator.MoveNextAsync(),
                        "Captured-policy export rebound or aborted."
                    );
            }
            Clean(fixture);
        }
    }
}
