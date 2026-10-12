using Kkts.DataAccess.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class PolicyStoreTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
    SqlitePolicyStore Store => new("Data Source=" + path);
    readonly AdministrationContext admin = new("admin", "scope", true);
    static string Payload =>
        PolicyJson.Serialize(CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow)));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    [Fact]
    public async Task Draft_publication_rollback_are_persistent_scoped_and_revision_checked()
    {
        var store = Store;
        await store.InitializeAsync();
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            store.CaptureAsync("scope", "Employee")
        );
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            store.ReadDraftAsync(admin with { IsAdministrator = false }, "scope", "Employee")
        );
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            store.SaveDraftAsync(admin, "other", "Employee", Payload, 0)
        );
        var draft = await store.SaveDraftAsync(admin, "scope", "Employee", Payload, 0);
        Assert.Equal(1, draft);
        await Assert.ThrowsAsync<PolicyConflictException>(() =>
            store.SaveDraftAsync(admin, "scope", "Employee", Payload, 0)
        );
        var version = await store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            1,
            0
        );
        var captured = await Store.CaptureAsync("scope", "Employee");
        Assert.Equal(version, captured.Version);
        Assert.Null((await store.ReadDraftAsync(admin, "scope", "Other")).Payload);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            store.CaptureAsync("other", "Employee")
        );
        await store.SaveDraftAsync(admin, "scope", "Employee", Payload, 1);
        Assert.Equal(version, (await store.CaptureAsync("scope", "Employee")).Version);
        var next = await store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            2,
            1
        );
        Assert.NotEqual(version, next);
        Assert.Equal(version, captured.Version);
        await Assert.ThrowsAsync<PolicyConflictException>(() =>
            store.PublishAsync(admin, "scope", CompilerTests.Schema, CompilerTests.Registry, 2, 1)
        );
        await store.RollbackAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            version,
            2
        );
        Assert.Equal(version, (await Store.CaptureAsync("scope", "Employee")).Version);
        Assert.Equal(5, (await store.HistoryAsync(admin, "scope", "Employee")).Count);
        Assert.Equal(
            "Rollback",
            (await store.HistoryAsync(admin, "scope", "Employee"))[^1].Operation
        );
    }

    [Fact]
    public async Task Concurrent_saves_and_incompatible_rollback_preserve_winner()
    {
        var store = Store;
        await store.InitializeAsync();
        await store.SaveDraftAsync(admin, "scope", "Employee", Payload, 0);
        var version = await store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            1,
            0
        );
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 2)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            await Store.SaveDraftAsync(admin, "scope", "Employee", Payload, 1);
                            return true;
                        }
                        catch (PolicyConflictException)
                        {
                            return false;
                        }
                    })
                )
        );
        Assert.Single(results, r => r);
        Assert.Single(results, r => !r);
        var incompatible = new ResourceSchema<CompilerTests.Employee>(
            "Employee",
            "2",
            CompilerTests.Schema.Fields.Values,
            "Id"
        );
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            store.RollbackAsync(admin, "scope", incompatible, CompilerTests.Registry, version, 1)
        );
        Assert.Equal(version, (await store.CaptureAsync("scope", "Employee")).Version);
    }

    [Fact]
    public async Task Invalid_and_failed_publication_preserve_pointer_history_and_audit()
    {
        var store = Store;
        await store.InitializeAsync();
        await store.SaveDraftAsync(admin, "scope", "Employee", Payload, 0);
        var version = await store.PublishAsync(
            admin,
            "scope",
            CompilerTests.Schema,
            CompilerTests.Registry,
            1,
            0
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PublishAsync(
                admin,
                "scope",
                CompilerTests.Schema,
                CompilerTests.Registry,
                1,
                1,
                beforeCommit: () => throw new InvalidOperationException("injected")
            )
        );
        Assert.Equal(version, (await store.CaptureAsync("scope", "Employee")).Version);
        Assert.Equal(2, (await store.HistoryAsync(admin, "scope", "Employee")).Count);
        var invalid = PolicyJson.Serialize(
            CompilerTests.Definition(
                CompilerTests.Rule("bad", Effect.Allow, CompilerTests.Leaf("Missing", "eq", 1))
            )
        );
        await store.SaveDraftAsync(admin, "scope", "Employee", invalid, 1);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            store.PublishAsync(admin, "scope", CompilerTests.Schema, CompilerTests.Registry, 2, 1)
        );
        Assert.Equal(version, (await store.CaptureAsync("scope", "Employee")).Version);
        await using var c = new SqliteConnection("Data Source=" + path);
        await c.OpenAsync();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE PolicyVersions SET Payload='secret'";
        await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        cmd.CommandText = "SELECT COUNT(*) FROM PolicyVersions";
        Assert.Equal(1L, await cmd.ExecuteScalarAsync());
        Assert.All(
            await store.HistoryAsync(admin, "scope", "Employee"),
            e =>
            {
                Assert.Equal("admin", e.Actor);
                Assert.DoesNotContain("Missing", e.Summary);
            }
        );
    }
}
