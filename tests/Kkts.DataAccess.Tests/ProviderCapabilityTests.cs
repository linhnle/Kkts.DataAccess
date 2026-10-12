using System.Data.Common;
using System.Text.Json;
using Kkts.DataAccess.DependencyInjection;
using Kkts.DataAccess.EntityFrameworkCore;
using Kkts.DataAccess.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Row = Kkts.DataAccess.Tests.ReadTests.Boundary;

namespace Kkts.DataAccess.Tests;

public class ProviderCapabilityTests : IAsyncLifetime
{
    readonly string path = Path.Combine(
        Path.GetTempPath(),
        $"kda-capabilities-{Guid.NewGuid():N}.db"
    );
    readonly string policyPath = Path.Combine(
        Path.GetTempPath(),
        $"kda-capability-policy-{Guid.NewGuid():N}.db"
    );
    readonly Commands commands = new();
    readonly List<TrackedDatabase> contexts = [];
    DbContextOptions<ReadTests.Database> Options =>
        new DbContextOptionsBuilder<ReadTests.Database>()
            .UseSqlite($"Data Source={path};Cache=Private")
            .AddInterceptors(commands)
            .Options;

    public sealed class TrackedDatabase(DbContextOptions<ReadTests.Database> options)
        : ReadTests.Database(options)
    {
        public int Disposals;

        public override void Dispose()
        {
            Disposals++;
            base.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            Disposals++;
            await base.DisposeAsync();
        }
    }

