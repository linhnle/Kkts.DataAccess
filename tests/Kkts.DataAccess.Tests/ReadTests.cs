using System.Text.Json;
using Kkts.DataAccess.Sqlite;
using Kkts.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class ReadTests : IAsyncLifetime
{
    public class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        public bool Disposed { get; private set; }

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
        }
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
        new DbContextOptionsBuilder<Database>().UseSqlite("Data Source=" + path).Options;
    SqlitePolicyStore Store => new("Data Source=" + policyPath);
    readonly AdministrationContext admin = new("admin", "scope", true);

    protected virtual string ProviderName => "Microsoft.EntityFrameworkCore.Sqlite";

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

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        using (var db = new Database(Options))
            db.Database.EnsureDeleted();
        File.Delete(path);
        File.Delete(policyPath);
        return Task.CompletedTask;
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
        Assert.Null((await reads.DetailAsync(new(JsonSerializer.SerializeToElement(4)))).Record);
        Assert.Null((await reads.DetailAsync(new(JsonSerializer.SerializeToElement(99)))).Record);
        Assert.NotNull((await reads.DetailAsync(new(JsonSerializer.SerializeToElement(1)))).Record);
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
            "key"
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
                    await repo.DetailAsync(new(JsonSerializer.SerializeToElement(1), Selection: []))
                ).Record!.Value.GetRawText()
            );
            Assert.Null(
                (
                    await repo.DetailAsync(new(JsonSerializer.SerializeToElement(3), Selection: []))
                ).Record
            );
            Assert.Null(
                (
                    await repo.DetailAsync(
                        new(JsonSerializer.SerializeToElement(99), Selection: [])
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
            await Assert.ThrowsAsync<NotSupportedException>(() =>
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
            await Assert.ThrowsAsync<AuthorizationException>(() => reads.DetailAsync(request));
        }
        var key = JsonSerializer.SerializeToElement(1);
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
                new ListReadRequest(Selection: ["Manager.Id"]),
                new ListReadRequest(Limit: 0),
            }
        )
            await Assert.ThrowsAsync<AuthorizationException>(() => reads.ListAsync(request));
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
        var detail = await reads.DetailAsync(new(JsonSerializer.SerializeToElement(1)));
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
            "Id"
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.ListAsync(new()));
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
            "key"
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
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                seek.SeekAsync(new(unmapped, null), 1, CancellationToken.None)
            );
            foreach (var name in new[] { "Floating", "Converted" })
            {
                var unsupported = new EntityReadPlan<Boundary>(
                    row => row.TenantId == 1,
                    [new(typeof(Boundary).GetProperty(name)!, false)]
                );
                var queries = commands.Sql.Count;
                await Assert.ThrowsAsync<NotSupportedException>(() =>
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

        public override ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData eventData,
            System.Data.Common.DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            Columns.Add(Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray());
            return ValueTask.FromResult(result);
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
}
