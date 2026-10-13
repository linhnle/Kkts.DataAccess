using System.ComponentModel.DataAnnotations.Schema;
using Kkts.DataAccess.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class TranslationTests
{
    public class Entity
    {
        public int Id { get; set; }

        [NotMapped]
        public int Computed => Id + 1;
    }

    class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<Entity>().HasKey(e => e.Id);
    }

    [Fact]
    public async Task Known_unmapped_predicate_aborts_preflight_without_fallback()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<Database>()
                .UseSqlite("Data Source=" + path)
                .Options;
            await using (var db = new Database(options))
            {
                await db.Database.EnsureCreatedAsync();
                db.Add(new Entity { Id = 1 });
                await db.SaveChangesAsync();
            }
            var schema = new ResourceSchema<Entity>(
                "Untranslatable",
                "1",
                [
                    ResourceSchema<Entity>.Field("Id", e => e.Id, [FieldOperation.Select]),
                    ResourceSchema<Entity>.Field("Computed", e => e.Computed, []),
                ],
                ["Id"]
            );
            var registry = new ContextRegistry(new Dictionary<string, Type>());
            var condition = CompilerTests.Leaf("Computed", "eq", 2);
            var policy = new PolicyDefinition(
                schema.Resource,
                schema.Version,
                [new("allow", schema.Resource, "Read", Effect.Allow, new([], []), condition)],
                []
            );
            var store = new SqlitePolicyStore("Data Source=" + path);
            await store.InitializeAsync();
            var admin = new AdministrationContext("admin", "scope", true);
            await store.SaveDraftAsync(
                admin,
                "scope",
                schema.Resource,
                PolicyJson.Serialize(policy),
                0
            );
            await store.PublishAsync(admin, "scope", schema, registry, 1, 0);
            var opened = 0;
            var reads = new AuthorizedReads<Entity>(
                schema,
                new(schema, registry, CompilerTests.Leaf("Id", "gt", 0)),
                store,
                () =>
                {
                    opened++;
                    return new Database(options);
                },
                () => new([], [], new Dictionary<string, object?>()),
                "scope"
            );
            await Assert.ThrowsAsync<ProviderUnsupportedException>(() => reads.ListAsync(new()));
            Assert.Equal(1, opened);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