    sealed class ConvertedDatabase(DbContextOptions<ReadTests.Database> options)
        : ReadTests.Database(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            model
                .Entity<Row>()
                .Property(row => row.Text)
                .HasConversion(
                    value => value == null ? null : value + "!",
                    value => value == null ? null : value.TrimEnd('!')
                );
        }
    }

    sealed class Commands : DbCommandInterceptor
    {
        public int Queries;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Queries++;
            return ValueTask.FromResult(result);
        }
    }

    sealed class Source(PolicyDefinition policy) : IActivePolicySource
    {
        public int Captures;

        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            Captures++;
            return Task.FromResult(
                new CapturedPolicy(scope, resource, "1", 1, PolicyJson.Serialize(policy))
            );
        }
    }

    sealed class IdentityProtector : IContinuationTokenProtector
    {
        public byte[] Protect(byte[] payload) => payload;

        public byte[] Unprotect(byte[] payload) => payload;
    }

    internal static ResourceSchema<Row> Schema(
        bool computedOutput = false,
        bool convertedSort = false
    ) =>
        new(
            "Boundary",
            "1",
            [
                ResourceSchema<Row>.Field("Id", row => row.Id, [FieldOperation.Select]),
                ResourceSchema<Row>.Field("TenantId", row => row.TenantId, []),
                ResourceSchema<Row>.Field(
                    "Text",
                    row => row.Text,
                    [FieldOperation.Select, FieldOperation.Filter, FieldOperation.Sort]
                ),
                ResourceSchema<Row>.Field(
                    "Amount",
                    row => row.Amount,
                    [FieldOperation.Select, FieldOperation.Filter]
                ),
                ResourceSchema<Row>.Field("Floating", row => row.Floating, [FieldOperation.Select]),
                ResourceSchema<Row>.Field(
                    "Converted",
                    row => row.Converted,
                    convertedSort
                        ? [FieldOperation.Select, FieldOperation.Sort]
                        : [FieldOperation.Select]
                ),
                ResourceSchema<Row>.Field(
                    "Computed",
                    row => row.Computed,
                    computedOutput ? [FieldOperation.Select] : []
                ),
            ],
            "Id"
        );

    internal static EfEntityReadAdapter<Row> Register(
        ResourceSchema<Row> schema,
        Func<DbContext> factory,
        string provider,
        ReadOperations operations
    ) =>
        provider switch
        {
            "Microsoft.EntityFrameworkCore.Sqlite" => SqliteEntityReads.Create<Row>(
                schema,
                factory,
                operations
            ),
            "Microsoft.EntityFrameworkCore.SqlServer" => SqlServer.SqlServerEntityReads.Create<Row>(
                schema,
                factory,
                operations
            ),
            "MySql.EntityFrameworkCore" => MySql.MySqlEntityReads.Create<Row>(
                schema,
                factory,
                operations
            ),
            _ => throw new ArgumentException("Unknown fixture provider.", nameof(provider)),
        };

    static ContextRegistry Registry =>
        new(
            new Dictionary<string, Type> { ["tenant"] = typeof(int), ["contextOnly"] = typeof(int) }
        );
    static Condition Mandatory => CompilerTests.Reference("TenantId", "tenant");

    internal static PolicyDefinition Policy(Condition? condition = null) =>
        new(
            "Boundary",
            "1",
            [new("allow", "Boundary", "Read", Effect.Allow, new(["manager"], []), condition)],
            [
                new(
                    "fields",
                    "Boundary",
                    "Read",
                    Effect.Allow,
                    new(["manager"], []),
                    new()
                    {
                        [FieldOperation.Select] = ["Id", "Text", "Converted"],
                        [FieldOperation.Sort] = ["Text"],
                    }
                ),
            ]
        );

    TrackedDatabase Create()
    {
        var context = new TrackedDatabase(Options);
        contexts.Add(context);
        return context;
    }

    AuthorizedReadRepository<Row> Repository(
        ResourceSchema<Row> schema,
        IEntityReadAdapter<Row> adapter,
        IActivePolicySource source,
        IEnumerable<IReadProviderCapabilities>? profiles = null
    ) =>
        new(
            schema,
            new(schema, Registry, Mandatory, capabilities: profiles),
            source,
            adapter,
            () => CompilerTests.Context(),
            "scope",
            cursor: new(new IdentityProtector(), () => "subject", "fixture")
        );

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var file in new[] { path, policyPath })
        {
            File.Delete(file);
            File.Delete(file + "-wal");
            File.Delete(file + "-shm");
        }
        return Task.CompletedTask;
    }

    [Fact]
    public void Profile_distinguishes_output_offset_seek_predicates_and_unverified_queries_without_data_access()
    {
        var schema = Schema();
        var adapter = SqliteEntityReads.Create<Row>(schema, Create, ReadOperations.All);
        var profile = Assert.IsType<ReadProviderCapabilities>(adapter.Capabilities);
        Assert.Equal(
            CapabilityStatus.Supported,
            profile.CheckField("Converted", ProviderFieldUse.Output).Status
        );
        Assert.Equal(
            CapabilityStatus.Unverified,
            profile.CheckField("Converted", ProviderFieldUse.OffsetOrder).Status
        );
        Assert.Equal(
            CapabilityStatus.Unsupported,
            profile.CheckField("Converted", ProviderFieldUse.SeekOrder).Status
        );
        Assert.Equal(
            CapabilityStatus.Unverified,
            profile.CheckField("Converted", ProviderFieldUse.Predicate, "eq").Status
        );
        Assert.Equal(
            CapabilityStatus.Unsupported,
            profile.CheckField("Computed", ProviderFieldUse.Output).Status
        );
        Assert.Equal(
            CapabilityStatus.Supported,
            profile.CheckField("Amount", ProviderFieldUse.OffsetOrder).Status
        );
        Assert.Equal(
            CapabilityStatus.Unsupported,
            profile.CheckField("Floating", ProviderFieldUse.SeekOrder).Status
        );
        Assert.Equal(
            CapabilityStatus.Supported,
            profile.CheckField("Id", ProviderFieldUse.SeekOrder).Status
        );
        Assert.Equal(CapabilityStatus.Unverified, profile.ComposedQuery.Status);
        Assert.Equal(18, profile.Fields["Amount"].Mapping.Precision);
        Assert.Equal(2, profile.Fields["Amount"].Mapping.Scale);
        Assert.Null(profile.Fields["Text"].Mapping.Collation);
        Assert.NotEmpty(profile.ImplementationVersion);
        Assert.Equal(0, commands.Queries);
        Assert.False(File.Exists(path));
        Assert.Equal(1, Assert.Single(contexts).Disposals);
    }

    [Theory]
    [InlineData(ReadOperations.Cursor)]
    [InlineData(ReadOperations.Export)]
    public void Advertised_converter_seek_fails_with_exact_operation_field_path(
        ReadOperations operation
    )
    {
        var error = Assert.Throws<ProviderUnsupportedException>(() =>
            SqliteEntityReads.Create<Row>(Schema(convertedSort: true), Create, operation)
        );
        Assert.Contains(
            error.Diagnostics,
            diagnostic => diagnostic.Path == "/operations/seek/fields/Converted"
        );
        Assert.Equal(1, Assert.Single(contexts).Disposals);
        Assert.Equal(0, commands.Queries);
    }

    [Fact]
    public async Task Count_only_registration_does_not_require_output_or_seek_and_disabled_reads_capture_nothing()
    {
        var schema = Schema(computedOutput: true, convertedSort: true);
        var adapter = SqliteEntityReads.Create<Row>(schema, Create, ReadOperations.Count);
        var source = new Source(Policy());
        var repository = Repository(schema, adapter, source);
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() => repository.ListAsync(new()));
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
            repository.DetailAsync(new(JsonSerializer.SerializeToElement(1)))
        );
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
            repository.ListCursorAsync(new())
        );
        await using var export = repository.ExportAsync(new()).GetAsyncEnumerator();
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
            export.MoveNextAsync().AsTask()
        );
        Assert.Equal(0, source.Captures);
        Assert.Single(contexts);
        Assert.Equal(0, commands.Queries);
    }

    [Fact]
    public void Capability_collections_are_owned_and_never_supply_grants()
    {
        var schema = Schema();
        var profile = Assert.IsType<ReadProviderCapabilities>(
            SqliteEntityReads.Create<Row>(schema, Create).Capabilities
        );
        var fields = profile.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        var keys = new[] { "Id" };
        var snapshot = new ReadProviderCapabilities(
            profile.Resource,
            profile.SchemaVersion,
            profile.Provider,
            profile.ImplementationVersion,
            profile.ModelIdentity,
            profile.Operations,
            fields,
            keys,
            profile.SnapshotShape
        );
        fields.Clear();
        keys[0] = "changed";
        Assert.Equal("Id", snapshot.KeyFields[0]);
        Assert.Equal(
            CapabilityStatus.Supported,
            snapshot.CheckField("Text", ProviderFieldUse.SeekOrder).Status
        );
        var noFields = Policy() with { FieldEntries = [] };
        var bound = new PolicyCompiler<Row>(
            schema,
            Registry,
            Mandatory,
            capabilities: [snapshot]
        ).Bind(
            new("scope", "Boundary", "1", 1, PolicyJson.Serialize(noFields)),
            CompilerTests.Context(),
            "scope"
        );
        Assert.Empty(bound.Fields[FieldOperation.Select]);
        Assert.Empty(bound.Fields[FieldOperation.Sort]);
    }

    [Fact]
    public void Provider_checks_walk_database_leaves_but_exclude_subject_context_trees()
    {
        var schema = Schema();
        var profile = SqliteEntityReads
            .Create<Row>(schema, Create, ReadOperations.Count)
            .Capabilities!;
        var condition = new Condition
        {
            All =
            [
                CompilerTests.Leaf("Id", "gt", 0),
                new() { Not = CompilerTests.Leaf("Computed", "eq", 0) },
            ],
        };
        var invalid = Policy(condition);
        Assert.Empty(new PolicyValidator<Row>(schema, Registry).Validate(invalid));
        var errors = new PolicyValidator<Row>(schema, Registry, capabilities: [profile]).Validate(
            invalid
        );
        Assert.Contains(
            errors,
            error =>
                error.Code == DataAccessErrorCode.ProviderUnsupported
                && error.Path == "/rules/0/condition/all/1/not/operator"
        );
        var subject = new Subject(["manager"], [], CompilerTests.Leaf("contextOnly", "eq", 1));
        var valid = Policy() with
        {
            Rules = [new("context", "Boundary", "Read", Effect.Allow, subject, null)],
        };
        Assert.Empty(
            new PolicyValidator<Row>(schema, Registry, capabilities: [profile]).Validate(valid)
        );
        var mandatory = Assert.Throws<InvalidRequestException>(() =>
            new PolicyCompiler<Row>(
                schema,
                Registry,
                CompilerTests.Leaf("Computed", "eq", 1),
                capabilities: [profile]
            )
        );
        Assert.Contains(
            mandatory.Diagnostics,
            error =>
                error.Code == DataAccessErrorCode.ProviderUnsupported && error.Path == "/operator"
        );
        Assert.Equal(0, commands.Queries);
    }

    [Fact]
    public async Task Provider_bound_activation_is_atomic_and_neutral_activation_is_rechecked_before_entity_queries()
    {
        var schema = Schema();
        var adapter = SqliteEntityReads.Create<Row>(schema, Create, ReadOperations.Count);
        var profiles = new[] { adapter.Capabilities! };
        var store = new SqlitePolicyStore($"Data Source={policyPath}");
        var admin = new AdministrationContext("admin", "scope", true);
        await store.InitializeAsync();
        await store.SaveDraftAsync(admin, "scope", "Boundary", PolicyJson.Serialize(Policy()), 0);
        var version = await store.PublishAsync(
            admin,
            "scope",
            schema,
            Registry,
            1,
            0,
            capabilities: profiles
        );
        var active = await store.CaptureAsync("scope", "Boundary");
        var incompatible = Policy() with
        {
            Rules =
            [
                Policy().Rules[0],
                new(
                    "inactive",
                    "Boundary",
                    "Read",
                    Effect.Allow,
                    new(["not-this-user"], []),
                    CompilerTests.Leaf("Computed", "eq", 1)
                ),
            ],
        };
        await store.SaveDraftAsync(
            admin,
            "scope",
            "Boundary",
            PolicyJson.Serialize(incompatible),
            1
        );
        var history = await store.HistoryAsync(admin, "scope", "Boundary");
        var error = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            store.PublishAsync(admin, "scope", schema, Registry, 2, 1, capabilities: profiles)
        );
        Assert.Contains(
            error.Diagnostics,
            diagnostic => diagnostic.Path == "/rules/1/condition/operator"
        );
        Assert.Equal(active, await store.CaptureAsync("scope", "Boundary"));
        Assert.Equal(history, await store.HistoryAsync(admin, "scope", "Boundary"));
        await store.PublishAsync(admin, "scope", schema, Registry, 2, 1);
        var repository = Repository(schema, adapter, store);
        await Assert.ThrowsAsync<SchemaIncompatibleException>(() => repository.CountAsync(new()));
        Assert.Equal(0, commands.Queries);
        Assert.False(File.Exists(path));
        Assert.All(contexts, context => Assert.Equal(1, context.Disposals));
        var current = await store.CaptureAsync("scope", "Boundary");
        history = await store.HistoryAsync(admin, "scope", "Boundary");
        await Assert.ThrowsAsync<InvalidRequestException>(() =>
            store.RollbackAsync(
                admin,
                "scope",
                schema,
                Registry,
                current.Version,
                2,
                capabilities: profiles
            )
        );
        Assert.Equal(current, await store.CaptureAsync("scope", "Boundary"));
        Assert.Equal(history, await store.HistoryAsync(admin, "scope", "Boundary"));
        Assert.Equal(version, active.Version);
    }

    static ReadProviderCapabilities RejectText(IReadProviderCapabilities original)
    {
        var profile = Assert.IsType<ReadProviderCapabilities>(original);
        return new(
            profile.Resource,
            profile.SchemaVersion,
            "host-second-target",
            "fixture",
            profile.ModelIdentity,
            profile.Operations,
            profile.Fields.Select(pair =>
                pair.Key == "Text"
                    ? new KeyValuePair<string, ProviderFieldCapabilities>(
                        pair.Key,
                        new(
                            pair.Value.Mapping,
                            pair.Value.Output,
                            pair.Value.OffsetOrder,
                            pair.Value.SeekOrder,
                            pair.Value.Predicates.Select(predicate =>
                                predicate.Key == "contains"
                                    ? new KeyValuePair<string, CapabilityResult>(
                                        "contains",
                                        new(
                                            CapabilityStatus.Unsupported,
                                            "Second target rejects contains."
                                        )
                                    )
                                    : predicate
                            )
                        )
                    )
                    : pair
            ),
            profile.KeyFields,
            profile.SnapshotShape
        );
    }

    [Fact]
    public async Task Multiple_target_profiles_have_independent_compiler_validation_identity_and_DI_preserves_them()
    {
        var schema = Schema();
        var adapter = SqliteEntityReads.Create<Row>(schema, Create, ReadOperations.Count);
        var profile = adapter.Capabilities!;
        var other = RejectText(profile);
        var policy = Policy(CompilerTests.Leaf("Text", "contains", "safe"));
        var captured = new CapturedPolicy(
            "scope",
            "Boundary",
            "1",
            1,
            PolicyJson.Serialize(policy)
        );
        var first = new PolicyCompiler<Row>(schema, Registry, Mandatory, capabilities: [profile]);
        first.Bind(captured, CompilerTests.Context(), "scope");
        var multiple = new PolicyCompiler<Row>(
            schema,
            Registry,
            Mandatory,
            capabilities: [profile, other]
        );
        Assert.NotEqual(first.ValidationIdentity, multiple.ValidationIdentity);
        await Assert.ThrowsAsync<SchemaIncompatibleException>(() =>
            Task.Run(() => multiple.Bind(captured, CompilerTests.Context(), "scope"))
        );
        var services = new ServiceCollection();
        services.AddAuthorizedReads<Row>(
            "boundary",
            resource =>
                resource
                    .WithSchema(schema)
                    .WithCompiler(Registry, Mandatory)
                    .WithAdapter(adapter)
                    .WithPolicySource(new Source(Policy()))
                    .WithCapabilities(other)
                    .WithTrustedContext(_ => throw new AuthorizationException())
                    .WithScope("scope")
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync();
        var compiler = provider.GetRequiredKeyedService<PolicyCompiler<Row>>("boundary");
        Assert.Equal(2, compiler.Capabilities.Count);
        Assert.Equal(multiple.ValidationIdentity, compiler.ValidationIdentity);
        Assert.Equal(0, commands.Queries);
    }

    [Fact]
    public async Task Runtime_checks_output_and_seek_before_optional_aggregate_or_retrieval()
    {
        foreach (var seek in new[] { false, true })
        {
            var schema = Schema(computedOutput: !seek, convertedSort: seek);
            var policy = Policy() with
            {
                FieldEntries =
                [
                    new(
                        "fields",
                        "Boundary",
                        "Read",
                        Effect.Allow,
                        new(["manager"], []),
                        new()
                        {
                            [FieldOperation.Select] = seek ? ["Id"] : ["Id", "Computed"],
                            [FieldOperation.Sort] = seek ? ["Converted"] : [],
                        }
                    ),
                ],
            };
            var adapter = new EfEntityReadAdapter<Row>(
                Create,
                "Microsoft.EntityFrameworkCore.Sqlite"
            );
            var repository = Repository(schema, adapter, new Source(policy));
            if (seek)
                await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
                    repository.ListCursorAsync(
                        new(Selection: ["Id"], Sort: [new("Converted")], Limit: 1)
                    )
                );
            else
                await Assert.ThrowsAsync<ProviderUnsupportedException>(() =>
                    repository.ListAsync(new(Selection: ["Computed"], Limit: 1))
                );
            Assert.Equal(0, commands.Queries);
        }
        Assert.All(contexts, context => Assert.Equal(1, context.Disposals));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Runtime_model_drift_is_rejected_without_queries()
    {
        var schema = Schema();
        var changed = false;
        DbContext Factory() => changed ? new ConvertedDatabase(Options) : Create();
        var adapter = SqliteEntityReads.Create<Row>(schema, Factory, ReadOperations.Count);
        var repository = Repository(schema, adapter, new Source(Policy()), [adapter.Capabilities!]);
        changed = true;
        await Assert.ThrowsAsync<ProviderUnsupportedException>(() => repository.CountAsync(new()));
        Assert.Equal(0, commands.Queries);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Unverified_composed_predicate_propagates_translation_failure_without_execution_fallback()
    {
        await using (var setup = new ReadTests.Database(Options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Add(
                new Row
                {
                    Id = 1,
                    TenantId = 1,
                    Text = "safe",
                }
            );
            await setup.SaveChangesAsync();
        }
        commands.Queries = 0;
        var adapter = SqliteEntityReads.Create<Row>(Schema(), Create, ReadOperations.Count);
        Assert.Equal(
            CapabilityStatus.Unverified,
            Assert.IsType<ReadProviderCapabilities>(adapter.Capabilities).ComposedQuery.Status
        );
        await using var session = await adapter.OpenAsync();
        var plan = new EntityReadPlan<Row>(
            row => row.TenantId == 1 && NotTranslatable(row.Text),
            []
        );
        await ((IEntityReadPlanValidator<Row>)session).ValidatePlanAsync(
            plan,
            ReadOperations.Count
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.CountAsync(plan, CancellationToken.None)
        );
        Assert.Equal(0, commands.Queries);
        Assert.Equal(2, contexts.Count);
    }

    static bool NotTranslatable(string? text) => text == "safe";

    sealed class NativeTimeRow
    {
        public int Id { get; set; }
        public DateTimeOffset When { get; set; }
    }

    sealed class NativeTimeDatabase(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<NativeTimeRow>();
    }

    [Fact]
    public void Native_sqlite_time_ordering_restriction_matches_actual_translation_without_sql()
    {
        var options = new DbContextOptionsBuilder()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(commands)
            .Options;
        var schema = new ResourceSchema<NativeTimeRow>(
            "NativeTime",
            "1",
            [
                ResourceSchema<NativeTimeRow>.Field("Id", row => row.Id, [FieldOperation.Select]),
                ResourceSchema<NativeTimeRow>.Field(
                    "When",
                    row => row.When,
                    [FieldOperation.Select, FieldOperation.Filter, FieldOperation.Sort]
                ),
            ],
            "Id"
        );
        var profile = SqliteEntityReads
            .Create<NativeTimeRow>(
                schema,
                () => new NativeTimeDatabase(options),
                ReadOperations.Count | ReadOperations.Detail
            )
            .Capabilities!;
        Assert.Equal(
            CapabilityStatus.Supported,
            profile.CheckField("When", ProviderFieldUse.Output).Status
        );
        Assert.Equal(
            CapabilityStatus.Unsupported,
            profile.CheckField("When", ProviderFieldUse.OffsetOrder).Status
        );
        Assert.Equal(
            CapabilityStatus.Unsupported,
            profile.CheckField("When", ProviderFieldUse.Predicate, "gt").Status
        );
        using var db = new NativeTimeDatabase(options);
        Assert.Throws<NotSupportedException>(() =>
            db.Set<NativeTimeRow>().OrderBy(row => row.When).ToQueryString()
        );
        var boundary = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidOperationException>(() =>
            db.Set<NativeTimeRow>().Where(row => row.When > boundary).ToQueryString()
        );
        Assert.Equal(0, commands.Queries);
    }

    [Fact]
    public void Profile_key_metadata_preserves_an_ordered_tuple_without_claiming_composite_execution()
    {
        var schema = Schema();
        var profile = Assert.IsType<ReadProviderCapabilities>(
            SqliteEntityReads.Create<Row>(schema, Create, ReadOperations.Count).Capabilities
        );
        var keys = new[] { "TenantId", "Id" };
        var tuple = new ReadProviderCapabilities(
            profile.Resource,
            profile.SchemaVersion,
            profile.Provider,
            profile.ImplementationVersion,
            profile.ModelIdentity,
            ReadOperations.Count,
            profile.Fields,
            keys,
            profile.SnapshotShape
        );
        keys[0] = "changed";
        Assert.Equal(["TenantId", "Id"], tuple.KeyFields);
        Assert.NotEqual(profile.Identity, tuple.Identity);
        Assert.Single(profile.KeyFields);
        Assert.Equal("Id", schema.StableKey.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CapabilityResult((CapabilityStatus)99, "Invalid status.")
        );
    }
}
