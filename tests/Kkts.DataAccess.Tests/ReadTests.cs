using System.Text.Json;
using Kkts.DataAccess.Sqlite;
using Kkts.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Kkts.DataAccess.Tests;

public partial class ReadTests : IAsyncLifetime
{
    public class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        public bool Disposed { get; private set; }

        public override void Dispose()
        {
            Disposed = true;
            base.Dispose();
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<CompilerTests.Employee>().HasKey(e => e.Id);
            b.Entity<CompilerTests.Employee>().Property(e => e.Id).ValueGeneratedNever();
            b.Entity<Boundary>().HasKey(e => e.Id);
            b.Entity<Boundary>().Property(e => e.Id).ValueGeneratedNever();
            b.Entity<Boundary>().Property(e => e.Amount).HasPrecision(18, 2);
            b.Entity<Boundary>().Property(e => e.Converted).HasConversion<string>();
            b.Entity<CompositeRow>().HasKey(row => new { row.GroupId, row.LocalId });
            b.Entity<CompositeRow>().Property(row => row.LocalId).ValueGeneratedNever();
            b.Entity<AdapterConformance.ConformanceEntity>()
                .HasKey(row => new { row.GroupId, row.LocalId });
            b.Entity<AdapterConformance.ConformanceEntity>()
                .Property(row => row.LocalId)
                .ValueGeneratedNever();
        }
    }

    public class CompositeRow
    {
        public int GroupId { get; set; }
        public int LocalId { get; set; }
        public int TenantId { get; set; }
        public int? Rank { get; set; }
        public string Label { get; set; } = "";
        public bool Denied { get; set; }
        public string Secret { get; set; } = "must not materialize";
    }

    [Fact]
    public async Task Provider_consumes_runner_neutral_conformance_checks_with_real_evidence()
    {
        var report =
            await new Testing.AdapterConformanceSuite<AdapterConformance.ConformanceEntity>().RunAsync(
                () =>
                    new ProviderConformanceFixture(this)
            );
        Assert.True(report.Passed, System.Text.Json.JsonSerializer.Serialize(report));
        Assert.Equal(Testing.ConformanceScenario.All.Count, report.Results.Count);
    }

    [Fact]
    public async Task Provider_missing_conformance_evidence_is_explicitly_not_run()
    {
        var report =
            await new Testing.AdapterConformanceSuite<AdapterConformance.ConformanceEntity>().RunAsync(
                () => new ProviderConformanceFixture(this, omitEvidence: true),
                [Testing.ConformanceScenario.DatabaseEvidence]
            );
        Assert.Equal(
            Testing.ConformanceStatus.NotRun,
            Assert
                .Single(
                    report.Results,
                    result => result.ScenarioId == Testing.ConformanceScenario.DatabaseEvidence
                )
                .Status
        );
        Assert.False(report.Passed);
    }

    sealed class ProviderConformanceFixture(ReadTests owner, bool omitEvidence = false)
        : Testing.IAdapterConformanceFixture<AdapterConformance.ConformanceEntity>
    {
        readonly SeekCommands commands = new();
        readonly List<Database> sessions = [];
        IContinuationTokenProtector? protector;
        bool translationFailure;
        int translationAttempts;
        Version? providerVersion;
        HashSet<string> expectedColumns = [];
        public Testing.ConformanceBindings<AdapterConformance.ConformanceEntity> Bindings { get; } =
            new(
                row => row.Id,
                row => row.TenantId,
                row => row.GroupId,
                row => row.LocalId,
                row => row.Name,
                row => row.Rank,
                row => row.Denied
            );
        public Testing.ConformanceCapabilities Capabilities => new(Snapshot: true);
        public Testing.ConformanceProvider Provider =>
            owner.ProviderName switch
            {
                "Microsoft.EntityFrameworkCore.Sqlite" => Testing.ConformanceProvider.Sqlite,
                "Microsoft.EntityFrameworkCore.SqlServer" => Testing.ConformanceProvider.SqlServer,
                "MySql.EntityFrameworkCore" => Testing.ConformanceProvider.MySql,
                _ => throw new NotSupportedException("Unknown conformance provider."),
            };
        public Version? ProviderVersion => providerVersion;
        public IContinuationTokenProtector ContinuationProtector =>
            protector ?? throw new InvalidOperationException("Fixture setup has not run.");

        public async ValueTask SetupAsync(
            IReadOnlyList<Testing.ConformanceRecord> records,
            Testing.OwnedFixtureResources ownership,
            CancellationToken token
        )
        {
            await owner.PrepareSnapshot();
            await using var setup = new Database(owner.Options);
            await setup.Set<AdapterConformance.ConformanceEntity>().ExecuteDeleteAsync(token);
            setup.AddRange(records.Select(AdapterConformance.ConformanceEntity.From));
            await setup.SaveChangesAsync(token);
            protector = new DataProtection.DataProtectionContinuationTokenProtector(
                new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()
            );
            commands.ColumnCheck = (sql, columns) =>
                owner.IsCountQuery(sql)
                || (
                    expectedColumns.Count == 0
                        ? columns.Length == 1
                            && !typeof(AdapterConformance.ConformanceEntity)
                                .GetProperties()
                                .Any(property => property.Name == columns[0])
                        : columns.Length == expectedColumns.Count
                            && columns.ToHashSet(StringComparer.Ordinal).SetEquals(expectedColumns)
                );
        }

        Database Open()
        {
            var options = new DbContextOptionsBuilder<Database>(owner.Options)
                .AddInterceptors(commands, MaterializationCounter.Instance)
                .Options;
            var database = new Database(options);
            sessions.Add(database);
            return database;
        }

        public IEntityReadAdapter<AdapterConformance.ConformanceEntity> CreateAdapter(
            ResourceSchema<AdapterConformance.ConformanceEntity> schema
        )
        {
            var operations = ReadOperations.All | ReadOperations.Snapshot;
            IEntityReadAdapter<AdapterConformance.ConformanceEntity> adapter =
                owner.ProviderName switch
                {
                    "Microsoft.EntityFrameworkCore.Sqlite" =>
                        SqliteEntityReads.Create<AdapterConformance.ConformanceEntity>(
                            schema,
                            Open,
                            operations
                        ),
                    "Microsoft.EntityFrameworkCore.SqlServer" =>
                        SqlServer.SqlServerEntityReads.Create<AdapterConformance.ConformanceEntity>(
                            schema,
                            Open,
                            operations
                        ),
                    "MySql.EntityFrameworkCore" =>
                        MySql.MySqlEntityReads.Create<AdapterConformance.ConformanceEntity>(
                            schema,
                            Open,
                            operations
                        ),
                    _ => throw new NotSupportedException("Unknown conformance provider."),
                };
            if (
                adapter
                    is not IReadProviderCapabilitySource
                    {
                        Capabilities: ReadProviderCapabilities profile
                    }
                || !Version.TryParse(
                    profile.ImplementationVersion.Split('+')[0],
                    out providerVersion
                )
            )
                throw new InvalidOperationException("Missing provider implementation version.");
            return new CustomReadAdapter.CustomAdapter<AdapterConformance.ConformanceEntity>(
                async token =>
                {
                    if (!translationFailure)
                        return new TrackedSession(this, await adapter.OpenAsync(token));
                    translationFailure = false;
                    return new TranslationFailureSession(this, Open());
                }
            );
        }

        public async ValueTask RenameAsync(int id, string name, CancellationToken token)
        {
            await using var mutation = new Database(owner.Options);
            var row = await mutation
                .Set<AdapterConformance.ConformanceEntity>()
                .SingleAsync(row => row.Id == id, token);
            row.Name = name;
            await mutation.SaveChangesAsync(token);
        }

        public ValueTask InjectTranslationFailureAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            translationFailure = true;
            return ValueTask.CompletedTask;
        }

        public Testing.ConformanceObservation Observe()
        {
            var rowQueries = commands.Sql.Where(sql => !owner.IsCountQuery(sql)).ToArray();
            var queryEvidence =
                rowQueries.Length != 0
                && rowQueries.All(sql =>
                {
                    var start = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
                    if (start < 0)
                        return false;
                    var predicate = sql[start..];
                    return predicate.Contains("TenantId", StringComparison.Ordinal)
                        && predicate.Contains("GroupId", StringComparison.Ordinal)
                        && predicate.Contains("Denied", StringComparison.Ordinal);
                });
            string[] permitted = ["Id", "Name", "Rank", "LocalId", "GroupId"];
            var columns = commands
                .Columns.Where(names => names.Any(name => permitted.Contains(name)))
                .ToArray();
            return new(
                sessions.Count,
                sessions.Count(database => database.Disposed),
                commands.Sql.Count(owner.IsCountQuery),
                rowQueries.Length + translationAttempts,
                DatabaseEvidenceAvailable: !omitEvidence
                    && rowQueries.Length != 0
                    && columns.Length != 0,
                AuthorizationInQuery: queryEvidence,
                RestrictedReaderColumns: commands.ColumnCheckPassed && columns.Length != 0,
                MaterializedEntities: commands.Entities
            );
        }

        public ValueTask DisposeAsync()
        {
            protector = null;
            return ValueTask.CompletedTask;
        }

        void Expect(
            EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
            EntitySeekPlan<AdapterConformance.ConformanceEntity>? seek = null
        ) =>
            expectedColumns = plan
                .OutputBindings.Select(binding => binding.Property.Name)
                .Concat(seek?.ContinuationBindings.Select(property => property.Name) ?? [])
                .ToHashSet(StringComparer.Ordinal);

        sealed class TrackedSession(
            ProviderConformanceFixture fixture,
            IEntityReadSession<AdapterConformance.ConformanceEntity> session
        )
            : IEntityProjectedSeekReadSession<AdapterConformance.ConformanceEntity>,
                IEntitySnapshotReadSession<AdapterConformance.ConformanceEntity>
        {
            public Task<long> CountAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                CancellationToken token
            ) => session.CountAsync(plan, token);

            public Task<IReadOnlyList<ProjectedReadRow>> PageProjectedAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                int offset,
                int limit,
                CancellationToken token
            )
            {
                fixture.Expect(plan);
                return (
                    (IEntityProjectedReadSession<AdapterConformance.ConformanceEntity>)session
                ).PageProjectedAsync(plan, offset, limit, token);
            }

            public Task<ProjectedReadRow?> SingleProjectedAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                CancellationToken token
            )
            {
                fixture.Expect(plan);
                return (
                    (IEntityProjectedReadSession<AdapterConformance.ConformanceEntity>)session
                ).SingleProjectedAsync(plan, token);
            }

            public Task<IReadOnlyList<ProjectedReadRow>> SeekProjectedAsync(
                EntitySeekPlan<AdapterConformance.ConformanceEntity> plan,
                int limit,
                CancellationToken token
            )
            {
                fixture.Expect(plan.Read, plan);
                return (
                    (IEntityProjectedSeekReadSession<AdapterConformance.ConformanceEntity>)session
                ).SeekProjectedAsync(plan, limit, token);
            }

            public Task BeginSnapshotAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                CancellationToken token
            ) =>
                (
                    (IEntitySnapshotReadSession<AdapterConformance.ConformanceEntity>)session
                ).BeginSnapshotAsync(plan, token);

            public Task<IReadOnlyList<AdapterConformance.ConformanceEntity>> PageAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                int offset,
                int limit,
                CancellationToken token
            ) => session.PageAsync(plan, offset, limit, token);

            public Task<AdapterConformance.ConformanceEntity?> SingleAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                CancellationToken token
            ) => session.SingleAsync(plan, token);

            public ValueTask DisposeAsync() => session.DisposeAsync();
        }

        sealed class TranslationFailureSession(
            ProviderConformanceFixture fixture,
            Database database
        ) : IEntityProjectedReadSession<AdapterConformance.ConformanceEntity>
        {
            static bool Untranslatable(string value) => value.Length > 0;

            public async Task<IReadOnlyList<ProjectedReadRow>> PageProjectedAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                int offset,
                int limit,
                CancellationToken token
            )
            {
                fixture.translationAttempts++;
                var rows = await database
                    .Set<AdapterConformance.ConformanceEntity>()
                    .Where(plan.Predicate)
                    .Where(row => Untranslatable(row.Name))
                    .Skip(offset)
                    .Take(limit)
                    .ToArrayAsync(token);
                return rows.Select(row => new ProjectedReadRow(
                        plan.OutputBindings.Select(binding => binding.Property.GetValue(row))
                    ))
                    .ToArray();
            }

            public Task<ProjectedReadRow?> SingleProjectedAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                CancellationToken token
            ) =>
                throw new NotSupportedException(
                    "Fault injection supports row-page translation only."
                );

            public Task<long> CountAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                CancellationToken token
            ) =>
                throw new NotSupportedException(
                    "Fault injection supports row-page translation only."
                );

            public Task<IReadOnlyList<AdapterConformance.ConformanceEntity>> PageAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                int offset,
                int limit,
                CancellationToken token
            ) =>
                throw new NotSupportedException(
                    "Fault injection supports projected translation only."
                );

            public Task<AdapterConformance.ConformanceEntity?> SingleAsync(
                EntityReadPlan<AdapterConformance.ConformanceEntity> plan,
                CancellationToken token
            ) =>
                throw new NotSupportedException(
                    "Fault injection supports row-page translation only."
                );

            public ValueTask DisposeAsync() => database.DisposeAsync();
        }
    }

    static readonly ResourceSchema<CompositeRow> CompositeSchema = new(
        "Composite",
        "1",
        [
            ResourceSchema<CompositeRow>.Field("GroupId", row => row.GroupId, []),
            ResourceSchema<CompositeRow>.Field("LocalId", row => row.LocalId, []),
            ResourceSchema<CompositeRow>.Field(
                "LocalAlias",
                row => row.LocalId,
                [FieldOperation.Sort]
            ),
            ResourceSchema<CompositeRow>.Field("TenantId", row => row.TenantId, []),
            ResourceSchema<CompositeRow>.Field("Rank", row => row.Rank, [FieldOperation.Sort]),
            ResourceSchema<CompositeRow>.Field("Label", row => row.Label, [FieldOperation.Select]),
            ResourceSchema<CompositeRow>.Field("Denied", row => row.Denied, []),
        ],
        ["GroupId", "LocalId"]
    );

    async Task SeedComposite()
    {
        await using var db = new Database(Options);
        db.AddRange(
            new CompositeRow
            {
                GroupId = 1,
                LocalId = 1,
                TenantId = 1,
                Rank = 0,
                Label = "g1l1",
            },
            new CompositeRow
            {
                GroupId = 1,
                LocalId = 2,
                TenantId = 1,
                Label = "g1l2",
            },
            new CompositeRow
            {
                GroupId = 2,
                LocalId = 1,
                TenantId = 1,
                Rank = 0,
                Label = "g2l1",
            },
            new CompositeRow
            {
                GroupId = 2,
                LocalId = 2,
                TenantId = 1,
                Label = "g2l2",
            },
            new CompositeRow
            {
                GroupId = 3,
                LocalId = 1,
                TenantId = 2,
                Label = "hidden",
            },
            new CompositeRow
            {
                GroupId = 4,
                LocalId = 1,
                TenantId = 1,
                Label = "denied",
                Denied = true,
            }
        );
        await db.SaveChangesAsync();
        var policy = new PolicyDefinition(
            "Composite",
            "1",
            [
                new("allow", "Composite", "Read", Effect.Allow, new([], []), null),
                new(
                    "deny",
                    "Composite",
                    "Read",
                    Effect.Deny,
                    new([], []),
                    CompilerTests.Leaf("Denied", "eq", true)
                ),
            ],
            [
                new(
                    "fields",
                    "Composite",
                    "Read",
                    Effect.Allow,
                    new([], []),
                    new()
                    {
                        [FieldOperation.Select] = ["Label"],
                        [FieldOperation.Sort] = ["Rank", "LocalAlias"],
                    }
                ),
            ]
        );
        await Store.SaveDraftAsync(admin, "scope", "Composite", PolicyJson.Serialize(policy), 0);
        await Store.PublishAsync(admin, "scope", CompositeSchema, CompilerTests.Registry, 1, 0);
    }

    AuthorizedReadRepository<CompositeRow> CompositeReads(
        List<Database> sessions,
        SeekCommands commands,
        ExportReadConfiguration? export = null
    )
    {
        var options = new DbContextOptionsBuilder<Database>(Options)
            .AddInterceptors(commands, MaterializationCounter.Instance)
            .Options;
        Func<DbContext> factory = () =>
        {
            var db = new Database(options);
            sessions.Add(db);
            return db;
        };
        var operations = ReadOperations.All | ReadOperations.Snapshot;
        IEntityReadAdapter<CompositeRow> adapter = ProviderName switch
        {
            "Microsoft.EntityFrameworkCore.Sqlite" => SqliteEntityReads.Create<CompositeRow>(
                CompositeSchema,
                factory,
                operations
            ),
            "Microsoft.EntityFrameworkCore.SqlServer" =>
                SqlServer.SqlServerEntityReads.Create<CompositeRow>(
                    CompositeSchema,
                    factory,
                    operations
                ),
            "MySql.EntityFrameworkCore" => MySql.MySqlEntityReads.Create<CompositeRow>(
                CompositeSchema,
                factory,
                operations
            ),
            _ => throw new NotSupportedException("Unknown composite provider."),
        };
        sessions.Clear();
        return new(
            CompositeSchema,
            new(
                CompositeSchema,
                CompilerTests.Registry,
                CompilerTests.Reference("TenantId", "tenant")
            ),
            Store,
            adapter,
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 1),
            new(
                new DataProtection.DataProtectionContinuationTokenProtector(
                    new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()
                ),
                () => "subject",
                "composite-dataset"
            ),
            export: export
        );
    }

    [Fact]
    public async Task Composite_provider_detail_list_cursor_and_export_use_the_full_hidden_tuple()
    {
        await SeedComposite();
        var sessions = new List<Database>();
        var commands = new SeekCommands();
        var reads = CompositeReads(sessions, commands);
        SortField[] sort = [new("Rank"), new("LocalAlias", true), new("LocalAlias", true)];
        string[] expected = ["g1l2", "g2l2", "g1l1", "g2l1"];
        var offset = new List<string?>();
        for (var start = 0; start < 4; start += 2)
        {
            var page = await reads.ListAsync(new(Sort: sort, Offset: start, Limit: 2));
            Assert.Equal(4, page.Total);
            offset.AddRange(page.Records.Select(row => row.GetProperty("Label").GetString()));
        }
        Assert.Equal(expected, offset);
        var cursor = new List<string?>();
        string? token = null;
        do
        {
            var page = await reads.ListCursorAsync(
                new(
                    Sort: sort,
                    Limit: 1,
                    ContinuationToken: token,
                    IncludeTotal: false,
                    IncludeHasMore: true
                )
            );
            Assert.Null(page.Total);
            cursor.AddRange(page.Records.Select(row => row.GetProperty("Label").GetString()));
            Assert.All(page.Records, row => Assert.Single(row.EnumerateObject()));
            token = page.NextContinuationToken;
            Assert.Equal(token != null, page.HasMore);
        } while (token != null);
        Assert.Equal(expected, cursor);
        var exported = new List<string?>();
        await foreach (var batch in reads.ExportAsync(new(Sort: sort)))
            exported.AddRange(batch.Records.Select(row => row.GetProperty("Label").GetString()));
        Assert.Equal(expected, exported);
        Assert.Equal(
            "g2l1",
            (
                await reads.DetailAsync(
                    new(JsonSerializer.SerializeToElement(new { GroupId = 2, LocalId = 1 }))
                )
            )
                .Record!.Value.GetProperty("Label")
                .GetString()
        );
        Assert.Null(
            (
                await reads.DetailAsync(
                    new(JsonSerializer.SerializeToElement(new { GroupId = 3, LocalId = 1 }))
                )
            ).Record
        );
        Assert.Null(
            (
                await reads.DetailAsync(
                    new(JsonSerializer.SerializeToElement(new { GroupId = 99, LocalId = 1 }))
                )
            ).Record
        );
        Assert.Equal(0, commands.Entities);
        Assert.All(
            commands.Columns,
            columns =>
            {
                Assert.DoesNotContain("Secret", columns);
                Assert.DoesNotContain("TenantId", columns);
                Assert.DoesNotContain("Denied", columns);
            }
        );
        Assert.Contains(
            commands.Columns,
            columns => columns.Contains("GroupId") && columns.Contains("LocalId")
        );
        Assert.All(sessions, db => Assert.True(db.Disposed));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Composite_export_modes_caps_deadlines_and_policy_abort(
        bool snapshot,
        bool abort
    )
    {
        await SeedComposite();
        await PrepareSnapshot();
        var sessions = new List<Database>();
        var commands = new SeekCommands();
        var clock = new ExportLifecycleTests.Clock();
        var configuration = new ExportReadConfiguration(
            abort
                ? ExportPolicyConsistency.AbortOnPolicyChange
                : ExportPolicyConsistency.CapturedPolicy,
            snapshot ? ExportDataConsistency.Snapshot : ExportDataConsistency.BestEffort,
            maxRows: 3,
            maxDuration: TimeSpan.FromSeconds(5),
            timeProvider: clock
        );
        var reads = CompositeReads(sessions, commands, configuration);
        SortField[] sort = [new("Rank"), new("LocalAlias", true)];
        await using (var iterator = reads.ExportAsync(new(Sort: sort)).GetAsyncEnumerator())
        {
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal(
                "g1l2",
                Assert.Single(iterator.Current.Records).GetProperty("Label").GetString()
            );
            await using (var mutation = new Database(Options))
            {
                var row = await mutation
                    .Set<CompositeRow>()
                    .SingleAsync(row => row.GroupId == 2 && row.LocalId == 2);
                row.Label = "changed";
                await mutation.SaveChangesAsync();
            }
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal(
                snapshot ? "g2l2" : "changed",
                Assert.Single(iterator.Current.Records).GetProperty("Label").GetString()
            );
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal(
                "g1l1",
                Assert.Single(iterator.Current.Records).GetProperty("Label").GetString()
            );
            Assert.False(await iterator.MoveNextAsync());
        }
        Assert.Equal(3, commands.Sql.Count);
        Assert.Equal(0, commands.Entities);
        Assert.All(sessions, db => Assert.True(db.Disposed));
        await using (var iterator = reads.ExportAsync(new(Sort: sort)).GetAsyncEnumerator())
        {
            Assert.True(await iterator.MoveNextAsync());
            clock.Advance(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<ExportDurationExceededException>(() =>
                iterator.MoveNextAsync().AsTask()
            );
        }
        await using (var iterator = reads.ExportAsync(new(Sort: sort)).GetAsyncEnumerator())
        {
            Assert.True(await iterator.MoveNextAsync());
            await Store.PublishAsync(admin, "scope", CompositeSchema, CompilerTests.Registry, 1, 1);
            if (abort)
                await Assert.ThrowsAsync<ExportPolicyChangedException>(() =>
                    iterator.MoveNextAsync().AsTask()
                );
            else
                Assert.True(await iterator.MoveNextAsync());
        }
        Assert.All(sessions, db => Assert.True(db.Disposed));
        Assert.Equal(0, clock.Timers);
    }

    public class Boundary
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public string? Text { get; set; }
        public decimal? Amount { get; set; }
        public DateTime Time { get; set; }
        public int? Number { get; set; }
        public DateTime? OptionalTime { get; set; }
        public double Floating { get; set; }
        public int Converted { get; set; }
        public string WidePayload { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public int Computed => Id + 1;
    }

    sealed class PolicySource(CapturedPolicy policy) : IActivePolicySource
    {
        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(policy);
        }
    }

    readonly string policyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
    readonly string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
    protected virtual DbContextOptions<Database> Options =>
        new DbContextOptionsBuilder<Database>()
            .UseSqlite("Data Source=" + path + ";Cache=Private")
            .Options;
    SqlitePolicyStore Store => new("Data Source=" + policyPath);
    readonly AdministrationContext admin = new("admin", "scope", true);

    protected virtual string ProviderName => "Microsoft.EntityFrameworkCore.Sqlite";

    [Fact]
    public async Task Provider_profiles_validate_native_output_count_and_reject_advertised_converter_seek()
    {
        var schema = ProviderCapabilityTests.Schema(convertedSort: true);
        var contexts = new List<Database>();
        var adapter = ProviderCapabilityTests.Register(
            schema,
            () =>
            {
                var context = new Database(Options);
                contexts.Add(context);
                return context;
            },
            ProviderName,
            ReadOperations.Count
        );
        var profile = Assert.IsType<ReadProviderCapabilities>(adapter.Capabilities);
        Assert.Equal(
            CapabilityStatus.Supported,
            profile.CheckField("Converted", ProviderFieldUse.Output).Status
        );
        Assert.Equal(
            CapabilityStatus.Unsupported,
            profile.CheckField("Converted", ProviderFieldUse.SeekOrder).Status
        );
        Assert.Equal(CapabilityStatus.Unverified, profile.ComposedQuery.Status);
        var failure = Assert.Throws<ProviderUnsupportedException>(() =>
            ProviderCapabilityTests.Register(
                schema,
                () => new Database(Options),
                ProviderName,
                ReadOperations.Cursor
            )
        );
        Assert.Contains(
            failure.Diagnostics,
            diagnostic => diagnostic.Path == "/operations/seek/fields/Converted"
        );
        await using (var setup = new Database(Options))
        {
            setup.Add(
                new Boundary
                {
                    Id = 501,
                    TenantId = 1,
                    Text = "capability",
                    Converted = 7,
                }
            );
            setup.Add(
                new Boundary
                {
                    Id = 502,
                    TenantId = 2,
                    Text = "hidden",
                    Converted = 7,
                }
            );
            await setup.SaveChangesAsync();
        }
        var registry = new ContextRegistry(
            new Dictionary<string, Type> { ["tenant"] = typeof(int) }
        );
        var policy = ProviderCapabilityTests.Policy(CompilerTests.Leaf("Converted", "eq", 7));
        var repository = new AuthorizedReadRepository<Boundary>(
            schema,
            new(
                schema,
                registry,
                CompilerTests.Reference("TenantId", "tenant"),
                capabilities: [profile]
            ),
            new PolicySource(new("scope", schema.Resource, "1", 1, PolicyJson.Serialize(policy))),
            adapter,
            () => CompilerTests.Context(),
            "scope"
        );
        Assert.Equal(1, (await repository.CountAsync(new())).Total);
        Assert.True(contexts[^1].Disposed);
    }

    AuthorizedReadRepository<CompilerTests.Employee> Reads() =>
        new(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            Store,
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<CompilerTests.Employee>(
                () => new Database(Options),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 2)
        );

    public async Task InitializeAsync()
    {
        await using var db = new Database(Options);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(
            new CompilerTests.Employee
            {
                Id = 1,
                TenantId = 1,
                DepartmentId = 10,
                Name = "Same",
                Salary = 100,
            },
            new CompilerTests.Employee
            {
                Id = 2,
                TenantId = 1,
                DepartmentId = 10,
                Name = "Same",
                Salary = 200,
            },
            new CompilerTests.Employee
            {
                Id = 3,
                TenantId = 1,
                DepartmentId = 10,
                Name = "Same",
                Salary = 300,
            },
            new CompilerTests.Employee
            {
                Id = 4,
                TenantId = 2,
                DepartmentId = 10,
                Name = "Secret",
                Salary = 400,
            }
        );
        await db.SaveChangesAsync();
        await Store.InitializeAsync();
        await Store.SaveDraftAsync(
            admin,
            "scope",
            "Employee",
            PolicyJson.Serialize(
                CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
            ),
            0
        );
        await Store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            1,
            0
        );
    }

    public async Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            await using var db = new Database(Options);
            await db.Database.EnsureDeletedAsync();
        }
        finally
        {
            File.Delete(path);
            File.Delete(policyPath);
        }
    }

    [Fact]
    public async Task Relational_list_detail_count_and_output_share_authorization()
    {
        var reads = Reads();
        var page = await reads.ListAsync(new(Sort: [new("Name")], Limit: 2));
        Assert.Equal(3, page.Total);
        Assert.Equal(new[] { 1, 2 }, page.Records.Select(r => r.GetProperty("Id").GetInt32()));
        Assert.All(
            page.Records,
            r =>
            {
                Assert.False(r.TryGetProperty("Salary", out _));
                Assert.False(r.TryGetProperty("TenantId", out _));
                Assert.Equal(2, r.EnumerateObject().Count());
            }
        );
        var next = await reads.ListAsync(new(Sort: [new("Name")], Offset: 2, Limit: 2));
        Assert.Equal(3, next.Records.Single().GetProperty("Id").GetInt32());
        Assert.Equal(3, (await reads.CountAsync(new())).Total);
        Assert.Null(
            (await reads.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 4 })))).Record
        );
        Assert.Null(
            (
                await reads.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 99 })))
            ).Record
        );
        Assert.NotNull(
            (await reads.DetailAsync(new(JsonSerializer.SerializeToElement(new { Id = 1 })))).Record
        );
        var empty = await reads.ListAsync(new(Selection: [], Limit: 2));
        Assert.All(empty.Records, r => Assert.Equal("{}", r.GetRawText()));
    }

    [Fact]
    public async Task Scalar_projection_excludes_hidden_payloads_and_entities_and_preserves_aliases_converters_and_empty_rows()
    {
        await using (var db = new Database(Options))
        {
            db.AddRange(
                new Boundary
                {
                    Id = 1,
                    TenantId = 1,
                    Text = "Stored",
                    Converted = 7,
                    WidePayload = new string('x', 65_536),
                },
                new Boundary
                {
                    Id = 2,
                    TenantId = 1,
                    Text = null,
                    Converted = 7,
                },
                new Boundary
                {
                    Id = 3,
                    TenantId = 2,
                    Text = "Hidden",
                    Converted = 9,
                }
            );
            await db.SaveChangesAsync();
        }
        var schema = new ResourceSchema<Boundary>(
            "Projection",
            "1",
            [
                ResourceSchema<Boundary>.Field("key", row => row.Id, [FieldOperation.Select]),
                ResourceSchema<Boundary>.Field("tenant", row => row.TenantId, []),
                ResourceSchema<Boundary>.Field(
                    "label",
                    row => row.Text,
                    [FieldOperation.Select, FieldOperation.Sort]
                ),
                ResourceSchema<Boundary>.Field("alias", row => row.Text, [FieldOperation.Select]),
                ResourceSchema<Boundary>.Field(
                    "converted",
                    row => row.Converted,
                    [FieldOperation.Select]
                ),
                ResourceSchema<Boundary>.Field(
                    "unmapped",
                    row => row.Computed,
                    [FieldOperation.Select]
                ),
            ],
            ["key"]
        );
        var policy = new PolicyDefinition(
            "Projection",
            "1",
            [new("allow", "Projection", "Read", Effect.Allow, new([], []), null)],
            [
                new(
                    "fields",
                    "Projection",
                    "Read",
                    Effect.Allow,
                    new([], []),
                    new()
                    {
                        [FieldOperation.Select] =
                        [
                            "key",
                            "label",
                            "alias",
                            "converted",
                            "unmapped",
                        ],
                        [FieldOperation.Sort] = ["label"],
                    }
                ),
            ]
        );
        var commands = new SeekCommands();
        var options = new DbContextOptionsBuilder<Database>(Options)
            .AddInterceptors(commands, MaterializationCounter.Instance)
            .Options;
        var compiler = new PolicyCompiler<Boundary>(
            schema,
            CompilerTests.Registry,
            CompilerTests.Reference("tenant", "tenant")
        );
        var source = new PolicySource(
            new("scope", "Projection", "1", 1, PolicyJson.Serialize(policy))
        );
        var adapter = new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<Boundary>(
            () => new Database(options),
            ProviderName
        );
        foreach (
            var mode in new[]
            {
                ReadProjectionMode.Required,
                ReadProjectionMode.AllowEntityMaterialization,
            }
        )
        {
            var repo = new AuthorizedReadRepository<Boundary>(
                schema,
                compiler,
                source,
                adapter,
                () => CompilerTests.Context(),
                "scope",
                projectionMode: mode
            );
            commands.Columns.Clear();
            var selected = await repo.ListAsync(
                new(
                    Selection: ["alias", "label", "converted"],
                    Sort: [new("label")],
                    IncludeTotal: false
                )
            );
            Assert.Equal(2, selected.Records.Count);
            Assert.All(selected.Records, row => Assert.Equal(3, row.EnumerateObject().Count()));
            Assert.Equal(
                selected.Records[0].GetProperty("alias").GetRawText(),
                selected.Records[0].GetProperty("label").GetRawText()
            );
            Assert.All(
                selected.Records,
                row => Assert.Equal(7, row.GetProperty("converted").GetInt32())
            );
            Assert.Equal(2, Assert.Single(commands.Columns).Length);
            Assert.DoesNotContain("WidePayload", commands.Columns[0]);
            Assert.DoesNotContain("TenantId", commands.Columns[0]);
            Assert.DoesNotContain("Id", commands.Columns[0]);
            commands.Columns.Clear();
            var empty = await repo.ListAsync(new(Selection: [], IncludeTotal: false));
            Assert.Equal(2, empty.Records.Count);
            Assert.All(empty.Records, row => Assert.Equal("{}", row.GetRawText()));
            Assert.Single(Assert.Single(commands.Columns));
            Assert.DoesNotContain("Id", commands.Columns[0]);
            Assert.Equal(
                "{}",
                (
                    await repo.DetailAsync(
                        new(JsonSerializer.SerializeToElement(new { key = 1 }), Selection: [])
                    )
                ).Record!.Value.GetRawText()
            );
            Assert.Null(
                (
                    await repo.DetailAsync(
                        new(JsonSerializer.SerializeToElement(new { key = 3 }), Selection: [])
                    )
                ).Record
            );
            Assert.Null(
                (
                    await repo.DetailAsync(
                        new(JsonSerializer.SerializeToElement(new { key = 99 }), Selection: [])
                    )
                ).Record
            );
            await using (var session = await adapter.OpenAsync())
            {
                var projected = Assert.IsAssignableFrom<IEntityProjectedReadSession<Boundary>>(
                    session
                );
                foreach (
                    var bindings in new IEnumerable<EntityOutputBinding>[]
                    {
                        [],
                        [new("label", typeof(Boundary).GetProperty("Text")!)],
                    }
                )
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        projected.SingleProjectedAsync(
                            new EntityReadPlan<Boundary>(row => row.TenantId == 1, [], bindings),
                            CancellationToken.None
                        )
                    );
            }
            commands.Sql.Clear();
            Assert.Equal(new ReadCount(1, 2), await repo.CountAsync(new()));
            Assert.Single(commands.Sql);
            Assert.True(
                IsCountQuery(commands.Sql[0]),
                "Expected the provider's exact count aggregate."
            );
            var executed = commands.Sql.Count;
            await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
                repo.ListAsync(new(Selection: ["unmapped"], IncludeTotal: false))
            );
            Assert.Equal(executed, commands.Sql.Count);
            Assert.Equal(0, commands.Entities);
            commands.Columns.Clear();
            var exported = new List<JsonElement>();
            await foreach (
                var batch in repo.ExportAsync(new(Selection: ["alias"], Sort: [new("label")]))
            )
                exported.AddRange(batch.Records);
            Assert.Equal(2, exported.Count);
            Assert.All(exported, row => Assert.Single(row.EnumerateObject()));
            Assert.All(
                commands.Columns,
                columns =>
                {
                    Assert.Equal(new[] { "Text", "Id" }, columns);
                    Assert.DoesNotContain("WidePayload", columns);
                }
            );
            Assert.Equal(0, commands.Entities);
        }
        await using (var db = new Database(options))
        {
            var model = db.Model.FindEntityType(typeof(Boundary))!;
            var sql = db.GetService<ISqlGenerationHelper>();
            var table = sql.DelimitIdentifier(model.GetTableName()!, model.GetSchema());
            var column = sql.DelimitIdentifier(
                model.FindProperty(nameof(Boundary.Converted))!.GetColumnName()!
            );
            var key = sql.DelimitIdentifier(nameof(Boundary.Id));
            var statement = $"UPDATE {table} SET {column} = {{0}} WHERE {key} = {{1}}";
            await db.Database.ExecuteSqlRawAsync(statement, "not-an-integer", 1);
        }
        foreach (
            var mode in new[]
            {
                ReadProjectionMode.Required,
                ReadProjectionMode.AllowEntityMaterialization,
            }
        )
        {
            var repo = new AuthorizedReadRepository<Boundary>(
                schema,
                compiler,
                source,
                adapter,
                () => CompilerTests.Context(),
                "scope",
                projectionMode: mode
            );
            commands.Sql.Clear();
            commands.Columns.Clear();
            await Assert.ThrowsAsync<FormatException>(() =>
                repo.ListAsync(new(Selection: ["converted"], IncludeTotal: false))
            );
            Assert.Single(commands.Sql);
            Assert.Equal(new[] { "Converted" }, Assert.Single(commands.Columns));
            Assert.Equal(0, commands.Entities);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Optional_metadata_uses_bounded_authorized_queries(bool total, bool hasMore)
    {
        var commands = new SeekCommands();
        var options = new DbContextOptionsBuilder<Database>(Options)
            .AddInterceptors(commands, MaterializationCounter.Instance)
            .Options;
        var configuration = new CursorReadConfiguration(
            new Kkts.DataAccess.DataProtection.DataProtectionContinuationTokenProtector(
                new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()
            ),
            () => "subject",
            "metadata-fixture"
        );
        var core = new AuthorizedReadRepository<CompilerTests.Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            Store,
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<CompilerTests.Employee>(
                () => new Database(options),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 2),
            configuration
        );
        var repositories = new List<IAuthorizedReadRepository<CompilerTests.Employee>> { core };
        if (ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            repositories.Add(
                new AuthorizedReads<CompilerTests.Employee>(
                    CompilerTests.Schema,
                    CompilerTests.Compiler(),
                    Store,
                    () => new Database(options),
                    () => CompilerTests.Context(),
                    "scope",
                    new(2, 100, 2),
                    configuration
                )
            );
        foreach (var reads in repositories)
        {
            commands.Sql.Clear();
            commands.Parameters.Clear();
            var page = await reads.ListAsync(
                new(
                    Selection: ["Id"],
                    Sort: [new("Name")],
                    Limit: 2,
                    IncludeTotal: total,
                    IncludeHasMore: hasMore
                )
            );
            Assert.Equal(total ? 3L : (long?)null, page.Total);
            Assert.Equal(hasMore ? true : (bool?)null, page.HasMore);
            Assert.Equal(
                new[] { 1, 2 },
                page.Records.Select(row => row.GetProperty("Id").GetInt32())
            );
            AssertQueryCounts(total, commands);
            Assert.Contains(hasMore ? 3 : 2, commands.Parameters[^1]);

            commands.Sql.Clear();
            var final = await reads.ListAsync(
                new(
                    Selection: [],
                    Offset: 2,
                    Limit: 2,
                    IncludeTotal: total,
                    IncludeHasMore: hasMore
                )
            );
            Assert.Equal("{}", Assert.Single(final.Records).GetRawText());
            Assert.Equal(hasMore ? false : (bool?)null, final.HasMore);
            AssertQueryCounts(total, commands);

            commands.Sql.Clear();
            var cursor = await reads.ListCursorAsync(
                new(Selection: ["Id"], Limit: 2, IncludeTotal: total, IncludeHasMore: hasMore)
            );
            Assert.Equal(total ? 3L : (long?)null, cursor.Total);
            Assert.Equal(hasMore ? true : (bool?)null, cursor.HasMore);
            Assert.NotNull(cursor.NextContinuationToken);
            Assert.Equal(
                new[] { 1, 2 },
                cursor.Records.Select(row => row.GetProperty("Id").GetInt32())
            );
            AssertQueryCounts(total, commands);
            Assert.Contains(3, commands.Parameters[^1]);

            commands.Sql.Clear();
            var resumed = await reads.ListCursorAsync(
                new(
                    Selection: ["Id"],
                    Limit: 2,
                    ContinuationToken: cursor.NextContinuationToken,
                    IncludeTotal: !total,
                    IncludeHasMore: !hasMore
                )
            );
            Assert.Equal(!total ? 3L : (long?)null, resumed.Total);
            Assert.Equal(!hasMore ? false : (bool?)null, resumed.HasMore);
            Assert.Equal(3, Assert.Single(resumed.Records).GetProperty("Id").GetInt32());
            Assert.Null(resumed.NextContinuationToken);
            AssertQueryCounts(!total, commands);

            commands.Sql.Clear();
            var count = await reads.CountAsync(new());
            Assert.Equal(new ReadCount(page.PolicyVersion, 3), count);
            Assert.Single(commands.Sql);
            Assert.True(
                IsCountQuery(commands.Sql[0]),
                "Expected the provider's exact count aggregate."
            );
        }
    }

    bool IsCountQuery(string sql) =>
        sql.Contains(
            ProviderName == "Microsoft.EntityFrameworkCore.SqlServer" ? "COUNT_BIG(" : "COUNT(",
            StringComparison.OrdinalIgnoreCase
        );

    void AssertQueryCounts(bool total, SeekCommands commands)
    {
        Assert.Equal(total ? 2 : 1, commands.Sql.Count);
        Assert.Equal(total ? 1 : 0, commands.Sql.Count(IsCountQuery));
        Assert.All(
            commands.Sql,
            sql =>
            {
                Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("TenantId", sql, StringComparison.Ordinal);
            }
        );
        var rowQuery = commands.Sql[^1];
        Assert.Contains("ORDER BY", rowQuery, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            rowQuery.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
                || rowQuery.Contains("FETCH", StringComparison.OrdinalIgnoreCase)
                || rowQuery.Contains("TOP(", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task Detail_keys_filters_and_empty_selection_preserve_relational_lookup()
    {
        var reads = Reads();
        foreach (
            var json in new[] { "{}", """{"key":null}""", """{"key":"1"}""", """{"key":1.5}""" }
        )
        {
            var request = JsonSerializer.Deserialize<DetailReadRequest>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )!;
            var error = await Assert.ThrowsAsync<InvalidRequestException>(() =>
                reads.DetailAsync(request)
            );
            Assert.Equal("/key", Assert.Single(error.Diagnostics).Path);
        }
        var key = JsonSerializer.SerializeToElement(new { Id = 1 });
        var present = await reads.DetailAsync(
            new(
                key,
                Selection: ["Id", "Name"],
                Filter: CompilerTests.Leaf("Status", "eq", "Active")
            )
        );
        Assert.Equal(
            new[] { "Id", "Name" },
            present.Record!.Value.EnumerateObject().Select(p => p.Name)
        );
        Assert.Null(
            (
                await reads.DetailAsync(
                    new(key, Filter: CompilerTests.Leaf("Status", "eq", "Inactive"))
                )
            ).Record
        );
        Assert.Equal(
            "{}",
            (await reads.DetailAsync(new(key, Selection: []))).Record!.Value.GetRawText()
        );
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            reads.DetailAsync(new(key, Filter: CompilerTests.Leaf("Id", "eq", 1)))
        );
    }

    [Fact]
    public async Task Forbidden_queries_and_unsupported_operations_fail_before_entity_database_access()
    {
        var reads = new AuthorizedReadRepository<CompilerTests.Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            Store,
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<CompilerTests.Employee>(
                () => throw new Exception("Provider executed"),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope"
        );
        foreach (
            var request in new[]
            {
                new ListReadRequest(Selection: ["Salary"]),
                new ListReadRequest(Filter: CompilerTests.Leaf("Salary", "eq", 100)),
                new ListReadRequest(Sort: [new("Id")]),
            }
        )
            await Assert.ThrowsAsync<AuthorizationException>(() => reads.ListAsync(request));
        var unknown = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            reads.ListAsync(new(Selection: ["Manager.Id"]))
        );
        Assert.Equal("/selection/0", Assert.Single(unknown.Diagnostics).Path);
        var pagination = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            reads.ListAsync(new(Limit: 0))
        );
        Assert.Equal("/limit", Assert.Single(pagination.Diagnostics).Path);
    }

    [Fact]
    public async Task Published_field_revocation_does_not_reuse_warmed_output_permissions()
    {
        var reads = Reads();
        var warm = await reads.ListAsync(new(Limit: 1));
        Assert.Equal(2, warm.Records[0].EnumerateObject().Count());
        var policy = CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow));
        policy = policy with
        {
            FieldEntries =
            [
                policy.FieldEntries[0] with
                {
                    Fields = new() { { FieldOperation.Select, ["Name"] } },
                },
            ],
        };
        await Store.SaveDraftAsync(admin, "scope", "Employee", PolicyJson.Serialize(policy), 1);
        await Store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            2,
            1
        );
        var page = await reads.ListAsync(new(Limit: 1));
        Assert.NotEqual(warm.PolicyVersion, page.PolicyVersion);
        Assert.Equal("{\"Name\":\"Same\"}", page.Records[0].GetRawText());
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            reads.ListAsync(new(Selection: ["Id"], Limit: 1))
        );
        var detail = await reads.DetailAsync(
            new(JsonSerializer.SerializeToElement(new { Id = 1 }))
        );
        Assert.Equal(page.PolicyVersion, detail.PolicyVersion);
        Assert.Equal("{\"Name\":\"Same\"}", detail.Record!.Value.GetRawText());
        var batches = 0;
        await foreach (var batch in reads.ExportAsync(new()))
        {
            batches++;
            Assert.Equal(page.PolicyVersion, batch.PolicyVersion);
            Assert.All(batch.Records, row => Assert.Equal("{\"Name\":\"Same\"}", row.GetRawText()));
        }
        Assert.Equal(2, batches);
    }

    [Fact]
    public async Task Export_retains_captured_policy_and_supports_cancellation()
    {
        var reads = Reads();
        await using var export = reads.ExportAsync(new()).GetAsyncEnumerator();
        Assert.True(await export.MoveNextAsync());
        var version = export.Current.PolicyVersion;
        Assert.Equal(2, export.Current.Records.Count);
        await Store.SaveDraftAsync(
            admin,
            "scope",
            "Employee",
            PolicyJson.Serialize(CompilerTests.Definition()),
            1
        );
        await Store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            2,
            1
        );
        Assert.True(await export.MoveNextAsync());
        Assert.Equal(version, export.Current.PolicyVersion);
        Assert.Single(export.Current.Records);
        Assert.False(await export.MoveNextAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => reads.CountAsync(new()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reads.ListAsync(new(Limit: 1), cancellation.Token)
        );
    }

    [Fact]
    public async Task Provider_null_deny_collation_precision_and_ordering_boundaries()
    {
        var schema = new ResourceSchema<Boundary>(
            "Boundary",
            "1",
            [
                ResourceSchema<Boundary>.Field("Id", e => e.Id, [FieldOperation.Select]),
                ResourceSchema<Boundary>.Field("TenantId", e => e.TenantId, []),
                ResourceSchema<Boundary>.Field(
                    "Text",
                    e => e.Text,
                    [FieldOperation.Select, FieldOperation.Filter]
                ),
                ResourceSchema<Boundary>.Field(
                    "Amount",
                    e => e.Amount,
                    [FieldOperation.Select, FieldOperation.Sort]
                ),
                ResourceSchema<Boundary>.Field(
                    "Time",
                    e => e.Time,
                    [FieldOperation.Select, FieldOperation.Sort]
                ),
                ResourceSchema<Boundary>.Field("Computed", e => e.Computed, []),
            ],
            ["Id"]
        );
        var policy = new PolicyDefinition(
            "Boundary",
            "1",
            [
                new("allow", "Boundary", "Read", Effect.Allow, new([], []), null),
                new(
                    "deny",
                    "Boundary",
                    "Read",
                    Effect.Deny,
                    new([], []),
                    CompilerTests.Leaf("Id", "eq", 3)
                ),
            ],
            [
                new(
                    "fields",
                    "Boundary",
                    "Read",
                    Effect.Allow,
                    new([], []),
                    new()
                    {
                        [FieldOperation.Select] = ["Id", "Text", "Amount", "Time"],
                        [FieldOperation.Filter] = ["Text"],
                        [FieldOperation.Sort] = ["Amount", "Time"],
                    }
                ),
            ]
        );
        var captured = new CapturedPolicy(
            "scope",
            "Boundary",
            "1",
            1,
            PolicyJson.Serialize(policy)
        );
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        await using (var db = new Database(Options))
        {
            db.AddRange(
                new Boundary
                {
                    Id = 1,
                    TenantId = 1,
                    Text = null,
                    Amount = null,
                    Time = time,
                },
                new Boundary
                {
                    Id = 2,
                    TenantId = 1,
                    Text = "Mixed",
                    Amount = 123.45m,
                    Time = time.AddDays(1),
                },
                new Boundary
                {
                    Id = 3,
                    TenantId = 1,
                    Text = "Mixed",
                    Amount = 2m,
                    Time = time,
                },
                new Boundary
                {
                    Id = 4,
                    TenantId = 2,
                    Text = "Mixed",
                    Amount = 3m,
                    Time = time,
                }
            );
            await db.SaveChangesAsync();
        }
        var repo = new AuthorizedReadRepository<Boundary>(
            schema,
            new(schema, CompilerTests.Registry, CompilerTests.Reference("TenantId", "tenant")),
            new PolicySource(captured),
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<Boundary>(
                () => new Database(Options),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope"
        );
        Assert.Equal(
            1,
            (await repo.CountAsync(new(Filter: CompilerTests.Leaf("Text", "eq", null)))).Total
        );
        var page = await repo.ListAsync(new(Sort: [new("Time")]));
        Assert.Equal(new[] { 1, 2 }, page.Records.Select(r => r.GetProperty("Id").GetInt32()));
        Assert.Equal(123.45m, page.Records[1].GetProperty("Amount").GetDecimal());
        var caseCount = await repo.CountAsync(
            new(Filter: CompilerTests.Leaf("Text", "eq", "mixed"))
        );
        Assert.Equal(
            ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" ? 0 : 1,
            caseCount.Total
        );
        Assert.Equal(2, (await repo.ListAsync(new(Sort: [new("Amount")]))).Total);
        policy = policy with
        {
            Rules =
            [
                new(
                    "bad",
                    "Boundary",
                    "Read",
                    Effect.Allow,
                    new([], []),
                    CompilerTests.Leaf("Computed", "eq", 2)
                ),
            ],
        };
        var failed = new AuthorizedReadRepository<Boundary>(
            schema,
            new(schema, CompilerTests.Registry, CompilerTests.Reference("TenantId", "tenant")),
            new PolicySource(captured with { Version = 2, Payload = PolicyJson.Serialize(policy) }),
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<Boundary>(
                () => new Database(Options),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope"
        );
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() => failed.ListAsync(new()));
    }

    [Fact]
    public async Task Structured_null_filters_and_alias_ordering_preserve_relational_results()
    {
        var schema = new ResourceSchema<Boundary>(
            "Boundary",
            "1",
            [
                ResourceSchema<Boundary>.Field("key", e => e.Id, [FieldOperation.Select]),
                ResourceSchema<Boundary>.Field("tenant", e => e.TenantId, []),
                ResourceSchema<Boundary>.Field(
                    "label",
                    e => e.Text,
                    [FieldOperation.Select, FieldOperation.Filter, FieldOperation.Sort]
                ),
                ResourceSchema<Boundary>.Field(
                    "amount",
                    e => e.Amount,
                    [FieldOperation.Select, FieldOperation.Filter]
                ),
                ResourceSchema<Boundary>.Field(
                    "time",
                    e => e.Time,
                    [FieldOperation.Filter, FieldOperation.Sort]
                ),
            ],
            ["key"]
        );
        var definition = new PolicyDefinition(
            "Boundary",
            "1",
            [
                new("allow", "Boundary", "Read", Effect.Allow, new([], []), null),
                new(
                    "deny",
                    "Boundary",
                    "Read",
                    Effect.Deny,
                    new([], []),
                    CompilerTests.Leaf("key", "eq", 4)
                ),
            ],
            [
                new(
                    "fields",
                    "Boundary",
                    "Read",
                    Effect.Allow,
                    new([], []),
                    new()
                    {
                        [FieldOperation.Select] = ["key", "label", "amount"],
                        [FieldOperation.Filter] = ["label", "amount", "time"],
                        [FieldOperation.Sort] = ["label", "time"],
                    }
                ),
            ]
        );
        var time = new DateTime(2026, 1, 1);
        var rows = new[]
        {
            new Boundary
            {
                Id = 1,
                TenantId = 1,
                Text = null,
                Amount = null,
                Time = time,
            },
            new Boundary
            {
                Id = 2,
                TenantId = 1,
                Text = "same",
                Amount = 2,
                Time = time,
            },
            new Boundary
            {
                Id = 3,
                TenantId = 1,
                Text = "same",
                Amount = 3,
                Time = time.AddDays(1),
            },
            new Boundary
            {
                Id = 4,
                TenantId = 1,
                Text = "denied",
                Amount = 4,
                Time = time,
            },
            new Boundary
            {
                Id = 5,
                TenantId = 2,
                Text = "same",
                Amount = 5,
                Time = time,
            },
        };
        await using (var db = new Database(Options))
        {
            db.AddRange(rows);
            await db.SaveChangesAsync();
        }
        var compiler = new PolicyCompiler<Boundary>(
            schema,
            CompilerTests.Registry,
            CompilerTests.Reference("tenant", "tenant")
        );
        var reads = new AuthorizedReadRepository<Boundary>(
            schema,
            compiler,
            new PolicySource(new("scope", "Boundary", "1", 1, PolicyJson.Serialize(definition))),
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<Boundary>(
                () => new Database(Options),
                ProviderName
            ),
            () => CompilerTests.Context(),
            "scope",
            new(200, 1_000_000, 1)
        );
        var cases = new[]
        {
            (CompilerTests.Leaf("label", "eq", null), new[] { 1 }),
            (CompilerTests.Leaf("label", "ne", null), new[] { 2, 3 }),
            (CompilerTests.Leaf("amount", "eq", null), new[] { 1 }),
            (CompilerTests.Leaf("amount", "ne", null), new[] { 2, 3 }),
            (new Condition { Field = "label", Operator = "isNull" }, new[] { 1 }),
            (new Condition { Field = "label", Operator = "isNotNull" }, new[] { 2, 3 }),
            (new Condition { Field = "amount", Operator = "isNull" }, new[] { 1 }),
            (new Condition { Field = "amount", Operator = "isNotNull" }, new[] { 2, 3 }),
            (new Condition { Field = "time", Operator = "isNull" }, Array.Empty<int>()),
            (new Condition { Field = "time", Operator = "isNotNull" }, new[] { 1, 2, 3 }),
            (CompilerTests.Leaf("label", "in", new object?[] { null, "same" }), new[] { 1, 2, 3 }),
            (CompilerTests.Leaf("amount", "in", new object?[] { null, 2 }), new[] { 1, 2 }),
            (
                new Condition
                {
                    Not = CompilerTests.Leaf("amount", "in", new object?[] { null, 2 }),
                },
                new[] { 3 }
            ),
            (new Condition { Not = CompilerTests.Leaf("label", "contains", "sam") }, new[] { 1 }),
            (
                new Condition
                {
                    Not = new() { Not = CompilerTests.Leaf("label", "contains", "sam") },
                },
                new[] { 2, 3 }
            ),
            (
                new Condition
                {
                    Not = new()
                    {
                        Any =
                        [
                            CompilerTests.Leaf("label", "contains", "sam"),
                            new() { Not = CompilerTests.Leaf("amount", "eq", null) },
                        ],
                    },
                },
                new[] { 1 }
            ),
            (CompilerTests.Leaf("label", "ne", "same"), new[] { 1 }),
            (CompilerTests.Leaf("label", "startsWith", "sam"), new[] { 2, 3 }),
            (CompilerTests.Leaf("label", "endsWith", "ame"), new[] { 2, 3 }),
            (CompilerTests.Leaf("label", "contains", null), Array.Empty<int>()),
            (CompilerTests.Leaf("amount", "lt", null), Array.Empty<int>()),
            (CompilerTests.Leaf("amount", "le", null), Array.Empty<int>()),
            (CompilerTests.Leaf("amount", "gt", null), Array.Empty<int>()),
            (CompilerTests.Leaf("amount", "ge", null), Array.Empty<int>()),
            (CompilerTests.Leaf("amount", "ge", 2), new[] { 2, 3 }),
            (new Condition { Not = CompilerTests.Leaf("amount", "gt", null) }, new[] { 1, 2, 3 }),
        };
        foreach (var (filter, expected) in cases)
        {
            var predicate = compiler.Predicate(filter, CompilerTests.Context()).Compile();
            Assert.Equal(
                expected,
                rows.Where(row => row.TenantId == 1 && row.Id != 4)
                    .Where(predicate)
                    .Select(row => row.Id)
            );
            var request = new ListReadRequest(Selection: ["key"], Filter: filter);
            var page = await reads.ListAsync(request);
            Assert.Equal(expected, page.Records.Select(row => row.GetProperty("key").GetInt32()));
            Assert.Equal(expected.Length, (await reads.CountAsync(new(Filter: filter))).Total);
        }
        var sorted = await reads.ListAsync(
            new(Selection: ["amount"], Sort: [new("label", true), new("label")])
        );
        Assert.Equal(
            new decimal?[] { 2, 3, null },
            sorted.Records.Select(row =>
                row.GetProperty("amount").ValueKind == JsonValueKind.Null
                    ? (decimal?)null
                    : row.GetProperty("amount").GetDecimal()
            )
        );
        Assert.All(sorted.Records, row => Assert.Single(row.EnumerateObject()));
        var multiKey = await reads.ListAsync(
            new(Selection: ["key"], Sort: [new("label"), new("time", true)])
        );
        Assert.Equal(
            new[] { 1, 3, 2 },
            multiKey.Records.Select(row => row.GetProperty("key").GetInt32())
        );
        var reverse = await reads.ListAsync(
            new(Selection: ["key"], Sort: [new("label", true), new("time")])
        );
        Assert.Equal(
            new[] { 2, 3, 1 },
            reverse.Records.Select(row => row.GetProperty("key").GetInt32())
        );
        var exported = new List<decimal?>();
        await foreach (
            var batch in reads.ExportAsync(new(Selection: ["amount"], Sort: [new("label", true)]))
        )
        {
            Assert.All(batch.Records, row => Assert.Single(row.EnumerateObject()));
            exported.AddRange(
                batch.Records.Select(row =>
                    row.GetProperty("amount").ValueKind == JsonValueKind.Null
                        ? (decimal?)null
                        : row.GetProperty("amount").GetDecimal()
                )
            );
        }
        Assert.Equal(new decimal?[] { 2, 3, null }, exported);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            reads.ListAsync(new(Sort: [new("key")]))
        );
    }

    [Fact]
    public async Task Seek_traversal_matches_provider_order_with_nulls_ties_collation_and_precision()
    {
        var time = new DateTime(2026, 1, 1);
        var rows = new[]
        {
            new Boundary
            {
                Id = 1,
                TenantId = 1,
                Text = null,
                Amount = null,
                Number = null,
            },
            new Boundary
            {
                Id = 2,
                TenantId = 1,
                Text = "A",
                Amount = 1234567890123456.78m,
                Number = 2,
                OptionalTime = time,
            },
            new Boundary
            {
                Id = 3,
                TenantId = 1,
                Text = "a",
                Amount = 1234567890123456.79m,
                Number = 2,
                OptionalTime = time.AddTicks(10_000),
            },
            new Boundary
            {
                Id = 4,
                TenantId = 1,
                Text = "a ",
                Amount = -1.01m,
                Number = 1,
                OptionalTime = time,
            },
            new Boundary
            {
                Id = 5,
                TenantId = 1,
                Text = "b",
                Amount = 0m,
                Number = 1,
            },
            new Boundary
            {
                Id = 6,
                TenantId = 2,
                Text = "a",
                Amount = 0m,
                Number = 1,
            },
            new Boundary
            {
                Id = 7,
                TenantId = 1,
                Text = "denied",
                Amount = 0m,
                Number = 1,
            },
        };
        foreach (var row in rows)
            row.Time = time;
        await using (var db = new Database(Options))
        {
            db.AddRange(rows);
            await db.SaveChangesAsync();
        }
        var commands = new SeekCommands();
        var options = new DbContextOptionsBuilder<Database>(Options)
            .AddInterceptors(commands, MaterializationCounter.Instance)
            .Options;
        var adapter = new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<Boundary>(
            () => new Database(options),
            ProviderName
        );
        foreach (
            var sorts in new[]
            {
                new[] { new EntitySort(typeof(Boundary).GetProperty("Id")!, false) },
                new[]
                {
                    new EntitySort(typeof(Boundary).GetProperty("Text")!, false),
                    new EntitySort(typeof(Boundary).GetProperty("Id")!, false),
                },
                new[]
                {
                    new EntitySort(typeof(Boundary).GetProperty("Text")!, true),
                    new EntitySort(typeof(Boundary).GetProperty("Text")!, false),
                    new EntitySort(typeof(Boundary).GetProperty("Id")!, false),
                },
                new[]
                {
                    new EntitySort(typeof(Boundary).GetProperty("Amount")!, false),
                    new EntitySort(typeof(Boundary).GetProperty("Id")!, true),
                },
                new[]
                {
                    new EntitySort(typeof(Boundary).GetProperty("Number")!, true),
                    new EntitySort(typeof(Boundary).GetProperty("OptionalTime")!, false),
                    new EntitySort(typeof(Boundary).GetProperty("Id")!, false),
                },
            }
        )
        {
            var read = new EntityReadPlan<Boundary>(row => row.TenantId == 1 && row.Id != 7, sorts);
            int[] expected;
            await using (var db = new Database(Options))
            {
                var ordered = db.Set<Boundary>()
                    .Where(read.Predicate)
                    .OrderBy(
                        sorts.Select(s => new Kkts.Expressions.OrderByInfo
                        {
                            Property = s.Property.Name,
                            Descending = s.Descending,
                        })
                    );
                expected = await ordered.Select(row => row.Id).ToArrayAsync();
            }
            var actual = new List<int>();
            object?[]? boundary = null;
            commands.Sql.Clear();
            commands.Entities = 0;
            for (var batch = 0; batch < rows.Length; batch++)
            {
                await using var session = await adapter.OpenAsync();
                var seek = Assert.IsAssignableFrom<IEntitySeekReadSession<Boundary>>(session);
                var page = await seek.SeekAsync(new(read, boundary), 1, CancellationToken.None);
                if (page.Count == 0)
                    break;
                actual.Add(Assert.Single(page).Id);
                boundary = sorts.Select(s => s.Property.GetValue(page[0])).ToArray();
            }
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Length, commands.Entities);
            Assert.Equal(expected.Length + 1, commands.Sql.Count);
            Assert.All(
                commands.Sql,
                sql =>
                {
                    Assert.DoesNotContain("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("TenantId", sql);
                    Assert.Contains("ORDER BY", sql);
                }
            );
            foreach (
                var output in new IEnumerable<EntityOutputBinding>[]
                {
                    [],
                    [
                        new("label", typeof(Boundary).GetProperty("Text")!),
                        new("alias", typeof(Boundary).GetProperty("Text")!),
                    ],
                }
            )
            {
                var projectedRead = new EntityReadPlan<Boundary>(read.Predicate, sorts, output);
                var projectedActual = new List<int>();
                boundary = null;
                commands.Sql.Clear();
                commands.Columns.Clear();
                commands.Entities = 0;
                for (var batch = 0; batch < rows.Length; batch++)
                {
                    await using var session = await adapter.OpenAsync();
                    var projected = Assert.IsAssignableFrom<
                        IEntityProjectedSeekReadSession<Boundary>
                    >(session);
                    var seek = new EntitySeekPlan<Boundary>(projectedRead, boundary);
                    var page = await projected.SeekProjectedAsync(seek, 1, CancellationToken.None);
                    if (page.Count == 0)
                        break;
                    var scalar = Assert.Single(page);
                    scalar.Validate(projectedRead, seek);
                    projectedActual.Add(Assert.IsType<int>(scalar.ContinuationValues[^1]));
                    if (projectedRead.OutputBindings.Count != 0)
                        Assert.Equal(scalar.OutputValues[0], scalar.OutputValues[1]);
                    boundary = scalar.ContinuationValues.ToArray();
                }
                Assert.Equal(expected, projectedActual);
                Assert.Equal(0, commands.Entities);
                var physical = sorts
                    .Select(sort => sort.Property.Name)
                    .Concat(projectedRead.OutputBindings.Select(binding => binding.Property.Name))
                    .Distinct()
                    .ToHashSet(StringComparer.Ordinal);
                Assert.All(
                    commands.Columns,
                    columns =>
                    {
                        Assert.Equal(physical.Count, columns.Length);
                        Assert.True(physical.SetEquals(columns));
                        Assert.DoesNotContain("TenantId", columns);
                        Assert.DoesNotContain("WidePayload", columns);
                    }
                );
                Assert.All(
                    commands.Sql,
                    sql => Assert.DoesNotContain("OFFSET", sql, StringComparison.OrdinalIgnoreCase)
                );
            }
        }
        await using (var session = await adapter.OpenAsync())
        {
            var seek = Assert.IsAssignableFrom<IEntitySeekReadSession<Boundary>>(session);
            var unmapped = new EntityReadPlan<Boundary>(
                row => row.TenantId == 1,
                [new(typeof(Boundary).GetProperty("Computed")!, false)]
            );
            await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
                seek.SeekAsync(new(unmapped, null), 1, CancellationToken.None)
            );
            foreach (var name in new[] { "Floating", "Converted" })
            {
                var unsupported = new EntityReadPlan<Boundary>(
                    row => row.TenantId == 1,
                    [new(typeof(Boundary).GetProperty(name)!, false)]
                );
                var queries = commands.Sql.Count;
                await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
                    seek.SeekAsync(new(unsupported, null), 1, CancellationToken.None)
                );
                Assert.Equal(queries, commands.Sql.Count);
            }
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                seek.SeekAsync(
                    new(new(row => true, [new(typeof(Boundary).GetProperty("Id")!, false)]), null),
                    0,
                    CancellationToken.None
                )
            );
        }
    }

    sealed class MaterializationCounter
        : Microsoft.EntityFrameworkCore.Diagnostics.IMaterializationInterceptor
    {
        internal static readonly MaterializationCounter Instance = new();

        public object InitializedInstance(
            Microsoft.EntityFrameworkCore.Diagnostics.MaterializationInterceptionData data,
            object entity
        )
        {
            var recorder = data
                .Context.GetService<IDbContextOptions>()
                .Extensions.OfType<CoreOptionsExtension>()
                .Single()
                .Interceptors!.OfType<SeekCommands>()
                .Single();
            recorder.Entities++;
            return entity;
        }
    }

    sealed class SeekCommands : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public List<object?[]> Parameters { get; } = [];
        public List<string[]> Columns { get; } = [];
        public int Entities;
        public Func<string, string[], bool>? ColumnCheck;
        public bool ColumnCheckPassed = true;
        public Func<Task>? AfterRead;

        public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData eventData,
            System.Data.Common.DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            var columns = Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray();
            Columns.Add(columns);
            if (ColumnCheck != null)
                ColumnCheckPassed &= ColumnCheck(command.CommandText, columns);
            if (AfterRead != null)
                await AfterRead();
            return result;
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Sql.Add(command.CommandText);
            Parameters.Add(
                command
                    .Parameters.Cast<System.Data.Common.DbParameter>()
                    .Select(p => p.Value)
                    .ToArray()
            );
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Real_provider_sessions_dispose_on_early_export_and_cancellation()
    {
        var sessions = new List<Database>();
        var adapter =
            new Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<CompilerTests.Employee>(
                () =>
                {
                    var db = new Database(Options);
                    sessions.Add(db);
                    return db;
                },
                ProviderName
            );
        var repo = new AuthorizedReadRepository<CompilerTests.Employee>(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            Store,
            adapter,
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 1)
        );
        await repo.ListAsync(new(Limit: 1));
        Assert.True(sessions.Single().Disposed);
        await using (var export = repo.ExportAsync(new()).GetAsyncEnumerator())
            Assert.True(await export.MoveNextAsync());
        Assert.All(sessions, db => Assert.True(db.Disposed));
        using var cancellation = new CancellationTokenSource();
        await using (var export = repo.ExportAsync(new(), cancellation.Token).GetAsyncEnumerator())
        {
            Assert.True(await export.MoveNextAsync());
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await export.MoveNextAsync()
            );
        }
        Assert.All(sessions, db => Assert.True(db.Disposed));
    }

    IEntityReadAdapter<CompilerTests.Employee> SnapshotAdapter(Func<DbContext> factory) =>
        ProviderName switch
        {
            "Microsoft.EntityFrameworkCore.Sqlite" =>
                SqliteEntityReads.Create<CompilerTests.Employee>(
                    CompilerTests.Schema,
                    factory,
                    ReadOperations.All | ReadOperations.Snapshot
                ),
            "Microsoft.EntityFrameworkCore.SqlServer" =>
                SqlServer.SqlServerEntityReads.Create<CompilerTests.Employee>(
                    CompilerTests.Schema,
                    factory,
                    ReadOperations.All | ReadOperations.Snapshot
                ),
            "MySql.EntityFrameworkCore" => MySql.MySqlEntityReads.Create<CompilerTests.Employee>(
                CompilerTests.Schema,
                factory,
                ReadOperations.All | ReadOperations.Snapshot
            ),
            _ => throw new NotSupportedException("Unknown snapshot provider."),
        };

    async Task PrepareSnapshot()
    {
        await using var db = new Database(Options);
        if (ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
        else if (ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON"
            );
    }

    AuthorizedReadRepository<CompilerTests.Employee> SnapshotReads(
        List<Database> sessions,
        ExportReadConfiguration export,
        SeekCommands? commands = null,
        IActivePolicySource? source = null
    )
    {
        var options =
            commands == null
                ? Options
                : new DbContextOptionsBuilder<Database>(Options)
                    .AddInterceptors(commands, MaterializationCounter.Instance)
                    .Options;
        var adapter = SnapshotAdapter(() =>
        {
            var db = new Database(options);
            sessions.Add(db);
            return db;
        });
        sessions.Clear(); // Registration validates metadata in a separate, nonquerying context.
        return new(
            CompilerTests.Schema,
            CompilerTests.Compiler(),
            source ?? Store,
            adapter,
            () => CompilerTests.Context(),
            "scope",
            new(2, 100, 1),
            export: export
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Snapshot_preserves_initial_rows_values_and_sort_after_independent_mutations(
        bool abort
    )
    {
        await PrepareSnapshot();
        var commands = new SeekCommands();
        var sessions = new List<Database>();
        var reads = SnapshotReads(
            sessions,
            new(
                abort
                    ? ExportPolicyConsistency.AbortOnPolicyChange
                    : ExportPolicyConsistency.CapturedPolicy,
                ExportDataConsistency.Snapshot
            ),
            commands
        );
        var initial = (
            await Reads().ListAsync(new(Selection: ["Id", "Name"], Sort: [new("Name")], Limit: 2))
        )
            .Records.Select(row => row.GetRawText())
            .ToList();
        initial.Add("""{"Id":3,"Name":"Same"}""");
        var result = new List<string>();
        await using var iterator = reads
            .ExportAsync(new(Selection: ["Id", "Name"], Sort: [new("Name")]))
            .GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        result.AddRange(iterator.Current.Records.Select(row => row.GetRawText()));
        Assert.Single(sessions);
        Assert.False(sessions[0].Disposed);
        var native = sessions[0].Database.CurrentTransaction!.GetDbTransaction();
        Assert.Equal(
            ProviderName switch
            {
                "Microsoft.EntityFrameworkCore.SqlServer" => System.Data.IsolationLevel.Snapshot,
                "MySql.EntityFrameworkCore" => System.Data.IsolationLevel.RepeatableRead,
                _ => System.Data.IsolationLevel.Serializable,
            },
            native.IsolationLevel
        );
        await using (var writer = new Database(Options))
        {
            var changed = await writer
                .Set<CompilerTests.Employee>()
                .SingleAsync(row => row.Id == 2);
            changed.Name = "ZZZ";
            changed.TenantId = 2;
            writer.Remove(
                await writer.Set<CompilerTests.Employee>().SingleAsync(row => row.Id == 3)
            );
            writer.Add(
                new CompilerTests.Employee
                {
                    Id = 5,
                    TenantId = 1,
                    Name = "AAA",
                }
            );
            await writer.SaveChangesAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        while (await iterator.MoveNextAsync())
            result.AddRange(iterator.Current.Records.Select(row => row.GetRawText()));
        Assert.Equal(initial, result);
        Assert.Single(sessions);
        Assert.True(sessions[0].Disposed);
        Assert.True(
            native.Connection == null
                || native.Connection.State == System.Data.ConnectionState.Closed
        );
        Assert.Equal(0, commands.Entities);
        Assert.Equal(4, commands.Sql.Count);
        Assert.All(commands.Columns, columns => Assert.Equal(new[] { "Id", "Name" }, columns));
    }

    [Theory]
    [InlineData("cap")]
    [InlineData("cancel")]
    [InlineData("duration")]
    [InlineData("dispose")]
    [InlineData("loss")]
    [InlineData("transaction")]
    public async Task Snapshot_termination_releases_transaction_without_replay(string reason)
    {
        await PrepareSnapshot();
        var clock = new ExportLifecycleTests.Clock();
        var sessions = new List<Database>();
        var commands = new SeekCommands();
        using var cancellation = new CancellationTokenSource();
        var reads = SnapshotReads(
            sessions,
            new(
                dataConsistency: ExportDataConsistency.Snapshot,
                maxRows: reason == "cap" ? 1 : null,
                maxDuration: TimeSpan.FromSeconds(1),
                timeProvider: clock
            ),
            commands
        );
        await using var iterator = reads
            .ExportAsync(new(Selection: []), cancellation.Token)
            .GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal("{}", Assert.Single(iterator.Current.Records).GetRawText());
        Assert.Single(sessions);
        var native = sessions[0].Database.CurrentTransaction!.GetDbTransaction();
        switch (reason)
        {
            case "cap":
                Assert.False(await iterator.MoveNextAsync());
                break;
            case "cancel":
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await iterator.MoveNextAsync()
                );
                break;
            case "duration":
                clock.Advance(TimeSpan.FromSeconds(1));
                await Assert.ThrowsAsync<ExportDurationExceededException>(async () =>
                    await iterator.MoveNextAsync()
                );
                break;
            case "loss":
                await sessions[0].Database.GetDbConnection().CloseAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await iterator.MoveNextAsync()
                );
                break;
            case "transaction":
                await native.RollbackAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await iterator.MoveNextAsync()
                );
                break;
            case "dispose":
                await iterator.DisposeAsync();
                break;
        }
        Assert.Single(sessions);
        Assert.True(sessions[0].Disposed);
        Assert.True(
            native.Connection == null
                || native.Connection.State == System.Data.ConnectionState.Closed
        );
        Assert.Single(commands.Sql);
        Assert.Equal(0, commands.Entities);
        Assert.Equal(new[] { "Id" }, Assert.Single(commands.Columns));
        Assert.Equal(0, clock.Timers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Snapshot_policy_reads_remain_authoritative_including_same_file_SQLite(
        bool duringQuery
    )
    {
        await PrepareSnapshot();
        var source = Store;
        if (ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            source = new SqlitePolicyStore("Data Source=" + path + ";Cache=Private");
            await source.InitializeAsync();
            await source.SaveDraftAsync(
                admin,
                "scope",
                "Employee",
                PolicyJson.Serialize(
                    CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
                ),
                0
            );
            await source.PublishAsync(
                admin,
                "scope",
                CompilerTests.Schema,
                CompilerTests.Registry,
                1,
                0
            );
        }
        var original = await source.CaptureActivationAsync("scope", "Employee");
        async Task Activate()
        {
            await source.PublishAsync(
                admin,
                "scope",
                CompilerTests.Schema,
                CompilerTests.Registry,
                1,
                1
            );
            await source.RollbackAsync(
                admin,
                "scope",
                CompilerTests.Schema,
                CompilerTests.Registry,
                original.Policy.Version,
                2
            );
        }
        var sessions = new List<Database>();
        var commands = new SeekCommands { AfterRead = duringQuery ? Activate : null };
        var reads = SnapshotReads(
            sessions,
            new(ExportPolicyConsistency.AbortOnPolicyChange, ExportDataConsistency.Snapshot),
            commands,
            source
        );
        await using var iterator = reads.ExportAsync(new(Selection: ["Name"])).GetAsyncEnumerator();
        if (!duringQuery)
        {
            Assert.True(await iterator.MoveNextAsync());
            await Activate();
        }
        await Assert.ThrowsAsync<ExportPolicyChangedException>(async () =>
            await iterator.MoveNextAsync()
        );
        Assert.Single(commands.Sql);
        Assert.Single(sessions);
        Assert.True(sessions[0].Disposed);
        Assert.Equal(
            new(original.Policy.Version, 3),
            await source.ReadActivationAsync("scope", "Employee")
        );
    }

    [Fact]
    public async Task Snapshot_initialization_is_single_owned_and_rejects_existing_reads_or_transactions()
    {
        await PrepareSnapshot();
        var plan = new EntityReadPlan<CompilerTests.Employee>(
            row => row.Id > 0,
            [new(typeof(CompilerTests.Employee).GetProperty("Id")!, false)]
        );
        var adapter = SnapshotAdapter(() => new Database(Options));
        await using (var session = await adapter.OpenAsync())
        {
            var snapshot = Assert.IsAssignableFrom<
                IEntitySnapshotReadSession<CompilerTests.Employee>
            >(session);
            await snapshot.BeginSnapshotAsync(plan, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                snapshot.BeginSnapshotAsync(plan, CancellationToken.None)
            );
        }
        await using (var session = await adapter.OpenAsync())
        {
            await session.CountAsync(plan, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ((IEntitySnapshotReadSession<CompilerTests.Employee>)session).BeginSnapshotAsync(
                    plan,
                    CancellationToken.None
                )
            );
        }
        Database? context = null;
        adapter = SnapshotAdapter(() => context = new Database(Options));
        await using (var session = await adapter.OpenAsync())
        {
            await using var existing = await context!.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ((IEntitySnapshotReadSession<CompilerTests.Employee>)session).BeginSnapshotAsync(
                    plan,
                    CancellationToken.None
                )
            );
        }
        Assert.True(context!.Disposed);
    }

    sealed class FailedActivationSource(SqlitePolicyStore source) : IActivationAwarePolicySource
    {
        public bool Fail;

        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        ) => source.CaptureAsync(scope, resource, cancellation);

        public Task<CapturedPolicyActivation> CaptureActivationAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        ) => source.CaptureActivationAsync(scope, resource, cancellation);

        public Task<PolicyActivationStamp> ReadActivationAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        ) =>
            Fail
                ? throw new InvalidOperationException("authoritative source failed")
                : source.ReadActivationAsync(scope, resource, cancellation);
    }

    [Fact]
    public async Task Snapshot_source_check_failure_releases_real_transaction_and_suppresses_batch()
    {
        await PrepareSnapshot();
        var source = new FailedActivationSource(Store);
        var commands = new SeekCommands
        {
            AfterRead = () =>
            {
                source.Fail = true;
                return Task.CompletedTask;
            },
        };
        var sessions = new List<Database>();
        await using var iterator = SnapshotReads(
                sessions,
                new(ExportPolicyConsistency.AbortOnPolicyChange, ExportDataConsistency.Snapshot),
                commands,
                source
            )
            .ExportAsync(new())
            .GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await iterator.MoveNextAsync()
        );
        Assert.Single(commands.Sql);
        Assert.True(Assert.Single(sessions).Disposed);
    }

    [Fact]
    public async Task Snapshot_provider_version_is_recorded()
    {
        await using var db = new Database(Options);
        await db.Database.OpenConnectionAsync();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = ProviderName switch
        {
            "Microsoft.EntityFrameworkCore.Sqlite" => "SELECT sqlite_version()",
            "Microsoft.EntityFrameworkCore.SqlServer" =>
                "SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(50))",
            "MySql.EntityFrameworkCore" => "SELECT VERSION()",
            _ => throw new NotSupportedException("Unknown provider."),
        };
        var version = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.False(string.IsNullOrWhiteSpace(version));
        Console.WriteLine(
            $"Snapshot conformance provider: {ProviderName}, server version: {version}"
        );
    }
}
